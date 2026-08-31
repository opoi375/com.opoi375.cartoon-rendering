# 像素化后期

`PixelatePostProcessFeature` 把画面量化为大像素块的复古像素风效果。

## 特点

- **Volume 驱动**：所有参数都在 Volume Profile 上 —— 添加 **CartoonRendering/Pixelate Post Process** override，把 **Intensity** 调到 0 以上即启用
- **零资产配置**：材质运行时从 shader 惰性构建
- 可与 Volume 混合系统配合做局部 / 过渡效果（如进入特定区域画面像素化）

## 设置

1. URP Renderer：**Add Renderer Feature > Pixelate Post Process Feature**
2. 场景 Volume（Global 或 Local）：Add Override > **CartoonRendering > Pixelate Post Process**
3. 调 Intensity 与像素块大小
