// ============================================================================
// SSIL.shader
// ----------------------------------------------------------------------------
// 屏幕空间间接光照 + 可见性位掩码（SSILVB）。
//
// 论文："Screen Space Indirect Lighting with Visibility Bitmask"
//       Olivier Therrien, Yannick Levesque, Guillaume Gilet (arXiv:2301.11376)
//
// 传统 GTAO 用"两个地平线角"描述遮挡，本方法把每个切片的半球离散成 32 个
// 扇区，用一个 uint 位掩码记录哪些扇区被挡。好处：
//   1. 每个采样点给表面一个**固定厚度**，光可以从薄物体（栅栏、栏杆）背后
//      绕过去 —— 不需要 falloff，也不会出现 GTAO 的过度压暗 / 光晕
//   2. 位掩码自带方向信息，所以除了 AO 还能顺手收集间接光（论文 Algorithm 1）
//
// 算法（论文 Algorithm 1 逐行对应）：
//   每个切片（slice）在屏幕平面取一个方向 omega，从中心像素向外采样：
//     - 每个样本有"前表面"角 θf 和"后表面"角 θb（= 前表面沿视线向后推
//       thickness 得到），两角之间就是这个样本占的立体角
//     - 把 [θmin, θmax] 映射成扇区区间，置位到该切片的位掩码 b_i
//     - 该样本能贡献的间接光 = 它盖住、且**尚未被更近样本挡住**的扇区占比
//       （论文第 23 行：countbits(b_j & ~b_i) / Nb）× 光照缓冲采样值
//       × (n_p · l)(n_j · -l)
//     - b_i |= b_j（后到的样本被先到的挡住）
//   AO = 1 - countbits(b_i)/Nb，对所有切片取平均
//
// 角度约定：θ 是样本方向在切片平面内、相对**投影法线**的有符号角，
// 半球域 [-π/2, π/2] 映射到 [0, 1]，正好铺满 32 个扇区（论文"bit field
// is centered on projected normal"）。切平面以下（表面背后）的角度夹到边缘，
// 天然实现了"无限厚只挡到掠射角为止"。
//
// Pass 0  Trace      半分辨率追踪：RGB = 间接光，A = 可见性（1 = 全开）
// Pass 1  BlurH      横向双边模糊（深度相似度权重，防止跨深度边缘渗色）
// Pass 2  BlurV      纵向双边模糊
// Pass 3  Composite  深度感知双边上采样，Blend DstColor Zero 乘回场景色
//                     —— 只"写"不"读" activeColor，绕开 RenderGraph 里
//                     读 activeColor 读到空缓冲的坑（同 VolumetricLight 注释）
// Pass 4  Debug      调试视图，Blend Off 直接盖屏
//
// 光照缓冲用的是 _CameraOpaqueTexture（不透明场景色副本）：论文用的是单独的
// 直接光 G-Buffer，这里用"已着色的场景色"当直接光近似 —— 卡通渲染下反而更
// 合适，反弹过来的光直接带着物体的固有色。
// ============================================================================
Shader "CartoonRendering/PostProcessing/SSIL"
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
        //  全屏三角形顶点着色器（自带，不依赖 Blit.hlsl / TextureXR）
        // ---------------------------------------------------------------
        struct SsilAttributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct SsilVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        SsilVaryings SsilVert(SsilAttributes input)
        {
            SsilVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            // vertexID 0/1/2 → uv (0,0) (2,0) (0,2) → 覆盖整个裁剪空间的大三角形
            float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            output.uv = uv;
            return output;
        }

        // ---------------------------------------------------------------
        //  Uniform（由 SSILPass 用 MaterialPropertyBlock 每帧喂）
        // ---------------------------------------------------------------
        float _SSILRadius;          // 采样半径（世界米）
        float _SSILThickness;       // 表面厚度（世界米）
        float _SSILSamples;         // 每个切片向外采样次数
        float _SSILSlices;          // 切片（屏幕方向）数量
        float _SSILJitter;          // 抖动强度 0~1
        float _SSILMaxDistance;     // 效果最远距离（米）
        float _SSILDistanceFade;    // 距离淡出起点（最远距离的百分之几）
        float _SSILAoStrength;      // AO 强度 0~1
        float _SSILIndirectStrength;// 间接光强度
        float _SSILAoBanding;       // 卡通分层档数（0 = 平滑）
        float _SSILBlurRadius;      // 模糊步长（低分辨率纹素）
        float _SSILDebug;           // 0 = 关，1 = AO，2 = 间接光
        float4 _SSILIndirectTint;   // 间接光染色（rgb）
        float4 _SSILTexelSize;      // xy = 1/低分辨率尺寸，zw = 低分辨率尺寸

        // 模糊 / 合成阶段读的追踪结果（半分辨率，RGB = 间接光，A = 可见性）
        TEXTURE2D_X(_SSILTex);

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

        // 天空判定：远裁剪面清空值 = 天空。UNITY_REVERSED_Z 下远平面是 0。
        // （URP 自己的 SSAO.hlsl 也有一个同名常量，但没有导出，这里自己判）
        bool IsSkyDepth(float rawDepth)
        {
        #if UNITY_REVERSED_Z
            return rawDepth <= 1e-7;
        #else
            return rawDepth >= 0.9999999;
        #endif
        }

        // ---------------------------------------------------------------
        //  位掩码工具（论文 Algorithm 1 第 18~19 行）
        // ---------------------------------------------------------------
        static const uint kSectorCount = 32u;

        // 手写 popcount：不依赖 countbits 内建（部分 GLES 后端 SM 3.5 没有）
        // https://graphics.stanford.edu/~seander/bithacks.html
        uint CountBits(uint v)
        {
            v = v - ((v >> 1u) & 0x55555555u);
            v = (v & 0x33333333u) + ((v >> 2u) & 0x33333333u);
            v = (v + (v >> 4u)) & 0x0F0F0F0Fu;
            return (v * 0x01010101u) >> 24u;
        }

        // 把归一化角度区间 [minA, maxA]（0~1，对应 [-π/2, π/2]）置进 32 位掩码。
        // 起始扇区向下取整、扇区数向上取整 = 论文的 round 准则（半覆盖即命中）。
        uint UpdateSectors(float minA, float maxA)
        {
            minA = saturate(minA);
            maxA = saturate(maxA);

            uint startBit = min((uint)(minA * kSectorCount), kSectorCount - 1u);
            uint span     = (uint)ceil((maxA - minA) * kSectorCount);
            span = min(span, kSectorCount - startBit);
            if (span == 0u)
                return 0u;

            // span == 32 时移位量为 0，直接全 1；否则取低 span 位再左移
            uint bits = (span >= kSectorCount) ? 0xFFFFFFFFu
                                               : (0xFFFFFFFFu >> (kSectorCount - span));
            return bits << startBit;
        }

        // 视图空间法线（_CameraNormalsTexture 存的是世界空间，转到视图空间
        // 与视图空间位置同一套数学）
        float3 SampleNormalVS(float2 uv)
        {
            float3 normalWS = SampleSceneNormals(uv);
            return normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
        }

        // 追踪缓冲的深度感知上采样：4 抽头，按与中心像素的深度相似度加权
        half4 UpsampleSSIL(float2 uv, float centerEye)
        {
            float2 texel  = _SSILTexelSize.xy;
            float2 baseUV = uv - texel * 0.5;

            half4  acc  = half4(0, 0, 0, 0);
            float  wsum = 0.0;

            [unroll]
            for (int y = 0; y <= 1; y++)
            {
                [unroll]
                for (int x = 0; x <= 1; x++)
                {
                    float2 s = baseUV + float2(x, y) * texel;
                    float  eye = LinearEyeDepth(SampleSceneDepth(s), _ZBufferParams);
                    float  w   = saturate(1.0 - abs(eye - centerEye) / (0.15 * centerEye + 0.25));
                    acc  += SAMPLE_TEXTURE2D_X(_SSILTex, sampler_LinearClamp, s) * w;
                    wsum += w;
                }
            }

            return wsum > 1e-4 ? acc / wsum
                               : SAMPLE_TEXTURE2D_X(_SSILTex, sampler_LinearClamp, uv);
        }

        // 双边模糊：高斯权重 × 深度相似度权重，避免跨深度边缘把遮挡渗到
        // 背景 / 天空上。dir 已含 _SSILBlurRadius 步长。
        half4 BilateralBlur(float2 uv, float2 dir)
        {
            float2 texel = _SSILTexelSize.xy * dir;
            float  centerEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            float  tol = 0.15 * centerEye + 0.25;

            half4 acc  = SAMPLE_TEXTURE2D_X(_SSILTex, sampler_LinearClamp, uv) * 0.27;
            float wsum = 0.27;

            [unroll]
            for (int i = 1; i <= 2; i++)
            {
                float  offset = (float)i;
                float  gw     = (i == 1) ? 0.23 : 0.12;
                float2 s0 = uv + texel * offset;
                float2 s1 = uv - texel * offset;
                float  e0 = LinearEyeDepth(SampleSceneDepth(s0), _ZBufferParams);
                float  e1 = LinearEyeDepth(SampleSceneDepth(s1), _ZBufferParams);
                float  w0 = gw * saturate(1.0 - abs(e0 - centerEye) / tol);
                float  w1 = gw * saturate(1.0 - abs(e1 - centerEye) / tol);

                acc  += SAMPLE_TEXTURE2D_X(_SSILTex, sampler_LinearClamp, s0) * w0;
                acc  += SAMPLE_TEXTURE2D_X(_SSILTex, sampler_LinearClamp, s1) * w1;
                wsum += w0 + w1;
            }

            return acc / max(wsum, 1e-4);
        }
        ENDHLSL

        // ====================================================================
        //  Pass 0：可见性位掩码追踪（半分辨率）
        // ====================================================================
        Pass
        {
            Name "SSILTrace"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   SsilVert
            #pragma fragment FragTrace
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            half4 FragTrace(SsilVaryings input) : SV_Target
            {
                float2 uv       = input.uv;
                float  rawDepth = SampleSceneDepth(uv);
                float  eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);

                float fadeStart = _SSILMaxDistance * saturate(_SSILDistanceFade);
                float fadeLen   = max(_SSILMaxDistance - fadeStart, 1e-3);
                float fade      = saturate(1.0 - (eyeDepth - fadeStart) / fadeLen);

                // 天空 / 超出作用距离：完全不动画面（可见性 1，间接光 0）
                if (IsSkyDepth(rawDepth) || fade <= 0.0)
                    return half4(0.0, 0.0, 0.0, 1.0);

                // ---- 中心像素的视图空间几何 -----------------------------
                float3 posVS    = ComputeViewSpacePosition(uv, rawDepth, UNITY_MATRIX_I_P);
                float3 normalVS = SampleNormalVS(uv);
                float3 viewDir  = normalize(-posVS);   // 指向相机

                // ---- 采样布局 ------------------------------------------
                // 静态 IGN 抖动：以像素坐标为种子，静止画面不会闪
                float jitter = InterleavedGradientNoise(input.positionCS.xy, 0) - 0.5;
                jitter *= saturate(_SSILJitter);

                // 世界半径 r 投到 uv：x 方向 0.5*r*P00/z；y 方向按纵横比补齐，
                // 保证屏幕上是圆而不是椭圆
                float sampleScale = 0.5 * _SSILRadius * UNITY_MATRIX_P._m00 / eyeDepth;
                float2 aspect     = float2(1.0, _ScreenParams.x / _ScreenParams.y);

                int sliceCount  = (int)clamp(_SSILSlices, 1.0, 16.0);
                int sampleCount = (int)clamp(_SSILSamples, 1.0, 32.0);
                float sliceRotation = TWO_PI / (float)sliceCount;

                float  visibility = 0.0;
                float3 lighting   = float3(0.0, 0.0, 0.0);

                [loop]
                for (int slice = 0; slice < sliceCount; slice++)
                {
                    // ---- 切片平面 -------------------------------------
                    float phi   = sliceRotation * (slice + 0.5 + jitter) + PI;
                    float2 omega = float2(cos(phi), sin(phi));
                    float3 dirVS  = float3(omega, 0.0);

                    float3 axis = cross(dirVS, viewDir);
                    float  axisLen2 = dot(axis, axis);
                    if (axisLen2 < 1e-6)
                        continue;                       // 切片退化（dir ∥ viewDir）
                    axis *= rsqrt(axisLen2);

                    // 切片内的投影法线 + 与之垂直的切向（角度基准）
                    float3 projN = normalVS - axis * dot(normalVS, axis);
                    float  projLen = length(projN);
                    if (projLen < 1e-4)
                        continue;                       // 法线与切片平面垂直
                    float3 projNhat = projN / projLen;
                    float3 tangent  = cross(axis, projNhat);

                    uint sliceMask = 0u;

                    [loop]
                    for (int s = 0; s < sampleCount; s++)
                    {
                        // 从近到远均匀铺开；起点略抬高避免采到自己
                        float stepT = max((s + 0.5 + jitter) / (float)sampleCount, 0.02);

                        float2 sampleUV = uv + stepT * sampleScale * omega * aspect;
                        if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                            continue;                   // 屏幕外 = 没有信息

                        float sampleRawDepth = SampleSceneDepth(sampleUV);
                        if (IsSkyDepth(sampleRawDepth))
                            continue;

                        float3 samplePosVS = ComputeViewSpacePosition(sampleUV, sampleRawDepth, UNITY_MATRIX_I_P);
                        float3 delta = samplePosVS - posVS;
                        float  dist  = length(delta);
                        if (dist < 1e-4)
                            continue;

                        float3 l = delta / dist;        // 中心 → 样本
                        // 后表面：沿视线向后推 thickness（表面有厚度，光可从背后过）
                        float3 backDir = normalize(delta - viewDir * _SSILThickness);

                        // 切片内相对投影法线的有符号角 ∈ [-π/2, π/2]（半球域）。
                        // 夹角在切平面以下（表面背后）时夹到边缘：样本只挡到掠射角。
                        float thetaF = atan2(dot(l, tangent), dot(l, projNhat));
                        float thetaB = atan2(dot(backDir, tangent), dot(backDir, projNhat));
                        thetaF = clamp(thetaF, -HALF_PI, HALF_PI);
                        thetaB = clamp(thetaB, -HALF_PI, HALF_PI);

                        float minA = (min(thetaF, thetaB) + HALF_PI) / PI;
                        float maxA = (max(thetaF, thetaB) + HALF_PI) / PI;

                        uint sampleMask = UpdateSectors(minA, maxA);

                        // 论文 Algorithm 1 第 23 行：该样本盖住、且之前没被挡住的
                        // 扇区占半球的比例，就是它能贡献的间接光份额
                        uint visible = sampleMask & ~sliceMask;
                        float w = (float)CountBits(visible) * (1.0 / (float)kSectorCount);
                        if (w > 0.0)
                        {
                            float3 sampleLight = SampleSceneColor(sampleUV);
                            float3 sampleNVS   = SampleNormalVS(sampleUV);
                            float geom = saturate(dot(normalVS, l)) *
                                         saturate(dot(sampleNVS, -l));
                            lighting += w * sampleLight * geom;
                        }

                        sliceMask |= sampleMask;
                    }

                    visibility += 1.0 - (float)CountBits(sliceMask) * (1.0 / (float)kSectorCount);
                }

                visibility /= (float)sliceCount;
                lighting   /= (float)sliceCount;

                // 距离淡出：远处样本密度不够，不淡出会在远端出噪点
                visibility = lerp(1.0, visibility, fade);
                lighting   *= fade;

                return half4(lighting, visibility);
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 1：横向双边模糊
        // ====================================================================
        Pass
        {
            Name "SSILBlurH"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   SsilVert
            #pragma fragment FragBlurH
            #pragma target 3.5

            half4 FragBlurH(SsilVaryings input) : SV_Target
            {
                return BilateralBlur(input.uv, float2(_SSILBlurRadius, 0.0));
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 2：纵向双边模糊
        // ====================================================================
        Pass
        {
            Name "SSILBlurV"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   SsilVert
            #pragma fragment FragBlurV
            #pragma target 3.5

            half4 FragBlurV(SsilVaryings input) : SV_Target
            {
                return BilateralBlur(input.uv, float2(0.0, _SSILBlurRadius));
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 3：上采样 + 乘回场景色
        //  Blend DstColor Zero = dst * src：等于把 (1 + 间接光) * AO 乘到
        //  场景色上，不需要回读目标（RenderGraph 里读 activeColor 是坑）
        // ====================================================================
        Pass
        {
            Name "SSILComposite"
            Blend DstColor Zero, Zero One

            HLSLPROGRAM
            #pragma vertex   SsilVert
            #pragma fragment FragComposite
            #pragma target 3.5

            half4 FragComposite(SsilVaryings input) : SV_Target
            {
                float  centerEye = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                half4  ssil      = UpsampleSSIL(input.uv, centerEye);

                float visibility = ssil.a;

                // 卡通分层：把可见性量化成若干档，硬边 AO（0 = 平滑）
                float bands = _SSILAoBanding;
                if (bands >= 1.0)
                    visibility = floor(visibility * bands + 0.5) / bands;

                float  ao       = lerp(1.0, visibility, saturate(_SSILAoStrength));
                float3 indirect = ssil.rgb * _SSILIndirectStrength * _SSILIndirectTint.rgb;

                // 乘性混合（不是加雾）：AO 压暗 + 间接光提亮并带色
                float3 factor = ao * (1.0 + indirect);
                return half4(factor, 1.0);
            }
            ENDHLSL
        }

        // ====================================================================
        //  Pass 4：调试视图（Blend Off 直接盖屏）
        // ====================================================================
        Pass
        {
            Name "SSILDebug"
            Blend Off

            HLSLPROGRAM
            #pragma vertex   SsilVert
            #pragma fragment FragDebug
            #pragma target 3.5

            half4 FragDebug(SsilVaryings input) : SV_Target
            {
                float centerEye = LinearEyeDepth(SampleSceneDepth(input.uv), _ZBufferParams);
                half4 ssil      = UpsampleSSIL(input.uv, centerEye);

                int dbg = (int)(_SSILDebug + 0.5);
                if (dbg == 1)   // 可见性（白 = 无遮挡）
                    return half4(ssil.a, ssil.a, ssil.a, 1.0);
                if (dbg == 2)   // 间接光
                    return half4(ssil.rgb * _SSILIndirectStrength * _SSILIndirectTint.rgb, 1.0);
                return half4(1.0, 1.0, 1.0, 1.0);
            }
            ENDHLSL
        }
    }
}
