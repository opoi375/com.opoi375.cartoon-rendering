# 体积云

半分辨率 RT 上光线步进的体积云。密度是**连续 3D 噪声场**，管线与 Schneider《Real-time Volumetric Cloudscapes of Horizon: Zero Dawn》/ Guerrilla 的 **Nubis** 一致。

![体积云：菜花状起伏、亮顶暗底、远处被大气透视淡入](/sky/volumetric-clouds.webp)

::: tip 这一版为什么重做
曾经用过"离散云团实例"（把世界切成 cell、每格摆一朵椭球云）来避免连续场渗流成一整块。**那条路走不通**：几何椭球 + 径向剖面做出来永远是药丸形，叠多少噪声都救不回来。

真实实现里：云的**形状完全来自连续 3D 噪声的膨胀与侵蚀**，"一朵一朵"的分离感来自**天气图的局部覆盖率**。已按此重做。
:::

## 密度管线

```
天气图(wuv)
  ├── R → 局部覆盖率 coverage
  ├── G → 云型 type（层云 ↔ 积云，决定高度带形状）
  └── BA → 域扭曲（推开采样坐标，云缘才卷）

shape  = tex3D(CloudShape3D, uvw)          R = Perlin-Worley，GBA = Worley FBM @ f4/f8/f16
fbmW   = dot(shape.gba, (0.625, 0.25, 0.125))
base   = remap(shape.r, -(1 - fbmW), 1)    ← 用 Worley 膨胀 Perlin（椰菜花的团块结构）
base  *= heightGradient(type)              ← 底部渐入、顶部渐出
base   = remap(base, 1 - coverage, 1) * coverage
density = remap(base, detail * 0.22, 1)    ← 细节噪声只抬一点下界，只啃边缘
```

三张图都由 **Tools > Clouds > Bake Noise** 生成，包内自带：

| 资产 | 尺寸 | 内容 |
| --- | --- | --- |
| `CloudShape3D` | 128³ | R = `Remap(perlinFBM, 0, 1, worleyFBM, 1)`；GBA = Worley FBM @ f4 / f8 / f16 |
| `CloudDetail3D` | 32³ | 高频 Worley FBM，边缘的细碎咬口 |
| `CloudWeather2D` | 512² | R = 覆盖率（云系），G = 云型，BA = 域扭曲 |

::: warning 不要做直方图均衡（CDF）
均匀分布看着对比更强，但它会**抹平 Perlin-Worley 的团块结构**，云就散成一层均匀棉絮。这是更早一版的错误。
:::

## 关键参数

| 参数 | 说明 |
| --- | --- |
| **Shape Scale** | **一块噪声图块对应多少米 —— 最直接的"云多大"旋钮。** 积云 1500~2500；太小会读成"烟雾条纹"（455m 就是这样），太大整片糊成一块 |
| **Coverage** | 全局覆盖率。实际覆盖率由天气图 R 给出，这个值做**整体平移**（不是相乘） |
| **Base Height / Thickness** | 云层高度范围（米，绝对世界 Y） |
| **Weather Scale** | 天气图世界尺度。0.00004 → 一块 ≈ 25km（大尺度云系），越大云系越碎 |
| **Warp** | 域扭曲强度（噪声图块单位） |
| **Density** | 消光 |
| **Detail** | 边缘侵蚀强度（只啃边缘，云芯不动） |
| **March / Light Steps** | 视线步进 / 朝太阳的自阴影步进 |
| **Noise LOD** | 噪声 mip。远处抬高可减闪烁，代价是细节变软 |
| **G Forward / Backward / Phase Forward / Scatter Octaves** | 双叶 HG 相位 + Wrenninge 倍频多重散射 |
| **Aerial Perspective** | 远处云按"消光 + 内散射"染进天空色，而不是只淡出 alpha |
| **Sun / Ambient Intensity** | 直射与漫射的独立强度。**太阳色按亮度归一化后**才乘，所以 1.0 ≈ 纯白 |
| **Shade Steps / Smooth** | cel 明暗分区。**默认 0（关闭）**：写实要连续渐变 |
| **Temporal Enabled** | 时序累积。见下方"已知问题"，默认关闭 |
| **Debug** | 六个调试视图，见下 |

完整参数表见[天空系统参数参考](/reference/sky-parameters#体积云)。

## 调试视图（`volCloudDebug`）

"天上没有云"和"云画了但看不见"用眼睛分不出来。这次重做里几个致命 bug 全是靠它们定位的。

| 视图 | 正常应该看到 | 不正常说明什么 |
| --- | --- | --- |
| `Coverage` | 天气图的成片深浅分区 | 整屏一个灰度 = 天气图没接上 |
| `RayCoverage` | 离散云块 | 整屏黑 = 密度恒 0（阈值 / 覆盖率问题） |
| `Height` | 竖向渐变 | 纯色 = 高度带把云整个裁掉了 |
| `Density` | 云块内部有值 | 全黑 = 3D 噪声没采到（尺度或 mip 问题） |
| `Depth` | 有值 | **纯红 = 射线根本没穿过云层**（云底太高 / 太薄） |
| `Shadow` | 云底亮、云顶暗 | 全暗 = 自阴影步进没生效 |

## 已知问题（如实记录）

- **时序累积（`volCloudTemporalEnabled`）会把云压成半透明薄雾**，所以默认关闭。历史缓冲亮度系统性低于当前帧，0.3 权重仍可见。已加"至少渲染过一帧才采样历史"的保护，根因未解。
- **云缘偏"烟絮"**，还不够蓬松的积云质感。
- **云层底部在极低仰角有一条硬边**（平板求交的固有现象）。
- **地面没有云影**。文献里这是"云看着假"的头号线索，需要新增一个 pass 与若干光照挂钩，尚未实现。
- 光照仍是简化模型（Beer-Powder + 双叶 HG + 倍频多重散射），没有真正的多次散射与自阴影级联。

## 性能：着色器编译

`Worley3` 是 27 邻域搜索。**绝对不要用 `[unroll]`** —— 它会在主步进、光照步进与调试视图里各内联一份，展开后等于把 27×3 次哈希复制到几百个调用点，D3D11 编译器会卡在 99% 好几分钟（实测 72 秒仍在跑）。当前实现已把噪声全部移到烘焙贴图里，着色器只做纹理采样。

## 渲染管线

```
全屏三角形（半分辨率 RT）
  → 射线与云层平板求交（baseY ~ baseY + thickness）
  → 固定步数步进；每步查天气图 → 高度带剔除 → 3D 形状噪声 →
    膨胀 → 覆盖率 remap → 细节侵蚀
  → 朝太阳二次步进求光学深度 → 双叶 HG 相位 × 倍频多重散射
  → 均匀薄片的解析积分 L·(1 - Tstep) → cel 量化（可选）
  → 大气透视 → 时序累积（可选）
  → 天空盒 pass 双线性上采样合成
```
