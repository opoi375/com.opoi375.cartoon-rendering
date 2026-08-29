// =============================================================================
// CartoonRendering/UI/SDFShapes —— SDF2D.hlsl 基础形状验证示例
// 一个材质切换 5 种形状（圆形/方形/圆角方形/圆环/三角形），
// 支持填充色 + 描边色 + 抗锯齿，可用在 UI Image 或场景 Quad 上。
// =============================================================================
Shader "CartoonRendering/UI/SDFShapes"
{
    Properties
    {
        [Enum(Circle,0,Box,1,RoundedBox,2,Ring,3,Triangle,4)]
        _ShapeType    ("Shape Type",     Float)   = 0
        _FillColor    ("Fill Color",     Color)   = (0.2, 0.6, 1.0, 1)
        _StrokeColor  ("Stroke Color",   Color)   = (1, 1, 1, 1)
        _StrokeWidth  ("Stroke Width",   Range(0, 0.2)) = 0.02
        _HalfSize     ("Half Size (XY)", Vector)  = (0.3, 0.3, 0, 0)
        _CornerRadius ("Corner Radius",  Range(0, 0.3)) = 0.08
        _RingWidth    ("Ring Width",     Range(0.005, 0.15)) = 0.04
        _Aspect       ("Aspect (W/H)",   Float)   = 1.0
    }

    SubShader
    {
        // UI 标准渲染状态：透明队列、不写深度、常规 alpha 混合
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
            Name "SDFShapes"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // 相对路径引用同目录的函数库
            #include "SDF2D.hlsl"

            float  _ShapeType;
            float4 _FillColor;
            float4 _StrokeColor;
            float  _StrokeWidth;
            float4 _HalfSize;
            float  _CornerRadius;
            float  _RingWidth;
            float  _Aspect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;   // UI Image 的顶点色（Image.color）
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
                // 局部坐标：居中 + 宽高比修正，单位与 UV 一致（分辨率无关）
                float2 p = input.uv - 0.5;
                p.x *= _Aspect;

                // ---------- 按类型取形状 SDF ----------
                float2 halfSize = _HalfSize.xy;
                float d;
                int type = (int)(_ShapeType + 0.5);
                if      (type == 0) d = sdCircle(p, halfSize.x);                        // 圆形
                else if (type == 1) d = sdBox(p, halfSize);                             // 方形
                else if (type == 2) d = sdRoundedBox(p, halfSize, _CornerRadius);       // 圆角方形
                else if (type == 3) d = sdRing(p, halfSize.x, _RingWidth);              // 圆环
                else                d = sdEquilateralTriangle(p, halfSize.x);           // 三角形

                // ---------- 距离场 -> 填充 + 描边（屏幕空间抗锯齿） ----------
                float aa     = fwidth(d) * 1.2;
                float fill   = sdfFill(d, aa);
                float stroke = sdfStroke(d, _StrokeWidth, aa);

                // 描边叠在填充之上；整体乘 UI 顶点色（Image.color）
                float3 rgb = lerp(_FillColor.rgb, _StrokeColor.rgb, stroke) * input.color.rgb;
                float  a   = max(fill * _FillColor.a, stroke * _StrokeColor.a) * input.color.a;

                return float4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
