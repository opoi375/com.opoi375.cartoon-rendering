# 世界弯曲（小星球视角）

动森（Animal Crossing）式"圆形地球"效果：以相机为圆心，把远处顶点按**水平距离的平方向下弯曲**，地平线自然垂落，营造"站在小星球上"的感觉。

![极端曲率演示：角色沉入弯曲的地面，而未使用包内 shader 的物体不受影响](/worldbend/curvature-demo.png)

> 上图为 `curvature = 0.005` 的夸张演示：使用包内 shader 的角色已沉入"地面"以下，只剩帽尖；灰色方块与默认地面使用 URP 内置 Lit，不受弯曲影响。

## 原理

```hlsl
// 以相机为圆心，按水平距离平方下沉（Shaders/Library/WorldBend.hlsl）
w = max(distance(posWS.xz, cameraPos.xz) - deadZone, 0)
posWS.y -= w * w * curvature     // curvature = 1 / (2 × 星球半径)
```

- 相机正下方保持原位，越远处垂得越低 → 视觉上地平线向下弯
- `deadZone` 让脚下区域保持平直，避免近处穿帮
- 可选法线同步倾斜（`ApplyWorldBendNormal`），让光照跟随曲面

包内所有几何 shader（PBRToon 全家、水面、草地、建筑）的**全部 Pass** 都已接入，包括 ShadowCaster / DepthOnly / DepthNormals / GBuffer / 描边，因此阴影、AO、轮廓线都不会与弯曲后的画面脱节。

## 使用

把 **World Bend Controller** 组件挂到场景任意激活物体上（菜单 `Add Component → Cartoon Rendering → World Bend Controller`）：

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `curvature` | 0.0004 | 世界曲率 = 1/(2×星球半径)。`0.001` 夸张动森感（半径 500m），`0.0004` 适中（1250m），`0.0001` 微妙（5000m），`0` 关闭 |
| `deadZone` | 10 | 相机附近保持平直的半径（米） |
| `bendNormals` | true | 是否同步倾斜法线，远处光照更自然 |

组件带 `[ExecuteAlways]`，编辑模式下 Scene 视图实时预览；`curvature = 0` 时零开销（一次乘加）。组件被禁用时自动还原全局参数。

## 注意事项

::: warning 纯视觉效果
碰撞、寻路、物理仍在**未弯曲**的平直世界运行（动森也是这样做的）。角色"沉下去"只是视觉，逻辑位置不变。
:::

- **只有包内 shader 会弯曲**。URP 内置 Lit、Unity Terrain 不受控 —— 想让地面一起弯，请把地面材质换成包内 shader（如 `CartoonBuilding` 或 `PBRToon/Base`）
- 视锥剔除按弯曲前的包围盒计算，远处会有少量多画（安全方向，不会误剔除）
- 天空盒 / 体积云 / 后期效果不弯曲（它们是屏幕空间效果，本就不该弯）

## 接入自定义 shader

自己写的 shader 想支持弯曲，在顶点阶段注入三行即可：

```hlsl
#include "../Library/WorldBend.hlsl"   // 按实际相对路径

// 在每个 Pass 的顶点函数里：
VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
posInputs.positionWS = ApplyWorldBend(posInputs.positionWS);
posInputs.positionCS = TransformWorldToHClip(posInputs.positionWS);
nrmInputs.normalWS   = ApplyWorldBendNormal(nrmInputs.normalWS, posInputs.positionWS);
```

ShadowCaster / DepthOnly / DepthNormals 等辅助 Pass 也要同样处理，否则阴影和 AO 会脱节。
