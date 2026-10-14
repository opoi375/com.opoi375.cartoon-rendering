// ============================================================================
// CartoonVolumetricClouds.shader
// ----------------------------------------------------------------------------
// Stylised volumetric cloud layer, rendered as a FULL-SCREEN TRIANGLE into a
// HALF-RESOLUTION render texture by CartoonSkyboxFeature, then composited
// over the procedural sky by CartoonSkyboxFullscreen.
//
// Technique (simplified Guerrilla "Nubis"/Horizon-style model):
//   1. The pixel's world-space view ray is intersected with a horizontal
//      cloud slab [baseHeight, baseHeight + thickness] above the camera.
//   2. Inside the slab the ray is marched in N steps, with the march
//      range clamped to the aerial-perspective fade distance so no step
//      is wasted on invisible space. Density comes from low-frequency FBM
//      shaped by a cumulus height profile (flat bottom, round top),
//      remapped by a coverage parameter and eroded by high-frequency
//      detail noise near the cloud edges. Every step samples the noise
//      mip matching its world footprint (distance-adaptive LOD) and the
//      detail erosion fades out with LOD - together these eliminate the
//      far-field slice banding that fixed-mip marching produces.
//   3. Lighting marches a few steps towards the sun (Beer transmittance)
//      with a Powder edge-brightening term; the light response is then
//      quantised into EASE-IN-OUT cel bands (SoftQuantizeSteps - same
//      idea as the Building shader) for the stylised look.
//   4. Ambient light is sampled from the sky's own day/sunset/night
//      palettes so the clouds follow the time-of-day cycle automatically.
//      A view-aligned "silver lining" term adds the back-lit golden edge.
//
// All art parameters are pushed per-frame by CartoonSkyboxFeature from the
// CartoonProceduralSky asset - this shader has no material Inspector UI.
// ============================================================================
Shader "CartoonRendering/CartoonVolumetricClouds"
{
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Background"
            "RenderType"      = "Background"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CartoonVolumetricClouds"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Library/WorldBend.hlsl"

            // ----------------------------------------------------------------
            // Uniforms (pushed per frame by CartoonSkyboxFeature)
            // ----------------------------------------------------------------
            float4x4 _CartoonInvVP;

            float4 _CartoonSunDirection;  // light direction (sun -> scene)
            float4 _CartoonSunColor;
            float4 _CloudTint;

            // x = coverage [0..1], y = base height above camera,
            // z = slab thickness,  w = world UV scale
            float4 _CloudShapeParams;

            // 世界弯曲联动（WorldBendController 推送）：云层弯曲弱化系数 + 最大下垂量。
            // 云层比地面高得多，全强度下垂会让远处云快速沉出天际，
            // 弱化 + 钳制后云向地平线柔和聚拢但不消失。
            float _WorldBendCloudScale;
            float _WorldBendCloudMaxDroop;

            float CloudBendDroop(float2 posXZ, float2 camXZ)
            {
                return min(WorldBendOffsetFrom(posXZ, camXZ) * _WorldBendCloudScale,
                           _WorldBendCloudMaxDroop);
            }

            // x = absorption (density), y = march step count,
            // z = light step count, w = detail erosion strength
            float4 _CloudMarchParams;

            // x = wind speed, yz = wind direction
            float4 _CloudWind;

            // x = shade steps, y = shade edge softness,
            // z = silver-lining intensity, w = silver-lining power
            float4 _CloudShadeParams;

            // Ambient palettes (mirrors of the sky gradient stops; the
            // clouds derive their ambient light from the same day /
            // sunset / night blend the sky uses).
            float4 _AmbDayTop;
            float4 _AmbDayHorizon;
            float4 _AmbSunsetTop;
            float4 _AmbSunsetHorizon;
            float4 _AmbNightTop;
            float4 _AmbNightHorizon;
            float2 _AmbBlendParams;       // x = sunsetBlendStart, y = dayBlendEnd

            // Tileable 128^3 Perlin-Worley volume baked by
            // CloudNoiseTextureBaker (R = base shape, GBA = worley detail).
            // Requires the mip-chained re-bake (Tools > Cloud > Bake Cloud
            // Noise 3D) for the distance-adaptive LOD below to work.
            TEXTURE3D(_CloudNoiseTex); SAMPLER(sampler_CloudNoiseTex);

            // 2D 天气 / 扭曲图（CloudNoiseTextureBaker.BakeShapeMaps 烘焙，
            // 同样做过 CDF 归一化）：
            //   R = 大尺度天气（云系住在哪一片）
            //   G = 中尺度（把云系拆成一朵朵）
            //   BA = 域扭曲 xy（让轮廓不再是圆滚滚的）
            // 没有它的时候，整片天空的覆盖度是同一个常数，噪声又是各态历经的，
            // 于是云只会均匀地铺满天 —— 看着像磨砂玻璃，不像天气。
            TEXTURE2D(_CloudShape2D); SAMPLER(sampler_CloudShape2D);

            // x = 天气图世界尺度（每米对应多少个图块），y = 域扭曲强度（图块单位），
            // z = 天气对覆盖度的调制幅度，w = 中尺度混入比例
            float4 _CloudWeatherParams;

            // Temporal accumulation: previous frame's half-res cloud output
            // plus the VP matrix that rendered it and the blend weight.
            TEXTURE2D(_CloudHistoryTex); SAMPLER(sampler_CloudHistoryTex);
            float4x4 _CloudPrevVP;
            float    _CloudHistoryWeight;
            float    _CartoonCloudFrame;
            // Explicit render-target size: _ScreenParams tracks the CAMERA,
            // not the offscreen history RT this pass renders into, so NDC
            // reconstruction must use the real target dimensions.
            float4   _CloudRTSize;

            // 调试视图：0=关 1=覆盖度 2=步进区间 3=原始噪声 4=高度剖面 5=mip
            // （整屏黑到底是"没密度"还是"有密度但被裁掉/看不见"，只能靠它区分）
            float    _CloudDebug;

            // ----------------------------------------------------------------
            // Full-screen triangle
            // ----------------------------------------------------------------
            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                float2 pos = float2(
                    (input.vertexID == 1) ?  3.0 : -1.0,
                    (input.vertexID == 2) ?  3.0 : -1.0);
                output.positionCS = float4(pos, 0.0, 1.0);
                return output;
            }

            // ----------------------------------------------------------------
            // Noise - same value-noise family as the skybox shader so the
            // volumetric and flat cloud layers share a visual language.
            // ----------------------------------------------------------------
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Hash21 remains unused; march offset uses interleaved
            // gradient noise below.

            // Interleaved gradient noise: spatially STABLE structured dither
            // (unlike white-noise hash, it does not flicker or read as
            // salt-and-pepper) - used to offset the march start per pixel
            // so slice banding breaks up into an imperceptible pattern.
            float InterleavedGradientNoise(float2 xy)
            {
                return frac(52.9829189 * frac(dot(xy, float2(0.06711056, 0.00583715))));
            }

            // ----------------------------------------------------------------
            // Sun / palette helpers (mirror CartoonSkyboxFullscreen)
            // ----------------------------------------------------------------
            float GetSunElevation()
            {
                return -normalize(_CartoonSunDirection.xyz).y;
            }

            float3 BlendPalette(float3 nightC, float3 sunsetC, float3 dayC)
            {
                float sunY  = GetSunElevation();
                float start = _AmbBlendParams.x;
                float end   = max(_AmbBlendParams.y, start + 0.05);
                float wSunset = smoothstep(start - 0.20, start, sunY);
                float wDay    = smoothstep(start, end, sunY);
                return lerp(lerp(nightC, sunsetC, wSunset), dayC, wDay);
            }

            // Ambient cloud light at a normalised height inside the slab:
            // horizon influence at the bottom, zenith at the top.
            float3 AmbientAtHeight(float h01)
            {
                float3 top     = BlendPalette(_AmbNightTop.rgb,     _AmbSunsetTop.rgb,     _AmbDayTop.rgb);
                float3 horizon = BlendPalette(_AmbNightHorizon.rgb, _AmbSunsetHorizon.rgb, _AmbDayHorizon.rgb);
                return lerp(horizon, top, saturate(h01 * 0.7 + 0.15)) * _CloudTint.rgb;
            }

            // Sun colour tinted warm near the horizon (mirrors the skybox).
            float3 SunLitColor()
            {
                float warm = 1.0 - smoothstep(0.0, 0.35, GetSunElevation());
                float3 warmCol = float3(1.0, 0.45, 0.20);
                float3 c = lerp(_CartoonSunColor.rgb, warmCol * _CartoonSunColor.a, warm);
                // Kill the direct sun term once the sun is below the horizon.
                return c * smoothstep(-0.06, 0.10, GetSunElevation());
            }

            // ----------------------------------------------------------------
            // Soft cel quantisation - ease-in-out (smoothstep) band edges,
            // same model as the Building shader's SoftQuantizeSteps.
            // ----------------------------------------------------------------
            float SoftQuantizeSteps(float x, float steps, float softness)
            {
                float scaled = saturate(x) * max(steps, 1.0);
                float band   = floor(scaled);
                float f      = scaled - band;
                float soft   = smoothstep(0.5 - softness, 0.5 + softness, f);
                return (band + soft) / max(steps, 1.0);
            }

            // ----------------------------------------------------------------
            // Density model:
            //   base FBM -> coverage remap -> cumulus height profile ->
            //   detail erosion (strongest where density is low: the edges)
            // ----------------------------------------------------------------
            //   lod: footprint-matched mip level for the noise volume -
            //   at long range one march step spans many noise texels and
            //   mip-0 sampling aliases into slice banding.
            //   stepFrac: march step length as a slab fraction - used to
            //   widen the analytic height ramps so they stay continuously
            //   sampled when steps get coarse at distance.
            /// 天气层：返回 [0,1] 的局部天气强度，并带出域扭曲偏移。
            ///
            /// 用【绝对世界坐标】，不像 3D 形状那样以相机为原点重新居中：
            /// 重新居中的话采样窗口永远钉在 uv=0 附近，头顶这片天看到的
            /// 永远是图块同一个角落 —— 天气调制会直接退化成常数（实测就是
            /// 这个症状：Weather 调试视图整屏一个灰度）。飞到哪里是什么天气，
            /// 才是天气图存在的意义。
            /// frac 只为了把数值压回 float 精度舒服的范围，Repeat 环绕下
            /// 结果完全等价。风也要吹它（乘 0.35：天气比云团移动得慢）。
            /// 调试视图也要看它，所以单独成函数，不让两处实现漂移。
            float CloudWeather(float3 p, float2 windOffset, float2 camXZ, float lod, out float2 warp)
            {
                float wScale = _CloudWeatherParams.x;
                float2 wuv = frac(p.xz * wScale) + windOffset * 0.35;
                // 天气是低频场，比形状图晚两级退出细节即可
                float4 wx = SAMPLE_TEXTURE2D_LOD(_CloudShape2D, sampler_CloudShape2D,
                                                 wuv, clamp(lod - 1.0, 0.0, 7.0));
                // 域扭曲：把 3D 采样坐标按天气图推一把，云缘就卷起来而不是圆滚滚
                warp = (wx.ba - 0.5) * _CloudWeatherParams.y;
                return lerp(wx.r, wx.g, _CloudWeatherParams.w);
            }

            // 同上，另外把"阈值后的噪声场值 r"与"高度剖面 hg"带出来给调试视图用。
            float CloudDensity(float3 p, float h01, float2 windOffset, float2 camXZ, float lod, float stepFrac,
                               out float outR, out float outHg)
            {
                // Tileable Perlin-Worley volume, sampled in ABSOLUTE world
                // coordinates (see CloudWeather for why the old
                // camera-centred version is wrong). frac keeps the UV in a
                // float-friendly range; Repeat wrap makes it equivalent.
                // ISOTROPIC world-scale sampling: the Y axis uses the same
                // metres-per-tile as XZ, so noise cells are ROUND blobs
                // instead of vertically squashed pancakes (mapping the slab
                // onto the full 0..1 tile made cells ~4x flatter than wide).
                float scale = _CloudShapeParams.w;
                float slabBaseY = _CloudShapeParams.y;

                // ---- 天气层（2D）：先决定"这一片天归不归云管" ----
                float2 warp;
                float weather = CloudWeather(p, windOffset, camXZ, lod, warp);

                float3 uvw = float3(frac(p.x * scale) + windOffset.x + warp.x,
                                    (p.y - slabBaseY) * scale,
                                    frac(p.z * scale) + windOffset.y + warp.y);
                float4 n = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex, uvw, lod);

                // Second sample at ~3x scale, offset half a tile: adds
                // mid-scale shape wobble and finer erosion detail so the
                // surface doesn't read as one-size round blobs. +2 mips:
                // it runs ~3x finer, so it must fade out two levels earlier.
                float4 n2 = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex,
                                                 uvw * 2.9 + float3(0.37, 0.11, 0.53),
                                                 min(lod + 2.0, 7.0));

                // Coverage remap with a SOFT threshold so silhouettes stay
                // organic. n2 wobbles the base field for richer contours.
                // 覆盖度不再是全屏一个常数：天气图在局部把它上下推。
                // 均值仍等于美术调的那个值，所以滑块的语义没变。
                float coverage = saturate(_CloudShapeParams.x
                                        + (weather - 0.5) * _CloudWeatherParams.z);

                // Detail erosion from the Worley channels (both scales),
                // applied to the RAW field BEFORE the threshold smoothstep.
                // Eroding after the threshold carved near-discontinuous
                // cliffs that the march aliased into comb-stripe banding.
                float detail = n.g * 0.5 + n.b * 0.3 + n.a * 0.12 + n2.g * 0.08;
                float edgeWeight = smoothstep(0.75, 0.35, n.r); // erode where the field is thin
                // Erosion and fine wobble are high-frequency by nature -
                // fade them out with LOD so distant clouds keep a clean
                // silhouette instead of aliased, eroded speckle.
                float hfFade = saturate(1.0 - lod * 0.2);
                float r = saturate(n.r + (n2.r - 0.5) * 0.18 * hfFade)
                        - detail * _CloudMarchParams.w * 0.6
                              * edgeWeight * (0.4 + 0.6 * h01) * hfFade;

                // 取 mip 的那一档会把场往均值 0.5 拉：阈值固定时，远处的云
                // 会因为"被抹平了所以过不了阈值"而整片消失（这正是之前
                // 天空空掉的原因之一）。按 lod 把偏离量放大回去，让任意
                // 距离上通过阈值的体素比例大致不变。
                // 补偿要有上限：不封顶的话 lod 6 会把一个已经糊掉的场放大 3 倍，
                // 地平线附近（一步跨几百米）直接碎成盐粒。到 lod 4 就收手。
                float boost = lerp(1.0, 1.8, saturate(lod * 0.25));
                r = 0.5 + (r - 0.5) * boost;

                // 阈值带也跟着放宽：远处体素本来就分辨不出边界，
                // 硬阈值会把残余噪声读成一圈锯齿边。
                float soft = 0.14 + lod * 0.045;
                float d = smoothstep(1.0 - coverage, 1.0 - coverage + soft, r);
                outR = r;

                // Height profile: gentle ramp-in over the bottom of the
                // slab, wispy top fade. Both ramps WIDEN when a march step
                // spans a large slab fraction: a ramp narrower than ~2
                // steps is sampled discontinuously, which is exactly the
                // horizontal terracing seen on distant cloud undersides.
                // Near steps are tiny, so the authored 0.22/0.20 ramps
                // survive unchanged up close.
                // 积云要"平底圆顶"：底部剖面收得陡，才有那块平的底座；
                // 0.22 会把底也摊圆，云就成了一团没性格的棉花。
                float pad   = stepFrac * 2.0;
                float rampB = max(0.12, pad);
                float rampT = max(0.20, pad);
                // 天气弱的地方只留一层摊平的薄云（顶部提前收），天气强的
                // 地方才允许云向上长厚 —— 否则无论天气图怎么变，云的竖向
                // 形状都一样，就白叠了。
                float topW = max(lerp(rampT * 2.6, rampT, weather), pad);
                float hg = smoothstep(0.0, rampB, h01)
                         * (1.0 - smoothstep(1.0 - topW, 1.0, h01));

                outHg = hg;
                return d * hg;
            }

            // 6 参数版：调试视图不需要中间量，HLSL 没有前置声明，
            // 所以这个包装必须排在带 out 参数的定义之后。
                        float CloudDensity(float3 p, float h01, float2 windOffset, float2 camXZ, float lod, float stepFrac)
            {
                float rOut, hgOut;
                return CloudDensity(p, h01, windOffset, camXZ, lod, stepFrac, rOut, hgOut);
            }

            // ----------------------------------------------------------------
            // Light march towards the sun: Beer transmittance through the
            // slab. Only a few fixed steps - stylised clouds forgive a lot.
            // ----------------------------------------------------------------
            float LightTransmittance(float3 p, float3 toSun, float baseY, float thickness, float2 windOffset, float2 camXZ, float lod)
            {
                int   lightSteps = (int)clamp(_CloudMarchParams.z, 2.0, 8.0);
                float ls = thickness * 0.05;  // fine steps: coarse light march banded

                float acc = 0.0;
                [loop] for (int i = 1; i <= 8; i++)
                {
                    if (i > lightSteps) break;
                    float3 sp = p + toSun * (ls * (float)i);
                    // 世界弯曲：光照步进的云底同样随水平距离下垂（弱化+钳制）
                    float h = saturate((sp.y - (baseY - CloudBendDroop(sp.xz, camXZ))) / thickness);
                    acc += CloudDensity(sp, h, windOffset, camXZ, lod, 0.05) * ls;
                }
                return exp(-acc * _CloudMarchParams.x * 0.035);
            }

            // ----------------------------------------------------------------
            // Fragment: ray-slab intersection + march + composite.
            // Output = premultiplied cloud colour (rgb) and coverage (a);
            // the skybox pass composites with result*(1-a) + rgb.
            // ----------------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                // Reconstruct the world-space view ray (same as the skybox).
                float2 ndc = (input.positionCS.xy / _CloudRTSize.xy) * 2.0 - 1.0;
                ndc.y = -ndc.y;
                float4 unprojected = mul(_CartoonInvVP, float4(ndc, 0.0, 1.0));
                float3 camPos  = _WorldSpaceCameraPos.xyz;
                float3 viewDir = normalize(unprojected.xyz / unprojected.w - camPos);

                // Rays near the horizon see no volumetric layer (the flat
                // billboard clouds in the skybox own that band). abs() so the
                // same holds when the camera is ABOVE the slab looking down.
                float upness = abs(viewDir.y);
                // 地平线带交给空气透视去收，而不是靠这个 8° 的硬窗口：
                // 原来 smoothstep(0.02,0.14) 会把 0~8° 整个挖空，而平流云层
                // 通常是关着的，于是天上出现一条"没有任何云"的带子。
                // 低仰角的射线 tStart 本来就在 12km 以外，下面的 aerial
                // perspective 会把它整段淡出，不需要在这里再切一刀。
                float horizonFade = smoothstep(0.010, 0.075, upness);
                if (horizonFade <= 0.001 && _CloudDebug <= 0.5)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // WORLD-ANCHORED slab: absolute heights, independent of the
                // camera. A camera-relative slab made the whole cloud layer
                // swim with every camera move.
                float baseY     = _CloudShapeParams.y;
                float thickness = max(_CloudShapeParams.z, 1.0);
                float topY      = baseY + thickness;

                // Ray-slab intersection, order-agnostic: works when the
                // camera is below, inside, or above the slab.
                const float kMaxDist = 24000.0;
                float tA = (baseY - camPos.y) / viewDir.y;
                float tB = (topY  - camPos.y) / viewDir.y;

                // 世界弯曲联动：远处云层随“地面”下垂，云底变为
                // baseY - k·d²（d = 到相机的水平距离）。按射线到达平板层
                // 远端的水平距离估算下垂量，把相交底平面同步下移，
                // 防止下垂后的云被平直底面裁掉。
                float droopFar  = min(WorldBendOffsetFrom(camPos.xz + viewDir.xz * max(max(tA, tB), 0.0), camPos.xz)
                                      * _WorldBendCloudScale, _WorldBendCloudMaxDroop);
                float bentBaseY = baseY - droopFar;
                if (droopFar > 0.0) tA = (bentBaseY - camPos.y) / viewDir.y;

                float tStart = max(min(tA, tB), 0.0);
                // Aerial perspective kills everything beyond ~16 cloud
                // altitudes (see the alpha fade at the bottom of Frag), so
                // clamp the march exactly there: steps spent in fully faded
                // space are invisible, and concentrating the FIXED step
                // budget inside the visible range shrinks stepLen - the
                // direct cause of far-field slice banding (48 steps spread
                // over ~20km undersample the ~30m noise texels badly).
                float tEnd   = min(max(tA, tB), min(kMaxDist, baseY * 16.0));
                // 调试模式下"射线没穿过云层"本身就是要看的结果，画成纯红，
                // 不要和"穿过了但密度为 0"（Coverage 视图的纯黑）混成一回事。
                if (tEnd <= tStart)
                    return _CloudDebug > 0.5 ? half4(1.0, 0.0, 0.0, 1.0)
                                             : half4(0.0, 0.0, 0.0, 0.0);

                int   marchSteps = (int)clamp(_CloudMarchParams.y, 8.0, 48.0);
                float stepLen    = (tEnd - tStart) / (float)marchSteps;
                // Extinction coefficient: scaled by 1/thickness so that a
                // fully dense slab ends up with optical depth ~8 (alpha~1)
                // while thin wisps stay translucent. Without this rescale,
                // raw stepLen * absorption is ~48 per step and the first
                // speck of density saturates to a hard opaque mask.
                float sigma = _CloudMarchParams.x * 8.0 / thickness;

                // Wind drift in noise-tile units (Repeat wrap handles the
                // unbounded magnitude, but frac keeps UVs friendly).
                float windPhase = frac(_Time.y * _CloudWind.x * _CloudShapeParams.w);
                float2 windOffset = normalize(_CloudWind.yz + float2(1e-4, 0.0))
                                  * windPhase;

                float3 toSun   = -normalize(_CartoonSunDirection.xyz);
                float3 sunCol  = SunLitColor();
                float  viewSun = saturate(dot(viewDir, toSun));

                // March-start jitter, ANIMATED per frame with a proper
                // per-pixel integer hash - but ONLY when temporal
                // accumulation is active (_CloudHistoryWeight > 0 covers
                // both the asset toggle and non-game cameras): without
                // history to average it, an animated jitter reads as
                // flickering grain, so the hash freezes to a stable
                // per-pixel pattern instead.
                uint frameTerm = _CloudHistoryWeight > 0.001
                               ? (uint)(_Time.y * 61.0) : 0u;
                uint jh = (uint)input.positionCS.x * 1664525u
                        ^ (uint)input.positionCS.y * 1013904223u
                        ^ frameTerm * 22695477u;
                jh ^= jh >> 16; jh *= 2246822519u; jh ^= jh >> 13;
                float jitter = ((float)(jh & 0xFFFFFFu) / 16777216.0 - 0.5) * stepLen * 0.9;

                // Noise footprint for LOD selection: the 128^3 tile spans
                // 1/scale world metres, so one texel covers texelWorld m.
                float texelWorld = 1.0 / max(_CloudShapeParams.w * 128.0, 1e-5);
                // March undersampling term, Nyquist-biased: pick the mip
                // whose texel is ~2x the step length, so every noise
                // feature is sampled at least twice. Critical sampling
                // (texel == stepLen) still stairs the density field.
                float lodStep    = log2(max(stepLen * 2.0 / texelWorld, 1.0));

                // March front-to-back with early-out.
                float3 col = 0.0;
                float  T   = 1.0;
                [loop] for (int s = 0; s < 48; s++)
                {
                    if (s >= marchSteps) break;
                    float  t   = tStart + jitter + ((float)s + 0.5) * stepLen;
                    float3 p   = camPos + viewDir * t;
                    // 世界弯曲：按采样点的水平距离求云底下垂量（弱化+钳制）
                    float  h01 = saturate((p.y - (baseY - CloudBendDroop(p.xz, camPos.xz))) / thickness);
                    // Pixel-footprint undersampling term: at half-res one
                    // RT texel is ~2 screen px, so its world size at
                    // distance t is ~2t/RTHeight. Order-of-magnitude LOD
                    // is all we need - no exact FOV required.
                    float lodPix = log2(max(t * 2.0 / (_CloudRTSize.y * texelWorld), 1.0));
                    float lod    = clamp(max(lodStep, lodPix), 0.0, 7.0);
                    float  dn  = CloudDensity(p, h01, windOffset, camPos.xz, lod, stepLen / thickness);
                    if (dn <= 0.003) continue;

                    // Beer towards the sun + Powder edge term. Lighting stays
                    // SMOOTH per slice - quantising here banded every slice
                    // into stripes. Cel banding is applied once after the
                    // march instead (see below).
                    float lt     = LightTransmittance(p, toSun, baseY, thickness, windOffset, camPos.xz, lod);
                    float powder = 1.0 - exp(-dn * stepLen * sigma * 2.0);
                    float lightE = lt * lerp(0.6, 1.0, powder);

                    // Ambient is attenuated by the sun-ward transmittance:
                    // deep inside the cloud the sky light is occluded too,
                    // so cores go grey while rims stay bright.
                    float3 S = AmbientAtHeight(h01) * (0.35 + 0.65 * lt)
                             + sunCol * lightE;

                    // Silver lining - back-lit golden edge towards the sun.
                    // 银边必须按"这个体素到底有多少云"门控：薄边缘上 lt≈1、
                    // 又正好是背光方向，不门控就会在轮廓外沿撒一圈白色盐粒
                    // （时间累积收敛之后依然存在，不是抖动，是真实的错）。
                    float silver = pow(viewSun, _CloudShadeParams.w)
                                 * _CloudShadeParams.z * lt
                                 * saturate(dn * 6.0);
                    S += sunCol * silver;

                    // Square the density for a soft extinction onset: thin
                    // edge regions accumulate gradually (fewer bands, less
                    // dither contrast), dense cores still saturate via the
                    // compensating 2.2x factor.
                    float a = 1.0 - exp(-dn * dn * stepLen * sigma * 2.2);
                    col += T * S * a;
                    T   *= (1.0 - a);
                    if (T < 0.02) break;
                }

                // Stylised cel banding: quantise the ACCUMULATED intrinsic
                // cloud brightness once (not per slice). Yields 2-3 clean
                // shading zones per cloud (white crown / grey base) instead
                // of per-slice stripes. steps <= 1.5 disables it.
                // ---- 调试视图 ------------------------------------------------
                // 在云层中点重新取一次样，把密度模型的各个中间量画出来。
                // 直接 return，不走 temporal 混合（否则历史会把调试色洗淡）。
                if (_CloudDebug > 0.5)
                {
                    float  tm    = lerp(tStart, tEnd, 0.5);
                    float3 pd    = camPos + viewDir * tm;
                    float  h01d  = saturate((pd.y - (baseY - CloudBendDroop(pd.xz, camPos.xz))) / thickness);
                    float  rr, hg;
                    float  lodProbe = clamp(max(lodStep,
                        log2(max(tm * 2.0 / (_CloudRTSize.y * texelWorld), 1.0))), 0.0, 7.0);
                    float2 probeWarp;
                    float  weatherAt = CloudWeather(pd, windOffset, camPos.xz, lodProbe, probeWarp);
                    CloudDensity(pd, h01d, windOffset, camPos.xz, lodProbe,
                                 stepLen / thickness, rr, hg);
                    float3 dbg = 0.0;
                    if (_CloudDebug < 1.5)      dbg = (1.0 - T);                       // 累积覆盖度
                    else if (_CloudDebug < 2.5) dbg = saturate(tStart / kMaxDist * 8.0) // 区间起点
                                                  + float3(0.0, saturate(tEnd / kMaxDist * 8.0), 0.0);
                    else if (_CloudDebug < 3.5) dbg = saturate(rr);                     // 原始噪声场
                    else if (_CloudDebug < 4.5) dbg = saturate(hg);                     // 高度剖面
                    else if (_CloudDebug < 5.5) dbg = saturate(clamp(max(lodStep, 0.0), 0.0, 7.0) / 7.0); // mip
                    else                        dbg = saturate(weatherAt);              // 天气场
                    return half4(dbg, 1.0);
                }

                float coverageA = 1.0 - T;
                if (_CloudShadeParams.x > 1.5 && coverageA > 1e-3)
                {
                    float lumI = dot(col, float3(0.299, 0.587, 0.114)) / coverageA;
                    float q = SoftQuantizeSteps(lumI, _CloudShadeParams.x, _CloudShadeParams.y);
                    col *= q / max(lumI, 1e-4);
                }

                float alpha = (1.0 - T) * horizonFade;
                // Aerial perspective: distant clouds dissolve into the sky
                // haze instead of staying crisp white specks down to a hard
                // horizon line. Fade range scales with cloud altitude.
                alpha *= 1.0 - smoothstep(baseY * 5.0, baseY * 16.0, tStart);
                // Cull ultra-faint veil texels: at half resolution they read
                // as dither speckle along silhouettes, not as soft haze.
                alpha = smoothstep(0.05, 0.4, alpha);
                half4 cur = half4(col * horizonFade, alpha);

                // ---- Temporal accumulation ---------------------------------
                // Reproject this pixel's slab anchor (ray x slab mid-plane)
                // into the previous frame and blend with the history buffer.
                // Combined with the animated march jitter, banding and
                // dither converge away within a few frames while static
                // views stay sharp. Weight 0 (scene view, first frame, or
                // temporal disabled) skips the blend.
                if (_CloudHistoryWeight > 0.001)
                {
                    float tAnchor = (baseY + thickness * 0.5 - camPos.y) / viewDir.y;
                    if (tAnchor <= 0.0) tAnchor = baseY * 8.0;
                    tAnchor = min(tAnchor, kMaxDist);
                    float3 anchorW = camPos + viewDir * tAnchor;
                    float4 pc = mul(_CloudPrevVP, float4(anchorW, 1.0));
                    // Y is negated to mirror the unprojection convention
                    // used at the top of this shader (GPU projection flips
                    // clip-space Y, and the inverse path pre-negates ndc.y).
                    float2 prevUV = float2(pc.x, -pc.y) / max(pc.w, 1e-4) * 0.5 + 0.5;
                    if (pc.w > 0.0 && all(prevUV > 0.002) && all(prevUV < 0.998))
                    {
                        float4 hist = SAMPLE_TEXTURE2D(_CloudHistoryTex,
                                                       sampler_CloudHistoryTex, prevUV);
                        cur = lerp(cur, half4(hist), _CloudHistoryWeight);
                    }
                }
                return cur;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
