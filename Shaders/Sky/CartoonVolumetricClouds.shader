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

            // 天空调色板（由 CartoonSkyboxFeature 推送，与天空盒共用同一套）。
            // 之前 shader 没声明这些 uniform —— 所以"云跟着时间走"从来只是一句
            // 注释，黄昏时云底也不会带上暖色。
            float4 _AmbDayTop;
            float4 _AmbDayHorizon;
            float4 _AmbSunsetTop;
            float4 _AmbSunsetHorizon;
            float4 _AmbNightTop;
            float4 _AmbNightHorizon;
            float2 _AmbBlendParams;       // x = sunsetBlendStart, y = dayBlendEnd

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

            // ============================================================
            // billow / Worley —— 云的"菜花感"只来自这里
            // ============================================================
            // 值噪声 FBM 是一堆高斯软球：用它做出来的云形只能是棉花团（实测
            // 被吐槽就是这样）。真实积云的轮廓是【鼓包 + 收腰】的，特征来自：
            //   * billow(n) = 1 - |2n - 1|  —— 把平滑场折成脊，凸处更凸、凹处收缝
            //   * Worley 的胞边             —— 菜花的折痕
            // 所以噪声要按层叠，每层分工不同：
            //   L1  cell 实例（摆放）         → 大尺度的朵与朵
            //   L2  billow(值噪声) 低频        → 轮廓扭曲（鼓包/拉长）
            //   L3  billow(Worley) 中频        → 菜花的大瓣
            //   L4  billow(Worley) 高频        → 边缘的小咬口
            //   L5  billow(Worley) 内部        → 云芯的明暗纹理
            // 只有 L2 用值噪声（它需要连续平滑），L3~L5 必须是 Worley。
            float Billow(float n)
            {
                return 1.0 - abs(2.0 * n - 1.0);
            }

            // Worley：F1 距离。27 邻域、每胞 1 个特征点。
            //
            // ⚠ 这里【必须】用 [loop]，绝不能 [unroll]：CloudDensity 会在主步进
            // （最多 64 次）、朝太阳的光照步进（最多 8 次）以及调试视图里各内联
            // 一份。用三层 [unroll] 时 27 邻域会被完整展开，每个调用点复制
            // 27×3 次哈希，最终 D3D11 编译卡在 99% 好几分钟（实测 72 秒仍在跑）。
            // 单层 [loop] 把规模压回一个数量级。
            float Worley3(float3 p)
            {
                float3 i = floor(p);
                float best = 8.0;
                [loop] for (int k = 0; k < 27; k++)
                {
                    float3 offs = float3((float)(k % 3) - 1.0,
                                         (float)((k / 3) % 3) - 1.0,
                                         (float)(k / 9) - 1.0);
                    float3 c = i + offs;
                    float3 jitter = float3(Hash13(c + 0.5),
                                           Hash13(c + 19.1),
                                           Hash13(c + 43.7));
                    float3 d = c + jitter - p;
                    best = min(best, dot(d, d));
                }
                return saturate(1.0 - sqrt(best));
            }

            // billow(Worley)：菜花的折痕。
            // 只用【一档】：两档在轮廓带里是双倍开销，而大瓣由 L2 的 billow
            // 值噪声（轮廓扭曲）已经给足了 —— 这一层只需要中频的折痕。
            float WorleyLobes(float3 p)
            {
                return Billow(Worley3(p));
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
            // 与天空盒同一套日夜 blend（太阳高度 -> 日 / 黄昏 / 夜 调色板）。
            float3 BlendPalette(float3 night, float3 sunset, float3 day)
            {
                float sunY  = -normalize(_CartoonSunDirection.xyz).y;
                float start = _AmbBlendParams.x;
                float end   = max(_AmbBlendParams.y, start + 0.05);
                float wSunset = smoothstep(start - 0.20, start, sunY);
                float wDay    = smoothstep(start, end, sunY);
                return lerp(lerp(night, sunset, wSunset), day, wDay);
            }

            // 环境光：直接取天空自己的调色板，云因此跟着时间走（黄昏云底带暖，
            // 夜里云是暗蓝而不是死灰）。
            //
            // 【真实感的关键在这里】云底必须【明显暗】于云顶：云底朝下，只看到
            // 一小块天空立体角、又被自身遮住；而上一版把"下方色"写成
            // (0.62, 0.76, 0.92)（白天），云底和云顶一样亮 —— 于是整朵云被压成
            // 一团没有层次的白棉花。这里底 = 地平线色 × 0.45，且竖向走 k² 曲线，
            // 中段更暗，顶/底的对比被拉开。
            float3 CloudAmbient(float h01)
            {
                float3 top     = BlendPalette(_AmbNightTop.rgb,     _AmbSunsetTop.rgb,     _AmbDayTop.rgb);
                float3 horizon = BlendPalette(_AmbNightHorizon.rgb, _AmbSunsetHorizon.rgb, _AmbDayHorizon.rgb);

                float k = saturate(h01 * 0.85 + 0.15);
                float3 sky = lerp(horizon * 0.45, top, k * k);

                // 地面反弹：地面把光打回云底。真实云底不会死黑靠的就是它 ——
                // 一个便宜的半球项，日出 / 日落时地面反上来的暖色尤其明显。
                float3 bounce = horizon * 0.12 * (1.0 - k);

                return (sky + bounce) * _CloudTint.rgb;
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
            // detailMul: 1 = 完整（含 Worley 侵蚀与内部纹理），0 = 只要"有没有云"。
            // 朝太阳的自阴影步进用 0 —— 那趟最多 8 次、且一次主步进里就调一遍，
            // 每次再跑 27 邻域 Worley 是算力灾难（每条射线每像素上千次哈希）。
            // 自阴影本来也不需要逐体素的轮廓细节，只需要"穿过多少云"。
            float CloudDensity(float3 p, float2 windOffset, float2 camXZ,
                               out float outH01, out float outAlive, out float outEdge,
                               float detailMul)
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

                // ---- 3×3 cell 检测：最多 9 朵候选云，取径向值最大者 ---------
                float2 cell = floor(xz / spacing);
                float  bestRadial = 0.0;
                float  bestH = 0.0, bestAlive = 1.0;
                float3 bestSeed = float3(0.0, 0.0, 0.0);
                bool   warpDone = false;

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
                        float rad  = spacing * sizeFrac * lerp(1.0 - sizeJit, 1.0, h.z);
                        float thick= maxThick * lerp(1.0 - thickJit, 1.0, h.y);
                        float2 c   = (id + 0.5 + (h.xy - 0.5) * 0.55) * spacing;

                        float dd = length(xz - c);
                        if (dd > rad + halfSpan) continue;          // 离得远，跳过

                        // 竖向剖面：h=0 底部半径最大（平底），h=1 收成圆顶。
                        // (1 - h²)^dome：dome 小 = 扁平宽（层云），dome 大 = 圆墩（积云）
                        float h01 = (p.y - baseAt) / thick;
                        if (h01 <= 0.0 || h01 >= 1.0) continue;

                        // 竖向剖面：真实的积云不是半球糖球 —— 它【下段接近直筒】、
                        // 只在接近顶部时才收圆（云塔）。
                        // (1-h²)^0.5 在 h=0.5 处半径只剩 0.87、h=0.8 处只剩 0.6，
                        // 于是渲出来就是一个冰淇淋球（实测正是如此）。
                        // 改成 (1-h³)^dome：h=0.5 处仍有 0.97、h=0.8 处 0.87，
                        // 形状才立得起来。dome 继续控制顶部收得多狠（层云/积云）。
                        float hh3  = h01 * h01 * h01;
                        float prof = pow(max(1.0 - hh3, 0.0), dome);
                        float rEff = rad * prof;
                        if (rEff <= 1.0) continue;

                        // 归一化径向值：1 = 云芯，0 = 椭球表面。
                        //
                        // 【这是本次最关键的改动】旧实现用一条 feather = rad*0.05
                        // （约 27m）的软边，于是 body 几乎是二值 —— 密度场等于一个
                        // 实心椭球，噪声只能啃到最外一层皮，渲出来就是"白药丸"
                        // （实测截图正是如此）。现在密度在整个体积内连续变化，
                        // 噪声才能真正参与塑形。
                        float radial = (rEff - dd) / rEff;
                        if (radial <= 0.0) continue;

                        // 域扭曲：按 billow 值噪声把采样点推开，云缘不再是正圆
                        if (!warpDone)
                        {
                            float3 wp = float3(xz.x, (p.y - baseAt) * 0.6, xz.y) * freq;
                            float w1 = Billow(FBM(wp, 2)) - 0.5;
                            float w2 = Billow(FBM(wp + 19.3, 2)) - 0.5;
                            warp = float2(w1, w2) * (2.0 * halfSpan);
                            warpDone = true;
                        }

                        float radialW = (rEff - length((xz + warp) - c)) / rEff;
                        if (radialW <= bestRadial) continue;

                        bestRadial = radialW;
                        bestH      = h01;
                        bestAlive  = alive;
                        bestSeed   = h.xyz;     // 每朵云自己的噪声相位
                    }
                }

                if (bestRadial <= 0.0) return 0.0;

                // ============================================================
                // 噪声叠加 —— 云"长得像云"就靠这一段
                // ============================================================
                // 三层，全部在世界空间取样，并用【每朵云自己的相位】偏移：
                // 不偏移的话同一片噪声会套在每朵云上，天上会出现一排一模一样的云
                // （之前的截图正是如此）。
                //   nLo  ~ 1/freq 米   billow(Worley) 的胞边 = 菜花折痕
                //   nMid ~ 0.4/freq 米 billow(值噪声)，把大瓣再撕开一点
                // 密度 = 径向梯度 - 噪声侵蚀：
                //   shape*1.5       让云芯保持实心
                //   carve*detail    把外圈啃成瓣状，并在【内部】留下疏密差 ——
                //                   自阴影因此有层次，而不是一整块死白
                float bodyOut;
                if (detailMul > 0.001)
                {
                    float3 np = float3(xz.x, (p.y - baseAt), xz.y) * freq + bestSeed * 137.0;

                    float nLo  = WorleyLobes(np);
                    float nMid = Billow(FBM(np * 2.5 + 3.7, 2));
                    float carve = (1.0 - nLo) * 0.62 + (1.0 - nMid) * 0.38;

                    float shape = pow(saturate(bestRadial), 0.75);
                    bodyOut = saturate(shape * 1.5 - carve * detail * 1.1);

                    // 收边：把 0~0.25 这段密度陡起来。不清的话云缘会挂一层
                    // 低密度絮状"白色喷雾"（半分辨率下读成噪点，非常脏）。
                    // 内部的密度（≈1）完全不受影响。
                    bodyOut = smoothstep(0.0, 0.25, bodyOut);
                    // 顶部更碎（向上翻滚的读感），底部更整
                    bodyOut *= lerp(1.06, 0.94, bestH);
                }
                else
                {
                    // 自阴影步进用的廉价路径：只要"有没有云"（跳过 Worley）
                    bodyOut = saturate(pow(saturate(bestRadial), 0.75) * 1.5);
                }

                outH01   = bestH;
                outAlive = bestAlive;
                // 边带：径向值落在中段的"壳层"，调试视图用它看侵蚀是否生效
                outEdge  = smoothstep(0.0, 0.30, bestRadial)
                         * (1.0 - smoothstep(0.30, 0.95, bestRadial));
                return bodyOut * bestAlive;
            }

            // 无调试输出的简洁版本（完整细节）
            float CloudDensity(float3 p, float2 windOffset, float2 camXZ)
            {
                float a, b, c;
                return CloudDensity(p, windOffset, camXZ, a, b, c, 1.0);
            }

            // 只要"有没有云"的廉价版本：自阴影步进专用（跳过 Worley 侵蚀）。
            float CloudDensityCheap(float3 p, float2 windOffset, float2 camXZ)
            {
                float a, b, c;
                return CloudDensity(p, windOffset, camXZ, a, b, c, 0.0);
            }

            // =================================================================
            // 朝太阳的光学深度（自阴影）
            // =================================================================
            // 朝太阳的光学深度。
            //
            // 步长必须【按云的厚度取】而不是按视线步长取：原来用 stepLen*0.7，
            // 视线步长在远处有几十米，几步只覆盖 ~100m —— 而云厚 420m，
            // 于是光线"穿不透云"，朝太阳的光学深度恒偏小，云底因此拿到满强度
            // 阳光、整朵云变成一团没有暗底的白棉花（实测就是这个症状）。
            // 现在每个步进走 thickness*1.2/steps，几步走完整朵云。
            float LightOpticalDepth(float3 p, float3 toSun, float2 windOffset, float2 camXZ,
                                    float thickness, int steps)
            {
                float ls = thickness * 1.2 / max((float)steps, 1.0);
                float acc = 0.0;
                [loop] for (int i = 1; i <= 8; i++)
                {
                    if (i > steps) break;
                    float3 sp = p + toSun * (ls * (float)i);
                    float d = CloudDensityCheap(sp, windOffset, camXZ);
                    if (d <= 0.002) continue;
                    acc += d * ls;
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
                    float dn = CloudDensity(p, windOffset, camPos.xz, hh, al, ed, 1.0);
                    if (dn <= 0.003) continue;

                    // 消光与本步光学深度
                    float ext = max(dn * sigma, 1e-6);
                    float Tstep = exp(-ext * stepLen);

                    // 自阴影：朝太阳的光学深度
                    float od = LightOpticalDepth(p, toSun, windOffset, camPos.xz,
                                                 maxThick, lightSteps) * sigma;
                    // 太阳色按【亮度归一化】再用：项目里的 sunColor 是 HDR
                    // （这份预设约 11），直接乘任何强度都会把云裁成纯白，
                    // 内部的所有明暗层次随之全部丢失（实测就是这样）。
                    // 归一化后强度参数 = "云面相对亮度"，1.0 ≈ 纯白。
                    float3 sunN = sunCol / max(dot(sunCol, float3(0.299, 0.587, 0.114)), 1e-3);
                    float3 L = sunN * sunUp * CloudMultiScatter(od, mu)
                             * _CloudProfileParams.z;      // 太阳强度

                    // Powder：云缘那圈"透过去"的亮边
                    float powder = 1.0 - exp(-dn * sigma * stepLen * 1.5);
                    L *= lerp(0.6, 1.0, powder);

                    // 银边（背光方向的暖高光），按体素密度门控以免轮廓外撒点
                    float silver = pow(viewSun, _CloudShadeParams.w)
                                 * _CloudShadeParams.z * saturate(dn * 6.0) * sunUp;
                    L += sunCol * float3(1.0, 0.72, 0.45) * silver;

                    // 环境（下暗上亮）
                    // 环境项：越往云芯被遮得越多（lt = 朝太阳的透射率，
                    // 粗糙但便宜的遮蔽代理）。
                    float lt = exp(-od);
                    L += CloudAmbient(hh) * (0.45 + 0.55 * lt)
                       * _CloudProfileParams.w;            // 环境强度

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
                    float v = CloudDensity(pd, windOffset, camPos.xz, hh, al, ed, 1.0);

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
                    else if (_CloudDebug < 6.5)
                    {
                        // 6 = 自阴影强度：探针点朝太阳的光学深度（0..6 映射）。
                        // 亮 = 该点被自己身体挡得很厉害。整屏黑 = 自阴影根本没生效，
                        // 那 HDR 太阳（约 11×）就会直接把云冲成白棉花。
                        // 探针固定在云层中点，可能落在两朵云之间的空隙里（那样
                        // od 天然为 0，测不出问题）。改为先沿射线找【最密】的那一点，
                        // 在那一点上测自阴影 —— 这才是渲染时真正需要被遮蔽的样本。
                        float bestDn = 0.0; float3 bestP = pd;
                        for (int bs = 0; bs < 64; bs++)
                        {
                            if (bs >= marchSteps) break;
                            float bt = tStart + ((float)bs + 0.5) * stepLen;
                            float3 bp = camPos + viewDir * bt;
                            float bd = CloudDensity(bp, windOffset, camPos.xz);
                            if (bd > bestDn) { bestDn = bd; bestP = bp; }
                        }
                        float odP = LightOpticalDepth(bestP, toSun, windOffset, camPos.xz,
                                                      maxThick, lightSteps) * sigma;
                        dbg = saturate(odP / 6.0);
                    }
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
