# Changelog
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


## [Unreleased]

### Fixed
- **编辑器循环里的两处每帧开销**：`GrassBladePreview.Update` 原来每帧无条件 `DestroyImmediate` 旧网格再重建一个（`[ExecuteAlways]` 下编辑模式每帧都跑，注释写的是"参数变更时重建"但代码没判），现在参数快照一致就直接返回；`OnDrawGizmos` 也不再每帧重取 `mesh.vertices` / `mesh.triangles`（两份托管数组拷贝），改用上一次的缓存
- **`CartoonProceduralSkyUpdater` 每帧全场扫 Light、且没太阳时把退化值写进共享材质资产**：`ApplyImmediate` 走 `LateUpdate`，`sunLight` 没指定时原来每帧做一次 `FindAnyObjectByType<Light>()`（现在缓存，被销毁才重找）；更要紧的是找不到光时 `GetSunDirection(null)` 返回 `(0,-1,0)`，而这个方法写的是 `.mat` 资产上的属性，实测打开一个没有方向光的场景就会把 `CartoonSky.mat` 的 `_CartoonSunDirection` / `_CartoonTimeOfDay` 静悄悄改掉，现在没有太阳直接不写


### Docs
- 文档站补上 19 张实机配图（`Documentation~/public/`，全部为 WebP）：展示场景三个时段与小星球总览、PBRToon 角色、天空正午/黄昏、体积云、卡通水面/水下、交互草地、像素化开关对比、LED 点阵屏、SDF UI 四个材质、体积光光柱与间接光烘焙对比
- 中英双语 12 个页面插入配图（共 24 篇）：概述 / 快速上手 / 卡通角色 / 程序化天空 / 体积云 / 水面 / 草地 / 像素化 / LED / SDF UI / 体积光 / 世界弯曲

## [1.4.1] - 2026-09-18

### Fixed
- **交互纹理在部分平台静默回退**：`GrassField` 的交互 RT 用 `R8` 创建时默认按 sRGB 申请，不支持 `R8_SRGB` 的平台会回退成 `RGBA32` 并打警告。改为显式 `RenderTextureReadWrite.Linear`
- **`WorldBendController` 命名空间不一致**：它是包内唯一一个 `Opoi375.CartoonRendering` 命名空间的脚本，只 `using CartoonRendering` 的脚本会找不到该类型。统一为 `CartoonRendering`

### Changed
- `GrassField` 新增 `VerboseDebug` 开关（默认**关**）。此前每 2 秒无条件打印脚印 / 交互强度日志，正式运行会刷屏
- 示例天空预设重新调参：`Data/SkySetting/CartoonProceduralSky.asset` 体积云（覆盖度 1.0 / 密度 2.74 / 步进 18 / 细节 0.173）与 `Materials/CartoonSky.mat` 太阳朝向、时刻

## [1.4.0] - 2026-09-17

### Added
- **体积光（上帝光 / 光柱）**：屏幕空间光线步进 + 主光阴影图采样，真散射而非径向模糊伪造
  - 新增 `Runtime/VolumetricLight/VolumetricLightPass.cs`（RenderGraph）：半分辨率步进（Beer 定律累积透射率 + Henyey-Greenstein 相位函数），再以 3×3 帐篷核按深度相似度双边上采样后叠加合成；散射的 alpha 通道顺便存场景视图深度用于深度感知滤波
  - 新增 `VolumetricLight` Volume 组件：强度 / 密度 / 各向异性 / 最远距离 / 高度雾 / 阴影压制 / 步进 / 抖动 / 尘埃噪声 / 卡通分层，共 17 个参数；`Intensity` 默认 0（VolumeManager 对未被覆盖的组件也返回默认实例，默认非 0 会变成「没放 Volume 也生效」）
  - 新增内置调试视图（Off / Shadow / Steps / SceneDepth）—— `Shadow` 模式用来确认主光阴影关键字有没有接上，接不上时效果会静默退化成均匀雾
  - 新增 `Shaders/PostProcessing/VolumetricLight.shader` + `VolumetricLightCommon.hlsl`，自带全屏三角形顶点着色器（不依赖 `Blit.hlsl` / `TextureXR.hlsl` / `_BlitScaleBias`）
  - 新增编辑器工具 `Tools > Volumetric Light > …`（Setup In Renderer / Create Demo Scene / Build In Current Scene / Dump State / Debug）
  - 新增文档页 [体积光（上帝光）](/volumetric-light/)

### Fixed
- **`VolumeProfile` 子资产未落盘**：两个演示场景构建器原来用 `profile.Add<T>()`，它只创建内存实例、不会写进资产，序列化后变成 `fileID: 0` —— 当前会话里看着正常，场景一重开 Volume 覆盖项全丢。改用 `VolumeProfileFactory.CreateVolumeComponent`
- **演示场景构建器卡死编辑器**：`SaveCurrentModifiedScenesIfUserWantsTo()` 从脚本 / 自动化调用时会弹模态对话框，无人点击则主线程永久阻塞。改用静默的 `EditorSceneManager.SaveOpenScenes()`
- Editor 程序集补上 `Unity.RenderPipelines.Core.Editor` 引用

## [1.3.0] - 2026-09-17

### Added
- **LED 点阵文字屏（LedDotMatrix）**：把任意文字渲染成 LED 点阵屏
  - 新增 `Shaders/LED/LEDDotMatrixText.shader`（`CartoonRendering/LED/DotMatrix Text`）：UV 网格化 + 程序化圆形点阵 + 遮罩采样 + UV 滚动 + 双频正弦闪烁；点边缘用 `fwidth` 做解析抗锯齿，避免远距离 / 斜视角摩尔纹
  - 新增 `LedTextMaskBaker` 组件：走 `Font.GetCharacterInfo` 把字形位图直接拼成单通道遮罩纹理，不依赖字体资产与额外相机，支持运行时换文案与中文
  - 新增 `LedCurvedScreen` 组件：程序化弧面屏网格，UV 按弧长均匀展开（点距在弧面上仍等距），法线朝向自纠正
  - 新增编辑器工具 `Tools > LED > Create Demo Scene / Build In Current Scene / Dump Mask PNG`，生成物落在 `Assets/CartoonRendering/LED/`
  - 新增文档页 [LED 点阵文字屏](/led/)

## [1.2.0] - 2026-09-06

### Added
- **世界弯曲 × 天空/体积云联动**：
  - 天空渐变地平线随曲率下沉（`_WorldBendSkyDip`，只偏移渐变分带，太阳/星星/2D 云保持真实方向），新增 `skyHorizonDistance` 与 `maxSkyDip` 钳制参数
  - 体积云云底按采样点水平距离下垂，slab 相交底面同步下移防止裁切；新增 `cloudBendScale`（弱化系数，默认 0.35）与 `cloudMaxDroop`（最大下垂量，默认 300m），远处云向地平线聚拢而不消失
  - `WorldBend.hlsl` 新增 `WorldBendOffsetFrom(posXZ, camXZ)` 显式参考点变体

## [1.1.1] - 2026-09-06

### Fixed
- PBRToonHair 的 ForwardLit / GBuffer 顶点阶段漏接世界弯曲（法线带 tangent 变体导致注入未命中），头发在弯曲时不跟随身体下沉

## [1.1.0] - 2026-09-06

### Added
- **世界弯曲（小星球视角 / World Bend）**：动森式圆形地球效果，远处顶点按到相机的水平距离平方下沉
  - 新增共享库 `Shaders/Library/WorldBend.hlsl`（顶点弯曲 + 法线修正）
  - 新增 `WorldBendController` 组件（曲率 / 死区 / 法线修正，编辑模式实时预览，曲率为 0 时零开销）
  - 包内全部几何 shader（PBRToon×4、水面×3、草地、建筑）的所有 Pass 接入，含 ShadowCaster / DepthOnly / DepthNormals / GBuffer / 描边

## [1.0.1] - 2026-08-29

### Fixed
- 体积云远距离断层（条带/台阶状 banding）：
  - 步进范围收紧到大气透视淡出距离（baseY×16），消除不可见区域的无效步进
  - 距离自适应噪声 LOD：按步长与像素足迹选择 128³ 噪声 mip（奈奎斯特偏置），细节侵蚀随 LOD 淡出
  - 云底/云顶高度剖面斜坡随步长自动加宽，远处保持连续采样
  - march jitter 扩大到 ±0.9 步长

### Added
- `volCloudTemporalEnabled`：体积云时序累积（TAA）开关。关闭时不分配历史缓冲、jitter 自动冻结防闪烁

### Changed
- CloudNoise3D 噪声体积重新烘焙，带完整 mip 链（128³→1³）+ 三线性过滤

## [1.0.0] - 2025-01-01

### Added
- 首次发布：卡通建筑/水面/草地 Shader、程序化卡通天空（昼夜循环）、水下与像素化后期效果、SDF UI 图形、交互草地工具。
