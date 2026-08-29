// ============================================================================
// GrassInteractionStamp.shader
// ----------------------------------------------------------------------------
// Draws a soft radial "footprint" blob into the interaction RenderTexture.
// Used by GrassField.BakeInteractionTexture via CommandBuffer:
// one quad per footprint, intensity = strength * smoothstep falloff from centre.
// Output is a single-channel strength value (r), sampled later by
// CartoonGrass.shader.
// ============================================================================
Shader "CartoonRendering/Grass/GrassInteractionStamp"
{
    Properties
    {
        _Color ("Strength", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend SrcAlpha One

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Distance from quad centre in [0, 1]
                float2 d = input.uv * 2.0 - 1.0;
                float dist = length(d);
                // Soft falloff: 1 at centre -> 0 at edge
                float falloff = saturate(1.0 - dist);
                falloff = falloff * falloff * (3.0 - 2.0 * falloff); // smoothstep
                return half4(_Color.r * falloff, 0, 0, falloff);
            }
            ENDHLSL
        }
    }
}
