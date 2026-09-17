// ============================================================================
// LedMaskCopyAlpha.shader
// ----------------------------------------------------------------------------
// 内部工具：把纹理的 alpha 通道搬到 RGB，用于把 Unity 动态字体图集读成 CPU 数组。
//
// Unity 的字体图集把字形覆盖率存在 alpha 里，直接 Graphics.Blit 会得到一片白。
// 只有当字体图集不可读、或不是单通道格式时才会走到这里；
// 正常情况下 LedTextMaskBaker 直接 GetRawTextureData 就够了。
// 不会出现在材质下拉菜单里。

Shader "Hidden/CartoonRendering/LED/MaskCopyAlpha"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 frag (Varyings IN) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                return float4(a, a, a, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
