// =============================================================================
// CartoonRendering/PostProcessing/PixelatePostProcess
// 纯屏幕后处理像素化（Unity 6 / URP / Render Graph 兼容，Volume 驱动）
//
// 技术流程（严格按顺序）：
//   1. 获取屏幕 UV -> 像素坐标 (uv * _ScreenParams.xy)
//   2. 像素化：UV 量化到网格，Point Filter 采样 _BlitTexture
//   3. 有序抖动：基于"像素块坐标"取 Bayer 阈值，加到颜色上（量化之前！）
//   4. 色阶量化：floor(c * (L-1) + 0.5) / (L-1)
//   5. 按 _Intensity 与原始画面混合（供 Volume 权重淡入淡出）
//   6. 输出
// =============================================================================
Shader "CartoonRendering/PostProcessing/PixelatePostProcess"
{
    Properties
    {
        // 像素块边长，单位：屏幕物理像素（分辨率自适应，视觉大小与分辨率无关）
        _PixelSize      ("Pixel Size (screen px)", Range(1, 64)) = 4
        // 每通道色阶数
        _ColorLevels    ("Color Levels",           Range(2, 64)) = 16
        // 抖动强度 0~1（1 = 一个完整量化步长的抖动幅度）
        _DitherStrength ("Dither Strength",        Range(0, 1))  = 0.5
        // 0 = 4x4 Bayer，1 = 8x8 Bayer
        [KeywordEnum(Bayer4x4, Bayer8x8)]
        _BayerMode      ("Bayer Mode",             Float)        = 1
        // 效果整体强度（Volume 淡入淡出用）：0 = 原图，1 = 完整像素化
        _Intensity      ("Intensity",              Range(0, 1))  = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "PixelateFullscreen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // 与 [KeywordEnum] 对应的两个 keyword，由 C# 侧 EnableKeyword 切换
            #pragma shader_feature_local _BAYERMODE_BAYER4X4 _BAYERMODE_BAYER8X8

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Blit.hlsl 提供 Attributes / Varyings / Vert，
            // 并声明 _BlitTexture、sampler_LinearClamp、sampler_PointClamp
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _PixelSize;
            float _ColorLevels;
            float _DitherStrength;
            float _BayerMode;
            float _Intensity;

            // ---------------- Bayer 矩阵（直接写在 HLSL，不依赖外部纹理） ----------------
            // 4x4，取值 0~15，使用时 /16 归一化
            static const float BAYER4[16] =
            {
                 0,  8,  2, 10,
                12,  4, 14,  6,
                 3, 11,  1,  9,
                15,  7, 13,  5
            };

            // 8x8，取值 0~63，使用时 /64 归一化
            static const float BAYER8[64] =
            {
                 0, 32,  8, 40,  2, 34, 10, 42,
                48, 16, 56, 24, 50, 18, 58, 26,
                12, 44,  4, 36, 14, 46,  6, 38,
                60, 28, 52, 20, 62, 30, 54, 22,
                 3, 35, 11, 43,  1, 33,  9, 41,
                51, 19, 59, 27, 49, 17, 57, 25,
                15, 47,  7, 39, 13, 45,  5, 37,
                63, 31, 55, 23, 61, 29, 53, 21
            };

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // ------------------------------------------------------------------
                // 【步骤 1】获取屏幕坐标
                // input.texcoord 即全屏 Blit 的屏幕 UV（Blit.hlsl 的 Vert 已处理
                // _BlitScaleBias / 平台 UV 翻转）
                // ------------------------------------------------------------------
                float2 uv        = input.texcoord;
                float2 screenRes = _ScreenParams.xy;
                float2 pixelCoord = uv * screenRes;              // 物理像素坐标

                // ------------------------------------------------------------------
                // 【步骤 2】像素化（Pixelation）
                // pixelSize 以"屏幕物理像素"为单位：
                //   同一 _PixelSize 在 1080p / 4K 下块边长都是固定像素数，
                //   视觉大小因此与分辨率解耦（分辨率自适应）。
                // 等价于 gridSize = screenRes / pixelSize 的
                //   floor(uv * gridSize) / gridSize，
                // 这里额外 +0.5 取块中心采样，避免边缘采样到相邻块的纹素。
                // ------------------------------------------------------------------
                float pixelSize  = max(_PixelSize, 1.0);
                float2 blockCoord    = floor(pixelCoord / pixelSize);          // 像素块整数坐标
                float2 quantizedUV   = (blockCoord + 0.5) * pixelSize / screenRes; // 块中心 UV

                // Point Filter 采样原始屏幕颜色（LOD 0，关闭 mip 插值）
                float4 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, quantizedUV, 0);

                // ------------------------------------------------------------------
                // 【步骤 3】有序抖动（Ordered Dither - Bayer）
                // 关键点：抖动图案基于"像素块坐标 blockCoord"取模，
                // 而不是原始像素坐标 —— 保证抖动图案与像素块严格对齐。
                // ------------------------------------------------------------------
                float threshold;
                #if defined(_BAYERMODE_BAYER8X8)
                    uint bx = (uint)fmod(blockCoord.x, 8.0);
                    uint by = (uint)fmod(blockCoord.y, 8.0);
                    threshold = BAYER8[by * 8 + bx] * (1.0 / 64.0);   // 归一化到 0~1
                #else
                    uint bx = (uint)fmod(blockCoord.x, 4.0);
                    uint by = (uint)fmod(blockCoord.y, 4.0);
                    threshold = BAYER4[by * 4 + bx] * (1.0 / 16.0);   // 归一化到 0~1
                #endif

                // 量化步长（一个色阶的高度）
                float levels    = max(_ColorLevels - 1.0, 1.0);
                float quantStep = 1.0 / levels;

                // 将 Bayer 阈值乘以 _DitherStrength 后加到颜色上。
                // 说明：阈值减 0.5 做居中，使抖动不引入整体亮度偏移；
                // 再乘 quantStep，使 _DitherStrength = 1 恰好对应一个量化步长
                // （数学上等价于经典有序量化 floor(c*levels + threshold)/levels，
                //   但避免了正向偏移导致的整体变亮）。
                // ★ 必须在量化之前执行，顺序不可颠倒 ★
                float dither = (threshold - 0.5) * _DitherStrength * quantStep;
                color.rgb += dither;

                // ------------------------------------------------------------------
                // 【步骤 4】色阶量化（Posterize）
                // floor(c * (L-1) + 0.5) / (L-1)，RGB 逐通道
                // ------------------------------------------------------------------
                color.rgb = floor(saturate(color.rgb) * levels + 0.5) / levels;

                // ------------------------------------------------------------------
                // 【步骤 5】Volume 淡入淡出：与原始（未像素化）画面按 _Intensity 混合
                // 原始画面用 Linear 采样原 UV，保证 0 强度时画面完全无损
                // ------------------------------------------------------------------
                float3 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                color.rgb = lerp(original, color.rgb, saturate(_Intensity));

                // 【步骤 6】输出
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
