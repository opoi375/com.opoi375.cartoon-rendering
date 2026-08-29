// =============================================================================
// CartoonRendering/UI/SDFBooleans —— SDF2D.hlsl 布尔运算演示
// 操作数：A = 圆（可动画左右移动），B = 圆角矩形（固定）
// 一个材质通过 Op Type 切换 6 种运算：
//   0 Union            并集        min(dA, dB)
//   1 Subtract         差集        B - A（圆当"剪刀"挖盒子）
//   2 Intersect        交集        max(dA, dB)
//   3 SmoothUnion      平滑并集    融球：靠近时粘连
//   4 SmoothSubtract   平滑差集    圆滑地挖洞
//   5 SmoothIntersect  平滑交集    圆滑的透镜形相交
// 开启 Animate 后圆 A 左右往复，可动态观察每种运算的过渡。
// =============================================================================
Shader "CartoonRendering/UI/SDFBooleans"
{
    Properties
    {
        [Enum(Union,0,Subtract,1,Intersect,2,SmoothUnion,3,SmoothSubtract,4,SmoothIntersect,5)]
        _OpType       ("Op Type (Subtract=B-A)", Float) = 0
        _FillColor    ("Fill Color",         Color) = (0.25, 0.65, 1.0, 1)
        _BorderColor  ("Border Color",       Color) = (1, 1, 1, 1)
        _BorderWidth  ("Border Width",       Range(0, 0.05)) = 0.012
        _Smoothness   ("Smoothness (k)",     Range(0, 0.3))  = 0.1

        _RadiusA      ("A: Circle Radius",   Range(0.02, 0.4)) = 0.18
        _CenterA      ("A: Circle Center",   Vector) = (-0.12, 0, 0, 0)
        _HalfSizeB    ("B: Box Half Size",   Vector) = (0.26, 0.2, 0, 0)
        _CornerB      ("B: Box Corner",      Range(0, 0.2)) = 0.05

        [Toggle] _ShowOperands ("Show Operand Outlines", Float) = 1
        [Toggle] _Animate      ("Animate A",             Float) = 1
        _Travel       ("A Travel Distance",  Range(0, 0.4)) = 0.22
        _Speed        ("A Move Speed",       Range(0, 4))   = 1.0
        _Aspect       ("Aspect (W/H)",       Float) = 1.0
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
            Name "SDFBooleans"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SDF2D.hlsl"

            float  _OpType;
            float4 _FillColor;
            float4 _BorderColor;
            float  _BorderWidth;
            float  _Smoothness;
            float  _RadiusA;
            float4 _CenterA;
            float4 _HalfSizeB;
            float  _CornerB;
            float  _ShowOperands;
            float  _Animate;
            float  _Travel;
            float  _Speed;
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

                // ---------- 操作数 A：圆（可动画） ----------
                float2 cA = _CenterA.xy;
                if (_Animate > 0.5)
                    cA.x = sin(_Time.y * _Speed) * _Travel;
                cA.x *= _Aspect;
                float dA = sdCircle(p - cA, _RadiusA);

                // ---------- 操作数 B：圆角矩形 ----------
                float dB = sdRoundedBox(p, _HalfSizeB.xy, _CornerB);

                // ---------- 核心：6 种布尔运算 ----------
                // 差集方向固定为 B - A（圆 A 当"剪刀"去挖盒子 B）
                float k = max(_Smoothness, 0.0);
                float d;
                int op = (int)(_OpType + 0.5);
                if      (op == 0) d = opUnion(dA, dB);                  // 并
                else if (op == 1) d = opSubtract(dB, dA);               // 差
                else if (op == 2) d = opIntersect(dA, dB);              // 交
                else if (op == 3) d = opSmoothUnion(dA, dB, k);         // 融球·并
                else if (op == 4) d = opSmoothSubtract(dB, dA, k);      // 融球·差
                else              d = opSmoothIntersect(dA, dB, k);     // 融球·交

                // ---------- 结果：填充 + 边框 ----------
                float aa     = fwidth(d) * 1.2;
                float fill   = sdfFill(d, aa);
                float border = sdfStroke(d, _BorderWidth, aa);

                float3 rgb = lerp(_FillColor.rgb, _BorderColor.rgb, border);
                float  a   = max(fill * _FillColor.a, border * _BorderColor.a);

                // ---------- 操作数参考轮廓（半透明细线，便于理解运算） ----------
                if (_ShowOperands > 0.5)
                {
                    float lineA = sdfStroke(dA, 0.004, fwidth(dA) * 1.2);
                    float lineB = sdfStroke(dB, 0.004, fwidth(dB) * 1.2);
                    float lines = max(lineA, lineB) * 0.5;
                    rgb = lerp(rgb, float3(1, 1, 1), lines * (1.0 - border));
                    a   = max(a, lines * 0.6);
                }

                return float4(rgb * input.color.rgb, a * input.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
