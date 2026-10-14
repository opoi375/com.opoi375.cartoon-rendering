# 更新日志
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
