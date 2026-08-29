// =============================================================================
// CartoonRendering/UI/SDFTicket —— 链式布尔实战示例：票券 / 优惠券
// 演示多个布尔运算的组合链：
//   票身  = 圆角矩形
//   打孔  = 票身 − 左右两个半圆缺口          （opSubtract + opUnion）
//   撕裂线 = 再 − 中间一排小圆（虚线撕裂孔）  （循环 opUnion 后整体 opSubtract）
//   内框  = 用 sdfStroke 画一圈内缩描边装饰   （非布尔，纯绘制）
// 全部是解析 SDF，无纹理、无顶点，任意分辨率下边缘锐利。
// =============================================================================
Shader "CartoonRendering/UI/SDFTicket"
{
    Properties
    {
        _BodyColor    ("Body Color",      Color) = (1.0, 0.85, 0.3, 1)
        _BorderColor  ("Border Color",    Color) = (0.85, 0.55, 0.1, 1)
        _HalfSize     ("Body Half Size",  Vector) = (0.42, 0.26, 0, 0)
        _Corner       ("Body Corner",     Range(0, 0.15)) = 0.05
        _HoleRadius   ("Punch Hole Radius", Range(0.01, 0.15)) = 0.07
        [Toggle] _TearLine ("Tear Line (dashed holes)", Float) = 1
        _TearRadius   ("Tear Hole Radius",  Range(0.003, 0.05)) = 0.016
        _TearX        ("Tear Line X (-1..1)", Range(-1, 1)) = 0.35
        _BorderWidth  ("Inner Border Width", Range(0, 0.04)) = 0.012
        _BorderInset  ("Inner Border Inset", Range(0, 0.1)) = 0.03
        _Aspect       ("Aspect (W/H)",    Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"             = "Transparent"
            "IgnoreProjector"   = "True"
            "RenderType"        = "Transparent"
            "PreviewType"       = "Plane"
            "CanUseSpriteAtlas" = "True"
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SDFTicket"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SDF2D.hlsl"

            float4 _BodyColor;
            float4 _BorderColor;
            float4 _HalfSize;
            float  _Corner;
            float  _HoleRadius;
            float  _TearLine;
            float  _TearRadius;
            float  _TearX;
            float  _BorderWidth;
            float  _BorderInset;
            float  _Aspect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv         = input.uv;
                output.color      = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv - 0.5;
                p.x *= _Aspect;

                float2 halfSize = _HalfSize.xy;

                // ---------- 1) 票身：圆角矩形 ----------
                float d = sdRoundedBox(p, halfSize, _Corner);

                // ---------- 2) 打孔：票身 − (左半圆 ∪ 右半圆) ----------
                float holeL = sdCircle(p - float2(-halfSize.x, 0.0), _HoleRadius);
                float holeR = sdCircle(p - float2( halfSize.x, 0.0), _HoleRadius);
                d = opSubtract(d, opUnion(holeL, holeR));

                // ---------- 3) 撕裂线：再 − 一排 7 个小圆（竖向虚线孔） ----------
                if (_TearLine > 0.5)
                {
                    float tear = 1e5; // 空集（很大的正距离）
                    float tearX = _TearX * halfSize.x;
                    [unroll] for (int i = 0; i < 7; i++)
                    {
                        float y = lerp(-halfSize.y * 0.85, halfSize.y * 0.85, i / 6.0);
                        tear = opUnion(tear, sdCircle(p - float2(tearX, y), _TearRadius));
                    }
                    d = opSubtract(d, tear);
                }

                // ---------- 4) 填充票身 ----------
                float aa   = fwidth(d) * 1.2;
                float fill = sdfFill(d, aa);

                // ---------- 5) 内缩装饰框：对"票身向内缩 _BorderInset"的等距线描边 ----------
                // SDF 的妙用：d + inset 就是向内偏移后的等距边界，无需重新建模
                float inner = sdfStroke(d + _BorderInset, _BorderWidth, aa) * fill;

                float3 rgb = lerp(_BodyColor.rgb, _BorderColor.rgb, inner);
                float  a   = max(fill * _BodyColor.a, inner * _BorderColor.a);

                return float4(rgb * input.color.rgb, a * input.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
