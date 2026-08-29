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
//   2. Inside the slab the ray is marched in N steps. Density comes from
//      low-frequency FBM shaped by a cumulus height profile (flat bottom,
//      round top), remapped by a coverage parameter and eroded by
//      high-frequency detail noise near the cloud edges.
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

            // Tileable 64^3 Perlin-Worley volume baked by
            // CloudNoiseTextureBaker (R = base shape, GBA = worley detail).
            TEXTURE3D(_CloudNoiseTex); SAMPLER(sampler_CloudNoiseTex);

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
            float CloudDensity(float3 p, float h01, float2 windOffset, float2 camXZ)
            {
                // Sample the tileable Perlin-Worley volume. The pattern is
                // RE-CENTRED on the camera horizontally so clouds always
                // surround the viewer no matter how far they travel, while
                // the slab height stays absolute so the layer never swims
                // when the camera moves or rotates. Repeat wrap means no
                // float32 precision collapse either.
                // ISOTROPIC world-scale sampling: the Y axis uses the same
                // metres-per-tile as XZ, so noise cells are ROUND blobs
                // instead of vertically squashed pancakes (mapping the slab
                // onto the full 0..1 tile made cells ~4x flatter than wide).
                float scale = _CloudShapeParams.w;
                float slabBaseY = _CloudShapeParams.y;
                float3 uvw = float3((p.x - camXZ.x) * scale + windOffset.x,
                                    (p.y - slabBaseY) * scale,
                                    (p.z - camXZ.y) * scale + windOffset.y);
                float4 n = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex, uvw, 0);

                // Second sample at ~3x scale, offset half a tile: adds
                // mid-scale shape wobble and finer erosion detail so the
                // surface doesn't read as one-size round blobs.
                float4 n2 = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex,
                                                 uvw * 2.9 + float3(0.37, 0.11, 0.53), 0);

                // Coverage remap with a SOFT threshold so silhouettes stay
                // organic. n2 wobbles the base field for richer contours.
                float coverage = saturate(_CloudShapeParams.x);

                // Detail erosion from the Worley channels (both scales),
                // applied to the RAW field BEFORE the threshold smoothstep.
                // Eroding after the threshold carved near-discontinuous
                // cliffs that the march aliased into comb-stripe banding.
                float detail = n.g * 0.5 + n.b * 0.3 + n.a * 0.12 + n2.g * 0.08;
                float edgeWeight = smoothstep(0.75, 0.35, n.r); // erode where the field is thin
                float r = saturate(n.r + (n2.r - 0.5) * 0.18)
                        - detail * _CloudMarchParams.w * 0.6
                              * edgeWeight * (0.4 + 0.6 * h01);

                float d = smoothstep(1.0 - coverage, 1.0 - coverage + 0.2, r);

                // Height profile: gentle ramp-in over the bottom fifth of
                // the slab (a steep ramp was the source of the horizontal
                // luminance bands on cloud undersides), wispy top fade.
                float hg = smoothstep(0.0, 0.22, h01)
                         * (1.0 - smoothstep(0.8, 1.0, h01));

                return d * hg;
            }

            // ----------------------------------------------------------------
            // Light march towards the sun: Beer transmittance through the
            // slab. Only a few fixed steps - stylised clouds forgive a lot.
            // ----------------------------------------------------------------
            float LightTransmittance(float3 p, float3 toSun, float baseY, float thickness, float2 windOffset, float2 camXZ)
            {
                int   lightSteps = (int)clamp(_CloudMarchParams.z, 2.0, 8.0);
                float ls = thickness * 0.05;  // fine steps: coarse light march banded

                float acc = 0.0;
                [loop] for (int i = 1; i <= 8; i++)
                {
                    if (i > lightSteps) break;
                    float3 sp = p + toSun * (ls * (float)i);
                    float h = saturate((sp.y - baseY) / thickness);
                    acc += CloudDensity(sp, h, windOffset, camXZ) * ls;
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
                float horizonFade = smoothstep(0.02, 0.14, upness);
                if (horizonFade <= 0.001)
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
                float tStart = max(min(tA, tB), 0.0);
                // Aerial perspective: nothing beyond ~16 cloud-altitudes
                // contributes (the distance fade below kills it anyway) -
                // clamp the march so far grazing rays don't waste steps.
                float tEnd   = min(max(tA, tB), min(kMaxDist, baseY * 20.0));
                if (tEnd <= tStart)
                    return half4(0.0, 0.0, 0.0, 0.0);

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
                // per-pixel integer hash. (IGN + phase shift only translates
                // the fixed diagonal pattern, whose spatial correlation
                // survives temporal averaging; an LCG/xorshift hash keyed by
                // pixel AND frame decorrelates fully, so the temporal blend
                // converges to a clean mean.)
                uint jh = (uint)input.positionCS.x * 1664525u
                        ^ (uint)input.positionCS.y * 1013904223u
                        ^ (uint)(_Time.y * 61.0) * 22695477u;
                jh ^= jh >> 16; jh *= 2246822519u; jh ^= jh >> 13;
                float jitter = ((float)(jh & 0xFFFFFFu) / 16777216.0 - 0.5) * stepLen * 0.45;

                // March front-to-back with early-out.
                float3 col = 0.0;
                float  T   = 1.0;
                [loop] for (int s = 0; s < 48; s++)
                {
                    if (s >= marchSteps) break;
                    float  t   = tStart + jitter + ((float)s + 0.5) * stepLen;
                    float3 p   = camPos + viewDir * t;
                    float  h01 = saturate((p.y - baseY) / thickness);
                    float  dn  = CloudDensity(p, h01, windOffset, camPos.xz);
                    if (dn <= 0.003) continue;

                    // Beer towards the sun + Powder edge term. Lighting stays
                    // SMOOTH per slice - quantising here banded every slice
                    // into stripes. Cel banding is applied once after the
                    // march instead (see below).
                    float lt     = LightTransmittance(p, toSun, baseY, thickness, windOffset, camPos.xz);
                    float powder = 1.0 - exp(-dn * stepLen * sigma * 2.0);
                    float lightE = lt * lerp(0.6, 1.0, powder);

                    // Ambient is attenuated by the sun-ward transmittance:
                    // deep inside the cloud the sky light is occluded too,
                    // so cores go grey while rims stay bright.
                    float3 S = AmbientAtHeight(h01) * (0.35 + 0.65 * lt)
                             + sunCol * lightE;

                    // Silver lining - back-lit golden edge towards the sun.
                    float silver = pow(viewSun, _CloudShadeParams.w)
                                 * _CloudShadeParams.z * lt;
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
                alpha = smoothstep(0.02, 0.4, alpha);
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
