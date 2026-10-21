# 编辑器工具一览

包内自带的编辑器工具，菜单位于 **Tools** 与 **CartoonRendering** 下。

## SDF 生成器

菜单：**Tools > SDF > SDF Generator**（GPU 加速，Compute Shader 烘焙）

- **页签 1「遮罩 → SDF」**：把黑白遮罩图烘焙成归一化 SDF 贴图（0.5 为分界线）—— 用于 PBRToon 面部阴影图、溶解效果等
- **页签 2「多帧 → 渐变」**：把一组名字以 `_帧号` 结尾的 SDF 贴图（如 `xxx_SDF_177`）插值合成渐变贴图 —— 用于溶解 / 燃烧 / 生长类效果

## 云噪声

**Tools > Clouds > Bake Noise** 一次烘三张图（体积云用）：

| 菜单 | 资产 | 内容 |
| --- | --- | --- |
| Bake Shape Noise 3D | `CloudShape3D` 128³ | R = Perlin-Worley，GBA = Worley FBM @ f4/f8/f16 |
| Bake Detail Noise 3D | `CloudDetail3D` 32³ | 高频 Worley FBM（边缘咬口） |
| Bake Weather Map 2D | `CloudWeather2D` 512² | R = 覆盖率，G = 云型，BA = 域扭曲 |

三张图都随包提供，正常情况下不需要重烘。改噪声构造后再执行一次即可，体积云会自动使用新图。

::: warning 不要给通道做直方图均衡（CDF）
均匀分布看着对比更强，但会抹平 Perlin-Worley 的团块结构，云会散成一层均匀棉絮。
:::

## 水面生成

- **CartoonRendering > Water > Create Water Plane**：创建带卡通水材质的水面
- Water Assets Generator：水面配套资产生成

## 天空材质

**CartoonRendering > Sky** 相关菜单可创建 / 修复天空材质，输出到工程的 `Assets/` 下。

## 草地工具

Grass Field Tool：编辑期生成草场布局、烘焙交互数据，见[交互草地](/grass/)。

## 水下后期

- **Tools > Underwater > Setup Underwater Post Process**：给当前激活的 URP 渲染器装上 Feature，并创建一个配好参数的 `Underwater Zone`（局部 Volume + Trigger BoxCollider）
- **Tools > Underwater > Create Demo Scene**：生成并保存水下演示场景到 `Assets/Scenes/UnderwaterDemo.unity` —— 水池 + 一排**同材质、只有距离不同**（15 / 20 / 26 / 32 / 38 米）的球 + 水面 + 局部 Volume 区域
- **Tools > Underwater > Build In Current Scene**：只在当前场景里重建，不写场景文件
- **Tools > Underwater > Dump State**：打印渲染器 / 相机 / 管线 / Volume / shader / 球排距离，用来回答"为什么看不到效果"
- **Tools > Underwater > Debug > Sky Mask / Fog / Depth / Caustics / Absorption / Off**：调试视图

生成的材质与 Volume Profile 落在 `Assets/CartoonRendering/Underwater/`。详见 [卡通水面与水下效果](/water/)。

## LED 点阵屏

- **Tools > LED > Create Demo Scene**：一键生成 LED 点阵文字屏演示场景（含材质、Volume Profile、相机与灯光），保存到 `Assets/Scenes/LEDDotMatrix.unity`
- **Tools > LED > Build In Current Scene**：只在当前场景里重建，不写场景文件
- **Tools > LED > Dump Mask PNG**：诊断用，导出烘焙出来的文字遮罩并打印材质 / 网格状态

生成的材质与 Volume Profile 落在 `Assets/CartoonRendering/LED/`。详见 [LED 点阵文字屏](/led/)。

## 体积光

- **Tools > Volumetric Light > Setup In Renderer**：把 VolumetricLightFeature 一键写进当前激活的 URP 渲染器
- **Tools > Volumetric Light > Create Demo Scene**：生成并保存演示场景到 `Assets/Scenes/VolumetricLight.unity`
- **Tools > Volumetric Light > Build In Current Scene**：只在当前场景重建，不写场景文件
- **Tools > Volumetric Light > Dump State**：诊断用，打印渲染器 / 主光 / 管线 / Volume / shader 状态
- **Tools > Volumetric Light > Debug/**：切换调试视图（Shadow / Steps / Scene Depth），**阴影接不上时首选 `Shadow`**

演示场景的材质与 Volume Profile 落在 `Assets/CartoonRendering/VolumetricLight/`。详见[体积光（上帝光）](/volumetric-light/)。

### 工厂内景演示脚本

演示工程侧还带了一套「废弃工厂内景」脚本（位于 `Assets/CartoonRendering/VolumetricLight/Editor/`，不随包发布 —— 做自己的场景时可以照抄思路）：

- **Tools > Volumetric Light > Create Factory Interior Scene**：用 Blender 程序化生成的 `FactoryRoom.fbx`（16m × 26m × 7.5m 厂房，多格高窗 + 天窗 + 桁架）搭场景，保存到 `Assets/Scenes/FactoryInterior.unity`
- **Tools > Volumetric Light > Build Factory In Current Scene** / **Dump Factory Model**：只在当前场景重建 / 打印模型每个子物体的包围盒
- **Tools > Volumetric Light > Cycle Factory Viewpoint**：在三个预设机位间循环（暗厅侧看 / 部分逆光 / 光束扇面）
- **Tools > Volumetric Light > GI/Configure | Bake (Async) | Check Progress | Reattach Lighting Data | Clear Baked Data**：配静态标记与 Mixed 灯光 → 异步烘间接光 → 查进度 / 重挂 Lighting Data → 清数据

模型本身由 `Assets/CartoonRendering/VolumetricLight/Blender/factory_room.py` 生成（跑法与参数见同目录 `README.md`）；改房间尺寸后 Unity 侧的太阳方向与机位（`FactoryInteriorSceneBuilder` 顶部的 `SunDir` / `ViewPos`）需要跟着调。间接光烘焙的注意事项见[体积光（上帝光）](/volumetric-light/)。
