# 安装

## 通过 Git URL（推荐）

1. 打开 **Window > Package Manager**
2. 左上角 **+** → **Add package from git URL…**
3. 输入：

```
https://github.com/opoi375/com.opoi375.cartoon-rendering.git
```

指定版本（推荐用于生产）：

```
https://github.com/opoi375/com.opoi375.cartoon-rendering.git#v1.0.1
```

## 通过本地路径

如果你克隆了仓库，选择 **Add package from disk…**，选中仓库根目录的 `package.json`。

## 安装后检查

1. 确认 URP 版本 ≥ 17.5.0（依赖会自动安装）
2. 天空 / 体积云需要在 **URP Renderer** 上添加 Render Feature：
   - 打开你的 Renderer 资产（如 `PC_Renderer`）
   - **Add Renderer Feature > Cartoon Skybox Feature**
3. 水面泡沫与水下效果需要相机开启 **Depth Texture**（URP Asset 中勾选）

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Runtime/` | 运行时脚本（Render Feature、天空、水下、草地等） |
| `Editor/` | 编辑器工具（SDF 烘焙、水面生成、草地工具等） |
| `Shaders/` | Shader 与 HLSL 库 |
| `Materials/` | 示例材质 |
| `Data/` | 天空配置 ScriptableObject 等数据资产 |
| `Textures/` | 预烘焙噪声体积、LUT 等 |

## 升级

Git URL 方式：在 Package Manager 中选中包 → **Update**，或把 URL 后缀改成新的 tag。

升级前建议看一眼[更新日志](/changelog)。
