using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 单根草的一次弯曲结果：世界空间弯曲方向（远离交互源，水平归一化）+ 强度 0..1。
    /// </summary>
    public readonly struct GrassBendResult
    {
        public readonly Vector3 Direction;
        public readonly float Strength;

        public GrassBendResult(Vector3 direction, float strength)
        {
            Direction = direction;
            Strength = strength;
        }
    }

    /// <summary>
    /// 草地交互纯函数规划器：给定草位置、交互源位置与交互半径，
    /// 计算该草应获得的弯曲方向与强度。无状态、确定性，可直接在 EditMode 测试。
    ///
    /// 强度模型：强度 = 1 - (distance / radius)，线性衰减；
    /// 草在半径内 → 向远离交互源的水平方向弯曲；
    /// 草在半径外或与源重合 → 强度为零。
    /// </summary>
    public static class GrassInteractionPlanner
    {
        /// <summary>
        /// 计算单根草在单一交互源下的弯曲。
        /// 草在半径内 → 向远离交互源的水平方向弯曲，强度随距离单调衰减；
        /// 草在半径外 → 强度为零。
        /// </summary>
        public static GrassBendResult EvaluateBend(Vector3 grassPosition, Vector3 sourcePosition, float interactionRadius)
        {
            if (interactionRadius <= 0f)
            {
                return new GrassBendResult(Vector3.zero, 0f);
            }

            var offset = grassPosition - sourcePosition;
            // 只保留水平分量（草沿地面弯曲，不向地下/天空方向弯）
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance <= 0.0001f || distance >= interactionRadius)
            {
                // 与源重合（无明确方向）或超出半径 → 不弯曲
                return new GrassBendResult(Vector3.zero, 0f);
            }

            float strength = 1f - distance / interactionRadius;
            Vector3 direction = offset / distance; // 远离源的归一化水平方向
            return new GrassBendResult(direction, strength);
        }
    }
}
