# 交互草地

GPU 实例化卡通草场，支持角色**踩踏压弯与自动恢复**。

## 工作原理

每帧流程：

1. 从玩家 Transform 盖章脚印 → `GrassInteractionField`（CPU 真相，有测试覆盖）
2. Tick 恢复衰减
3. `CommandBuffer.DrawProcedural` 把脚印烘焙进交互 RenderTexture
4. `Graphics.DrawMeshInstanced` 提交草实例 → `CartoonGrass.shader` 读取交互 RT 压弯草叶

## 组件

| 组件 / 工具 | 说明 |
| --- | --- |
| `GrassField` | 场景组件：组装布局生成、交互场与 GPU 提交 |
| `GrassFieldGenerator` | 纯逻辑布局生成（可单测） |
| `GrassInteractionPlanner` | 交互碰撞规划 |
| Grass Field Tool（Editor） | 编辑期生成 / 烘焙草场的工具窗口 |

## 使用

1. 给场景添加 `GrassField` 组件
2. 指定生成范围、密度、草叶材质（`CartoonRendering/CartoonGrass`）
3. 把玩家 / 交互物体 Transform 挂到交互源列表

草叶为风格化卡通造型，支持风摆、颜色渐变、踩踏回弹。
