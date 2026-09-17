# Changelog

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
