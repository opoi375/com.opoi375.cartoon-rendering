# 卡通水面与水下效果

## 两个水面 Shader

![卡通水面：解析波形 + 深度泡沫，岸边芦苇与村舍入水](/water/surface.webp)

### CartoonWaterSimple —— 波浪

内置**距离 LOD 曲面细分**：hull/domain 阶段按与相机的距离细分三角形，再用解析波场置换顶点。

> 网格密度不再重要 —— 4 顶点 Quad 与 1000×1000 网格产出完全相同的平滑水面，源网格只提供外轮廓。

另带 SubShader 回退：不支持曲面细分的平台自动降级为普通顶点置换。

### CartoonWaterAdvanced —— 波浪 + 泡沫融合

在 Simple 基础上融合 `CartoonWater` 的着色：

- 深度差驱动的颜色渐变（`_CameraDepthTexture`）
- 滚动扭曲噪声泡沫，smoothstep 抗锯齿

由于着色发生在置换后的曲面上，**波峰抬升水面、缩小深度差 → 泡沫自然聚集到波峰与岸线**。

## 使用

1. 菜单 **CartoonRendering > Water > Create Water Plane** 一键创建
2. 或手工：材质选 `CartoonRendering/CartoonWaterAdvanced`，赋给任意平面网格
3. 泡沫需要 URP Asset 开启 **Depth Texture**

包内自带三个示例材质（`Materials/CartoonWater*.mat`）。

## 水下后期

`UnderwaterPostProcessFeature`：相机没入水面后的水下效果。

它**不是**"糊一层青色"，而是两个互相独立的项叠出来的：

| 项 | 参数 | 在做什么 |
|---|---|---|
| 逐通道吸收（out-scattering） | `absorptionRate` × `absorptionColor` | `T = exp(-σ·d)`。红光先被吃掉，绿其次，蓝最后 —— 远处物体**自己**就往蓝绿偏，不靠硬涂 |
| 内散射（in-scattering） | `fogDensity` / `color` / `deepColor` / `tintStrength` | 水体自己被照亮后加进来的那层颜色。浅水色与深水色按"在水面下多深"插值，所以低头看脚下和远望是两种颜色 |

![同一片场景：左为 Volume 关闭，右为水下后期](/water/underwater-compare.webp)

![水下后期：越深越冷，远处物体往蓝绿雾色收](/water/underwater.webp)

演示场景里摆了一排**同材质、只有距离不同**的球，把"距离"这个变量单独隔离出来。取球心的颜色：

| 距离 | 无效果 | 水下 | 饱和度 |
|---|---|---|---|
| 15 m | (232,133,64) | (126, 88, 43) | 0.72 → 0.66 |
| 26 m | (206,118,55) | ( 90, 84, 56) | 0.73 → 0.38 |
| 38 m | (167, 95,45) | ( 22, 43, 40) | 0.73 → 0.20 |

38 米处蓝色分量已经超过红色 —— 就是水把红光吃掉的顺序。

### 附加项

- `causticsIntensity` / `causticsScale` / `causticsSpeed` / `causticsDepthFade`：贴在水下表面上的焦散亮带。法线由**世界坐标对屏幕求导**估出来再三向投影混合，不需要法线图。水面下 0.6 米内淡入（刚离开水面的光还没被聚成亮带，不然水面自己会接到一份满强度花纹，看着像天花板在发光），到 `causticsDepthFade` 米深彻底消失 —— 池底不该被照得像打了灯
- `waveStrength` / `waveScale` / `waveSpeed`：屏幕空间折射抖动，只作用于不透明像素（读 `_CameraOpaqueTexture`）。贴脸的几何体不抖，随距离长出来
- `vignette`：暗角按**雾量**生效，只在浑水里有存在感
- `banding`：把雾量量化成 N 档，得到硬边的卡通深度分层（< 1.5 关闭）
- `waterSurfaceY`：**必须**与真实水面高度一致 —— 所有"在水面下多深"的计算都以它为基准
- `debug`：Depth / Fog / Caustics / Absorption / SkyMask 五个调试视图

### 设置

1. **Tools > Underwater > Setup Underwater Post Process** —— 给当前激活的 URP 渲染器装上 Feature，并生成一个带好参数的 `Underwater Zone`
2. 把 Zone 的 BoxCollider 移动 / 缩放到覆盖水体（水面以下）。相机进入区域后由 Volume 系统按 `blendDistance` 淡入淡出，`intensity` 默认 0，不会"没放 Volume 也生效"
3. 想直接看对照效果：**Tools > Underwater > Create Demo Scene**

材质运行时自动构建，不需要资产配置。

### 前提与排查

这个效果同时依赖深度图与不透明图，缺任何一张都会"什么都看不见"，而且 v1.4.1 之前是**静默**失败：

- 必须开启 **Depth Texture**（管线资产或相机）。没有深度就没有距离梯度；拿不到时通道会跳过并在 Console 打一条警告，而不是安静地什么都不画
- 注入点默认 **BeforeRenderingPostProcessing**：水面本身是透明物体，只有跑到它后面才可能被雾盖住
- 通道声明了 `ConfigureInput(Depth | Color)`。**不声明依赖，RenderGraph 就不会把深度图绑进来**，`SampleSceneDepth` 恒为 0，于是每个像素都被判成"天空"、alpha 输出 0 —— 效果一次都没画出来，也不报错
- 注入在 post stack 之前时，要写的目标与相机渲染出的场景纹理在 D3D / Metal 上差一次 Y 翻转（URP 自己的全屏 pass 靠 `_BlitScaleBias` 处理）。本通道按 `SystemInfo.graphicsUVStartsAtTop` 自己算。症状很特别：**雾糊在近处、远处反而清楚**，并带上下镜像重影
- 排查顺序：**Debug > Sky Mask**（整屏白 = 深度没接上）→ **Debug > Fog**（必须有从近到远的灰度梯度，没梯度就是平涂）→ **Dump State**（打印渲染器 / 相机 / Volume / shader 状态）

> v1.4.1 及之前，本功能卡的就是上面那条深度依赖：Feature 装上了、Volume 也激活了，画面却一点变化都没有。

