# 程序化卡通天空

`CartoonProceduralSky` 是一个 ScriptableObject 配置资产 + `CartoonSkyboxFeature`（URP Render Feature）组成的天空系统。

## 为什么不用 RenderSettings.skybox

Unity 6 / URP 17 的原生 skybox 管线只绘制四个内置 skybox shader，自定义 shader 会被**静默忽略**。本系统用全屏三角形 + 解析重建视线方向（NDC → 逆 VP → 世界射线），在 `BeforeRenderingSkybox` 注入，彻底绕开该限制，同时避免了相机立方体方案在三角形接缝处的色带问题。

## 功能组成

- **五段式天空渐变**：Top / Middle / Bottom / Horizon / Background 五个色标
- **三套调色板**：白天 / 黄昏 / 夜晚，根据方向光高度自动混合（`sunsetBlendStart`、`dayBlendEnd` 控制过渡区间）
- **太阳圆盘**：可调大小、内外边界柔化，可整体关闭
- **星星**：夜间自动出现，可调颜色 / 强度 / 大小 / 密度 / 缩放，支持自定义纹理
- **2D 云层**：贴图驱动的平流云，可调高度 / 阈值 / 缩放 / 强度
- **体积云**：独立模块，见[体积云](/sky/volumetric-clouds)
- **渐变 Ramp**：可用一张 `gradientRamp` 贴图精细控制渐变

## 昼夜循环

`TimeOfDaySystem` 从方向光的旋转推导归一化时间（0..1），天空 shader 据此在三套调色板间混合。

**只需要旋转方向光**，天空、太阳位置、环境光、星星全部自动跟随。`CartoonProceduralSkyUpdater` 组件可挂在方向光上自动同步环境光颜色。

## 快速配置

1. `Assets > Create > Cartoon Rendering > Procedural Sky`
2. URP Renderer 添加 **Cartoon Skybox Feature**，把资产赋给 Sky 字段
3. 旋转方向光查看昼夜变化

全部参数见[天空系统参数参考](/reference/sky-parameters)。
