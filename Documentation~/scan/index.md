# 扫描特效（Scan Effect）

以自身为中心的扫描 / 雷达面扩散。半屏全屏后期单 Pass，接在 URP Renderer Feature 上。

![体素立体方格扫描：地面与墙面上的 3D 立方格、棱线描边、青色科技感](/scan/voxel-scan.webp)

> **当前状态：可运行，两种模式均已截图验证。**
> 上图是体素模式在本包演示场景里的实拍（`pattern = Voxel`、`voxelSize = 1.2m`）。

## 实现方案

```
ScanEffectController (场景里，玩家/相机上)
        │ 只写
        ▼
ScanEffectRuntime (静态动画状态：active / origin / forward / front / fadeOut)
        │ 只读                        ScanEffect (VolumeComponent：外观参数)
        ▼                                      │ 只读
ScanEffectFeature.AddRenderPasses ─────────────┘
        │ 三道闸都过才 EnqueuePass
        ▼
ScanEffectPass.RecordRenderGraph
        │ UseTexture(Depth) + SetRenderAttachment(activeColor)
        ▼
ScanEffect.shader (全屏三角形，半径/投影距离 → 波前 → 细节 → 合成)
```

职责分离是刻意的：**外观归 Volume（美术改）、动画归 Runtime（玩法推）、Pass 只读**。
所以删掉控制器不会破坏渲染，换 Volume 预设也不会影响触发逻辑。

### 两种形态

| 形态 | 距离度量 | 用途 |
| --- | --- | --- |
| `Radial` | `d = length(posWS - origin)` | 以角色为球心向四周扩散（雷达） |
| `Frontal` | `d = dot(posWS - origin, forward)`，再按 `cosHalfAngle` 裁扇形 | 视野前方推进扫描 |

`Sector Angle = 360` 时 `cos(180°) = -1`，扇形裁切自然退化成"全通过"，所以两种形态共用一条公式。

### 体素模式（立体方格）

除了平面网格，还提供**真正的 3D 立方格**扫描 —— 世界被量化成边长为 `voxelSize` 的立方体，
波前经过时格子**逐块点亮**，形成涌动的体素扫描感。

与平面网格的区别：

| | 基础线框 | 体素模式 |
| --- | --- | --- |
| 几何 | 屏幕对齐的三轴网格 + 格心点阵 | 世界空间立方体（`floor(p / voxelSize)`） |
| 棱线 | 单轴格线 | **两个及以上轴同时贴面** → 立方体的 12 条棱 |
| 内部 | 无 | 按格哈希给每块一个亮度，读作"实心体素块" |
| 点亮 | 波前扫过即达 | `reveal = (front - 格心距离) / (voxelSize × 1.5)`，逐层点亮 |
| 开关 | `voxelSize = 0`（默认） | `voxelSize > 0` |

关键实现（`ScanVoxel`）：

```hlsl
float3 f  = frac(p / cell);
float3 nrm = min(f, 1 - f) * cell;              // 到各轴格面的距离（米）
float  w   = max(fwidth(p.x), 1e-4) * 1.2;      // 线宽随屏幕导数自适应，远处不摩尔纹
float3 on  = 1 - smoothstep(0, w, nrm);         // 该轴贴近格面

float s    = on.x + on.y + on.z;
float edge = saturate(s - 1.0);                 // ≥2 轴 → 立方体棱
float face = saturate(s) - edge;                // 恰好 1 轴 → 格面
```

`voxelSize` 大于 0 时 shader 自动切到体素分支，`voxelFill` 控制空心格 / 实心块，
`voxelReveal` 控制格子被点亮的柔和程度。

::: tip 已验证
上图就是体素模式的实际输出：世界空间立方格在地面、墙面、天花板上都按透视正确排列，
棱线分明，波前经过时逐块点亮。注意它不是屏幕空间网格 —— 转动视角时方格是"贴"在世界上的。
:::

### 合成方式（为什么不读 activeColor）

与包内水下后期同一套约定：`Blend One OneMinusSrcAlpha`，且**不读** `activeColor`
（RenderGraph 里它是外部导入资源，读了可能拿到空缓冲）。于是：

```
result = src.rgb + dst.rgb · (1 - src.a)
```

着色器据此编码：

```
src.a   = a              ← 被扫过的强度，0 = 完全恢复原样
src.rgb = tech · a + glow
```

合成后 `result = dst·(1-a) + tech·a + glow`，即"向扫描色靠拢 a，再叠自发光"。
**`a = 0` 时逐像素恒等于原图**，所以关闭或未扫到时一个像素都不动。

### 五个视觉层次

| 层 | 公式 / 做法 | 关掉的参数 |
| --- | --- | --- |
| 波前 | `exp(-((d - front)/lineWidth)²)`，薄壳 + 屏幕导数下限防闪 | `lineWidth` 极大 |
| 拖尾 | `exp(-(front - d)/trailLength)`，只在波前后方 | `trailLength = 0` |
| 扫描区细节 | 三轴网格（`fwidth` 自适应反走样）+ 格心点阵 + 沿 Y 滚动的水平扫描线 | `detailIntensity = 0` |
| 边缘描边 | 深度 Roberts 差 / 自身深度，相对量 → 近处细远处不致糊 | `edgeIntensity = 0` 或质量 Low |
| 辉光 | 波前 + 拖尾 + 描边三处叠加，颜色取 `edgeColor`，交给管线 Bloom | `glowIntensity = 0` |

**噪波扰动**：把波前半径按方向抖动（`ScanFbm`），避免一个完美的圆规圆。质量 High 时叠第二层更碎的噪波。

**恢复**：`age = (front - d) / 速度`；`restore = curve(age / restoreTime)`，三种曲线（线性 / 平滑 / 二次）。

### 质量档位

| 档 | 噪波扰动 | 深度描边 | 网格/点阵 | 适用 |
| --- | --- | --- | --- | --- |
| Low | 关 | 关 | 简化（无点阵淡出） | 移动端 / 同屏多次扫描 |
| Medium | 单层 | 关 | 完整 | 默认 |
| High | 双层 | 开 | 完整 | PC / 特写镜头 |

质量档不改接口，只改采样与附加效果开关；`IsActive()` 为 false 时 Pass 根本不入图（零开销）。

### 触发方式

| 方式 | 入口 | 说明 |
| --- | --- | --- |
| 手动 | `controller.Trigger()` | 代码 / UnityEvent |
| 技能 | `controller.TriggerSkill()` | 同一动画，半径 ×1.6、速度 ×1.3，后半段倍率回落避免连续释放叠加 |
| 自动演示 | `autoDemo` + `autoInterval` | 循环触发，看效果 / 录屏用 |
| 快捷键 | `hotkey`(默认 F5) / `toggleKey`(默认 F6) | 兼容新 Input System 与旧 Input Manager |

### 与既有后处理的兼容

- 默认注入 `BeforeRenderingPostProcessing`：在本包 Underwater / 体积光之后入图，辉光仍会被 Bloom / Tonemap 接手。
- 未激活时**不 EnqueuePass**，也不会声明深度依赖 —— 不影响普通渲染表现。
- 只做加法与向扫描色靠拢，不读 `activeColor`，因此不与水下后期、体积光、SSIL 抢写同一块缓冲。
- 预览相机 / 反射探针直接跳过。
- 拿不到深度图时打印一次警告并跳过，而不是静默画一层平涂。

## 关键参数表

### 主控

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `intensity` | 1 | 总强度。0 = Pass 不入图 |
| `enabled` | true | 主开关，快捷键 F6 切换 |
| `pattern` | Radial | 形态：四周扩散 / 前方推进 |
| `quality` | Medium | 质量档位 |

### 范围

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `radius` | 40 | 最大扫描半径（米），Radial 用 |
| `distance` | 80 | 最大推进距离（米），Frontal 用 |
| `speed` | 45 | 扫描速度（米/秒） |
| `sectorAngle` | 360 | 扇形半角 ×2（度）。360 = 整圈；Frontal 常用 30~70 |

### 外观

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `lineWidth` | 1.2 | 波前厚度（米）。薄 = 一道锐利光，厚 = 发光带 |
| `color` | 青 | 扫描主色（网格 / 点阵 / 染色） |
| `edgeColor` | 亮青白 | 强调色（波前 / 描边 / 辉光） |
| `edgeIntensity` | 1 | 边缘高亮强度 |
| `glowIntensity` | 1.2 | 辉光强度 |
| `trailLength` | 6 | 拖尾长度（米） |
| `gridDensity` | 0.2 | 网格密度（每米格数），0.2 ≈ 5 米一格 |
| `dotDensity` | 0.5 | 点阵密度（每米点数） |
| `detailIntensity` | 1 | 扫描区细节总强度 |

### 体素风格

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `voxelSize` | 0 | 体素边长（米）。**> 0 即切到立体方格模式**；0 = 用基础线框。常用 1~3 |
| `voxelFill` | 0.45 | 内部填充强度。0 = 空心线框，1 = 实心块（每块带哈希明暗） |
| `voxelReveal` | 3 | 波前揭开宽度（米）。越小格子点亮越崩脆，越大越柔和 |

### 行为

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `restoreTime` | 0.8 | 恢复时间（秒） |
| `restoreCurve` | Smooth | 线性 / 平滑 / 二次 |
| `noise` | 0.15 | 波前噪波扰动 |
| `attenuation` | 1 | 距离衰减指数，0 = 不衰减 |

## 测试验收清单

### 接入

- [ ] `Tools > Scan > Setup Scan Effect` 后，Renderer 资产的 `m_RendererFeatures` 与
      `m_RendererFeatureMap` **长度一致**（只加列表不刷 map 是静默失效）
- [ ] Volume Profile 里出现 `Scan Effect` 组件（不是 `fileID: 0` 的空引用）
- [ ] URP 管线资产 `m_RequireDepthTexture = 1`

### 功能

- [ ] `Trigger()` 能触发一次完整扫描并自动恢复到原材质
- [ ] `TriggerSkill()` 半径/速度明显大于手动版
- [ ] `autoDemo` 按间隔循环；关掉后不再自动触发
- [ ] F5 触发、F6 开关（新 Input System 与旧 Input Manager 两种工程都不抛异常）
- [ ] `centerMode = Target` 时以角色为中心；`Target` 为空时回退到相机

### 视觉

- [ ] 波前是**薄壳**而不是整块发光
- [ ] 波前后面有拖尾，且拖尾比波前暗
- [ ] 网格是**三轴**的（含竖直方向），点阵只出现在格心
- [ ] 远处的网格因亚像素反走样而淡出，**不出现摩尔纹 / 闪烁**
- [ ] 扫描区几何轮廓有描边（质量 Medium 及以上）
- [ ] 恢复过程平滑，**没有硬切**
- [ ] 关闭（F6）后画面与开启前**逐像素一致**

### 兼容

- [ ] 水下场景里两者同时开启：顺序正确、不互相覆盖、不爆白
- [ ] 只有体积云 / 体积光的场景里，关掉扫描特效后画面无变化
- [ ] 相机不透明纹理关闭时（`m_RequireOpaqueTexture = 0`）仍正常工作（本通道不依赖它）
- [ ] 反射探针与预览窗口不出现扫描

### 性能

- [ ] 关闭时 Frame Debugger 里**看不到** `ScanEffect` 通道
- [ ] 开启时只有 1 个 draw call，无额外 RT 分配
- [ ] Low / Medium / High 三档之间 GPU 时间有可测差异（High 才开深度描边）

## 已知问题

### 已解决：之前为什么"完全画不出来"

排查耗时最长，两处根因都值得记住：

| # | 根因 | 现象 |
| --- | --- | --- |
| 1 | **Volume 参数的 YAML 格式写错** | 我最初把参数写成单行 `diagnosticSolid: 1`。Volume 参数是嵌套的 `VolumeParameter`（`m_OverrideState` / `m_Value`），单行会被 Unity 直接忽略 —— 所以"强制纯红"从来没生效过，我因此误判了整整几轮。改成嵌套结构后立刻出红 |
| 2 | **场景没有 Volume，或 Volume 的 profile 里没这个组件** | `VolumeManager.stack.GetComponent<ScanEffect>()` 返回 null → 通道不入图 → 屏幕上毫无迹象 |

对应的代码改动：

- `ScanEffectFeature` 在"有扫描在跑、但 Volume 栈里没有组件"时**打一次警告**并说明怎么补，
  不再静默不画。
- `Tools > Scan > Setup Scan Effect` 现在会：刷新 `m_RendererFeatureMap`（无条件）、
  给场景里的 Volume 补组件、**场景完全没有 Volume 时自动新建一个 Global Volume**。

### 另一个坑：Y 翻转

原先照抄了水下后期那套 `graphicsUVStartsAtTop ? (1,-1,0,1) : (1,1,0,0)`，实测**整屏上下颠倒**：
深度采样拿到镜像位置 → 世界坐标错 → 距离判断全崩 → 整屏都被判成"已扫过"。
本通道写的是 `activeColorTexture`，深度与它同向，**不需要翻转**，已改成恒等变换。

### 已修正：三态诊断的推送

`diagnosticSolid` 原本是 `BoolParameter`，C# 侧 `? 1f : 0f` 只会推 0/1，所以即使把它设成 2
（"未激活时转绿"）也会走成"无条件纯红"。已改为 `ClampedFloatParameter(0..2)` 并原样推送。
这个坑值得注意：**改了着色器的诊断分支，一定要同步改 C# 的推送类型**，否则诊断工具本身在骗人。

### 仍未验证

- 与水下后期 / 体积光同场景同时开启时的叠加表现未测。

### 排查工具

`Tools > Scan > Diagnose Draw (RenderTexture)` 会做四组对照（本 shader / 水下的对照 ×
DrawProcedural / Blit），一次跑完就能判定"是着色器不出像素"还是"渲染图接入位置不对"。

注意：它回读的是**自己新建的临时 RT**，与 Game View 的呈现路径不同，所以结论是
"材质能不能出像素"，不能据此判断注入点。

