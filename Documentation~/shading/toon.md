# 卡通角色渲染（PBRToon）

PBRToon 是面向角色的卡通着色器家族，沿用 URP Lit.shader 的 Pass 结构，但光照模型替换为卡通光照（`PBRToon.hlsl`）。

## 四个 Shader

| Shader | 用途 |
| --- | --- |
| `CartoonRendering/PBRToonBase` | 通用：身体、服装、道具。基色 + 阴影色阶 + 边缘光 + 描边 |
| `CartoonRendering/PBRToonFace` | 面部：支持 **SDF 面部阴影图**，光照随阳光角度平滑过渡 |
| `CartoonRendering/PBRToonEye` | 眼睛：高光、虹膜细节 |
| `CartoonRendering/PBRToonHair` | 头发：各向异性高光、色阶明暗 |

## 核心特性

- **阴影色阶（Shadow Ramp）**：明暗被量化为 2~3 个色区，边界可软化
- **边缘光（Rim）**：视角相关的轮廓提亮
- **SDF 面部阴影**：用一张预烘焙的 SDF 图代替法线阴影，任意光照角度下面部阴影都干净（配合 [SDF 生成器](/tools/#sdf-生成器)烘焙）
- **完整管线支持**：Progressive Lightmapper 烘焙（Meta pass）、Light Probe / Lightmap 采样、Forward+ 附加光、Shadowmask、APV

## 卡通建筑（CartoonBuilding）

`CartoonRendering/Building` 与 PBRToon 共享完整管线支持，但使用**平滑风格化光照**（无 cel 色阶）：

- Wrapped Lambert 主光（`_LightWrap`）、阴影染色（`_ShadowColor`）
- 平滑点光 / 聚光（Forward+ 簇遍历）
- 可选法线贴图 / 边缘光 / Blinn-Phong 高光 / 自发光
- 平滑烘焙 GI（`_BakedGIIntensity`）

适合场景建筑、道具等不需要硬色阶但要有卡通柔和感的物件。

## 使用建议

1. 角色材质尽量走 Face / Eye / Hair 专用 shader，效果比 Base 好
2. SDF 面部阴影图用 **Tools > SDF > SDF Generator** 从遮罩图烘焙
3. 描边宽度随相机距离衰减，远景不会糊成一团
