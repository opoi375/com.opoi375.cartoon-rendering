# SDF UI 图形

基于有符号距离场（SDF）的 UI 图形方案：边缘任意缩放都锐利，天然支持布尔运算与形变动画。

## 内置 Shader

| Shader | 内容 |
| --- | --- |
| `SDFShapesExample` | 基础图形：圆角矩形、圆形等 |
| `SDFBooleansExample` | 布尔运算：并 / 交 / 差 |
| `SDFMetaballsExample` | Metaball 融合 |
| `SDFTicketExample` | 票券造型（锯齿边等组合） |

公共函数库在 `Shaders/UI/SDF2D.hlsl`，可直接 include 写自己的 UI 图形。

## 典型用法

```hlsl
#include "Packages/com.opoi375.cartoon-rendering/Shaders/UI/SDF2D.hlsl"

float d = sdRoundedBox(uv, halfSize, radius);   // 距离场
float a = smoothstep(fwidth(d), 0.0, d);        // 抗锯齿边缘
color.a *= a;
```

## 配合 SDF 生成器

需要把位图遮罩转成 SDF 贴图（溶解、燃烧、面部阴影图等）时，用 [SDF 生成器](/tools/#sdf-生成器)。
