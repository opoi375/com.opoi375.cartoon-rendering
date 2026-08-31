# 编辑器工具一览

包内自带的编辑器工具，菜单位于 **Tools** 与 **CartoonRendering** 下。

## SDF 生成器

菜单：**Tools > SDF > SDF Generator**（GPU 加速，Compute Shader 烘焙）

- **页签 1「遮罩 → SDF」**：把黑白遮罩图烘焙成归一化 SDF 贴图（0.5 为分界线）—— 用于 PBRToon 面部阴影图、溶解效果等
- **页签 2「多帧 → 渐变」**：把一组名字以 `_帧号` 结尾的 SDF 贴图（如 `xxx_SDF_177`）插值合成渐变贴图 —— 用于溶解 / 燃烧 / 生长类效果

## 云噪声烘焙

菜单：**Tools > Cloud > Bake Cloud Noise 3D**

生成 128³ 可平铺 Perlin-Worley 体积噪声（R = 基础形状，GBA = Worley 细节），带完整 mip 链与三线性过滤，供体积云采样。

修改噪声参数后需重新烘焙，体积云会自动使用新纹理。

## 水面生成

- **CartoonRendering > Water > Create Water Plane**：创建带卡通水材质的水面
- Water Assets Generator：水面配套资产生成

## 天空材质

**CartoonRendering > Sky** 相关菜单可创建 / 修复天空材质，输出到工程的 `Assets/` 下。

## 草地工具

Grass Field Tool：编辑期生成草场布局、烘焙交互数据，见[交互草地](/grass/)。

## 水下设置

**CartoonRendering > Underwater > Setup Underwater Post Process**：一键给当前 URP Renderer 添加水下后期 Feature。
