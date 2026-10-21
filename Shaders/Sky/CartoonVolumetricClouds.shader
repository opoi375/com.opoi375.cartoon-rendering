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

            // 三张噪声图（Tools > Clouds > Bake Noise 烘焙）
            //   _CloudNoiseTex    : R = Perlin-Worley，GBA = Worley FBM @ f4/f8/f16
            //   _CloudNoiseDetail : R = 高频 Worley FBM（只用来啃边缘）
            //   _CloudWeather2D   : R = 覆盖率，G = 云型，BA = 域扭曲
            TEXTURE3D(_CloudNoiseTex);    SAMPLER(sampler_CloudNoiseTex);
            TEXTURE3D(_CloudNoiseDetail); SAMPLER(sampler_CloudNoiseDetail);
            TEXTURE2D(_CloudWeather2D);   SAMPLER(sampler_CloudWeather2D);

            // x = 天气图世界尺度（每米图块数） y = mip z = 域扭曲强度 w = 保留
            float4 _CloudWeatherParams;

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
            // 密度：连续噪声场（Schneider 2015 / Nubis 生产管线）
            // =================================================================
            // 走过的弯路记在这里：曾经用「离散云团实例」（世界切 cell、每格一朵
            // 椭球云）来做，理由是"避免连续场渗流成一整块"。但几何椭球 + 径向
            // 剖面做出来永远是药丸形，怎么叠噪声都救不回来。真实实现里：
            //   * 云的【形状】完全来自连续 3D 噪声的膨胀与侵蚀
            //   * 云的【分离感】来自天气图的局部覆盖率，不是几何格子
            //
            // 管线（与 Schneider / Nubis 一致）：
            //   shape   = tex3D(shapeNoise, uvw)        R=Perlin-Worley GBA=Worley
            //   fbmW    = dot(shape.gba, (0.625,0.25,0.125))
            //   base    = remap(shape.r, -(1-fbmW), 1)          ← Worley 膨胀 Perlin
            //   base   *= heightGradient                        ← 高度带
            //   base    = remap(base, 1-cov, 1) * cov            ← 局部覆盖率
            //   density = remap(base, detail*0.22, 1)            ← 只啃边缘
            float RemapLine(float v, float a, float b)
            {
                return saturate((v - a) / max(b - a, 1e-5));
            }

            // 高度带：底部渐入、顶部渐出。type 高 = 积云（平底高塔），低 = 层云（摊薄）。
            float HeightGradient(float hf, float type)
            {
                float bot = lerp(0.10, 0.34, saturate(type));
                float top = lerp(0.88, 0.58, saturate(type));
                return smoothstep(bot, bot + 0.10, hf)
                     * (1.0 - smoothstep(top - 0.16, top, hf));
            }

            // 天气图：R = 覆盖率，G = 云型，BA = 域扭曲
            float4 SampleWeather(float3 p, float2 windOffset, out float coverage, out float type)
            {
                float2 wuv = frac(p.xz * _CloudWeatherParams.x) + windOffset * 0.12;
                float4 w = SAMPLE_TEXTURE2D_LOD(_CloudWeather2D, sampler_CloudWeather2D,
                                                wuv, _CloudWeatherParams.y);
                // 覆盖率用【平移】而不是相乘：base 的典型值约 0.7，而 remap 下界是
                // (1-coverage)。一乘就把 coverage 压进 0.1~0.45，密度几乎恒为 0。
                coverage = saturate(w.r * 1.10 + (_CloudShapeParams.w - 0.55) * 1.3);
                type     = w.g;
                return w;
            }

            // 完整密度。outHf = 归一化高度，outCoverage = 局部覆盖率（调试用）。
            float CloudDensity(float3 p, float2 windOffset, float2 camXZ,
                               out float outHf, out float outCoverage, float detailMul)
            {
                outHf = 0.0; outCoverage = 0.0;

                float baseY  = _CloudShapeParams.x;
                float thick  = max(_CloudShapeParams.y, 1.0);
                float topY   = baseY + thick;
                if (p.y <= baseY || p.y >= topY) return 0.0;

                float hf = saturate((p.y - baseY) / thick);

                float coverage, type;
                float4 w = SampleWeather(p, windOffset, coverage, type);
                outCoverage = coverage;
                if (coverage <= 0.005) return 0.0;

                // 高度带先剔除：带外直接返回，省掉三次 3D 采样
                float hg = HeightGradient(hf, type);
                if (hg <= 0.002) return 0.0;

                // 域扭曲：把采样点按天气图的 BA 推开（云缘才卷，不是正圆）
                float2 warp = (w.ba - 0.5) * _CloudWeatherParams.z;

                // 绝对世界坐标取样；Y 用同一个 metres-per-tile ⇒ 采样单元各向同性
                float scale = max(_CloudShapeParams.z, 1e-6);
                float3 uvw = float3(frac(p.x * scale) + warp.x,
                                    (p.y - baseY) * scale,
                                    frac(p.z * scale) + warp.y);

                float lod = _CloudMarchParams.w;
                float4 shape = SAMPLE_TEXTURE3D_LOD(_CloudNoiseTex, sampler_CloudNoiseTex, uvw, lod);

                float fbmW = dot(shape.gba, float3(0.625, 0.25, 0.125));
                float baseCloud = RemapLine(shape.r, -(1.0 - fbmW), 1.0);
                if (baseCloud <= 0.002) return 0.0;

                baseCloud *= hg;
                baseCloud = RemapLine(baseCloud, 1.0 - coverage, 1.0) * coverage;
                if (baseCloud <= 0.002) return 0.0;

                // 细节侵蚀：只在高频噪声方向上抬一点点下界，云芯不动
                if (detailMul > 0.001 && _CloudMarchParams.z > 0.001)
                {
                    float hfN = SAMPLE_TEXTURE3D_LOD(_CloudNoiseDetail, sampler_CloudNoiseDetail,
                                                     uvw * 4.0 + float3(13.7, 5.1, 31.7),
                                                     lod).r;
                    float erode = lerp(1.0, hfN, saturate(_CloudMarchParams.z * detailMul));
                    baseCloud = RemapLine(baseCloud, erode * 0.22, 1.0);
                }

                outHf = hf;
                return saturate(baseCloud);
            }

            float CloudDensity(float3 p, float2 windOffset, float2 camXZ)
            {
                float hf, cov;
                return CloudDensity(p, windOffset, camXZ, hf, cov, 1.0);
            }

            float CloudDensityCheap(float3 p, float2 windOffset, float2 camXZ)
            {
                float hf, cov;
                return CloudDensity(p, windOffset, camXZ, hf, cov, 0.0);
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

                    float hh, covNow;
                    float dn = CloudDensity(p, windOffset, camPos.xz, hh, covNow, 1.0);
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


                // ---- 调试视图 -------------------------------------------------
                // 放在积分之后：只有算完 1-T 才能回答"云太薄"还是"云太暗"。
                if (_CloudDebug > 0.5)
                {
                    float tm = lerp(tStart, tEnd, 0.5);
                    float3 pd = camPos + viewDir * tm;
                    float3 dbg = 0.0;

                    if (_CloudDebug < 1.5)
                    {
                        float cov, typ;                        // 1 = 天气图覆盖率
                        SampleWeather(pd, windOffset, cov, typ);
                        dbg = cov;
                    }
                    else if (_CloudDebug < 2.5)
                    {
                        dbg = 1.0 - T;                         // 2 = 整条射线的覆盖度
                    }
                    else if (_CloudDebug < 3.5)
                    {
                        float hf, cov;                         // 3 = 归一化高度
                        CloudDensity(pd, windOffset, camPos.xz, hf, cov, 1.0);
                        dbg = hf;
                    }
                    else if (_CloudDebug < 4.5)
                    {
                        float hf, cov;                         // 4 = 中点密度
                        dbg = CloudDensity(pd, windOffset, camPos.xz, hf, cov, 1.0);
                    }
                    else if (_CloudDebug < 5.5)
                    {
                        dbg = saturate(tStart / kMaxDist * 8.0);    // 5 = 相交起点
                    }
                    else
                    {
                        // 6 = 最密点朝太阳的光学深度（亮 = 被自己挡得厉害）
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
