# 更新日志
## v1.14.0

### 新增（扫描特效 Scan Effect）
- **以自身为中心的扫描 / 雷达扩散特效**，作为 RenderGraph 全屏后期单 Pass 接入：
  `ScanEffectFeature` + `ScanEffectPass` + `ScanEffect`（Volume）+ `ScanEffectController`。
- **两种形态**：`Radial`（以角色/相机为球心四周扩散）与 `Frontal`（沿朝向推进，可加扇形范围）。
  两者共用一条公式 —— `Sector Angle = 360` 时 `cos(180°) = -1`，扇形裁切自然退化成全通过。
- **五层视觉**：波前薄壳（带屏幕导数下限防闪）、拖尾、三轴网格 + 格心点阵 + 滚动扫描线、
  深度 Roberts 描边、辉光（交给管线 Bloom）。
- **可参数化**：半径 / 距离 / 速度 / 扇形半角 / 扫描线粗细 / 边缘高亮 / 辉光 / 网格密度 /
  点阵密度 / 恢复时间与曲线 / 颜色 / 噪波扰动 / 距离衰减 / 质量档。
- **四种触发方式**：手动 `Trigger()`、技能 `TriggerSkill()`（半径×1.6 速度×1.3，后半段倍率回落）、
  `autoDemo` 自动演示、F5 触发 / F6 开关的快捷键。
- **三档质量**：Low（无噪波、无描边）/ Medium（默认）/ High（双层噪波 + 深度描边）。
- **关闭即零开销**：`IsActive()` 为 false 时不 EnqueuePass，也不声明深度依赖。
- 编辑器菜单 `Tools > Scan >`：一键接入、加控制器、切换开关、诊断绘制。

### 兼容
- 合成用 `Blend One OneMinusSrcAlpha` 且**不读 activeColor**（RenderGraph 里它是外部导入资源），
  因此不会与水下后期、体积光、SSIL 抢写同一块缓冲。
- 默认注入 `BeforeRenderingPostProcessing`，辉光仍会被 Bloom / Tonemap 接手。
- `a = 0` 时逐像素恒等原图：关闭或未扫到的区域**一个像素都不动**。
- 预览相机与反射探针直接跳过；缺深度图时打印一次警告并跳过，而不是静默平涂。

### 修复 / 踩坑记录
- **`m_RendererFeatureMap` 必须刷新**：只往 `m_RendererFeatures` 里加而不刷新这本账，
  Feature 就在列表里但根本不跑，且不报错。接入菜单改为反射调用 `ValidateRendererFeatures()`，
  并且**无条件**刷新（之前只在"新增"分支里刷，导致重跑菜单也修不好）。
- **新 Input System 下 `Input.GetKeyDown` 会抛异常**，而它位于 `Update` 最前面，会把动画推进与
  自动演示一起中断（表现为"按了没反应、自动演示也不跑"）。新增 `ScanInput` 双后端：
  旧管理器直连，新系统走反射（避免包对 `com.unity.inputsystem` 产生硬依赖）。
- **`line` 是 HLSL 保留字**（几何着色器图元），当局部变量名会报
  `syntax error: unexpected token 'line'`。

### 已知问题
- **片元输出到画面这一步尚未打通**：draw 已被记录并执行（`SetRenderFunc` 执行、材质
  `passCount = 1`、shader 名正确、深度有效），但即使强制 `return half4(1,0,0,0)` 也没有任何
  像素落到最终缓冲上。诊断入口：`Tools > Scan > Diagnose Draw (RenderTexture)`。

### 新增（体素风格）
- **体素模式**：`pattern = Voxel` 或用 `voxelSize > 0` 切到**真正的 3D 立方格**扫描 ——
  世界按 `voxelSize` 量化成立方体，波前经过时**逐块点亮**。
  与平面网格的区别：棱线是"两个及以上轴同时贴面"（立方体的 12 条棱），
  内部按格哈希给明暗（实心体素块），点亮按 `(front - 格心距离)` 逐层涌出。
- 新参数 `voxelSize`（边长，0 = 关闭）/ `voxelFill`（空心↔实心）/ `voxelReveal`（揭开宽度）。

### 修复（这两条是本轮排查的全部结论）
- **Volume 参数的 YAML 格式**：最初写成单行 `diagnosticSolid: 1`，而 Volume 参数是嵌套的
  `VolumeParameter`（`m_OverrideState` / `m_Value`），单行会被 Unity **静默忽略** ——
  导致"强制纯红"从未生效，并且误判了好几轮。改成嵌套结构后立刻出红。
- **场景没有 Volume / profile 里没组件 → 静默不画**：`ScanEffectFeature` 现在会在
  "有扫描在跑但没有组件"时打一次警告并说明怎么补；
  `Setup Scan Effect` 菜单会无条件刷新 `m_RendererFeatureMap`、给场景 Volume 补组件、
  **场景完全没有 Volume 时自动新建 Global Volume**。
- **Y 翻转用错了**：原先照抄水下后期的 `graphicsUVStartsAtTop` 翻转，实测整屏上下颠倒
  （深度采到镜像位置 → 世界坐标错 → 距离判断全崩 → 整屏被判成已扫过）。本通道写
  `activeColorColorTexture`、深度与之同向，**不需要翻转**，已改为恒等。
- 基础染色从 0.42 降到 0.12：原来那项会把整屏刷成厚厚一层青色，网格/点阵全被盖住。

### 未验证
- **体素模式的观感**没有截图确认（验证期间另一条会话切换了场景，编辑器反复卡在重编译）。

### 验证与修正（本轮收尾）
- **体素模式已截图验证**：在演示场景里实测 `pattern = Voxel / voxelSize = 1.2m`，
  世界空间立方格在地面、墙面、天花板上按透视正确排列，棱线分明、逐块点亮。文档已配图。
- **三态诊断推送修正**：`diagnosticSolid` 原是 `BoolParameter`，C# 侧只推 0/1，
  所以设成 2（未激活转绿）也会走"无条件纯红"分支 —— 诊断工具本身在骗人。
  已改为 `ClampedFloatParameter(0..2)` 并原样推送。
- **Setup 菜单的保存顺序 bug**：`SaveAssets()` 发生在补 Volume 组件之前，导致新组件只在内存里、
  重开编辑器就丢失（YAML 搜不到）。已在其后再补一次 `SaveAssets()` + `Refresh()`。

## v1.12.0

### 变更（体积云密度模型重做）
- **密度改回连续噪声场 —— 与 Schneider 2015 / Nubis 的生产管线一致。** 上一版用的
  "离散云团实例"（世界切 cell、每格一朵椭球云）在架构上就走不通：几何椭球 + 径向
  剖面做出来永远是药丸形，怎么叠噪声都救不回来（实测被评价为"白药丸"）。
  真实实现里形状**完全来自连续 3D 噪声的膨胀与侵蚀**，云的分离感来自**天气图的
  局部覆盖率**，不是几何格子。管线：
  ```
  fbmW = dot(shape.gba, (0.625, 0.25, 0.125))
  base = remap(shape.r, -(1 - fbmW), 1)        // Perlin 被 Worley 膨胀
  base *= heightGradient                       // 高度带（底/顶渐入渐出）
  base = remap(base, 1 - coverage, 1) * coverage
  density = remap(base, detail * 0.22, 1)      // 细节只啃边缘
  ```
- **重新引入三张烘焙图**（`Tools > Clouds > Bake Noise`）：
  - `CloudShape3D` 128³：R = Perlin-Worley（`Remap(perlin, 0,1, worleyFBM, 1)`），GBA = Worley FBM @ f4/f8/f16
  - `CloudDetail3D` 32³：高频 Worley FBM，只在边缘啃细碎咬口
  - `CloudWeather2D` 512²：R = 覆盖率（云系），G = 云型（层云↔积云），BA = 域扭曲
- **不做直方图均衡（CDF）**：均匀分布看着对比更强，但它会抹平 Perlin-Worley 的团块
  结构，云会散成一层均匀棉絮（这是更早一版的错误）。
- 覆盖率改为**整体平移**而不是相乘：`base` 的典型值约 0.7，而 remap 下界是
  `1 - coverage`，一乘就把 coverage 压进 0.1~0.45，密度几乎恒为 0（实测天空全空）。

### 新增
- `volCloudShapeScale`：一块噪声图块对应多少米（**最直接的"云多大"旋钮**，积云 1500~2500）
- `volCloudWeatherScale` / `volCloudWarp` / `volCloudNoiseLod`：天气图尺度、域扭曲、噪声 mip
- `Tools > Clouds > Bake Noise`：一次烘三张图

### 移除
- 实例模型遗留的 `volCloudSpacing` / `volCloudSize` / `volCloudSizeJitter` /
  `volCloudHeightJitter` / `volCloudDome` / `volCloudNoiseFreq` 字段

### 修复
- **着色器一度整体失效**：编辑过程中文件的 `Shader "..."` 声明被误删，导致
  `Shader.Find` 返回 null、体积云 pass 被静默跳过（天空完全没有云且不报错）。
- 调试视图重建为对新管线有意义的六项：天气覆盖率 / 射线覆盖度 / 归一化高度 /
  中点密度 / 相交起点 / 最密点自阴影光学深度。

## v1.11.0

### 修复
- **云是"白药丸"**：密度函数用了一条 `feather = rad*0.05`（约 27m）的软边，于是 `body` 几乎是二值 —— 密度场等于一个**实心椭球**，噪声只能啃到最外一层皮，渲出来就是光滑的白色药丸。现在改成**归一化径向梯度铺满整个体积**（1 = 云芯，0 = 表面），噪声真正参与塑形；边缘不再是一条薄皮
- **所有云长得一模一样**：噪声在世界空间取样但**没有每朵云的相位偏移**，同一片噪声套在每朵云上。现在每朵云用自己的哈希做噪声相位
- **竖向剖面是半球**：`(1-h²)^dome` 在 h=0.5 处半径只剩 0.87、h=0.8 处只剩 0.6 —— 渲出来就是"冰淇淋球"。改成 `(1-h³)^dome`：下段接近直筒、只在接近顶部时收圆，云塔才立得起来
- **整片云裁成纯白、内部层次全丢**：项目里的 `sunColor` 是 HDR（预设约 11），直接乘强度不管调多小都过曝。现在太阳色**按亮度归一化**后再用，强度参数的含义变成"云面相对亮度"（1.0 ≈ 纯白），云内部才有明暗
- **云缘挂着一层白色絮状喷雾**：低密度体素在半分辨率下读成噪点。加了一道收边（`smoothstep(0, 0.25, density)`），云缘变实、内部密度不受影响
- **云团互相重叠成一整块**：`size 0.55 × spacing 950` 得出半径约 520m 而间距只有 950m，相邻两朵必然相交。预设改为 `spacing 1300 / size 0.34`
- **云底照得太亮**：环境项的"下方色"用的是地平线色原值（白天 0.62/0.76/0.92），云底和云顶一样亮。改成地平线色 ×0.45 且竖向走 k² 曲线，并补一项地面反弹（真实云底不死黑靠的就是它）
- **环境色从未接入天空调色板**：`_AmbDayTop` 等 uniform 一直被 Feature 推送，但着色器从没声明过 —— 所以"云跟着时间走"只是一句注释，黄昏云底也不会带暖色。现在声明并真正使用

### 性能
- **体积云着色器编译卡在 99% 好几分钟**（实测 72 秒以上仍未结束）：`Worley3` 用三层 `[unroll]` 展开 27 邻域，而 `CloudDensity` 会在主步进（最多 64 次）、朝太阳的光照步进（最多 8 次）与调试视图里各内联一份 —— 等于把 27×3 次哈希复制到几百个调用点，D3D11 编译器被撑死。改为单层 `[loop]`（不展开），并给 `CloudDensity` 加 `detailMul`：**自阴影步进走廉价路径**（跳过 Worley，只要"有没有云"）。改后同一份着色器重编译 **5~6 秒**
- 内部纹理不再查 Worley（只做细微密疏差，值噪声足够）

### 新增
- 噪声按层叠加，每层分工明确（着色器内程序化，仍然无烘焙资产）：
  | 层 | 内容 | 负责 |
  | --- | --- | --- |
  | L1 | cell 实例 | 大尺度的朵与朵 |
  | L2 | billow(值噪声) 低频 | 轮廓扭曲（鼓包/拉长） |
  | L3 | billow(Worley) | 菜花折痕、大瓣 |
  | L4 | billow(值噪声) 中频 | 把大瓣再撕开 |
  | L5 | billow(值噪声) 内部 | 云芯密疏差 |
- `volCloudSunIntensity` / `volCloudAmbientIntensity` 两个独立强度（打包进已有的 `_CloudProfileParams.zw`，不新增 uniform）
- `volCloudShadeSteps` 默认改为 **0（关闭）**：写实要连续渐变；卡通硬边分区仍可开到 2~4

## v1.10.0
### 新增
- **屏幕空间间接光照（SSILVB）**：按论文 *Screen Space Indirect Lighting with Visibility Bitmask*（Therrien / Levesque / Gilet, arXiv:2301.11376）实现的后处理，一次追踪同时产出**环境光遮蔽 + 带色间接光**。核心是把 GTAO 的"两个地平线角"换成 **32 扇区可见性位掩码**（一个 uint）：每个样本把自己占的扇区区间置位，能贡献的间接光 = 它盖住且尚未被更近样本挡住的扇区占比（论文 Algorithm 1 第 23 行 `countbits(b_j & ~b_i)/Nb`）× 光照缓冲采样 × 两个余弦项；AO = 1 − 置位扇区数/32。与 SSAO / GTAO 相比：
  - **固定表面厚度**：样本有前后表面两个角，薄物体（栅栏、栏杆、梯子）只挡自己张开的那一小段角度，光从背后穿过去 —— 不需要 falloff，也不会把栅栏后面压黑一片 / 拖光晕
  - **带方向信息**：位掩码天然知道"哪边没被挡"，所以能在同一趟采样里收集邻居物体反弹过来的颜色（乘性合成 `场景色 × (1 + 间接光) × AO`，不是加雾）
  - **噪声低**：位运算一次覆盖多个扇区，同样的采样数比随机 SSGI 干净得多
  - 输入只要深度 / 法线 / 不透明色拷贝，纯屏幕空间、零预计算，分辨率除数 + 采样数可随手缩放
- **卡通分层 AO**：`banding` 参数把可见性量化成 N 档，漫画式硬边阴影（与体积光的分层参数同一手感）
- **调试视图**：Volume 的 `debug` 可直接看可见性（白 = 无遮挡）或间接光贡献，调参不用靠猜
- `Tools/SSIL/Setup In Renderer` 一键把 Feature 写进当前 URP 渲染器；EditMode 冒烟测试（Volume 默认关闭、shader pass 数与 C# 索引一致）

### 实现备注
- 间接光的"直接光照缓冲"用的是 `_CameraOpaqueTexture`（已着色的不透明场景色）而非论文里的直接光 G-Buffer —— 反弹过来的光自带固有色，卡通场景下效果更讨喜，也省一张 G-Buffer
- 角度约定按论文"位掩码以投影法线为中心"：取样本方向在切片平面内**相对投影法线**的有符号角（`atan2`），半球域 [-π/2, π/2] 映射到 [0, 1] 正好铺满 32 个扇区；切平面以下的角度夹到边缘，天然实现"无限厚只挡到掠射角为止"
- 合成用 `Blend DstColor Zero`（乘法）只写不读 activeColor —— 避开 RenderGraph 里读 activeColor 读到空缓冲的老坑；模糊 / 上采样都带深度相似度权重，防止遮挡跨深度边缘渗色
- 博客 cybereality.com 的参考 GLSL 里间接光权重写成了 `1 - countbits(...)/Nb`（与论文第 23 行相反），本实现以论文公式为准

## v1.9.0

### 删除
- **2D 贴图平流云层**：投影到虚拟平面上的一张贴图，没有体积、没有明暗、没有自阴影，与体积云叠在一起只会互相打架。`cloudHeight` / `cloudThreshold` / `cloudScale` / `cloudIntensity` / `cloudTexture` 字段，以及天空盒 shader 里的 `EvaluateClouds` / `CloudFBM` / `ValueNoise2D` 与对应属性一并删除（`cloudColor` 保留，继续作为体积云色调）
- **旧体积云实现及其噪声管线**：`CloudNoise3D` / `CloudShape2D` 资产、`Tools > Cloud > Bake …` 两个烘焙菜单、`CloudNoiseTextureBaker`、以及"贴图引用没接上就什么都不画"那一整类静默失败

### 新增
- **离散云团实例密度模型**：世界平面按 `volCloudSpacing` 切 cell，每朵云由独立哈希决定有没有、多大、多厚，密度取各候选椭球剖面的最大值。**旧模型是"把连续噪声按覆盖率阈值化" —— 连续随机场有渗流阈值（约 0.3），覆盖率一过它整块场就拓扑连通，天空必然是一团/一整块甲板。那是拓扑问题，调噪声和侵蚀都救不了。** cell 模型天然保证朵与朵之间有留白
- 半径剖面 `(1 - h²)^dome`：底部平（凝结面）、顶部收成圆顶，`Dome` 决定层云↔积云
- **轮廓域扭曲 + 表面带侵蚀**：着色器内程序化 3D 值噪声（不再需要烘焙贴图，因此也没有贴图引用会失联）
- **大尺度天气调制** `Weather`：低频噪声推本地覆盖率，天上有云系分区（这边成片、那边一片空），而不是均匀撒糖
- **七个调试视图**：Coverage / Instance / Height / Edge / Depth / Scatter / **积分诊断**（R = 真实不透明度、G = 云自身亮度，一次抓图就能分辨"云太薄"还是"云太暗"）

### 修复
- **时序累积把云压成半透明薄雾**：历史缓冲的亮度系统性低于当前帧，权重 0.9 时整层云只剩约 10%。加了"至少渲染过一帧才采样历史"的保护，但根因未解决 —— `volCloudTemporalEnabled` 改为**默认关闭**，并在 Tooltip 与文档里标注已知问题
- **白天也永远砍掉 35% 不透明度**：`smoothstep(0.0, -0.12, _CartoonSunDirection.y)` 参数顺序写反了。该方向是【太阳 → 场景】，太阳在地平线以上时 `.y` 为负，负值在反向 smoothstep 下饱和成 1
- 调试视图范围差一位（`Coverage` 实际画的是 `Instance` 的内容），且 `Instance` 只在命中云团时才写存活权重、没命中保留默认值 1.0 —— 整屏白，等于没有信息。改成独立查最近 cell
- 步进范围收到 `baseY*10`：原来一路到 24 km，32 步摊开每步跨约 700 m，600 m 宽的云会被整段跨过去（远处一片空）
- 域扭曲被锁死：第一个候选"不在云里"时就把 `warpDone` 置位，导致全程不算扭曲，云缘一直是光滑椭球
- 软边 `rad*0.22` 在 1 km 的云上拖出约 230 m 的渐变带，剪影糊成雾球。收成 `rad*0.05`
- 预设里清掉 9 条死键（指向已删除的贴图资产）
- 测试程序集补上 Core / Universal RP 引用：新增的 SSIL 测试引用了 `VolumeComponent` / `ScriptableRendererFeature`，而 `CartoonRendering.Editor.Tests.asmdef` 没引 RP 程序集，整个工程的编译都被卡住

## v1.7.0

### 修复
- **噪声生成根本产不出云的结构**（把包内体积噪声导成图才看出来）：形状通道是**一堆同一尺度的软球** —— 没有大尺度连通云团，也没有菜花状的鼓包与收腰。这样的场无论光照怎么改都只能渲成棉花。三处成因：
  - `PerlinFbm(baseFreq 4, 6 倍频)` 把能量摊到中高频，本该"就是云团"的最大那一档被淹掉 → 改成基频 2、3 倍频
  - **完全没有 billow**（`1 - |2n - 1|` 折叠）。云的"鼓包 + 收腰"就来自这个折叠，普通 FBM 堆再多倍频也只是高斯状软球
  - Worley 的锐利胞边界（褶皱的另一半）被 tie-break 混合与 CDF 拉伸抹平，等于只剩 Perlin
- **billow 用错了层**：第一版把 billow 加在质量场上，质量场变成双峰脊场 —— 一过阈值只剩细丝，云糊成一缕一缕的雾纱。按 Nubis 的分工：**质量场要平滑**（只决定哪里有云团、云团多大），**褶皱来自 billowed Worley 膨胀器**
- **多重散射没有归一化**：倍频数一多整片天跟着变亮，3 倍频时云底被抬到单次散射的 5 倍，亮顶 / 暗底的对比直接没了 —— 而那是积云最像立体的线索。现在除以"零光学深度"处的和，语义变成 1 = 完全透明
- **能量守恒积分多除了一次 σ**：`L` 已经是入射辐射（发射率 j = σ·L），再除一次 σ 等于把 1/σ 重复计入，曝光被放大约 20 倍，cel 量化当场饱和成一片死白
- **云底被地平线色照得太亮**：环境项只做 horizon↔zenith 插值，而地平线色本身是天空最亮的一条 → 积云最标志性的"亮顶 + 暗平底"消失。补一个越靠底越暗的系数（云底朝下，看到的天空立体角小、又被自身遮住）
- **云缘一圈灰色描边**（贴纸感的主要来源）：阈值软带过宽 + powder 下限过高，一起收紧

### 新增
- **散射模型**：双叶 Henyey-Greenstein 相位函数（`volCloudGForward` / `volCloudGBackward` / `volCloudPhaseForward`）+ Wrenninge 倍频多重散射（`volCloudScatterOctaves`）。此前整个光照模型**没有任何角度依赖** —— 而"朝太阳看过去云会发亮"是真实云最强的线索
- **天气决定云型**（Nubis 的 dimensional profile）：`volCloudTypeContrast` / `volCloudTypeBias`。天气不再只决定"有多少云"，还决定"这是摊平的层云还是长厚的积云"。两种云都有平底（云底是凝结面，本来就平），差别在向上能长多厚
- **低空补覆盖** `volCloudBaseCoverageBoost`：tiled 3D 噪声在低仰角长射线上会啃出形状古怪的云（远处尤其明显），把云底垫实能压住这个毛病
- **云的大气透视** `volCloudAerialPerspective`：远处云按"消光 + 内散射"染进天空色，而不是只淡出 alpha —— 只淡 alpha 的结果是一块悬在半空、与天空无关的半透明白斑
- **烘焙内域扭曲**：采样点先被另一组噪声推一把。没有它，各向同性 FBM 的 blob 全是圆的，而真实云的褶皱是被剪切、拉长的

### 变更
- 细节侵蚀从"直接减法"改成 Nubis 的 `Remap(Base, Detail, 1, 0, 1) = (Base - Detail) / (1 - Detail)`：减法会把云内部一起拖暗（厚云发灰），Remap 保证 Base=1 仍是 1 —— 实心内部不动，只啃边缘，碎口才利落。而且它单调，CDF 归一化换来的"coverage = 字面占比"不被破坏
- 高频细节随 LOD 淡出时留 35% 下限：全淡掉之后远处云一个碎口都不剩，就完全没有尺度参照物（小特征衬托大特征正是云有体积、有距离的关键）
- 颜色与透过率共用同一个 `σ·ds`。旧代码颜色用 `dn²·σ·2.2`、alpha 用另一套凑出来的系数，等于两处各自调参
- 默认参数按新噪声重新标定：`coverage` 0.26→0.22、`scale` 0.00048→0.00042、`detail` 0.7→0.4、`weatherContrast` 0.55→0.6、`typeBias` 0→0.25。新质量场连通性更强，同样的数值现在会盖住更多天

## v1.6.0

### 修复
- **体积云开了却几乎什么都不画**：形状体积噪声用 Schneider 的 Perlin-Worley 组合 `pw = (perlin - (1 - worley)) / worley`，在 `worley` 小的地方**整片塌成精确的 0** —— 实测包内资产里约一半体素是 0，而覆盖度阈值在 0.48 以上，于是没有东西能过。表现为"Feature 装了、Volume 开着、天上一朵云都没有"，且不报错。烘焙末尾现在加一步 CDF 直方图归一化，把每个通道重映射到均匀分布，`coverage` 恢复成字面的"天空被遮住的比例"
- **归一化救不了并列**：一半样本挤在同一个值上时，重映射后仍是一整块常数，`coverage` 滑过那个值会让云量从 0% 直接跳到 50%。因此先把 `perlin` 按 12% 混回 `pw`，给死区留下排序信息
- **天气层退化成常数**：2D 天气图最初跟着 3D 形状"以相机为原点重新居中"，采样窗口永远钉在 uv≈0，头顶这片天看到的永远是图块同一个角落。现在两张图都用绝对世界坐标 + `frac()`（Repeat 环绕下等价，只把数值压回 float 精度舒服的范围）
- **资产引用类型写错导致天气图为 null**：生成型（`AssetDatabase.CreateAsset`）纹理的引用必须用 `type: 2`，按导入资源的 `type: 3` 写会解析成 null，天气层静默失效
- **远处地平线撒白盐粒**：LOD 对比补偿不封顶时，lod 6 会把已经糊掉的场放大 3 倍。补偿上限收到 1.8×，阈值软带同步随 lod 放宽
- **银边在轮廓外撒点**：薄边缘拿到满强度阳光，`a` 很小却 deposit 出亮色，且时序收敛之后依然存在（不是抖动，是画错了）。银边改为按体素密度门控
- **地平线有一条没云的带子**：`horizonFade` 的 `smoothstep(0.02, 0.14)` 把 0~8° 整个挖空，而平流云层默认关着。低仰角射线的 `tStart` 本来就在大气透视淡出范围之外，不需要再切一刀，窗口收窄到 `0.010~0.075`
- **新建的天空预设看不到云**：`CreateAssetMenu` 不会填纹理引用，`volCloudNoiseTexture` 为空时噪声恒采样为 0 → 密度永远过不了阈值 → 静默什么都不画。现在编辑期自动补上包内两张图，运行期缺图各打一条警告（每类只一次，不刷屏）
- 烘焙器里三线性插值的最后一维误用 `w` 当权重（应为 `v`），顺手修掉

### 新增
- **2D 天气 / 域扭曲图 `Textures/CloudShape2D.asset`**（512²，随包提供）：密度场从"一层 3D 噪声"变成四层叠加 —— 天气（哪一片天有云）→ 形状（一朵云的轮廓）→ 域扭曲（云缘卷起来）→ 高频侵蚀（椰菜花碎口）。单层 3D 噪声是各态历经的，铺满天空只会得到一块均匀磨砂玻璃
- **`Tools > Cloud > Bake Cloud Shape Maps`**：重烘焙天气图；两个烘焙工具现在都做 CDF 归一化
- **体积云参数 4 个**：`volCloudShapeTexture` / `volCloudWeatherScale` / `volCloudWeatherContrast` / `volCloudWeatherDetail` / `volCloudWarp`
- **`volCloudDebug` 六个调试视图**：Coverage / MarchRange / Field / Profile / Lod / Weather。"整屏没有云"和"云画了但看不见"用眼睛分不出来，只能把中间量画出来 —— 本次三个根因全是这么定位的
- 调试视图新增 `Weather` 通道，并把"射线根本没穿过云层"画成纯红，与"穿过了但密度为 0"的纯黑区分开

### 变更
- 体积云默认参数按"分得开的积云"重调：`coverage` 0.5→0.24、`baseHeight` 600→800、`thickness` 700→300、`scale` 0.0005→0.00065、`density` 1.2→1.7、`marchSteps` 40→32、`detail` 0.35→0.8、`silverIntensity` 0.6→0.3、`windSpeed` 12→7。包内与示例天空预设同步更新
- 云底高度剖面从 0.22 收到 0.12 —— 积云要"平底圆顶"，底部摊圆了就没有性格
- 噪声体积的 Perlin FBM 从 5 倍频增到 6 倍频

## v1.5.0

### 修复
- **水下后期从未画出任何像素**：`UnderwaterPostProcessPass` 读 `_CameraDepthTexture` 却没有声明 `ConfigureInput(Depth)`，RenderGraph 不会把深度图绑进来 → `SampleSceneDepth` 恒为 0 → 每个像素都被判成"天空"、alpha 输出 0。Feature 装上了、Volume 也激活了，画面却毫无变化，而且不报任何错。现在通道声明 `Depth | Color` 依赖；拿不到深度时跳过并打一条警告，不再静默
- **`Data/Underwater/UnderwaterZoneProfile.asset` 是个空 profile**：`UnderwaterSetup` 用 `profile.Add<T>()` 建覆盖项，那只是内存实例，序列化出来是 `fileID: 0` —— 当前会话正常，场景一重开 Zone 里什么都没有。改用 `VolumeProfileFactory.CreateVolumeComponent<T>()` 写成子资产
- **注入点被代码硬覆盖**：`Feature.Create()` 无条件写成 `BeforeRenderingTransparents`，Inspector 改了没用。现在尊重序列化值，默认 `BeforeRenderingPostProcessing`（水面是透明物体，只有跑到它后面才可能被雾盖住）
- **场景纹理 Y 翻转**：注入在 post stack 之前时，要写的目标与相机渲染出的深度 / 不透明图在 D3D/Metal 上差一次翻转，表现为"雾糊在近处、远处反而清楚"并带上下镜像重影。按 `SystemInfo.graphicsUVStartsAtTop` 算出 `_UWSceneScaleBias`，深度、不透明图与世界坐标重建共用同一个翻转后的 uv
- **雾色亮 3-4 倍**：`MaterialPropertyBlock.SetColor` 不做 sRGB→线性转换（与 Inspector 的 Color 字段不同），雾色按线性值直接用会糊成一层白雾。现在显式转线性
- **焦散频率与亮度**：`causticsScale` 文档写"每米周期数"，实现却按弧度用（0.55 实际周期 11 米），亮带糊成整片白；亮度系数也过高，会把雾整个盖掉。现在按 2π 换算并压到合理范围，另在水面下 0.6 米内淡入 —— 否则水面自己接到一份满强度花纹，像天花板在发光
- **不激活也进渲染图**：Feature 原来每帧无条件 `EnqueuePass`；通道一旦声明依赖，就会让不相关的场景白付一次深度 / 不透明图拷贝。现在 `AddRenderPasses` 里按 Volume `IsActive()` 判断

### 变更
- 编辑器工具菜单统一到 **`Tools > Underwater/`**（原文档写的 `CartoonRendering > Underwater > …` 与代码不符）
- 渲染器不再写死 `Assets/Settings/CartoonRP_Renderer.asset`，改从 `GraphicsSettings.currentRenderPipeline` 解析 —— 包放进任何工程都能用
- 着色器去掉 `Blit.hlsl` / `TextureXR.hlsl` 依赖，改用包内自带的全屏三角形（与体积光一致）
- 旧行为"整屏平涂一层颜色"改成"雾必须随距离长出来"：`color` / `tintStrength` 仍在，但只是新雾模型的两个参数

### 新增
- **水下 Volume 参数从 3 个扩到 19 个**：逐通道吸收（`absorptionRate` / `absorptionColor`）、内散射雾（`fogDensity` / `deepColor` / `depthToFullColor`）、焦散（强度 / 频率 / 速度 / 存活深度）、折射抖动、按雾量生效的暗角、卡通分层 `banding`、`waterSurfaceY`、5 个调试视图
- **`Tools > Underwater > Create Demo Scene`**：水池 + 15/20/26/32/38 米排开的同材质球排 + 水面 + 局部 Volume 区域，专门用来隔离"距离"这一个变量
- **`Tools > Underwater > Dump State`**：打印渲染器 / 相机 / 管线 / Volume / shader / 球排距离
- **`Tools > Underwater/Debug/*`**：Sky Mask / Fog / Depth / Caustics / Absorption 调试视图菜单


## v1.4.1

### 修复
- **交互纹理在部分平台静默回退**：`GrassField` 的交互 RT 用 `R8` 创建时默认按 sRGB 申请，不支持 `R8_SRGB` 的平台会回退成 `RGBA32` 并打警告 —— 改为显式 `RenderTextureReadWrite.Linear`
- **`WorldBendController` 命名空间不一致**：它是包内唯一一个 `Opoi375.CartoonRendering` 命名空间的脚本，只 `using CartoonRendering` 的脚本会找不到该类型，统一为 `CartoonRendering`

### 变更
- `GrassField` 新增 `VerboseDebug` 开关（默认**关**）：此前每 2 秒无条件打印脚印 / 交互强度日志，正式运行会刷屏
- 示例天空预设重新调参：`CartoonProceduralSky.asset` 体积云与 `CartoonSky.mat` 太阳朝向 / 时刻

## v1.4.0

### 新增
- **体积光（上帝光 / 光柱）**：屏幕空间光线步进 + 主光阴影图采样，真散射而非径向模糊伪造
  - 半分辨率步进（Beer 定律 + Henyey-Greenstein 相位函数）→ 3×3 帐篷核按深度相似度双边上采样 → 叠加合成；散射的 alpha 通道顺便存场景深度
  - `VolumetricLight` Volume 组件：强度 / 密度 / 各向异性 / 最远距离 / 高度雾 / 阴影压制 / 步进 / 抖动 / 尘埃噪声 / 卡通分层；`Intensity` 默认 0，避免「没放 Volume 也生效」
  - 内置调试视图：`Shadow` 模式用来确认主光阴影关键字有没有接上 —— 接不上时效果会静默退化成均匀雾
  - 编辑器工具 `Tools > Volumetric Light > …`（装 Feature / 演示场景 / 诊断 / 调试）

### 修复
- **`VolumeProfile` 子资产未落盘**：两个演示场景构建器原来用 `profile.Add<T>()`，它只创建内存实例，序列化后变成 `fileID: 0` —— 当前会话正常、场景重开全丢
- **演示场景构建器卡死编辑器**：`SaveCurrentModifiedScenesIfUserWantsTo()` 从脚本调用会弹模态框，无人点击则主线程永久阻塞

## v1.3.0

### 新增
- **LED 点阵文字屏**：把任意文字渲染成 LED 点阵屏 —— 圆点纯程序化生成（UV 网格化 + 格内画圆），`fwidth` 解析抗锯齿，HDR 辉光 + 扫描线 + 双频正弦逐点闪烁
  - `LedTextMaskBaker` 运行时把文字烘成单通道遮罩纹理，不依赖字体资产、不需要额外相机，支持中文与运行时换文案
  - `LedCurvedScreen` 程序化弧面屏网格，UV 按弧长均匀展开，点距在弧面上仍等距
  - 编辑器工具 `Tools > LED > …`，生成物落在 `Assets/CartoonRendering/LED/`

## v1.2.0

### 新增
- 世界弯曲 × 天空/体积云联动：天空渐变地平线下沉（maxSkyDip 钳制），体积云底随距离下垂（cloudBendScale 弱化 + cloudMaxDroop 钳制），新增 skyHorizonDistance 参数


## v1.1.1

### 修复
- PBRToonHair 的 ForwardLit / GBuffer 顶点阶段漏接世界弯曲，头发在弯曲时不跟随身体下沉

## v1.1.0

### 新增
- **世界弯曲（小星球视角）**：动森式圆形地球效果，远处顶点按到相机的水平距离平方下沉
  - 新增共享库 `Shaders/Library/WorldBend.hlsl`（顶点弯曲 + 法线修正）
  - 新增 `WorldBendController` 组件（曲率 / 死区 / 法线修正，编辑模式实时预览，曲率为 0 时零开销）
  - 包内全部几何 shader（PBRToon×4、水面×3、草地、建筑）的所有 Pass 接入，含 ShadowCaster / DepthOnly / DepthNormals / GBuffer / 描边

## v1.0.1

### 修复
- **体积云远距离断层**（条带/台阶状 banding）：
  - 步进范围收紧到大气透视淡出距离（baseY×16），消除不可见区域的无效步进
  - 距离自适应噪声 LOD：按步长与像素足迹选择 128³ 噪声 mip（奈奎斯特偏置），细节侵蚀随 LOD 淡出
  - 云底/云顶高度剖面斜坡随步长自动加宽，远处保持连续采样
  - march jitter 扩大到 ±0.9 步长

### 新增
- `volCloudTemporalEnabled`：体积云时序累积（TAA）开关。关闭时不分配历史缓冲、jitter 自动冻结防闪烁

### 变更
- CloudNoise3D 噪声体积重新烘焙，带完整 mip 链（128³→1³）+ 三线性过滤

## v1.0.0

首个公开发布：

- PBRToon 卡通角色着色器家族（Base / Face / Eye / Hair）
- 卡通建筑 shader（平滑风格化光照，完整烘焙/Forward+/延迟管线支持）
- 程序化卡通天空（昼夜循环、星星、2D 云）
- 体积云（光线步进 + 时序累积）
- 卡通水面 Simple / Advanced + 水下后期
- 像素化后期
- 交互草地
- SDF UI 图形 + SDF 生成器等编辑器工具
