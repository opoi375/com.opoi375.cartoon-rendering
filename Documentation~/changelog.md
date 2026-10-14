# 更新日志
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
