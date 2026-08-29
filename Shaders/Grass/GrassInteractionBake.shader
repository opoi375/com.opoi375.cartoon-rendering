// ============================================================================
// GrassInteractionBake.shader
// ----------------------------------------------------------------------------
// Re-bakes the CPU-side GrassInteractionField into the interaction
// RenderTexture every frame. Drawn as a full-screen procedural triangle
// (SV_VertexID) via CommandBuffer.DrawProcedural:
//
//   vert: SV_VertexID -> fullscreen triangle (clip space + uv)
//   frag: for every footprint in _GrassFootprints[], accumulate the soft
//         radial strength falloff (1 - d/R), take the strongest contributor.
//
// Footprint layout (float4 per element):
//   x = world X, y = world Z, z = radius, w = strength
//
// The RT is cleared to black first; this shader is the only thing drawn into
// it, so the result is exactly the current interaction field snapshot.
// ============================================================================
Shader "CartoonRendering/Grass/GrassInteractionBake"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
        ZWrite Off
        ZTest Always
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define MAX_GRASS_FOOTPRINTS 64

            CBUFFER_START(UnityPerMaterial)
                float4 _GrassInteractionOrigin; // world xz origin of the field
                float4 _GrassInteractionSize;   // world xz size
                float4 _GrassFootprints[MAX_GRASS_FOOTPRINTS];
                int    _GrassFootprintCount;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0; // [0,1] across the field
            };

            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                // Fullscreen triangle from vertex id
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                output.positionCS = float4(uv * 2.0 - 1.0, 0, 1);
                output.uv = uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // D3D 等平台 RT 的 V 原点在顶部：直接按 clip 空间画全屏三角形时，
                // 写入的图像相对 UV 约定上下翻转。这里按平台翻转回来，
                // 保证采样方（CartoonGrass）用 uv=(worldXZ-origin)/size 读到的
                // 就是对应世界位置的场强（否则交互区域会在 Z 方向镜像错位）。
                float2 fieldUV = input.uv;
                #if UNITY_UV_STARTS_AT_TOP
                fieldUV.y = 1.0 - fieldUV.y;
                #endif

                // Convert uv [0,1] back to world xz
                float2 worldXZ = _GrassInteractionOrigin.xz + fieldUV * _GrassInteractionSize.xy;

                float best = 0.0;
                for (int i = 0; i < MAX_GRASS_FOOTPRINTS; i++)
                {
                    if (i >= _GrassFootprintCount) break;
                    float4 fp = _GrassFootprints[i];
                    float2 offset = worldXZ - fp.xy;
                    float dist = length(offset);
                    if (dist >= fp.z || fp.w <= 0.0) continue;
                    float falloff = 1.0 - dist / max(fp.z, 1e-4);
                    falloff = saturate(falloff);
                    best = max(best, fp.w * falloff);
                }

                return half4(best, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
