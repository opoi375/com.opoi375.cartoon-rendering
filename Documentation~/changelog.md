# 更新日志

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
