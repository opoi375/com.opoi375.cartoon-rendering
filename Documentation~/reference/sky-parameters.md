# 天空系统参数参考

`CartoonProceduralSky` ScriptableObject 的完整参数表（默认值即包内预设）。

## Sun

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `sunColor` | 暖白 | 太阳颜色 |
| `sunSize` | 0.04 | 圆盘角尺寸 |
| `sunInnerBound` | 0.2 | 内边界（实心部分占比） |
| `sunOuterBound` | 0.8 | 外边界（柔化范围） |
| `includeSunDisc` | true | 是否绘制太阳圆盘 |

## Stars

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `starColor` | 冷白 | 星星颜色 |
| `starIntensity` | 1.5 | 强度 |
| `starSize` | 0.5 | 大小 |
| `starScale` | 1.0 | 分布缩放 |
| `starDensity` | 0.5 | 密度（稀疏 ↔ 密集） |
| `starTexture` | — | 可选自定义星星纹理 |

## Clouds（2D 平流云）

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `cloudColor` | 白 | 云颜色 |
| `cloudHeight` | 60 | 视高 |
| `cloudThreshold` | 0.45 | 覆盖阈值 |
| `cloudScale` | 1.0 | 缩放 |
| `cloudIntensity` | 1.0 | 强度 |
| `cloudTexture` | — | 云噪声贴图 |

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | 总开关 |
| `volCloudShapeTex` | 内置 | 形状噪声 128³：R = Perlin-Worley，GBA = Worley FBM @ f4/f8/f16 |
| `volCloudDetailTex` | 内置 | 细节噪声 32³：高频 Worley FBM，只啃边缘 |
| `volCloudWeatherTex` | 内置 | 天气图 512²：R = 覆盖率（云系），G = 云型，BA = 域扭曲 |
| `volCloudShapeScale` | 1800 | **一块噪声图块对应多少米 —— "云多大"最直接的旋钮。** 积云 1500~2500；455 会读成烟雾条纹 |
| `volCloudCoverage` | 0.42 | 全局覆盖率，对天气图的局部覆盖率做**整体平移**（不是相乘） |
| `volCloudBaseHeight` | 900 | 云底高度（绝对世界 Y，米） |
| `volCloudThickness` | 1100 | 云层厚度（米） |
| `volCloudWeatherScale` | 0.00004 | 天气图世界尺度：1 米 = 多少图块。0.00004 → 一块 ≈ 25km |
| `volCloudWarp` | 0.12 | 域扭曲强度（噪声图块单位），云缘卷曲 |
| `volCloudNoiseLod` | 0 | 噪声 mip。远处抬高可减闪烁，代价是细节变软 |
| `volCloudDensity` | 6.0 | 消光 |
| `volCloudDetail` | 0.5 | 边缘侵蚀强度（只啃边缘，云芯不动） |
| `volCloudMarchSteps` | 48 | 视线步进数（8~64） |
| `volCloudLightSteps` | 6 | 朝太阳的自阴影步进数（1~8） |
| `volCloudGForward` | 0.75 | 双叶 HG 前叶各向异性 g |
| `volCloudGBackward` | -0.25 | 后叶 g |
| `volCloudPhaseForward` | 0.65 | 前叶权重 |
| `volCloudScatterOctaves` | 3 | 多重散射倍频数（1 = 单次散射，云芯死灰） |
| `volCloudAerialPerspective` | 0.8 | 云的大气透视（消光 + 内散射染进天空色） |
| `volCloudSunIntensity` | 1.5 | 直射强度（太阳色已按亮度归一化，1.0 ≈ 纯白） |
| `volCloudAmbientIntensity` | 1.0 | 环境（漫射）强度。云底暗不下来多半是它太高 |
| `volCloudWindSpeed` | 7 | 风速（米/秒） |
| `volCloudShadeSteps` | **0** | cel 明暗分区数。**0 = 关闭（连续渐变，写实默认）**；2~4 才是卡通硬边 |
| `volCloudShadeSmooth` | 0.35 | 分区边界软化 |
| `volCloudSilverIntensity` | 0.3 | 逆光银边强度（按体素密度门控） |
| `volCloudSilverPower` | 8 | 银边聚拢度 |
| `volCloudTemporalEnabled` | **false** | ⚠ 已知问题：开起来会把云压成半透明薄雾，详见文档 |
| `volCloudTemporal` | 0.8 | 累积权重（仅在上一项开启时生效） |
| `volCloudDebug` | Off | 调试视图：Coverage / RayCoverage / Height / Density / Depth / Shadow |

## Sky Gradient（三段调色板）

- **Day**：`topColor` / `middleColor` / `bottomColor` / `horizonColor` / `backgroundColor`
- **Sunset**：`sunsetTopColor` / `sunsetMiddleColor` / `sunsetHorizonColor`
- **Night**：`nightTopColor` / `nightMiddleColor` / `nightHorizonColor` / `nightBackgroundColor`
- `gradientSoftness`（0.15）：色标间柔化
- `sunsetBlendStart`（0.05）/ `dayBlendEnd`（0.35）：昼夜过渡区间
- `gradientRamp`：可选渐变贴图，精细控制

## Overall

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `intensity` | 1.0 | 天空整体亮度 |
