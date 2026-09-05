// ============================================================================
// CartoonWater.shader
// ----------------------------------------------------------------------------
// URP port of the Zhihu article:
//     《菜鸡都能学会的Unity卡通水shader》
//     https://zhuanlan.zhihu.com/p/425605759
//     (based on Roystan's Toon Water tutorial, roystan.net/articles/toon-water.html)
//
// The original article targets the Built-in Render Pipeline (CGPROGRAM /
// UnityCG.cginc). This file is a faithful URP (HLSL) port implementing every
// technique in the article, in the article's order:
//
//   1. Depth-based water colour
//        - camera depth texture (_CameraDepthTexture) sampled at the screen
//          position, converted to linear eye depth
//        - depthDifference = existingDepthLinear - waterDepth(screenPos.w)
//        - waterColor = lerp(_DepthGradientShallow, _DepthGradientDeep,
//                            saturate(depthDifference / _DepthMaxDistance))
//   2. Perlin-noise waves with a hard cutoff
//        - _SurfaceNoise sampled with tiled UVs, then thresholded against
//          _SurfaceNoiseCutoff to get the flat stylised "wave" patches
//   3. Shore / object-intersection foam
//        - the cutoff is lowered by how shallow the water is:
//          surfaceNoiseCutoff = foamDepthDifference01 * _SurfaceNoiseCutoff
//   4. Scrolling noise animation
//        - noise UV is offset by _Time.y * _SurfaceNoiseScroll
//   5. Surface distortion
//        - a second texture (_SurfaceDistortion, 2 channels) perturbs the
//          noise UV so the foam edges wobble irregularly
//   6. Foam band width (article part 3)
//        - the foam band is a fixed tight width (_FoamMinDistance); the
//          original article widened it on vertical surfaces with a normals
//          replacement camera, which was removed for simplicity - the foam
//          now reacts to depth only
//   7. Transparency
//        - Queue = Transparent, Blend SrcAlpha OneMinusSrcAlpha, ZWrite Off
//   8. Controllable foam colour
//        - _FoamColor applied to the foam alpha, combined with the water
//          colour via the custom alphaBlend() helper
//   9. Anti-aliased foam edges
//        - the binary cutoff is replaced by
//          smoothstep(cutoff - 0.01, cutoff + 0.01, noiseSample)
//
// Dependencies:
//   - Camera depth texture: URP renders _CameraDepthTexture when the camera's
//     URP component has "Depth Texture" = On (or the pipeline asset has it
//     enabled globally) AND the renderer's "Copy Depth Mode" is not
//     "After Transparents" (that would only make the depth texture available
//     after transparent objects like the water itself have been rendered).
// ============================================================================
Shader "CartoonRendering/ToonWater/Cartoon"
{
    Properties
    {
        [Header(Depth Colors)]
        _DepthGradientShallow("Shallow Color", Color) = (0.325, 0.807, 0.971, 0.725)
        _DepthGradientDeep("Deep Color", Color) = (0.086, 0.407, 1, 0.749)
        _DepthMaxDistance("Depth Max Distance", Float) = 5

        [Header(Noise Waves)]
        _SurfaceNoise("Surface Noise", 2D) = "white" {}
        _SurfaceNoiseCutoff("Surface Noise Cutoff", Range(0, 1)) = 0.777
        _SurfaceNoiseScroll("Surface Noise Scroll Amount", Vector) = (0.03, 0.03, 0, 0)

        [Header(Surface Distortion)]
        _SurfaceDistortion("Surface Distortion", 2D) = "white" {}
        _SurfaceDistortionAmount("Surface Distortion Amount", Range(0, 1)) = 0.27

        [Header(Foam)]
        _FoamColor("Foam Color", Color) = (1, 1, 1, 1)
        _FoamDistance("Foam Distance", Range(0.01, 1)) = 0.15

        [Header(Transparency)]
        _Alpha("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "Queue"           = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Library/WorldBend.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _DepthGradientShallow;
                float4 _DepthGradientDeep;
                float  _DepthMaxDistance;

                float4 _SurfaceNoise_ST;
                float  _SurfaceNoiseCutoff;
                float4 _SurfaceNoiseScroll;

                float4 _SurfaceDistortion_ST;
                float  _SurfaceDistortionAmount;

                float4 _FoamColor;
                float  _FoamDistance;

                float  _Alpha;
            CBUFFER_END

            TEXTURE2D(_SurfaceNoise);
            SAMPLER(sampler_SurfaceNoise);
            TEXTURE2D(_SurfaceDistortion);
            SAMPLER(sampler_SurfaceDistortion);

            // Article part 6: smoothstep band width for the foam edge.
            #define SMOOTHSTEP_AA 0.01

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS     : SV_POSITION;
                float4 screenPosition : TEXCOORD0;   // for depth lookup
                float2 noiseUV        : TEXCOORD1;   // tiled noise UV
                float2 distortUV      : TEXCOORD2;   // tiled distortion UV
            };

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = ApplyWorldBend(TransformObjectToWorld(input.positionOS.xyz));
                output.positionCS = TransformWorldToHClip(positionWS);
                output.screenPosition = ComputeScreenPos(output.positionCS);

                // Tiling + offset support for both textures.
                output.noiseUV   = TRANSFORM_TEX(input.uv, _SurfaceNoise);
                output.distortUV = TRANSFORM_TEX(input.uv, _SurfaceDistortion);
                return output;
            }

            // Article part 5: custom alpha blending that accumulates the
            // combined alpha, so the foam colour stays controllable.
            float4 alphaBlend(float4 top, float4 bottom)
            {
                float3 color = (top.rgb * top.a) + (bottom.rgb * (1 - top.a));
                float alpha = top.a + bottom.a * (1 - top.a);
                return float4(color, alpha);
            }

            half4 frag(Varyings input) : SV_Target
            {
                // ============================================================
                // 1. Depth difference (article part 1)
                // ============================================================
                float2 screenUV = input.screenPosition.xy / input.screenPosition.w;

                // Non-linear 0..1 depth, then converted to linear eye depth
                // (distance from the camera in world units, view-Z axis).
                float existingDepth01 = SampleSceneDepth(screenUV);
                float existingDepthLinear = LinearEyeDepth(existingDepth01, _ZBufferParams);

                // Water depth = clip-space w of the surface point.
                float waterDepth = input.screenPosition.w;
                float depthDifference = existingDepthLinear - waterDepth;

                // ============================================================
                // 3. Depth-based water colour (article part 1.3)
                // ============================================================
                float waterDepthDifference01 = saturate(depthDifference / _DepthMaxDistance);
                float4 waterColor = lerp(_DepthGradientShallow, _DepthGradientDeep, waterDepthDifference01);

                // ============================================================
                // 5. Surface distortion (article part 2.d)
                //    Distortion map is like a 2-channel normal map: remap
                //    [0,1] -> [-1,1] and scale by the distortion amount.
                // ============================================================
                float2 distortSample =
                    (SAMPLE_TEXTURE2D(_SurfaceDistortion, sampler_SurfaceDistortion, input.distortUV).xy * 2.0 - 1.0)
                    * _SurfaceDistortionAmount;

                // ============================================================
                // 4. Scrolling noise (article part 2.c)
                // ============================================================
                float2 noiseUV = float2(
                    input.noiseUV.x + _Time.y * _SurfaceNoiseScroll.x + distortSample.x,
                    input.noiseUV.y + _Time.y * _SurfaceNoiseScroll.y + distortSample.y);
                float surfaceNoiseSample =
                    SAMPLE_TEXTURE2D(_SurfaceNoise, sampler_SurfaceNoise, noiseUV).r;

                // ============================================================
                // 6. Foam band width (article part 3)
                //    The original tutorial widens the band on vertical
                //    surfaces via a normals replacement camera (lerp between
                //    _FoamMaxDistance/_FoamMinDistance); that was removed for
                //    simplicity, so the band width is a single parameter now.
                // ============================================================
                float foamDistance = _FoamDistance;
                float foamDepthDifference01 = saturate(depthDifference / foamDistance);

                // ============================================================
                // 2 + 3. Foam cutoff from depth (article part 2.b)
                //    Shallow water lowers the cutoff -> foam appears near the
                //    shore / at object intersections.
                // ============================================================
                float surfaceNoiseCutoff = foamDepthDifference01 * _SurfaceNoiseCutoff;

                // ============================================================
                // 9. Anti-aliased foam edge (article part 6)
                //    Smoothstep replaces the binary 0/1 cutoff.
                // ============================================================
                float surfaceNoise = smoothstep(
                    surfaceNoiseCutoff - SMOOTHSTEP_AA,
                    surfaceNoiseCutoff + SMOOTHSTEP_AA,
                    surfaceNoiseSample);

                // ============================================================
                // 8. Foam colour + custom blend (article part 5)
                // ============================================================
                float4 surfaceNoiseColor = _FoamColor;
                surfaceNoiseColor.a *= surfaceNoise;
                float4 finalColor = alphaBlend(surfaceNoiseColor, waterColor);
                finalColor.a *= _Alpha;

                return finalColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
