# LED 点阵文字屏

`CartoonRendering/LED/DotMatrix Text` 把任意文字渲染成 LED 点阵屏 —— 每个字由规则网格上的圆形发光点拼出来，带辉光、扫描线与逐点闪烁。

文字本身不是「画」出来的，而是**点**出来的：一张文字遮罩图决定网格上每个 LED 的亮灭。

## 特点

- **纯程序化点阵** —— 圆点由 `length(frac(uv)) + smoothstep` 生成，不需要任何点阵贴图
- **解析抗锯齿** —— 用 `fwidth` 按屏幕导数自动软化点边缘，远距离与斜视角下不会摩尔纹闪烁
- **运行时可换文案** —— `LedTextMaskBaker` 走 `Font.GetCharacterInfo` 把字形位图直接拼成遮罩纹理，不依赖字体资产、不需要额外相机
- **滚动 / 扫描 / 闪烁** —— 遮罩 UV 滚动、横向扫描线、双频正弦逐点闪烁
- **HDR 发光** —— 亮起点走 HDR，配合 Bloom 出真实辉光；熄灭的点仍然可见，保留面板质感

## 快速设置

1. 建一块屏（平面或弧面）：
   - 手动：给物体加 `LedCurvedScreen`，它会程序化生成弧面网格，UV 按**弧长**均匀展开
   - 或直接用菜单 **Tools > LED > Create Demo Scene** 一键生成整套演示场景
2. 新建材质，Shader 选 **CartoonRendering/LED/DotMatrix Text**
3. 物体上加 `LedTextMaskBaker`，填文案；材质列表留空即自动抓同物体 Renderer 上的材质
4. 改文案后右键组件 **Rebake**（或勾 `bakeOnEnable` 让它自己烘）
5. 在 Volume Profile 里加 **Bloom**，阈值 0.8 左右

## 文字遮罩从哪来

| 方式 | 适用 |
| --- | --- |
| `LedTextMaskBaker`（推荐） | 文案要运行时改，或要支持中文。`osFontName` 填系统字体名，如 `Microsoft YaHei` |
| 白字黑底 PNG | 固定文案，最省事。导入设置里压缩选 **None**，否则文字边缘会有脏边 |

遮罩是单通道（R8）纹理，白 = 亮。

`LedTextMaskBaker` 会自动把 `_Aspect` 写成遮罩宽高比；同物体上若有 `LedCurvedScreen`，还会按屏幕宽高比自动修正遮罩高度，保证点是正圆。

## 参数速查

### 网格

| 参数 | 说明 |
| --- | --- |
| `_Cols` | 横向 LED 数量，越大点越密 |
| `_Aspect` | 屏幕宽高比 W/H，由 baker 自动写入；手填时务必填对，否则点会变成椭圆 |
| `_DotRadius` | 点半径（以格子为单位），0.36 ~ 0.42 比较自然 |
| `_DotSoftness` | 点边缘柔化，和 `fwidth` 抗锯齿配合 |

### 遮罩与滚动

| 参数 | 说明 |
| --- | --- |
| `_MaskThreshold` / `_MaskSoftness` | 遮罩二值化阈值与过渡宽度 |
| `_MaskTransform` | 遮罩平铺 / 偏移（XY = Tiling，ZW = Offset） |
| `_Scroll` | UV 滚动速度。**默认保持 0** —— 编辑模式下 `_Time` 一直在跑，非零值会让文字慢慢滚出屏幕 |

### 闪烁与扫描线

| 参数 | 说明 |
| --- | --- |
| `_FlickerAmount` | 闪烁强度。每个点独立随机相位 + 双频正弦，所以不会整屏齐闪 |
| `_FlickerSpeed` / `_FlickerRatio` | 基频与第二频率倍率 |
| `_ScanIntensity` / `_ScanSpeed` / `_ScanWidth` | 横向扫描线 |

### 颜色

| 参数 | 说明 |
| --- | --- |
| `_OnColor` / `_OnIntensity` | 亮起点颜色（HDR）与强度，配合 Bloom |
| `_OffColor` | 熄灭点颜色。**别设全黑** —— 保留可见的暗点阵才有 LED 面板质感 |
| `_PanelColor` | 点与点之间的面板底色 |

## 工具

| 菜单 | 作用 |
| --- | --- |
| **Tools > LED > Create Demo Scene** | 新建并保存演示场景到 `Assets/Scenes/LEDDotMatrix.unity` |
| **Tools > LED > Build In Current Scene** | 只在当前场景重建，不写场景文件 |
| **Tools > LED > Dump Mask PNG** | 诊断用：导出遮罩到 `Assets/CartoonRendering/LED/`，并打印材质 / 网格状态 |

工具生成的材质与 Volume Profile 落在 `Assets/CartoonRendering/LED/`。

## 实现要点

1. **UV 网格化** —— `floor(uv × 格子数)` 拿到每个 LED 的 cell id，`frac` 拿到格内局部坐标（格子中心为原点）
2. **程序化圆形点阵** —— 格内画圆，用 `fwidth` 得到的屏幕导数决定边缘硬度
3. **纹理遮罩采样** —— 在格子**中心**采样文字遮罩，2×2 超采样防止细笔画被漏掉
4. **UV 滚动** —— 遮罩采样坐标加 `_Time.y × _Scroll`
5. **双频正弦闪烁** —— 两个频率叠加，每个点用 `frac(sin(dot(cell, …)))` 取独立相位

### 关于弧面屏

`LedCurvedScreen` 的 UV.x 按**弧长**均匀展开，而不是平面投影 —— 所以 LED 点在弧面上依然等距，文字不会被两头拉长。`arcAngle` 为正表示凹面朝向相机，为负则向外凸出。网格法线朝向会自动纠正，不会出现整块屏被背面剔除掉的情况。
