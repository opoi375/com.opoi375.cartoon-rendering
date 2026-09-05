# 概述

**Cartoon Rendering**（`com.opoi375.cartoon-rendering`）是一个面向 Unity 6 / URP 17 的风格化卡通渲染工具包，以 UPM 包形式通过 Git 分发。

它把一套完整的卡通渲染管线收进一个包里：从角色到环境、从天空到水面，所有模块风格统一、可以混用。

## 模块总览

| 模块 | 内容 | 入口 |
| --- | --- | --- |
| 卡通角色渲染 | PBRToon Base / Face / Eye / Hair 四个 shader + 光照库 | [详情](/shading/toon) |
| 程序化卡通天空 | 全屏天空盒 Render Feature、昼夜循环、星星、2D 云 | [详情](/sky/procedural-sky) |
| 体积云 | 光线步进体积云，卡通明暗分区 + 时序累积 | [详情](/sky/volumetric-clouds) |
| 卡通水面 | Simple（曲面细分波浪）/ Advanced（波浪 + 泡沫融合） | [详情](/water/) |
| 水下效果 | URP 后期 Render Feature | [详情](/water/#水下后期) |
| 像素化后期 | Volume 驱动的像素化效果 | [详情](/effects/) |
| 交互草地 | 踩踏压弯 + 恢复的 GPU 草场 | [详情](/grass/) |
| 世界弯曲 | 动森式小星球视角，全 Pass 接入 | [详情](/world-bend/) |
| SDF UI 图形 | 圆角矩形 / 布尔运算 / Metaball 等 UI 材质 | [详情](/ui-sdf/) |
| 编辑器工具 | SDF 生成器、云噪声烘焙、水面生成等 | [详情](/tools/) |

## 设计取向

- **URP 原生**：基于 Render Feature / RenderGraph / Volume 体系，不魔改管线
- **性能优先**：体积云半分辨率渲染 + 时序累积；水面用曲面细分代替高模网格；草场 GPU 实例化
- **可调可拆**：所有模块独立，只用其中一个 shader 也可以；参数集中在 ScriptableObject / Volume 上
- **工程化**：运行时与编辑器程序集分离（asmdef），注释详尽，含 TDD 测试场景出身

## 要求

- Unity **6000.5** 或更高版本
- Universal Render Pipeline **17.5.0+**（安装包时自动带入依赖）

## 许可

MIT License。包内不含任何第三方付费资源。
