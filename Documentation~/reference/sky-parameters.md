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

## Volumetric Clouds（体积云）

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | 总开关 |
| `volCloudNoiseTexture` | 内置 | 128³ 噪声体积（可用烘焙工具重生成） |
| `volCloudCoverage` | 0.5 | 云量 |
| `volCloudBaseHeight` | 600 | 云底高度（米） |
| `volCloudThickness` | 700 | 云层厚度（米） |
| `volCloudScale` | 0.0005 | 噪声平铺尺度的倒数（≈2000m/块） |
| `volCloudDensity` | 1.2 | 整体密度 |
| `volCloudMarchSteps` | 40 | 步进次数（8~48） |
| `volCloudLightSteps` | 5 | 朝太阳的光照步进数 |
| `volCloudDetail` | 0.35 | 细节侵蚀强度 |
| `volCloudWindSpeed` | 12 | 风速 |
| `volCloudShadeSteps` | 3 | 卡通明暗分区数 |
| `volCloudShadeSmooth` | 0.2 | 分区边界软化 |
| `volCloudSilverIntensity` | 0.6 | 银边强度 |
| `volCloudSilverPower` | 6 | 银边聚拢度 |
| `volCloudTemporalEnabled` | true | **时序累积开关**（关闭时不分配历史缓冲、jitter 冻结） |
| `volCloudTemporal` | 0.8 | 累积权重（0~0.9，0 = 关闭混合） |

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
