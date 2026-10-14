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

> v1.8.0 重写：密度模型改成【离散云团实例】（世界平面切 cell，每朵云独立摆放）。
> 旧模型是"把连续噪声按覆盖率阈值化" —— 覆盖率一过渗流阈值（约 0.3）整块场就
> 连通，天空必然是一团。这是拓扑问题，调参救不了。
> 2D 贴图平流云层已一并删除。

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `volumetricCloudsEnabled` | false | 总开关 |
| `volCloudSpacing` | 950 | 云团 cell 间距（米）。朵与朵之间的留白由它决定 |
| `volCloudCoverage` | 0.62 | 一个 cell 是否住着云。平滑淡入，同时被 Weather 上下推 |
| `volCloudSize` | 0.55 | 云团半径占 cell 的比例（×2 ≈ 两朵云最窄处） |
| `volCloudSizeJitter` | 0.6 | 半径随机度 |
| `volCloudWeather` | 0.4 | 大尺度天气调制（波长 = 8 cell）。0 = 均匀撒糖 |
| `volCloudBaseHeight` | 750 | 云底高度（绝对世界 Y，米） |
| `volCloudThickness` | 420 | 单朵云最大厚度（米），每朵按 Height Jitter 变化 |
| `volCloudHeightJitter` | 0.6 | 厚度随机度 |
| `volCloudDome` | 0.5 | 圆顶指数：0.25 = 宽而扁（层云），0.7 = 圆墩（积云） |
| `volCloudWarp` | 0.55 | 轮廓域扭曲（把采样点推开，云缘卷起来） |
| `volCloudNoiseFreq` | 0.002 | 扭曲 / 侵蚀噪声频率（每米循环数） |
| `volCloudDetail` | 0.7 | 边缘侵蚀强度（只啃表面带，云芯不动） |
| `volCloudDensity` | 1.9 | 消光 |
| `volCloudMarchSteps` | 32 | 视线步进数（8~64） |
| `volCloudLightSteps` | 4 | 朝太阳的自阴影步进数（1~8） |
| `volCloudGForward` | 0.75 | 双叶 HG 前叶各向异性 g |
| `volCloudGBackward` | -0.25 | 后叶 g |
| `volCloudPhaseForward` | 0.65 | 前叶权重 |
| `volCloudScatterOctaves` | 3 | 多重散射倍频数（1 = 单次散射，云芯死灰） |
| `volCloudAerialPerspective` | 0.8 | 云的大气透视（消光 + 内散射染进天空色） |
| `volCloudWindSpeed` | 7 | 风速（米/秒） |
| `volCloudShadeSteps` | 3 | cel 明暗分区数（≤1.5 关闭量化） |
| `volCloudShadeSmooth` | 0.35 | 分区边界软化 |
| `volCloudSilverIntensity` | 0.3 | 逆光银边强度（按体素密度门控） |
| `volCloudSilverPower` | 8 | 银边聚拢度 |
| `volCloudTemporalEnabled` | **false** | ⚠ 已知问题：开起来会把云压成半透明薄雾，详见文档 |
| `volCloudTemporal` | 0.8 | 累积权重（仅在上一项开启时生效） |
| `volCloudDebug` | Off | 调试视图：Coverage / Instance / Height / Edge / Depth / Scatter / 积分诊断 |

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
