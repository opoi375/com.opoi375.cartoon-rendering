// ============================================================================
// CartoonSkyboxFullscreen.shader
// ----------------------------------------------------------------------------
// Cartoon procedural sky rendered as a FULL-SCREEN TRIANGLE. The view
// direction of every pixel is reconstructed analytically from its NDC
// position through the inverse view-projection matrix, so the gradient is
// per-pixel exact - there is NO mesh interpolation and therefore none of
// the banding / triangle-edge discontinuities that a cube-mesh skybox
// produces when the camera rotates.
//
// Rendered by CartoonSkyboxFeature (ScriptableRendererFeature) which also
// uploads _CartoonInvVP every frame.
// ============================================================================
Shader "CartoonRendering/CartoonSkyboxFullscreen"
{
    Properties
    {
        [Header(Sun)]
        _CartoonSunDirection        ("Sun Direction (read only)", Vector) = (0, -1, 0, 0)
        _CartoonTimeOfDay           ("Time Of Day", Range(0, 1))          = 0.5
        [HDR] _CartoonSunColor      ("Sun Color", Color)                  = (1, 0.95, 0.85, 1)
        _CartoonSunParams           ("Sun Params (size, inner, outer)", Vector) = (0.04, 0.2, 0.8, 0)
        _CartoonIncludeSunDisc      ("Include Sun Disc", Range(0, 1))     = 1

        [Header(Stars)]
        _CartoonStarColor           ("Star Color", Color)                 = (0.9, 0.95, 1, 1)
        _CartoonStarParams          ("Star Params (intensity, size, scale, density)", Vector) = (1.5, 0.5, 1, 0.5)
        _CartoonHasStarMap          ("Use Star Map", Range(0, 1))         = 0
        [NoScaleOffset] _CartoonStarMap ("Star Noise Map", 2D)            = "white" {}

        [Header(Clouds)]
        _CartoonCloudColor          ("Cloud Color", Color)                = (1, 1, 1, 1)
        _CartoonCloudParams         ("Cloud Params (height, threshold, scale, intensity)", Vector) = (60, 0.45, 1, 1)
        _CartoonHasCloudMap         ("Use Cloud Map", Range(0, 1))        = 0
        [NoScaleOffset] _CartoonCloudMap ("Cloud Map", 2D)                = "white" {}

        [Header(Gradient Day)]
        _CartoonTopColor            ("Top Color", Color)                  = (0.15, 0.35, 0.75, 1)
        _CartoonMiddleColor         ("Middle Color", Color)               = (0.55, 0.75, 0.95, 1)
        _CartoonBottomColor         ("Bottom Color", Color)               = (0.85, 0.75, 0.60, 1)
        _CartoonHorizonColor        ("Horizon Color", Color)              = (0.95, 0.80, 0.65, 1)
        _CartoonBackgroundColor     ("Background Color", Color)           = (0.05, 0.10, 0.20, 1)

        [Header(Gradient Sunset)]
        _CartoonSunsetTopColor      ("Sunset Top", Color)                 = (0.20, 0.15, 0.40, 1)
        _CartoonSunsetMiddleColor   ("Sunset Middle", Color)              = (0.75, 0.35, 0.25, 1)
        _CartoonSunsetHorizonColor  ("Sunset Horizon", Color)             = (1.0, 0.45, 0.20, 1)

        [Header(Gradient Night)]
        _CartoonNightTopColor       ("Night Top", Color)                  = (0.01, 0.02, 0.06, 1)
        _CartoonNightMiddleColor    ("Night Middle", Color)               = (0.03, 0.05, 0.12, 1)
        _CartoonNightHorizonColor   ("Night Horizon", Color)              = (0.06, 0.08, 0.16, 1)
        _CartoonNightBackgroundColor("Night Background", Color)           = (0.01, 0.01, 0.03, 1)

        _CartoonBlendParams         ("Blend (start, end)", Vector)        = (0.05, 0.35, 0, 0)
        _CartoonGradientSoftness    ("Gradient Softness", Range(0, 1))    = 0.25
        _CartoonIntensity           ("Intensity", Range(0, 10))           = 1
        _CartoonHasGradientRamp     ("Has Gradient Ramp", Range(0, 1))    = 0
        [NoScaleOffset] _CartoonGradientRamp ("Gradient Ramp", 2D)        = "white" {}
    }

    SubShader
    {
        Tags
        {
            "Queue"           = "Background"
            "RenderType"      = "Background"
            "PreviewType"     = "Skybox"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CartoonSkyboxFullscreen"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ----------------------------------------------------------------
            // Uniforms
            // ----------------------------------------------------------------
            // Inverse view-projection matrix, uploaded per frame by
            // CartoonSkyboxFeature. Used to un-project each pixel's NDC
            // position into a world-space view direction.
            float4x4 _CartoonInvVP;

            float4 _CartoonSunDirection;
            float  _CartoonTimeOfDay;
            float4 _CartoonSunColor;
            float4 _CartoonSunParams;      // x=size, y=inner, z=outer

            float4 _CartoonStarColor;
            float4 _CartoonStarParams;     // x=intensity, y=size, z=scale, w=density
            TEXTURE2D(_CartoonStarMap); SAMPLER(sampler_CartoonStarMap);
            float  _CartoonHasStarMap;

            // Bill-board style clouds projected onto a virtual plane.
            float4 _CartoonCloudColor;
            float4 _CartoonCloudParams;    // x=plane height, y=threshold, z=scale, w=intensity
            TEXTURE2D(_CartoonCloudMap); SAMPLER(sampler_CartoonCloudMap);
            float  _CartoonHasCloudMap;

            // Half-resolution volumetric cloud layer rendered by
            // CartoonVolumetricClouds (premultiplied rgb + coverage a).
            // Bound globally per frame by CartoonSkyboxFeature.
            TEXTURE2D(_CartoonVolumetricCloudRT); SAMPLER(sampler_CartoonVolumetricCloudRT);
            float  _CartoonHasVolumetricClouds;

            float4 _CartoonTopColor;
            float4 _CartoonMiddleColor;
            float4 _CartoonBottomColor;
            float4 _CartoonHorizonColor;
            float4 _CartoonBackgroundColor;

            // Sunset palette (sun near the horizon).
            float4 _CartoonSunsetTopColor;
            float4 _CartoonSunsetMiddleColor;
            float4 _CartoonSunsetHorizonColor;

            // Night palette (sun below the horizon).
            float4 _CartoonNightTopColor;
            float4 _CartoonNightMiddleColor;
            float4 _CartoonNightHorizonColor;
            float4 _CartoonNightBackgroundColor;

            // x = sunsetBlendStart, y = dayBlendEnd (sun elevation 0..1).
            float4 _CartoonBlendParams;

            float  _CartoonGradientSoftness;
            float  _CartoonIntensity;
            float  _CartoonIncludeSunDisc;
            float  _CartoonHasGradientRamp;
            TEXTURE2D(_CartoonGradientRamp); SAMPLER(sampler_CartoonGradientRamp);

            // ----------------------------------------------------------------
            // Sun elevation helpers. _CartoonSunDirection stores the LIGHT
            // direction (from the sun towards the scene), so the sun's own
            // direction in the sky is -_CartoonSunDirection and its
            // elevation is -_CartoonSunDirection.y.
            // ----------------------------------------------------------------
            float GetSunElevation()
            {
                return -normalize(_CartoonSunDirection.xyz).y;
            }

            // Fast pseudo-random helper used by the starfield.
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            // Full-screen triangle (no vertex buffer needed):
            //   v0 = (-1, -1), v1 = (3, -1), v2 = (-1, 3)  in clip space.
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
            // Sky gradient - sequential blend so each colour stop fully
            // dominates its own band. Every colour stop is itself a blend of
            // the night / sunset / day palettes driven by the sun elevation
            // derived from the directional light:
            //   sun high   -> day palette    (blue sky)
            //   sun low    -> sunset palette (orange-red horizon)
            //   sun below  -> night palette  (near black)
            // ----------------------------------------------------------------
            float3 BlendPalette(float3 nightC, float3 sunsetC, float3 dayC)
            {
                float sunY = GetSunElevation();
                float start = _CartoonBlendParams.x;
                float end   = max(_CartoonBlendParams.y, start + 0.05);
                // night -> sunset as the sun approaches the horizon.
                float wSunset = smoothstep(start - 0.20, start, sunY);
                // sunset -> day as the sun climbs.
                float wDay    = smoothstep(start, end, sunY);
                return lerp(lerp(nightC, sunsetC, wSunset), dayC, wDay);
            }

            float3 EvaluateSkyGradient(float vertical)
            {
                float soft = clamp(_CartoonGradientSoftness, 0.02, 0.5);

                // Palette-blended colour stops.
                float3 topCol     = BlendPalette(_CartoonNightTopColor.rgb,
                                                 _CartoonSunsetTopColor.rgb,
                                                 _CartoonTopColor.rgb);
                float3 middleCol  = BlendPalette(_CartoonNightMiddleColor.rgb,
                                                 _CartoonSunsetMiddleColor.rgb,
                                                 _CartoonMiddleColor.rgb);
                float3 horizonCol = BlendPalette(_CartoonNightHorizonColor.rgb,
                                                 _CartoonSunsetHorizonColor.rgb,
                                                 _CartoonHorizonColor.rgb);
                float3 bottomCol  = BlendPalette(_CartoonNightBackgroundColor.rgb,
                                                 _CartoonSunsetHorizonColor.rgb * 0.6,
                                                 _CartoonBottomColor.rgb);
                float3 backCol    = BlendPalette(_CartoonNightBackgroundColor.rgb,
                                                 _CartoonSunsetHorizonColor.rgb * 0.35,
                                                 _CartoonBackgroundColor.rgb);

                float3 c;
                if (vertical >= 0.5)
                {
                    // 0 at horizon, 1 at zenith.
                    float t = saturate((vertical - 0.5) * 2.0);
                    c = lerp(horizonCol, middleCol, smoothstep(0.0, soft * 2.0, t));
                    c = lerp(c, topCol, smoothstep(1.0 - soft * 2.5, 1.0, t));
                }
                else
                {
                    // 0 at horizon, 1 at nadir.
                    float t = saturate((0.5 - vertical) * 2.0);
                    c = lerp(horizonCol, bottomCol, smoothstep(0.0, soft * 2.0, t));
                    c = lerp(c, backCol, smoothstep(1.0 - soft * 2.5, 1.0, t));
                }

                if (_CartoonHasGradientRamp > 0.5)
                {
                    float3 rampCol = SAMPLE_TEXTURE2D(_CartoonGradientRamp, sampler_CartoonGradientRamp,
                                                     float2(0.5, vertical)).rgb;
                    c = rampCol;
                }
                return c;
            }

            // ----------------------------------------------------------------
            // Sun disc - only visible while the sun is above the horizon;
            // tinted orange-red as it approaches the horizon.
            // ----------------------------------------------------------------
            float3 GetSunDiscColor()
            {
                float sunY = GetSunElevation();
                // Warm tint ramps in below ~20 degrees elevation.
                float warm = 1.0 - smoothstep(0.0, 0.35, sunY);
                float3 warmCol = float3(1.0, 0.45, 0.20);
                return lerp(_CartoonSunColor.rgb, warmCol * _CartoonSunColor.a, warm);
            }

            float EvaluateSunDisc(float3 viewDirWS)
            {
                if (_CartoonIncludeSunDisc < 0.5)
                    return 0.0;

                float3 sunDir = normalize(_CartoonSunDirection.xyz);
                float cosTheta = saturate(dot(viewDirWS, -sunDir));
                float dist = 1.0 - cosTheta;
                float area = 1.0 - dist / max(_CartoonSunParams.x, 1e-4);
                area = smoothstep(_CartoonSunParams.y, _CartoonSunParams.z, area);

                // Fade the disc out as the sun sinks below the horizon.
                float sunVis = smoothstep(-0.06, 0.02, GetSunElevation());

                return area * sunVis;
            }

            // ----------------------------------------------------------------
            // Stars - only visible while the sun is below the horizon.
            // Rendered as round noise-sampled points (never rectangles):
            // each grid cell receives a jittered point whose brightness
            // falls off radially. An optional noise texture can override
            // the procedural distribution.
            // ----------------------------------------------------------------
            float3 EvaluateStars(float3 viewDirWS)
            {
                // Night mask: 1 when the sun is well below the horizon.
                float starTimeMask = smoothstep(0.05, -0.12, GetSunElevation());
                if (starTimeMask <= 0.0)
                    return 0.0;

                float starUpMask = smoothstep(-0.05, 0.25, viewDirWS.y);

                float3 dir = normalize(viewDirWS);
                float u = atan2(dir.z, dir.x) * 0.1591549 + 0.5;
                float v = asin(clamp(dir.y, -1.0, 1.0)) * 0.3183099 + 0.5;
                float2 uv = float2(u, v);

                float star;
                if (_CartoonHasStarMap > 0.5)
                {
                    // Texture mode: sample the noise map and threshold it so
                    // only the brightest cells become stars.
                    float n = SAMPLE_TEXTURE2D_LOD(_CartoonStarMap, sampler_CartoonStarMap,
                                                   uv * _CartoonStarParams.z, 0).r;
                    star = smoothstep(0.75, 0.98, n);
                }
                else
                {
                    // Procedural mode: hash-sampled points inside a lattice.
                    float2 gridUV = uv * 160.0 * _CartoonStarParams.z;
                    float2 cell = floor(gridUV);
                    float2 f = frac(gridUV) - 0.5;

                    // Density: only a fraction of cells actually host a star.
                    float densityRnd = Hash21(cell);
                    float exists = step(1.0 - saturate(_CartoonStarParams.w), densityRnd);

                    // Jitter the star centre inside its cell.
                    float2 starPos = float2(Hash21(cell + 7.13), Hash21(cell + 3.71)) - 0.5;
                    starPos *= 0.6;

                    // Radial falloff -> round point.
                    float d = length(f - starPos);
                    float size = (0.05 + 0.12 * Hash21(cell + 11.37))
                                 * (0.5 + _CartoonStarParams.y);
                    star = exists * smoothstep(size, size * 0.25, d);

                    // Per-star brightness variation.
                    star *= 0.5 + 0.5 * Hash21(cell + 17.77);
                }

                return _CartoonStarColor.rgb * star * starUpMask * starTimeMask
                       * _CartoonStarParams.x;
            }

            // ----------------------------------------------------------------
            // Bill-board style clouds: the view ray is intersected with a
            // virtual horizontal plane and the cloud texture (or procedural
            // FBM noise when no texture is assigned) is sampled there.
            // ----------------------------------------------------------------
            float ValueNoise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 s = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, s.x), lerp(c, d, s.x), s.y);
            }

            float CloudFBM(float2 p)
            {
                float n = 0.0;
                n += 0.55 * ValueNoise2D(p);
                n += 0.28 * ValueNoise2D(p * 2.13 + 11.7);
                n += 0.17 * ValueNoise2D(p * 4.41 + 47.3);
                return n;
            }

            float3 EvaluateClouds(float3 viewDirWS)
            {
                if (_CartoonCloudParams.w <= 0.0)
                    return 0.0;

                float3 dir = normalize(viewDirWS);
                // Clouds only above the horizon.
                if (dir.y < 0.03)
                    return 0.0;

                // Intersect the ray with the cloud plane at the given
                // height (relative to the camera).
                float t = max(_CartoonCloudParams.x, 1.0) / dir.y;
                float2 worldXZ = _WorldSpaceCameraPos.xz + dir.xz * t;
                float2 planeUV = worldXZ * 0.008 * _CartoonCloudParams.z;

                // Gentle drift over time.
                planeUV += float2(_Time.y * 0.004, _Time.y * 0.0017);

                float cloud;
                if (_CartoonHasCloudMap > 0.5)
                {
                    cloud = SAMPLE_TEXTURE2D_LOD(_CartoonCloudMap, sampler_CartoonCloudMap,
                                                 planeUV, 0).r;
                }
                else
                {
                    cloud = CloudFBM(planeUV * 3.0);
                }

                // Threshold into soft bill-board shapes.
                float th = _CartoonCloudParams.y;
                cloud = smoothstep(th, th + 0.28, cloud);

                // Fade near the horizon where grazing rays stretch the plane.
                cloud *= smoothstep(0.03, 0.22, dir.y);

                // Clouds darken at night and warm up at sunset.
                float sunY = GetSunElevation();
                float dayW = smoothstep(-0.05, 0.20, sunY);
                float sunsetW = smoothstep(-0.15, 0.0, sunY) * (1.0 - dayW);
                float3 litCol = lerp(_CartoonCloudColor.rgb * 0.12,
                                     _CartoonCloudColor.rgb, dayW);
                litCol = lerp(litCol, _CartoonCloudColor.rgb * float3(1.0, 0.62, 0.45), sunsetW);

                return litCol * cloud * _CartoonCloudParams.w;
            }

            // ----------------------------------------------------------------
            // Fragment shader.
            // ----------------------------------------------------------------
            half4 Frag(Varyings input) : SV_Target
            {
                // Sky-only clipping: never paint over opaque geometry. The
                // depth texture (copied after opaques) marks pixels that have
                // an opaque object in front - discard those so the sky only
                // covers the actual sky region (ZTest Always would otherwise
                // overwrite the whole screen, washing out opaque colours).
                float2 skyUv = input.positionCS.xy / _ScreenParams.xy;
                float depth01 = SampleSceneDepth(skyUv);
#if UNITY_REVERSED_Z
                if (depth01 > 0.0001) discard; // far = 0 on reverse-Z
#else
                if (depth01 < 0.9999) discard; // far = 1 on standard Z
#endif

                // Reconstruct the world-space view direction from the pixel's
                // NDC position. Because every pixel is computed independently
                // (no mesh interpolation), the sky is perfectly continuous
                // regardless of camera orientation.
                float2 ndc = (input.positionCS.xy / _ScreenParams.xy) * 2.0 - 1.0;
                // The fragment raster position grows downward from the top
                // of the render target while clip-space Y grows upward,
                // so the Y axis must be inverted before un-projecting with
                // the inverse VP matrix built from
                // GL.GetGPUProjectionMatrix(renderIntoTexture:true).
                ndc.y = -ndc.y;
                float4 unprojected = mul(_CartoonInvVP, float4(ndc, 0.0, 1.0));
                float3 viewDirWS = normalize(unprojected.xyz / unprojected.w
                                             - _WorldSpaceCameraPos.xyz);

                float vertical = saturate(viewDirWS.y * 0.5 + 0.5);

                float3 sky = EvaluateSkyGradient(vertical);
                float3 clouds = EvaluateClouds(viewDirWS);
                float  sun = EvaluateSunDisc(viewDirWS);
                float3 stars = EvaluateStars(viewDirWS);

                // Stars sit behind the clouds.
                float3 result = sky + stars;
                result = lerp(result, clouds + stars * 0.15,
                              saturate(dot(clouds, 0.333) * 4.0));
                result += GetSunDiscColor() * sun;

                // Volumetric cloud layer (half-res RT, premultiplied):
                // occludes sky, stars and sun like real clouds would.
                if (_CartoonHasVolumetricClouds > 0.5)
                {
                    half4 vc = SAMPLE_TEXTURE2D_LOD(_CartoonVolumetricCloudRT,
                                                    sampler_CartoonVolumetricCloudRT,
                                                    skyUv, 0);
                    result = result * (1.0 - vc.a) + vc.rgb;
                }

                result *= _CartoonIntensity;

                return half4(result, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
