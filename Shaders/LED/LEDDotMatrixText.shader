// ============================================================================
// LEDDotMatrixText.shader
// ----------------------------------------------------------------------------
// LED 点阵文字屏（URP Unlit）。
//
// 原理：文字本身不是"画"出来的，而是"点"出来的。
//   1. UV 网格化       floor(uv * 格子数) 拿到每个 LED 的 cell id
//   2. 程序化圆形点阵   length(frac(...)) + smoothstep，不用任何贴图
//   3. 纹理遮罩采样     在格子中心去采样 _MainTex，决定这个点亮不亮
//   4. UV 滚动         遮罩 UV 加 _Time.y * _Scroll
//   5. 双频正弦闪烁     两个频率的正弦叠加 + 每点独立随机相位
//
// 遮罩由 LedTextMaskBaker 在运行时烘焙（也可以直接塞一张白字黑底 PNG）。
//
// 关键细节：
//   - 必须用 fwidth 做解析抗锯齿，否则远距离 / 斜视角下点阵会剧烈闪烁（摩尔纹）
//   - rows 要按宽高比推算，否则点会变成椭圆
//   - _OnColor 走 HDR + 后处理 Bloom，才有真实的发光感
//   - _OffColor 不要设全黑，熄灭的 LED 点仍然可见，这个细节很提升质感

Shader "CartoonRendering/LED/DotMatrix Text"
{
    Properties
    {
        [MainTexture] _MainTex ("文字遮罩 (R 通道)", 2D) = "black" {}
        _MaskThreshold ("遮罩阈值", Range(0, 1)) = 0.5
        _MaskSoftness  ("遮罩过渡", Range(0.001, 1)) = 0.35
        _MaskTransform ("遮罩 平铺/偏移 (XY=Tiling, ZW=Offset)", Vector) = (1, 1, 0, 0)

        [Header(Grid)][Space(4)]
        _Cols        ("横向 LED 数量", Float) = 96
        _Aspect      ("屏幕宽高比 W/H", Float) = 4
        _DotRadius   ("点半径", Range(0, 0.5)) = 0.36
        _DotSoftness ("点边缘柔化", Range(0, 0.3)) = 0.04

        [Header(Scroll)][Space(4)]
        _Scroll ("滚动速度 (XY)", Vector) = (0.06, 0, 0, 0)

        [Header(Flicker)][Space(4)]
        _FlickerAmount ("闪烁强度", Range(0, 1)) = 0.4
        _FlickerSpeed  ("闪烁速度", Float) = 6
        _FlickerRatio  ("第二频率倍率", Float) = 4.7

        [Header(Scanline)][Space(4)]
        _ScanIntensity ("扫描线强度", Range(0, 1)) = 0.15
        _ScanSpeed     ("扫描速度", Float) = 0.6
        _ScanWidth     ("扫描宽度", Range(0.01, 1)) = 0.12

        [Header(Color)][Space(4)]
        [HDR] _OnColor ("亮起 LED 颜色 (HDR)", Color) = (1, 1, 1, 1)
        _OnIntensity ("亮起强度", Float) = 3
        _OffColor    ("熄灭 LED 颜色", Color) = (0.08, 0.09, 0.10, 1)
        _PanelColor  ("面板底色",       Color) = (0.015, 0.018, 0.022, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 100

        Pass
        {
            Name "LEDForward"
            Tags { "LightMode"="UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MaskTransform;
                float4 _Scroll;
                float4 _PanelColor;
                float4 _OffColor;
                float4 _OnColor;
                float  _MaskThreshold;
                float  _MaskSoftness;
                float  _Cols;
                float  _Aspect;
                float  _DotRadius;
                float  _DotSoftness;
                float  _FlickerAmount;
                float  _FlickerSpeed;
                float  _FlickerRatio;
                float  _ScanIntensity;
                float  _ScanSpeed;
                float  _ScanWidth;
                float  _OnIntensity;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;                 // 网格基于原始 UV，不套 ST
                return OUT;
            }

            float4 frag (Varyings IN) : SV_Target
            {
                // ---------- 1. UV 网格化（rows 按宽高比换算，保证格子是正方形）----------
                float  cols  = max(_Cols, 1.0);
                float  rows  = cols / max(_Aspect, 1e-3);
                float2 gUV   = float2(IN.uv.x * cols, IN.uv.y * rows);
                float2 cell  = floor(gUV);
                float2 local = gUV - cell - 0.5;                    // 格子中心为原点

                // ---------- 2. 程序化圆形点阵 + 解析抗锯齿 ----------
                float2 gw = fwidth(gUV);
                float  aa = max(gw.x, gw.y) * 0.5;                  // 越远边缘越软 → 消除摩尔纹
                float  e0 = _DotRadius - aa - _DotSoftness;
                float  e1 = max(_DotRadius + aa + _DotSoftness, e0 + 1e-4);
                float  dotMask = 1.0 - smoothstep(e0, e1, length(local));

                // ---------- 3. 纹理遮罩采样（在格子中心取样，2x2 超采样防漏笔画）----------
                float2 cellCenter = (cell + 0.5) / float2(cols, rows);
                float2 texStep    = (0.25 / float2(cols, rows)) * _MaskTransform.xy;
                float2 maskUV     = cellCenter * _MaskTransform.xy + _MaskTransform.zw
                                  + _Scroll.xy * _Time.y;           // ---------- 4. UV 滚动 ----------

                float m  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, maskUV - texStep).r;
                      m += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, maskUV + float2( texStep.x, -texStep.y)).r;
                      m += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, maskUV + float2(-texStep.x,  texStep.y)).r;
                      m += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, maskUV + texStep).r;
                m = saturate(((m * 0.25) - _MaskThreshold) / max(_MaskSoftness, 1e-4));

                // ---------- 5. 双频正弦闪烁（每点独立相位，否则整屏齐闪很假）----------
                float2 cc = cell + 0.5;
                float  ph = frac(sin(dot(cc, float2(127.1, 311.7))) * 43758.5453) * 6.2831853;
                float  tw = _Time.y * _FlickerSpeed;
                float  wave  = 0.5 + 0.32 * sin(tw + ph) + 0.18 * sin(tw * _FlickerRatio + ph * 1.73);
                float  flick = lerp(1.0, saturate(wave), _FlickerAmount);

                // ---------- 可选：横向扫描线 ----------
                float scanPos = frac(_Time.y * _ScanSpeed);
                float scan    = 1.0 - smoothstep(0.0, max(_ScanWidth, 1e-3), abs(IN.uv.x - scanPos));
                float boost   = 1.0 + scan * _ScanIntensity;

                // ---------- 合成 ----------
                float  lit    = saturate(m * flick * boost);                     // 这个点的最终亮度 0~1
                float3 dotCol = lerp(_OffColor.rgb, _OnColor.rgb * _OnIntensity, lit);
                float3 col    = lerp(_PanelColor.rgb, dotCol, dotMask);          // 点与点之间露出面板底色

                return float4(col, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
