// ============================================================================
// VolumetricLight.shader
// ----------------------------------------------------------------------------
// URP 体积光（上帝光 / 光柱）。屏幕空间光线步进 + 主光阴影图采样。
//
// Pass 0  光线步进（半分辨率，写进自己的 RT）
//   1. 从深度图还原当前像素的场景距离，步进范围 = [近平面, min(场景距离, 最远距离)]
//   2. 每一步把世界坐标转到主光阴影坐标，采样得到该点是否被遮挡
//   3. Beer 定律累积透射率，Henyey-Greenstein 相位函数控制前向散射
//   4. 可选：高度雾（光柱贴地）、3D 值噪声（尘埃感）、远端淡出
//   5. 抖动步进起点打散步进条带；静态 IGN 而不是逐帧变化，避免闪烁
//
// Pass 1  深度感知双边上采样 + 叠加合成（Blend One One 盖回 activeColor）
//   4 抽头双线性权重 × 深度相似度权重，避免几何体边缘出现光晕渗色。
//   还会在这里做卡通分层（把累积值量化成若干档）。
//
// Pass 2  与 Pass 1 完全相同的 fragment，只是 Blend Off —— 调试视图需要直接
//         替换屏幕内容，否则叠加合成出来的画面看不清。
//
// 顶点着色器是本文件自带的：不用 Blit.hlsl 就不用拖进 TextureXR（两者都会
// 定义 FRAMEBUFFER_INPUT_X_* 会撞出重复宏警告），也不用每帧喂 _BlitScaleBias。
//
// 【重要】阴影采样依赖 URP 的全局关键字 _MAIN_LIGHT_SHADOWS /
// _MAIN_LIGHT_SHADOWS_CASCADE。这里声明 multi_compile 让对应变体能被编出来，
// C# 侧（VolumetricLightPass）每帧还会按 UniversalShadowData 显式设置局部
// 关键字，不依赖全局关键字的隐式回退 —— 否则开关级联会让光柱整体消失。
// ============================================================================
Shader "CartoonRendering/PostProcessing/VolumetricLight"
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

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // ---------------------------------------------------------------
        //  自带全屏三角形顶点着色器
        // ---------------------------------------------------------------
        struct VolAttributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct VolVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        VolVaryings VolVert(VolAttributes input)
        {
            VolVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            // vertexID 0/1/2 → uv (0,0) (2,0) (0,2) → 覆盖整个裁剪空间的大三角形
            float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            output.uv = uv;
            return output;
        }
        ENDHLSL

        // ====================================================================
        //  Pass 0：光线步进
        // ====================================================================
        Pass
        {
            Name "VolumetricLightRaymarch"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   VolVert
            #pragma fragment FragRaymarch
            #pragma target 3.5

            #pragma multi_compile_fragment _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.opoi375.cartoon-rendering/Shaders/PostProcessing/VolumetricLightCommon.hlsl"

            half4 FragRaymarch(VolVaryings input) : SV_Target
            {
                float2 uv       = input.uv;
                float  rawDepth = SampleSceneDepth(uv);

                VolRay ray = BuildVolRay(uv, rawDepth);
                float  eyeZOut = min(ray.eyeZ, kVolMaxStoredDepth);

                int dbg = (int)(_VolDebug + 0.5);

                // ---- 调试：阴影可见度 ------------------------------------
                // 直接看主光阴影采样结果：有级联的明暗分区才说明关键字接对了。
                if (dbg == 1)
                {
                    float t = min(ray.tScene, _VolMaxDistance) * 0.5;
                    float vis = SampleMainShadow(ray.originWS + ray.dirWS * t);
                    return half4(vis, vis, vis, eyeZOut);
                }
                // ---- 调试：步数热力图 ------------------------------------
                if (dbg == 2)
                {
                    float heat = saturate(_VolSteps / 64.0);
                    return half4(heat, heat * 0.3, 0.05, eyeZOut);
                }
                // ---- 调试：场景距离 --------------------------------------
                if (dbg == 3)
                {
                    float d = saturate(ray.eyeZ / max(_VolMaxDistance, 1.0));
                    return half4(d, d, d, eyeZOut);
                }

                float tEnd = min(ray.tScene, _VolMaxDistance);
                int   steps = (int)clamp(_VolSteps, 1.0, 64.0);
                float stepLen = tEnd / max((float)steps, 1.0);

                half3 accum = half3(0.0, 0.0, 0.0);
                float transmittance = 1.0;

                if (tEnd > 1e-3 && _VolDensity > 1e-5 && _VolIntensity > 1e-5)
                {
                    Light  mainLight = GetMainLight();
                    float3 L         = normalize(mainLight.direction);   // 指向光源
                    half3  lightCol  = mainLight.color;

                    // Henyey-Greenstein 相位函数，乘 4π 归一化：各向同性时 = 1
                    float g    = clamp(_VolAnisotropy, -0.95, 0.95);
                    float g2   = g * g;
                    float cosT = dot(ray.dirWS, L);
                    float phase = (1.0 - g2) / pow(abs(1.0 + g2 - 2.0 * g * cosT), 1.5);
                    phase = clamp(phase, 0.0, 32.0);

                    // 抖动步进起点：静态 IGN，逐帧变化会在没有 TAA 时闪
                    float jitter = InterleavedGradientNoise(uv * _ScreenParams.xy, 0);
                    float t = stepLen * jitter * saturate(_VolJitter);

                    float3 drift = float3(0.0,
                                          -_Time.y * _VolNoiseSpeed,
                                          _Time.y * _VolNoiseSpeed * 0.37);

                    float fadeStart = _VolMaxDistance * saturate(_VolDistanceFade);
                    float fadeLen   = max(_VolMaxDistance - fadeStart, 1e-4);

                    [loop]
                    for (int s = 0; s < steps; s++)
                    {
                        if (t >= tEnd) break;

                        float3 pWS = ray.originWS + ray.dirWS * t;

                        // 高度雾：_VolHeightStart 以上按指数衰减，光柱贴地
                        float heightFall = 1.0;
                        if (_VolHeightFalloff > 1e-5)
                            heightFall = exp(-max(pWS.y - _VolHeightStart, 0.0) * _VolHeightFalloff);

                        // 尘埃噪声：单倍频 3D 值噪声，均值归一到 1
                        float n = 1.0;
                        if (_VolNoiseStrength > 1e-4)
                            n = lerp(1.0, VolValueNoise3D(pWS * _VolNoiseScale + drift) * 2.0,
                                     _VolNoiseStrength);

                        // 远端淡出，避免在最远距离处出现硬边
                        float farFade = saturate(1.0 - (t - fadeStart) / fadeLen);

                        float sigma = _VolDensity * heightFall * n * farFade * stepLen;
                        float vis   = SampleMainShadow(pWS);

                        accum += lightCol * vis * sigma * transmittance;
                        transmittance *= exp(-sigma);

                        t += stepLen;

                        if (transmittance < 0.003) break;   // 已经不透光了，后面看不见
                    }

                    accum *= (half3)(phase * _VolIntensity) * (half3)_VolTint.rgb;
                }

                return half4(accum, eyeZOut);
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 1：双边上采样 + 叠加合成
        // ====================================================================
        Pass
        {
            Name "VolumetricLightComposite"
            Blend One One

            HLSLPROGRAM
            #pragma vertex   VolVert
            #pragma fragment FragComposite
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.opoi375.cartoon-rendering/Shaders/PostProcessing/VolumetricLightCommon.hlsl"

            half4 FragComposite(VolVaryings input) : SV_Target
            {
                return half4(VolComposite(input.uv), 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 2：调试视图（同一个 fragment，靠 Blend Off 直接盖屏）
        // ====================================================================
        Pass
        {
            Name "VolumetricLightDebug"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   VolVert
            #pragma fragment FragComposite
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.opoi375.cartoon-rendering/Shaders/PostProcessing/VolumetricLightCommon.hlsl"

            half4 FragComposite(VolVaryings input) : SV_Target
            {
                return half4(VolComposite(input.uv), 1.0);
            }
            ENDHLSL
        }
    }
}
