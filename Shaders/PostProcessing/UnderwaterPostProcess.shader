// ============================================================================
// UnderwaterPostProcess.shader
// ----------------------------------------------------------------------------
// Full-screen underwater post process for URP (RenderGraph) - COLOUR-ONLY:
// opaque pixels blend toward the underwater tint, scaled by the Volume zone's
// intensity. Sky and transparent pixels (no scene depth) are untouched.
//
//   opaque   -> lerp(opaque, underwaterColor, saturate(tintStrength * intensity))
//   sky/transparent -> alpha 0 (pass blends SrcAlpha OneMinusSrcAlpha over the
//                      active colour, so the skybox and water plane stay as
//                      the scene renders them)
//
// Source: the URP opaque texture (_CameraOpaqueTexture, pipeline asset
// "Opaque Texture" = On). It is filled and dependency-tracked by URP itself,
// so reading it is reliable - unlike the active colour texture, which is an
// imported (external) resource without a render-graph dependency (a copy of
// it can run before the opaque render and capture an empty buffer).
//
// A local Volume with a collider (Underwater Zone) fades the effect in when
// the camera enters the zone and out when it leaves (blendDistance controls
// the transition).
// ============================================================================
Shader "CartoonRendering/PostProcessing/UnderwaterPostProcess"
{
    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/TextureXR.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ---- driven by the C# pass every frame (volume values) ----
            float4 _UnderwaterColor;   // rgb = underwater tint
            float  _TintStrength;      // 0..1, blend amount at full intensity
            float  _Intensity;         // 0 = off, 1 = fully underwater

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;

                // Sky / transparent mask: no scene depth -> keep the existing
                // active colour (alpha 0, the pass blends over it).
                float depth01 = SampleSceneDepth(uv);
                bool isSky;
#if UNITY_REVERSED_Z
                isSky = depth01 < 0.0001;
#else
                isSky = depth01 > 0.9999;
#endif
                if (isSky)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Opaque pixels: blend toward the underwater tint.
                half3 opaqueColor = SampleSceneColor(uv);
                half3 finalColor = lerp(
                    opaqueColor,
                    _UnderwaterColor.rgb,
                    saturate(_TintStrength * _Intensity));

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}
