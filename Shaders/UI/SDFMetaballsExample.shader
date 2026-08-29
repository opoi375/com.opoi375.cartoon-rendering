// =============================================================================
// CartoonRendering/UI/SDFMetaballs —— 融球（Smooth Union）验证示例
// 两个圆通过 opSmoothUnion 融合：靠近时自动粘连成"水滴/史莱姆"效果。
// 可手动设置两球位置，也可开启 Animate 让两球绕轨道相互靠近/远离，
// 直观验证平滑融合参数 Smoothness 的作用。
// =============================================================================
Shader "CartoonRendering/UI/SDFMetaballs"
{
    Properties
    {
        _ColorA       ("Ball A Color",     Color) = (1.0, 0.45, 0.15, 1)
        _ColorB       ("Ball B Color",     Color) = (0.15, 0.55, 1.0, 1)
        _RadiusA      ("Ball A Radius",    Range(0.02, 0.4)) = 0.16
        _RadiusB      ("Ball B Radius",    Range(0.02, 0.4)) = 0.11
        _CenterA      ("Ball A Center",    Vector) = (-0.12, 0, 0, 0)
        _CenterB      ("Ball B Center",    Vector) = (0.12, 0, 0, 0)
        _Smoothness   ("Fusion Smoothness (k)", Range(0, 0.3)) = 0.12
        _GlowStrength ("Glow Strength",    Range(0, 1))   = 0.35
        _GlowRadius   ("Glow Radius",      Range(0.005, 0.2)) = 0.05
        [Toggle] _Animate ("Animate Orbit", Float) = 1
        _Orbit        ("Orbit Radius",     Range(0, 0.35)) = 0.2
        _Speed        ("Orbit Speed",      Range(0, 4))   = 1.2
        _Aspect       ("Aspect (W/H)",     Float) = 1.0
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
            Name "SDFMetaballs"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "SDF2D.hlsl"

            float4 _ColorA;
            float4 _ColorB;
            float  _RadiusA;
            float  _RadiusB;
            float4 _CenterA;
            float4 _CenterB;
            float  _Smoothness;
            float  _GlowStrength;
            float  _GlowRadius;
            float  _Animate;
            float  _Orbit;
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

                // ---------- 两球中心：手动 或 轨道动画 ----------
                float2 cA = _CenterA.xy;
                float2 cB = _CenterB.xy;
                if (_Animate > 0.5)
                {
                    float t = _Time.y * _Speed;
                    cA = float2(cos(t),            sin(t))            * _Orbit;
                    cB = float2(cos(t + 3.14159265), sin(t + 3.14159265)) * _Orbit;
                }
                cA.x *= _Aspect;
                cB.x *= _Aspect;

                // ---------- 核心：融球 = 两个圆的平滑并集 ----------
                float dA = sdCircle(p - cA, _RadiusA);
                float dB = sdCircle(p - cB, _RadiusB);
                float d  = opSmoothUnion(dA, dB, _Smoothness);   // ← 融球效果

                // ---------- 填充 + 发光 ----------
                float aa   = fwidth(d) * 1.2;
                float fill = sdfFill(d, aa);
                float glow = sdfGlow(d, _GlowRadius) * _GlowStrength;

                // 颜色按"离哪个球更近"在融合带内平滑过渡，粘连处颜色也连续
                float blend = clamp(0.5 + 0.5 * (dB - dA) / max(_Smoothness, 1e-3), 0.0, 1.0);
                float3 ballColor = lerp(_ColorA.rgb, _ColorB.rgb, blend);

                float3 rgb = ballColor * fill + ballColor * glow * (1.0 - fill);
                float  a   = saturate(fill + glow * (1.0 - fill));

                return float4(rgb * input.color.rgb, a * input.color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
