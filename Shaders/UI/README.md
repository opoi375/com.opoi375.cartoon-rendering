# SDF 2D — UI 用 SDF Shader 库与示例

## 文件
- `SDF2D.hlsl` — 函数库：基础形状 SDF + 布尔/融球运算 + 填充/描边/发光辅助
- `SDFShapesExample.shader` — 示例 1：`CartoonRendering/UI/SDFShapes`，单材质切换 5 种形状
- `SDFMetaballsExample.shader` — 示例 2：`CartoonRendering/UI/SDFMetaballs`，双圆融球
- `SDFBooleansExample.shader` — 示例 3：`CartoonRendering/UI/SDFBooleans`，6 种布尔运算演示
  （并/差/交 + 融球三变体，圆 A 可动画往复，操作数轮廓可开关）
- `SDFTicketExample.shader` — 示例 4：`CartoonRendering/UI/SDFTicket`，链式布尔实战：
  票券 = 圆角矩形 − 两侧打孔 − 虚线撕裂孔，另有内缩装饰框（`d + inset` 等距偏移描边）
- 示例材质：`Assets/CartoonRendering/Materials/UI/`（SDFShapes_* / SDFMetaballs_Demo /
  SDFBool_Union / Subtract / Intersect / SmoothUnion / SmoothSubtract / SmoothIntersect / SDFTicket_Demo）

## 验证方法（30 秒）
1. 场景或 Canvas 下建一个 **UI → Image**（不用指定 Sprite）。
2. 把示例材质拖到 Image 的 **Material** 槽。
3. 即可看到形状；Play 模式下 SDFMetaballs_Demo 会自动绕轨道演示粘连/分离。
4. 场景里不用 Canvas 也行：建一个 **Quad** 挂材质同样可见。

## 函数库速查

| 类别 | 函数 |
|---|---|
| 形状 | `sdCircle` `sdBox` `sdRoundedBox` `sdRing` `sdCapsule` `sdEquilateralTriangle` |
| 硬布尔 | `opUnion` `opSubtract` `opIntersect` |
| **融球（平滑布尔）** | `opSmoothUnion(d1, d2, k)` `opSmoothSubtract` `opSmoothIntersect` |
| 绘制 | `sdfFill(d, aa)` `sdfStroke(d, width, aa)` `sdfGlow(d, radius)` |

约定：`d < 0` 在形状内部；`aa` 传 `fwidth(d) * 1.2` 获得屏幕空间抗锯齿；
坐标居中写法 `float2 p = uv - 0.5; p.x *= aspect;`。

## 融球原理
`opSmoothUnion(d1, d2, k)` = 平滑最小值：两形状距离小于 k 时边界被"拉起"粘连，
k 越大粘连带越宽（水滴/史莱姆感），k = 0 退化为普通并集。

## 布尔运算速记
- 差集方向：`opSubtract(dBody, dCutter)` —— 第二个参数是"剪刀"
- 链式写法：多个孔先 `opUnion` 合并，再一次性 `opSubtract` 从主体挖掉
- 等距偏移：`d + offset` 整体向内缩（负偏移向外扩），配合 `sdfStroke` 画内框

## 注意事项
- 抗锯齿依赖 `fwidth` 屏幕空间导数，任何分辨率/缩放下边缘都锐利。
- 示例未接 `UNITY_UI_CLIP_RECT`（RectMask2D 裁剪）；需要时在 frag 里加
  `UnityGet2DClipping` 即可，SDF 部分不受影响。
- 在自己的 shader 里引用函数库：与 shader 同目录放 `#include "SDF2D.hlsl"`，
  或用完整路径 `#include "Assets/CartoonRendering/Shaders/UI/SDF2D.hlsl"`。
