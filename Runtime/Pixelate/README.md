# Pixelate — 纯屏幕后处理像素化（Unity 6 / URP / Render Graph / Volume 驱动）

## 文件
- `Assets/CartoonRendering/Shaders/PostProcessing/Pixelate.shader` — 手写 HLSL（`CartoonRendering/PostProcessing/PixelatePostProcess`）
- `Assets/CartoonRendering/Runtime/Pixelate/PixelatePostProcess.cs` — Volume 组件（参数来源）
- `Assets/CartoonRendering/Runtime/Pixelate/PixelatePostProcessFeature.cs` — Renderer Feature
- `Assets/CartoonRendering/Runtime/Pixelate/PixelatePostProcessPass.cs` — Render Graph Pass

## 使用方法
1. 打开 URP Renderer 资产（本工程：`Assets/Settings/CartoonRP_Renderer.asset`），
   **Add Renderer Feature → Pixelate Post Process Feature**（材质留空会自动创建）。
2. 选中场景中的 Global Volume（或新建 Local Volume），在 Profile 中
   **Add Override → CartoonRendering → Pixelate Post Process**。
3. 勾选并调高 **Intensity**（默认 0 = 关闭）即生效；其余参数按需勾选覆盖。

## Volume 参数
| 参数 | 默认 | 说明 |
|---|---|---|
| Intensity | 0 | 总开关/淡入淡出：0 = 原图，1 = 完整像素化 |
| Pixel Size | 4 | 像素块边长（屏幕物理像素），分辨率自适应 |
| Color Levels | 16 | 每通道色阶数（复古风 8，双色海报化 2） |
| Dither Strength | 0.5 | Bayer 抖动强度，1 = 一个量化步长 |
| Bayer Mode | Bayer 8x8 | 8x8 细腻 / 4x4 颗粒感 |

## 算法流程（严格顺序）
1. 屏幕 UV → 像素坐标（`uv * _ScreenParams.xy`）
2. 像素化：`blockCoord = floor(pixelCoord / _PixelSize)`，块中心 UV + Point Filter 采样
3. Bayer 有序抖动：基于 **blockCoord** 取模 → 阈值居中后 × DitherStrength 加到颜色上（量化之前！）
4. 色阶量化：`floor(saturate(c) * (L-1) + 0.5) / (L-1)`
5. 按 Intensity 与原图 lerp（供 Volume 权重淡入淡出）
6. 输出

## 注意事项
- **开关方式**：效果由 Volume 的 Intensity 控制（>0 才执行，整 Pass 直接跳过，零开销）；
  Renderer Feature 上不再暴露效果参数。
- **淡入淡出**：Local Volume + 碰撞体可实现进入区域时像素化渐入（Volume 权重 × Intensity）。
- **AA/MSAA**：注入点在 AA 之后；目标纹理强制 `MSAASamples.None`。
- **HDR**：量化前 saturate 钳制到 0~1；需要 HDR 海报化可移除 saturate。
- **深度纹理**：无依赖。
- **Render Graph**：仅实现 RG 路径；请保持项目处于 Render Graph 模式（Unity 6 默认）。
