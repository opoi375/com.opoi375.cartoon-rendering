// ============================================================================
// ScanEffect.shader
// ----------------------------------------------------------------------------
// 以自身为中心的扫描特效（URP RenderGraph 单 Pass，全屏）。
//
// 视觉构成（每一项都能单独关，默认全开但强度可控）：
//   1) 波前        —— 一圈薄薄的高亮壳，半径随时间推进
//   2) 拖尾        —— 波前后面一段指数衰减的余辉
//   3) 扫描区细节  —— 网格 / 点阵 / 水平扫描线，只在"被扫过"的区域显示
//   4) 边缘高亮    —— 深度不连续处描边（廉价线框轮廓），只在被扫过区域
//   5) 辉光        —— 加在波前与细节上的自发光，会被管线 Bloom 接手
//   6) 恢复        —— 波前经过后按 restoreTime 平滑退回原材质
//
// 两种形态：
//   Radial  —— 以扫描中心为球心，向四周扩散。d = length(posWS - origin)
//   Frontal —— 沿扫描方向推进（平面 / 限定扇形）。d = dot(posWS - origin, fwd)
//              扇形范围由 _ScanCosHalfAngle 限制，超出部分不扫描
//
// 为什么用 Blend One OneMinusSrcAlpha（和本包水下后期同一个理由）：
//   RenderGraph 里 activeColor 是外部导入资源，没有依赖关系，读它可能拿到
//   空缓冲。所以这个通道【不读 activeColor】，只写：
//       result = src.rgb + dst.rgb · (1 - src.a)
//   于是只要这样编码：
//       src.a   = a              → 被扫过的强度（0 = 完全恢复原样）
//       src.rgb = tech · a + glow
//   合成后就是 result = dst·(1-a) + tech·a + glow
//   即"向科技色靠拢 a，再叠一层自发光 glow"。a=0 时逐像素恒等于原图，
//   所以关闭 / 未扫到时【一个像素都不变】—— 不会污染无关画面。
//
// 天空像素（depth = 0）直接跳过：扫描是有距离的世界空间效果，套到天空上
// 会变成一条横贯天空的亮带。跳过后 a = 0，画面完全不变。
//
// 【重要】必须声明 ConfigureInput(Depth | Color)，C# 侧已声明。没有它
// RenderGraph 不会把深度图绑进来，SampleSceneDepth 恒为 0，全部像素被当
// 成天空 → 什么都不画，且不报错（本包水下后期踩过这个坑，这里同样适用）。
// ============================================================================
Shader "CartoonRendering/PostProcessing/ScanEffect"
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

        struct ScanAttributes
        {
            uint vertexID : SV_VertexID;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct ScanVaryings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        ScanVaryings ScanVert(ScanAttributes input)
        {
            ScanVaryings output;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

            float2 uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
            output.positionCS = float4(uv * 2.0 - 1.0, UNITY_NEAR_CLIP_VALUE, 1.0);
            output.uv = uv;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "ScanEffect"

            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   ScanVert
            #pragma fragment ScanFrag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // ---- 由 ScanEffectPass 每帧写入 ----
            // 动画（来自 ScanEffectRuntime，控制器推进）
            float4 _ScanOrigin;          // xyz = 扫描中心（世界），w = 当前波前半径
            float4 _ScanForward;         // xyz = 扫描方向（世界，已归一化），w = 进度 0..1
            float  _ScanActive;          // 1 = 正在扫描
            float  _ScanMode;            // 0 = Radial，1 = Frontal
            float  _ScanCosHalfAngle;    // 扇形半角的 cos（Frontal 用）

            // 外观（来自 Volume）
            float4 _ScanColorGlow;       // 扫描色
            float4 _ScanColorEdge;       // 边缘/强调色
            float  _ScanRadius;          // 最大半径（米）
            float  _ScanDistance;        // 最大推进距离（米，Frontal）
            float  _ScanLineWidth;       // 波前厚度（米）
            float  _ScanEdgeIntensity;   // 边缘高亮强度
            float  _ScanGlowIntensity;   // 辉光强度
            float  _ScanTrailLength;     // 拖尾长度（米）
            float  _ScanGridDensity;     // 网格密度（每米格数）
            float  _ScanDotDensity;      // 点阵密度（每米点数）
            float  _ScanDetailIntensity; // 扫描区细节总强度
            float  _ScanRestoreTime;     // 恢复时间（秒）
            float  _ScanNoise;           // 波前噪波扰动强度
            float  _ScanAttenuation;     // 距离衰减指数（0 = 不衰减）
            float  _ScanQuality;         // 0 低 / 1 中 / 2 高
            float  _ScanRestoreCurve;    // 0 线性 / 1 平滑 / 2 指数
            float  _ScanTime;            // 秒（网格滚动 / 噪波演化）
            float  _ScanIntensity;       // 总强度
            float  _ScanFadeOut;         // 结束时整体淡出 1 → 0
            float4 _ScanSceneScaleBias;  // 屏幕 uv → 场景纹理 uv
            float4 _ScanViewSize;        // xy = 目标像素尺寸
            float  _ScanVoxelSize;       // 体素边长（米）
            float  _ScanVoxelFill;       // 体素内部填充强度 0..1
            float  _ScanVoxelReveal;     // 体素逐个点亮的前沿宽度（米）
            float  _ScanDiag;            // 1 = 强制输出纯红（诊断这条通道有没有写进画面）

            float2 ScanSceneUV(float2 uv)
            {
                return uv * _ScanSceneScaleBias.xy + _ScanSceneScaleBias.zw;
            }

            bool ScanIsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth < 1e-5;
            #else
                return rawDepth > 1.0 - 1e-5;
            #endif
            }

            // ---- 便宜的 3D 值噪声（波前抖动 + 细节噪波）-------------------
            float ScanHash31(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float ScanNoise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = ScanHash31(i + float3(0, 0, 0));
                float n100 = ScanHash31(i + float3(1, 0, 0));
                float n010 = ScanHash31(i + float3(0, 1, 0));
                float n110 = ScanHash31(i + float3(1, 1, 0));
                float n001 = ScanHash31(i + float3(0, 0, 1));
                float n101 = ScanHash31(i + float3(1, 0, 1));
                float n011 = ScanHash31(i + float3(0, 1, 1));
                float n111 = ScanHash31(i + float3(1, 1, 1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            float ScanFbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 3; i++)
                {
                    s += a * ScanNoise3(p);
                    p *= 2.03;
                    a *= 0.5;
                }
                return s / 0.875;
            }

            // 恢复曲线：age / restoreTime → 1（刚扫过，全强度）… 0（已恢复）
            float ScanRestoreCurve(float t)
            {
                t = saturate(1.0 - t);                    // 1 → 0
                if (_ScanRestoreCurve < 0.5) return t;                        // 线性
                if (_ScanRestoreCurve < 1.5) return t * t * (3.0 - 2.0 * t);  // 平滑
                return t * t;                                                 // 二次（"停留久、退得快"）
            }

            // 反走样网格线：返回 0..1 的线强度（aa 随屏幕导数自适应，避免摩尔纹）
            float ScanGridLine(float coord, float widthPx)
            {
                float w = fwidth(coord);
                // 距离越远格子越密 → 收敛成 0.5 的平均值，防止远处糊成一片亮墙
                // 注意：局部变量不能叫 line —— 它是 HLSL 的几何着色器图元关键字
                float band = 1.0 - abs(frac(coord) * 2.0 - 1.0);
                float aa   = max(w * widthPx, 1e-4);
                float v    = smoothstep(1.0 - aa, 1.0, band);
                // 导数过大（亚像素格子）时淡出，交由点阵/整体细节承担
                return v * saturate(1.0 - w * 2.0);
            }

            // ------------------------------------------------------------------
            // 体素风格：把世界量化成立方格，画"立体方格"
            //
            // 不是屏幕空间网格 —— 是真正的 3D 立方格：
            //   * 棱线：某两个轴同时接近格面 → 得到立方体的 12 条棱
            //   * 格面：单个轴接近格面 → 得到方格面
            //   * 内部填充：按格哈希给每块一个亮度，读起来像"实心体素块"
            //   * 逐个点亮：按到波前的距离把格子一块块点亮，形成涌动的体素扫描
            // ------------------------------------------------------------------
            float ScanVoxel(float3 p, float cell, float front, float dist,
                            out float fillOut, out float faceOut)
            {
                float3 g  = p / max(cell, 1e-3);
                float3 f  = frac(g);
                float3 cellIdx = floor(g);

                // 到各轴格面的距离（米）
                float3 nrm = min(f, 1.0 - f) * cell;
                float  w   = max(fwidth(p.x), 1e-4) * 1.2;   // 线宽随屏幕导数自适应
                float3 on  = 1.0 - smoothstep(0.0, w, nrm);  // 该轴贴近格面

                // 棱 = 两个及以上轴同时贴面；面 = 单个轴贴面
                float s = on.x + on.y + on.z;
                float edge = saturate(s - 1.0);      // >=2 个轴 → 立方体棱
                float face = saturate(s) - edge;     // 恰好 1 个轴 → 格面

                // 每块体素的哈希亮度：让"实心块"有明暗颗粒感
                float h = frac(sin(dot(cellIdx, float3(12.9898, 78.233, 45.164))) * 43758.5453);

                // 逐个点亮：波前每越过一层格子，那块就亮起来
                float cellCenterDist = dist + (0.5 - f.x) * cell;   // 近似到格心
                float reveal = saturate((front - cellCenterDist) / max(cell * 1.5, 0.01));

                fillOut = h * reveal;
                faceOut = face * reveal;
                return max(edge, face * 0.45) * reveal;
            }

            half4 ScanFrag(ScanVaryings input) : SV_Target
            {
                    // 诊断三态：
                    //   _ScanDiag = 1 → 无条件纯红（证明这条 draw 能写进画面）
                    //   _ScanDiag = 2 → 未激活时输出纯绿（证明 runtime.active 到底有没有）
                    if (_ScanDiag > 0.5 && _ScanDiag < 1.5) return half4(1.0, 0.0, 0.0, 0.0);
                float2 uv = input.uv;
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 关闭时逐像素恒等：src.a = 0, src.rgb = 0 → result = dst
                if (_ScanActive < 0.5 || _ScanIntensity < 1e-4 || _ScanFadeOut < 1e-4)
                {
                    if (_ScanDiag > 1.5) return half4(0.0, 1.0, 0.0, 0.0);   // 诊断：绿 = 没激活
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                float2 sceneUV = ScanSceneUV(uv);
                float rawDepth = SampleSceneDepth(sceneUV);

                // 天空不扫（见文件头）
                if (ScanIsSky(rawDepth))
                    return half4(0.0, 0.0, 0.0, 0.0);

                float3 posWS = ComputeWorldSpacePosition(sceneUV, rawDepth, UNITY_MATRIX_I_VP);
                float3 toPix = posWS - _ScanOrigin.xyz;

                // ---- 1. 距离度量与扇形裁切 ----
                float d;      // 沿扫描方向的推进距离
                float radialR;// 用于衰减的"离中心多远"
                float sector  = 1.0;

                if (_ScanMode < 0.5)
                {
                    d       = length(toPix);
                    radialR = d;
                }
                else
                {
                    d       = dot(toPix, _ScanForward.xyz);
                    radialR = d;
                    // 扇形角度范围：超出半角不扫描（软过渡，避免硬切一条边）
                    float len = max(length(toPix), 1e-4);
                    float c   = dot(toPix / len, _ScanForward.xyz);
                    sector = saturate((c - _ScanCosHalfAngle) / 0.08);
                }

                if (d < 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // ---- 2. 噪波扰动：把波前半径按方向抖动，别是一把完美的圆规 ----
                float front = _ScanOrigin.w;
                if (_ScanNoise > 1e-4)
                {
                    float3 nPos = toPix * 0.02 + float3(0.0, _ScanTime * 0.15, 0.0);
                    float  n    = ScanFbm(nPos) - 0.5;
                    // 低质量只扰一层，高质量扰两层（更碎）
                    if (_ScanQuality > 0.5)
                        n += 0.35 * (ScanNoise3(nPos * 3.7) - 0.5);
                    front += n * _ScanNoise * 60.0;
                }

                // ---- 3. 波前 / 已扫过区域 / 恢复 ----
                float behind = front - d;                    // > 0 = 波前已越过
                float visited = saturate(behind * 0.08 + 0.5); // 波前后一点点的软过渡

                float speedGuess = max(_ScanRadius, _ScanDistance) * 0.9;
                float age      = max(behind, 0.0) / max(speedGuess, 1.0) * 10.0;
                float restore  = ScanRestoreCurve(age / max(_ScanRestoreTime, 0.01));

                // 距离衰减：越远越弱（读作"信号衰减"）
                float atten = _ScanAttenuation < 1e-4
                    ? 1.0
                    : pow(saturate(1.0 - radialR / max(_ScanRadius, _ScanDistance)), _ScanAttenuation);

                float wet = visited * restore * sector * atten * _ScanIntensity * _ScanFadeOut;

                // 波前亮壳（高斯剖面 + 屏幕导数下限，避免细线闪烁）
                float lw   = max(_ScanLineWidth, 0.05);
                float g    = (d - front) / lw;
                float wave = exp(-g * g);
                // 亚像素时按 fwidth 摊开，比硬 exp 稳（不闪）
                wave = max(wave, exp(-pow((d - front) / max(fwidth(d) * 1.5, lw), 2.0)) * 0.35);

                // 拖尾：波前后面一段指数余辉
                float trail = exp(-max(behind, 0.0) / max(_ScanTrailLength, 0.1)) * step(0.0, behind);

                // ---- 4. 扫描区细节：网格 / 点阵 / 水平扫描线 ----
                float detail = 0.0;
                float voxelFill = 0.0;      // 体素内部填充（单独加，染色靠它）
                if (_ScanDetailIntensity > 1e-4)
                {
                    if (_ScanVoxelSize > 0.0)
                    {
                        // ---- 体素立体方格 ----
                        float face;
                        float wire = ScanVoxel(posWS, _ScanVoxelSize, front, d, voxelFill, face);
                        voxelFill *= _ScanVoxelFill;
                        detail = wire * 0.85 + face * 0.25;
                    }
                    else
                    {
                        // ---- 基础线框：三轴网格 + 点阵 + 滚动扫描线 ----
                        float3 gp = posWS * max(_ScanGridDensity, 1e-4);
                        float gx = ScanGridLine(gp.x, 1.0);
                        float gz = ScanGridLine(gp.z, 1.0);
                        float gy = ScanGridLine(gp.y, 1.0);
                        float grid = max(max(gx, gz), gy * 0.5);

                        float3 cell = frac(posWS * max(_ScanDotDensity, 1e-4)) - 0.5;
                        float  dotM = 1.0 - smoothstep(0.06, 0.16, length(cell));
                        float  dotFade = saturate(1.0 - fwidth(posWS.x * _ScanDotDensity) * 3.0);

                        float scan = 0.5 + 0.5 * sin(posWS.y * 3.0 - _ScanTime * 6.0);
                        scan = pow(scan, 6.0);

                        detail = grid * 0.65 + dotM * dotFade * 0.5 + scan * 0.35;
                    }
                }

                // ---- 5. 边缘高亮（廉价线框）：深度不连续处描边 ----
                float edge = 0.0;
                if (_ScanEdgeIntensity > 1e-4 && _ScanQuality > 0.5)
                {
                    float2 texel = 1.0 / max(_ScanViewSize.xy, float2(1.0, 1.0));
                    float dR = LinearEyeDepth(SampleSceneDepth(sceneUV + float2(texel.x, 0.0)), _ZBufferParams);
                    float dU = LinearEyeDepth(SampleSceneDepth(sceneUV + float2(0.0, texel.y)), _ZBufferParams);
                    float dz = LinearEyeDepth(rawDepth, _ZBufferParams);
                    // 相对差（除以自身深度）→ 近处细、远处也不会整片糊掉
                    float eR = abs(dR - dz) / max(dz, 0.05);
                    float eU = abs(dU - dz) / max(dz, 0.05);
                    edge = saturate(saturate(eR + eU) * 8.0);
                }

                // ---- 合成：a 为"被扫过的强度"，rgb 为科技色 + 自发光 ----
                // 基础染色从 0.42 降到 0.12：原来那一项会把整屏刷成一层厚青色，
                // 网格 / 点阵全被盖住（实测就是这样）。现在让线框与填充主导，
                // 底色只负责"这块区域确实被扫过"的轻微偏色。
                float a = saturate(wet * (0.12 + detail * _ScanDetailIntensity * 0.9 + edge * _ScanEdgeIntensity));

                float3 tech = _ScanColorGlow.rgb;
                float3 emph = _ScanColorEdge.rgb;

                float3 additive = emph * (wave * _ScanGlowIntensity)
                                + emph * (trail * _ScanGlowIntensity * 0.5)
                                + emph * (edge * wet * _ScanEdgeIntensity * 1.2)
                                + tech * (detail * wet * _ScanDetailIntensity * 0.6)
                                + tech * (voxelFill * wet * _ScanDetailIntensity * 0.9);

                // a=0 时 result = dst + 0，逐像素恒等
                float3 src = tech * a + additive;
                return half4(src, a);
            }
            ENDHLSL
        }
    }
}
