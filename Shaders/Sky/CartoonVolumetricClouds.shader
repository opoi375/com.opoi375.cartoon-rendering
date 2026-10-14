// ============================================================================
// CartoonVolumetricClouds.shader — 体积云（离散云团模型，重写于 v1.8.0）
// ----------------------------------------------------------------------------
// 为什么是【离散云团实例】而不是"阈值一个连续噪声场"：
//
// 旧模型把 3D 噪声按 coverage 阈值化。连续随机场有一个渗流阈值（约 0.3），
// 覆盖率一过它整块场就【拓扑连通】了 —— 所以不管怎么调噪声、怎么加侵蚀，
// 天空看起来都是一整团/一整块甲板。这是拓扑问题，不是参数问题。
//
// 现在的模型：把世界平面切成 cell（间距 = volCloudSpacing），每个 cell 最多
// 住一朵云（由独立哈希决定它存不存在、多大、多厚）。密度 = 各候选云团的
// 椭球体剖面取最大值。
//   · cell 之间【天然有留白】→ 拓扑上就是离散的朵
//   · 每朵云有自己的半径/厚度/存在性 → 大小不一、有远有近
//   · 半径剖面 (1-h²)^dome：底部平（凝结面）、顶部收成圆顶 —— 积云的读法
//   · 轮廓用低频 FBM 做域扭曲 + 边缘带高频侵蚀 → 卷曲的椰菜花边
//
// 渲染：半分辨率 RT → 分步积分（相位函数 + 多重散射 + 能量守恒的薄片积分）
//       → cel 量化（风格化明暗分区）→ 大气透视 → 时序累积 → 天空盒合成。
//
// 参数全部由 CartoonSkyboxFeature.PushCloudParams 每帧推送，没有材质面板。
// ============================================================================
Shader "CartoonRendering/CartoonVolumetricClouds"
{
    Properties { }

    SubShader
    {
        Tags
        {
            "Queue"           = "Background"
            "RenderType"      = "Background"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "CartoonVolumetricClouds"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "../Library/WorldBend.hlsl"

            // ----------------------------------------------------------------
            // Uniforms（由 CartoonSkyboxFeature 每帧推送）
            // ----------------------------------------------------------------
            float4x4 _CartoonInvVP;
            float4 _CartoonSunDirection;   // 光照方向：太阳 -> 场景
            float4 _CartoonSunColor;
            float4 _CloudTint;

            // x = 云底高度(世界 Y)，y = 单朵云最大厚度，z = cell 间距，w = 覆盖率
            float4 _CloudShapeParams;
            // x = 半径占 cell 的比例，y = 半径随机度，z = 厚度随机度，w = 边缘侵蚀强度
            float4 _CloudShapeParams2;
            // x = 圆顶指数(0.25 平盘 → 0.7 圆顶)，y = 轮廓域扭曲幅度，z = 噪声频率，w = 密度
            float4 _CloudShapeParams3;

            // x = 步进次数，y = 光照步进次数，z = 云底到云顶的密度衰减，w = 未用
            float4 _CloudMarchParams;
            // x = 风速，yz = 风向
            float4 _CloudWind;
            // x = cel 分区数，y = 分区软化，z = 银边强度，w = 银边聚拢度
            float4 _CloudShadeParams;
            // x = 前叶 g，y = 后叶 g，z = 前叶权重，w = 多重散射倍频数
            float4 _CloudScatterParams;
            // x = 大气透视强度，y = 大尺度天气调制强度，zw = 未用
            float4 _CloudProfileParams;

            float4 _CloudRTSize;
            float  _CloudHistoryWeight;
            float4x4 _CloudPrevVP;
            float  _CloudDebug;
            TEXTURE2D(_CloudHistoryTex); SAMPLER(sampler_CloudHistoryTex);

            // 世界弯曲联动（WorldBendController 全局推送）
            float _WorldBendCloudScale;
            float _WorldBendCloudMaxDroop;

            float CloudBendDroop(float2 posXZ, float2 camXZ)
            {
                return min(WorldBendOffsetFrom(posXZ, camXZ) * _WorldBendCloudScale,
                           _WorldBendCloudMaxDroop);
            }

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float2 pos = float2((input.vertexID == 1) ? 3.0 : -1.0,
                                    (input.vertexID == 2) ? 3.0 : -1.0);
                o.positionCS = float4(pos, 0.0, 1.0);
                return o;
            }

            // =================================================================
            // 哈希 / 程序化 3D 噪声
            // =================================================================
            // 体积云重写成离散云团之后不再需要烘焙噪声图：密度形状由 cell
            // 实例给出，噪声只负责【扭曲轮廓 + 啃边缘】，程序化就够，而且
            // 省掉了"贴图引用没接上就什么都不画"这一整类静默失败。
            float Hash13(float3 p3)
            {
                p3 = frac(p3 * 0.1031);
                p3 += dot(p3, p3.zyx + 31.32);
                return frac((p3.x + p3.y) * p3.z);
            }

            float3 Hash32(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yxz + 33.33);
                return frac((p3.xxy + p3.yzz) * p3.zyx);
            }

            // 三线性插值的值噪声（1 个角落哈希 × 8）
            float VNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i + float3(0, 0, 0));
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y),
                            lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            float FBM(float3 p, int octaves)
            {
                float sum = 0.0, amp = 0.5, norm = 0.0;
                for (int o = 0; o < 4; o++)
                {
                    if (o >= octaves) break;
                    sum += amp * VNoise(p);
                    norm += amp;
                    amp *= 0.5;
                    p *= 2.07;
                    p.xz = p.zx;             // 每档换轴，避免轴向条纹
                }
                return sum / max(norm, 1e-4);
            }

            // =================================================================
            // 相位函数 + 多重散射
            // =================================================================
            float HGSingle(float cosT, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * PI * pow(max(1e-6, 1.0 + g2 - 2.0 * g * cosT), 1.5));
            }

            float CloudPhaseRaw(float cosT, float atten)
            {
                float gF = _CloudScatterParams.x * atten;
                float gB = _CloudScatterParams.y * atten;
                return lerp(HGSingle(cosT, gB), HGSingle(cosT, gF), _CloudScatterParams.z);
            }

            // 以 90 度方向归一化 → 相位变成"以 1 为中心的角度调制"。
            // 本包的太阳色是风格化的，直接套物理量纲会把整体亮度压低好几倍。
            float CloudPhase(float cosT)
            {
                return CloudPhaseRaw(cosT, 1.0) / max(CloudPhaseRaw(0.0, 1.0), 1e-6);
            }

            // Wrenninge 的倍频多重散射：把光学深度按一串逐倍衰减的指数项求和，
            // 每重同时衰减相位各向异性。除以"零深度"的和做归一化 —— 不归一化
            // 的话倍频数一多整片天跟着变亮，云底被抬到跟云顶一样，亮顶暗底的
            // 对比就没了（v1.7.0 实测踩过这个坑）。
            float CloudMultiScatter(float opticalDepth, float cosT)
            {
                int octaves = (int)clamp(_CloudScatterParams.w, 1.0, 4.0);
                float acc = 0.0, norm = 0.0;
                float a = 1.0, b = 1.0, c = 1.0;
                [loop] for (int i = 0; i < 4; i++)
                {
                    if (i >= octaves) break;
                    acc  += a * exp(-opticalDepth * b) * CloudPhaseRaw(cosT, c);
                    norm += a * CloudPhaseRaw(cosT, c);
                    a *= 0.5; b *= 0.5; c *= 0.5;
                }
                return acc / max(norm, 1e-6);
            }

            // =================================================================
            // 环境光 / cel 量化
            // =================================================================
            // 环境项只沿云的竖向高度插值（下暗上亮）。云底朝下，看到的天空立体
            // 角小、又被自身遮住 —— 积云最标志性的"亮顶 + 暗平底"就靠这个。
            float3 CloudAmbient(float h01, float3 sunDir)
            {
                // 取太阳方向两侧的天空色做上下差，跟天空盒的日/黄昏/夜晚
                // 调色板共用同一套 blend（BlendPalette 由太阳高度推导）。
                float sunY = -normalize(sunDir).y;
                float dayW = saturate((sunY - (-0.15)) / 0.45);
                float3 up   = lerp(float3(0.16, 0.22, 0.38), float3(0.36, 0.55, 0.86), dayW);
                float3 down = lerp(float3(0.05, 0.07, 0.13), float3(0.62, 0.76, 0.92), dayW);
                return lerp(down, up, saturate(h01)) * _CloudTint.rgb;
            }

            float SoftQuantizeSteps(float x, float steps, float softness)
            {
                float scaled = saturate(x) * max(steps, 1.0);
                float band   = floor(scaled);
                float f      = scaled - band;
                float soft   = smoothstep(0.5 - softness, 0.5 + softness, f);
                return (band + soft) / max(steps, 1.0);
            }

            // =================================================================
            // 密度：离散云团实例
            // =================================================================
            // outH01  = 该点在【命中云团内部】的归一化高度（0=底 1=顶）
            // outAlive= 该云团的存在权重（覆盖率滑块用，平滑淡入不 pop）
            // outEdge = 命中的边带权重，调试视图用
            float CloudDensity(float3 p, float2 windOffset, float2 camXZ,
                               out float outH01, out float outAlive, out float outEdge)
            {
                outH01 = 0.5; outAlive = 1.0; outEdge = 0.0;

                float baseY   = _CloudShapeParams.x;
                float maxThick= max(_CloudShapeParams.y, 1.0);
                float spacing = max(_CloudShapeParams.z, 1.0);
                float cover   = _CloudShapeParams.w;

                float sizeFrac = _CloudShapeParams2.x;
                float sizeJit  = _CloudShapeParams2.y;
                float thickJit = _CloudShapeParams2.z;
                float detail   = _CloudShapeParams2.w;

                float dome     = _CloudShapeParams3.x;
                float warpAmp  = _CloudShapeParams3.y;
                float freq     = max(_CloudShapeParams3.z, 1e-5);

                // 世界弯曲：按【世界坐标】算（弯曲是相对相机的世界距离的函数，
                // 不该跟着风偏移走）
                float droop = CloudBendDroop(p.xz, camXZ);

                // 风：整片云团网格随风平移（云是会走的，不是原地变形）
                float2 xz = p.xz + windOffset;

                // ---- 大尺度天气调制 -------------------------------------
                // 只按 cell 哈希决定"这个 cell 有没有云"是【均匀随机散布】，
                // 天上会呈现出"到处都有云朵"的撒糖感。真实天空是有云系的：
                // 这边成片、那边一片空。用一个很低频的噪声（波长 = 8 个 cell）
                // 把本地覆盖率往上/下推，就有了天气分区 —— 程序化实现，替代
                // 已删除的 2D 天气贴图，因此没有任何贴图引用会失联。
                float weather = _CloudProfileParams.y;
                if (weather > 0.001)
                {
                    float wf = 1.0 / (spacing * 8.0);
                    cover = saturate(cover
                           + (VNoise(float3(xz * wf, 17.0)) - 0.5) * 2.0 * weather);
                }
                float baseAt = baseY - droop;

                // ---- 轮廓域扭曲（低频 FBM，推的是采样点不是中心）-----------
                // 这一步是"云缘是卷的、不是圆的"的来源。只在候选测试有希望时
                // 才算（下面的宽松测试先 bail），大多数空气体素直接跳过。
                // 摆动范围 0.15*spacing，小于 cell 留白，所以两朵云不会被扭到连上。
                float halfSpan = 0.15 * spacing;
                float2 warp = float2(0.0, 0.0);
                bool maybe = true;

                // ---- 3×3 cell 检测：最多 9 朵候选云，取密度最大者 ---------
                float2 cell = floor(xz / spacing);
                float best = 0.0;
                float bestH = 0.0, bestAlive = 1.0, bestEdge = 0.0;
                bool warpDone = false;

                [unroll]
                for (int dy = -1; dy <= 1; dy++)
                {
                    [unroll]
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float2 id = cell + float2((float)dx, (float)dy);
                        float3 h  = Hash32(id + 0.37);

                        // 覆盖率 = 这个 cell 有没有云（平滑，所以滑覆盖度不会 pop）
                        float alive = smoothstep(cover + 0.10, cover - 0.10, h.x);
                        if (alive < 0.01) continue;

                        // 该云自己的半径 / 厚度（尺寸不一是"每朵都长得不一样"的关键）
                        float rad  = spacing * sizeFrac
                                   * lerp(1.0 - sizeJit, 1.0, h.z);
                        float thick= maxThick * lerp(1.0 - thickJit, 1.0, h.y);
                        float2 c   = (id + 0.5 + (h.xy - 0.5) * 0.55) * spacing;

                        float dd = length(xz - c);
                        if (dd > rad + halfSpan) continue;          // 离得远，跳过

                        // 竖向剖面：h=0 底部半径最大（平底），h=1 收成圆顶。
                        // (1 - h²)^dome：dome 小 = 扁平宽（层云），dome 大 = 圆墩（积云）
                        float h01 = (p.y - baseAt) / thick;
                        if (h01 <= 0.0 || h01 >= 1.0) continue;

                        float prof = pow(max(1.0 - h01 * h01, 0.0), dome);
                        float rEff = rad * prof;

                        // 软边：feather 取半径的一小份，世界单位 ⇒ 近处清晰远处自然融合
                        // 软边只留"一个可分辨的过渡"：rad*0.22 会在 1km 的云上
                        // 拖出 230m 的渐变，剪影必然糊成雾球（实测就是这么糊的）。
                        float feather = max(rad * 0.05, 6.0);
                        float body = saturate((rEff - dd) / feather);
                        if (body <= best) continue;      // 注意：不能在这里把 warpDone 置位！

                        // 首个有希望的候选到位后，算一次域扭曲再重测
                        if (!warpDone)
                        {
                            float3 wp = float3(xz.x, (p.y - baseAt) * 0.6, xz.y);
                            float w1 = FBM(wp * freq, 2) - 0.5;
                            float w2 = FBM(wp * freq + 19.3, 2) - 0.5;
                            warp = float2(w1, w2) * (2.0 * halfSpan);
                            warpDone = true;
                        }

                        body = saturate((rEff - length((xz + warp) - c)) / feather);
                        if (body <= best) continue;

                        best      = body;
                        bestH     = h01;
                        bestAlive = alive;
                        bestEdge  = 4.0 * body * (1.0 - body);   // 表面带权重
                    }
                }

                if (best <= 0.0) return 0.0;

                // ---- 边缘侵蚀（Remap 形式，只啃边、不动实心）---------------
                // erode = (body - e)/(1-e) 保证 body=1 仍是 1：云芯实心不变，
                // 只有表面带被啃出椰菜花的咬口；而且单调，覆盖率语义不被破坏。
                float e = 0.0;
                if (detail > 0.001 && bestEdge > 0.001)
                {
                    float3 ep = float3((xz.x + warp.x), (p.y - baseAt) * 0.9,
                                       (xz.y + warp.y)) * (freq * 5.5);
                    float n = FBM(ep, 2);
                    e = saturate((1.0 - n)) * detail * bestEdge * 0.9;
                }

                outH01    = bestH;
                outAlive  = bestAlive;
                outEdge   = bestEdge;
                float bodyOut = saturate((best - e) / max(1.0 - e, 1e-3));
                return bodyOut * bestAlive;
            }

            // 无调试输出的简洁版本
            float CloudDensity(float3 p, float2 windOffset, float2 camXZ)
            {
                float a, b, c;
                return CloudDensity(p, windOffset, camXZ, a, b, c);
            }

            // =================================================================
            // 朝太阳的光学深度（自阴影）
            // =================================================================
            float LightOpticalDepth(float3 p, float3 toSun, float2 windOffset, float2 camXZ,
                                    float stepLen, int steps)
            {
                float acc = 0.0;
                [loop] for (int i = 1; i <= 8; i++)
                {
                    if (i > steps) break;
                    float3 sp = p + toSun * (stepLen * (float)i);
                    float d = CloudDensity(sp, windOffset, camXZ);
                    if (d <= 0.002) continue;
                    acc += d * stepLen;
                }
                return acc;
            }

            // =================================================================
            // Fragment
            // =================================================================
            half4 Frag(Varyings input) : SV_Target
            {
                float2 ndc = (input.positionCS.xy / _CloudRTSize.xy) * 2.0 - 1.0;
                ndc.y = -ndc.y;
                float4 unprojected = mul(_CartoonInvVP, float4(ndc, 0.0, 1.0));
                float3 camPos  = _WorldSpaceCameraPos.xyz;
                float3 viewDir = normalize(unprojected.xyz / unprojected.w - camPos);

                // 低仰角交给大气透视（空气透视那一段），不需要硬窗口挖空天空。
                float horizonFade = smoothstep(0.010, 0.075, abs(viewDir.y));
                if (horizonFade <= 0.001 && _CloudDebug <= 0.5)
                    return half4(0, 0, 0, 0);

                float baseY    = _CloudShapeParams.x;
                float maxThick = max(_CloudShapeParams.y, 1.0);
                float spacing  = max(_CloudShapeParams.z, 1.0);
                float density0 = max(_CloudShapeParams3.w, 1e-3);
                float speed    = _CloudWind.x;
                float2 windDir = normalize(_CloudWind.yz + float2(1e-4, 0.0));
                // 注意不要用 frac：offset 归零那一刻所有云会瞬移（实测每几分钟一跳）。
                // 不封顶的位移 + floor 出来的 cell id 是稳定的（跑一天也才几百个 cell）。
                float2 windOffset = windDir * (_Time.y * speed);

                float droopFar = min(WorldBendOffsetFrom(camPos.xz + viewDir.xz * 4000.0, camPos.xz)
                                     * _WorldBendCloudScale, _WorldBendCloudMaxDroop);

                // 射线与云层带求交（相机可能在下方/内部/上方）
                const float kMaxDist = 24000.0;
                float tA = (baseY - droopFar - camPos.y) / viewDir.y;
                float tB = (baseY + maxThick - camPos.y) / viewDir.y;
                float tStart = max(min(tA, tB), 0.0);
                // 收紧到 baseY*10：再远的大气透视已经把云染没了，把步数预算
                // 留给看得见的区段。原来一路到 24km，32 步摊开每步跨 700m，
                // 600m 宽的云会被步进整段跨过去（表现为远处一片空）。
                float tEnd   = min(max(tA, tB), min(kMaxDist, baseY * 10.0));
                if (tEnd <= tStart)
                    return _CloudDebug > 0.5 ? half4(1, 0, 0, 1) : half4(0, 0, 0, 0);

                int   marchSteps = (int)clamp(_CloudMarchParams.x, 8.0, 64.0);
                float stepLen    = (tEnd - tStart) / (float)marchSteps;
                // 消光：把"整块满密度"标定成光学厚度 ~8（alpha≈1），
                // 这样薄羽是半透明的，厚芯才会饱和。除以 maxThick 让厚度参数
                // 改变时观感不至于跳。
                float sigma = density0 * 8.0 / maxThick;

                float3 toSun = -normalize(_CartoonSunDirection.xyz);
                float3 sunCol = _CartoonSunColor.rgb;
                float  viewSun = saturate(dot(viewDir, toSun));
                float sunUp = smoothstep(-0.06, 0.10, -_CartoonSunDirection.y);

                // 步进抖动（时序累积时逐帧动，关累积时冻结成稳定图案）
                uint frameTerm = _CloudHistoryWeight > 0.001 ? (uint)(_Time.y * 61.0) : 0u;
                uint jh = (uint)input.positionCS.x * 1664525u
                        ^ (uint)input.positionCS.y * 1013904223u
                        ^ frameTerm * 22695477u;
                jh ^= jh >> 16; jh *= 2246822519u; jh ^= jh >> 13;
                float jitter = ((float)(jh & 0xFFFFFFu) / 16777216.0 - 0.5) * stepLen * 0.9;

                float mu = dot(viewDir, toSun);
                int lightSteps = (int)clamp(_CloudMarchParams.y, 1.0, 8.0);

                float3 col = 0.0;
                float  T   = 1.0;
                float  dSum = 0.0, wSum = 0.0;     // 透射率加权代表深度（大气透视用）

                [loop] for (int s = 0; s < 64; s++)
                {
                    if (s >= marchSteps) break;
                    float  t = tStart + jitter + ((float)s + 0.5) * stepLen;
                    float3 p = camPos + viewDir * t;

                    float hh, al, ed;
                    float dn = CloudDensity(p, windOffset, camPos.xz, hh, al, ed);
                    if (dn <= 0.003) continue;

                    // 消光与本步光学深度
                    float ext = max(dn * sigma, 1e-6);
                    float Tstep = exp(-ext * stepLen);

                    // 自阴影：朝太阳的光学深度
                    float od = LightOpticalDepth(p, toSun, windOffset, camPos.xz,
                                                 stepLen * 0.7, lightSteps) * sigma;
                    float3 L = sunCol * sunUp * CloudMultiScatter(od, mu);

                    // Powder：云缘那圈"透过去"的亮边
                    float powder = 1.0 - exp(-dn * sigma * stepLen * 1.5);
                    L *= lerp(0.6, 1.0, powder);

                    // 银边（背光方向的暖高光），按体素密度门控以免轮廓外撒点
                    float silver = pow(viewSun, _CloudShadeParams.w)
                                 * _CloudShadeParams.z * saturate(dn * 6.0) * sunUp;
                    L += sunCol * float3(1.0, 0.72, 0.45) * silver;

                    // 环境（下暗上亮）
                    float lt = exp(-od);
                    L += CloudAmbient(hh, _CartoonSunDirection.xyz) * (0.3 + 0.7 * lt);

                    // 能量守恒的薄片积分：L 已是入射辐射，所以是 L*(1-Tstep)。
                    // （再除一次 sigma 就是把 1/sigma 重复计入，会过曝约 20 倍）
                    col += T * L * (1.0 - Tstep);
                    T   *= Tstep;

                    dSum += T * t;
                    wSum += T;

                    if (T < 0.02) break;
                }


                // ---- 调试：实例视图（在云层中点取一次，证明云朵是离散的）----
                if (_CloudDebug > 0.5)
                {
                    float tm = lerp(tStart, tEnd, 0.5);
                    float3 pd = camPos + viewDir * tm;
                    float hh, al, ed;
                    float v = CloudDensity(pd, windOffset, camPos.xz, hh, al, ed);

                    // Instance 视图要的是【cell 层面】的信息，不能复用密度函数的
                    // outAlive：那里只有命中云团才会写它，没命中就保留默认值 1.0，
                    // 画出来是整屏白（实测就是这么误导我的）。这里独立查最近 cell。
                    float aliveNear = 0.0;
                    {
                        // 这几个是 CloudDensity 的局部变量，Frag 里得自己再读一遍 uniform
                        float spacing2 = max(_CloudShapeParams.z, 1.0);
                        float sizeFrac2 = _CloudShapeParams2.x;
                        float sizeJit2  = _CloudShapeParams2.y;
                        float cover2    = saturate(_CloudShapeParams.w);
                        float2 probeXZ = pd.xz + windOffset;
                        float2 ccell   = floor(probeXZ / spacing2);
                        float  bestD   = 1e9;
                        [unroll]
                        for (int qy = -1; qy <= 1; qy++)
                        {
                            [unroll]
                            for (int qx = -1; qx <= 1; qx++)
                            {
                                float2 qid = ccell + float2((float)qx, (float)qy);
                                float3 qh  = Hash32(qid + 0.37);
                                float  qa  = smoothstep(cover2 + 0.10, cover2 - 0.10, qh.x);
                                float  qrr = spacing2 * sizeFrac2 * lerp(1.0 - sizeJit2, 1.0, qh.z);
                                float2 qc  = (qid + 0.5 + (qh.xy - 0.5) * 0.55) * spacing2;
                                float  qd  = length(probeXZ - qc);
                                if (qd < bestD) { bestD = qd; aliveNear = qa; }
                            }
                        }
                    }

                    float3 dbg;
                    if      (_CloudDebug < 1.5) dbg = v;                               // 覆盖度（密度）
                    else if (_CloudDebug < 2.5) dbg = aliveNear;                       // 实例/存活权重
                    else if (_CloudDebug < 3.5) dbg = hh;                              // 高度
                    else if (_CloudDebug < 4.5) dbg = ed;                              // 边带
                    else if (_CloudDebug < 5.5) dbg = saturate(tStart / kMaxDist * 8.0);
                    else if (_CloudDebug < 6.5) dbg = CloudMultiScatter(1.0, mu);      // 散射响应
                    else
                    {
                        // 7 = 整条射线的积分诊断：R = 真实累积的不透明度（1-T），
                        // G = 云自身的亮度（col/coverageA）。一次抓图就能区分
                        // "云太薄" 和 "云太暗" —— 前者 R 低，后者 G 低。
                        dbg = float3(1.0 - T,
                                     saturate(dot(col, float3(0.299, 0.587, 0.114))
                                              / max(1.0 - T, 1e-3)), 0.0);
                    }
                    return half4(dbg, 1.0);
                }

                // ---- 大气透视：远处云按"消光 + 内散射"染进天空 -------------
                float frontDepth = wSum > 1e-5 ? dSum / wSum : tStart;
                float ap = saturate((frontDepth - baseY * 4.0) / (baseY * 14.0))
                         * saturate(_CloudProfileParams.x);
                if (ap > 0.0)
                {
                    float sunY = -_CartoonSunDirection.y;
                    float dayW = saturate((sunY + 0.15) / 0.45);
                    float3 haze = lerp(float3(0.05, 0.07, 0.14), float3(0.62, 0.76, 0.92), dayW)
                                * _CloudTint.rgb;
                    col = col * (1.0 - ap) + haze * (1.0 - T) * ap;
                }

                float coverageA = 1.0 - T;

                // ---- cel 量化（一次性量化累积亮度，不逐片，否则出条带）------
                if (_CloudShadeParams.x > 1.5 && coverageA > 1e-3)
                {
                    float lumI = dot(col, float3(0.299, 0.587, 0.114)) / coverageA;
                    float q = SoftQuantizeSteps(lumI, _CloudShadeParams.x, _CloudShadeParams.y);
                    col *= q / max(lumI, 1e-4);
                }

                float alpha = coverageA * horizonFade;
                // 大气透视同时淡出远处云的整体可见度（与上面的染色同步）
                alpha *= 1.0 - ap * 0.55;
                // 太阳落下后云跟着变暗。注意方向约定：_CartoonSunDirection 是
                // 【太阳 -> 场景】，所以太阳在地平线以上时 .y 是【负】的。
                // 上一版写成 smoothstep(0.0, -0.12, .y)，负值直接饱和成 1 ——
                // 白天也永远砍掉 35% 不透明度（云因此一直半透明）。参数顺序反了。
                alpha *= 1.0 - 0.35 * smoothstep(-0.12, 0.0, _CartoonSunDirection.y);
                // 滤掉极淡雾纱：半分辨率下只会读成噪点
                alpha = smoothstep(0.05, 0.4, alpha);
                half4 cur = half4(col * horizonFade, alpha);

                // ---- 时序累积 ------------------------------------------------
                if (_CloudHistoryWeight > 0.001)
                {
                    float tAnchor = (baseY + maxThick * 0.5 - camPos.y) / viewDir.y;
                    if (tAnchor <= 0.0) tAnchor = baseY * 8.0;
                    tAnchor = min(tAnchor, kMaxDist);
                    float3 anchorW = camPos + viewDir * tAnchor;
                    float4 pc = mul(_CloudPrevVP, float4(anchorW, 1.0));
                    float2 prevUV = float2(pc.x, -pc.y) / max(pc.w, 1e-4) * 0.5 + 0.5;
                    if (pc.w > 0.0 && all(prevUV > 0.002) && all(prevUV < 0.998))
                    {
                        float4 hist = SAMPLE_TEXTURE2D(_CloudHistoryTex, sampler_CloudHistoryTex, prevUV);
                        cur = lerp(cur, half4(hist), _CloudHistoryWeight);
                    }
                }
                return cur;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
