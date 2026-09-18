# 更新日志

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
