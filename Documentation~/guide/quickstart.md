# 快速上手

三个最常见的使用场景，各 2 分钟。

## 1. 给角色换卡通材质

1. 选中角色的材质，Shader 改为：
   - 身体 / 服装 → `CartoonRendering/PBRToonBase`
   - 脸 → `CartoonRendering/PBRToonFace`（配合 SDF 面部阴影图）
   - 眼睛 → `CartoonRendering/PBRToonEye`
   - 头发 → `CartoonRendering/PBRToonHair`
2. 调 **Shadow Color / Ramp** 相关参数控制明暗分区，**Rim** 参数控制边缘光

详细参数见[卡通角色渲染](/shading/toon)。

## 2. 打开卡通天空

1. 在 URP Renderer 上 **Add Renderer Feature > Cartoon Skybox Feature**
2. `Assets > Create > Cartoon Rendering > Procedural Sky` 创建天空配置资产
3. 把资产拖到 Feature 的 **Sky** 字段（或留空，Feature 会在运行时自动构建默认材质）
4. 旋转场景里的方向光 —— 天空会自动经历白天 → 黄昏 → 夜晚

体积云：在天空资产上勾选 **Volumetric Clouds Enabled** 即可，见[体积云](/sky/volumetric-clouds)。

## 3. 放一块卡通水面

菜单 **CartoonRendering > Water > Create Water Plane**（或手工创建一个材质用 `CartoonRendering/CartoonWaterAdvanced` 的 Quad）。

曲面细分会处理网格密度 —— 一个 4 顶点 Quad 和 1000×1000 网格效果相同，网格只提供轮廓。

泡沫需要相机开启 Depth Texture，见[卡通水面](/water/)。
