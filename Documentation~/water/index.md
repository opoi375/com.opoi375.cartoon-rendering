# 卡通水面与水下效果

## 两个水面 Shader

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

`UnderwaterPostProcessFeature`：相机没入水面后的卡通雾效。

**设置**：菜单 **CartoonRendering > Underwater > Setup Underwater Post Process**，或手动在 Renderer 上 Add Renderer Feature。材质运行时自动构建，无需资产配置。

水深雾颜色、密度等参数在 Feature / Volume 上调节。
