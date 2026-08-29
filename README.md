# Cartoon Rendering

适用于 Unity URP 的风格化卡通渲染工具包。

## 功能

- **卡通建筑 Shader** — 带自定义光照 HLSL 的 Toon 建筑着色器
- **卡通水面** — 简单 / 高级水面材质与水面生成工具
- **程序化卡通天空** — 昼夜循环（Time-of-Day）系统与天空盒渲染特性
- **水下后期效果** — URP Render Feature 实现的水下雾效
- **像素化后期效果** — Pixelate Render Feature
- **交互草地** — 草地生成器、交互碰撞规划与编辑期烘焙工具
- **SDF UI 图形** — 圆角矩形、圆形、布尔运算、Metaball 等 UI SDF 材质

## 安装

### 通过 Git URL

在 Package Manager 中选择 **Add package from git URL**：

```
https://github.com/<your-org>/com.opoi375.cartoon-rendering.git
```

### 通过本地路径

**Add package from disk**，选择本目录下的 `package.json`。

## 要求

- Unity 6000.5 或更高版本
- Universal Render Pipeline 17.5.0+（自动作为依赖安装）

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Runtime/` | 运行时脚本（URP Render Feature、天空、水下、草地等） |
| `Editor/` | 编辑器工具（SDF 烘焙、水面生成、草地工具等） |
| `Shaders/` | Shader 与 HLSL 库 |
| `Materials/` | 示例材质 |
| `Textures/` | 纹理资源 |
| `Data/` | ScriptableObject 配置（天空、水下配置） |
| `Tests/` | 编辑器测试 |
