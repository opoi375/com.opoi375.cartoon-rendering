using System.Collections.Generic;
using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// 单根草的确定性生成数据：世界位置、高度、弯曲值。
    /// </summary>
    public readonly struct GrassBladeData
    {
        public readonly Vector3 Position;
        public readonly float Height;
        public readonly float Bend;

        public GrassBladeData(Vector3 position, float height, float bend)
        {
            Position = position;
            Height = height;
            Bend = bend;
        }
    }

    /// <summary>
    /// 草场确定性生成器：把边界、密度、种子转换为确定性草布局。
    /// 纯数据、无状态，同一种子总是产生相同结果，可直接在 EditMode 测试。
    ///
    /// 布局约定：草场原点在 (0, 0)，范围 [0, sizeX] × [0, sizeZ]。
    /// 数量 = round(面积 × 密度)；位置与属性由种子驱动的伪随机序列决定。
    /// </summary>
    public static class GrassFieldGenerator
    {
        /// <summary>
        /// 在边界内按密度生成草，属性（高度/弯曲）落在配置范围内。
        /// </summary>
        public static List<GrassBladeData> Generate(
            Vector2 boundsSize, float densityPerSquareMeter, int seed,
            float minHeight, float maxHeight, float minBend, float maxBend)
        {
            var result = new List<GrassBladeData>();

            float area = boundsSize.x * boundsSize.y;
            if (area <= 0f || densityPerSquareMeter <= 0f)
            {
                return result;
            }

            int count = Mathf.RoundToInt(area * densityPerSquareMeter);
            var rng = new System.Random(seed);

            for (int i = 0; i < count; i++)
            {
                float x = (float)rng.NextDouble() * boundsSize.x;
                float z = (float)rng.NextDouble() * boundsSize.y;
                float height = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble());
                float bend = Mathf.Lerp(minBend, maxBend, (float)rng.NextDouble());
                result.Add(new GrassBladeData(new Vector3(x, 0f, z), height, bend));
            }

            return result;
        }

        /// <summary>
        /// 均匀草场生成（网格抖动）：把草场按网格间距划分格子，每格放一根草，
        /// 位置在格内加少量随机偏移。保证任意两根草间距有下界，避免纯随机的聚类。
        /// 网格间距 = 1/sqrt(密度)；偏移幅度 = 间距 × 0.35。
        /// </summary>
        public static List<GrassBladeData> GenerateUniform(
            Vector2 boundsSize, float densityPerSquareMeter, int seed,
            float minHeight, float maxHeight, float minBend, float maxBend)
        {
            var result = new List<GrassBladeData>();

            if (boundsSize.x <= 0f || boundsSize.y <= 0f || densityPerSquareMeter <= 0f)
            {
                return result;
            }

            float gridSpacing = 1f / Mathf.Sqrt(densityPerSquareMeter);
            float jitter = gridSpacing * 0.35f;

            int cellsX = Mathf.Max(1, Mathf.FloorToInt(boundsSize.x / gridSpacing));
            int cellsZ = Mathf.Max(1, Mathf.FloorToInt(boundsSize.y / gridSpacing));

            var rng = new System.Random(seed);

            for (int cz = 0; cz < cellsZ; cz++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    // 格中心
                    float baseX = (cx + 0.5f) * gridSpacing;
                    float baseZ = (cz + 0.5f) * gridSpacing;

                    // 格内抖动（±jitter，保证不越界到相邻格中心）
                    float x = baseX + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;
                    float z = baseZ + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter;

                    float height = Mathf.Lerp(minHeight, maxHeight, (float)rng.NextDouble());
                    float bend = Mathf.Lerp(minBend, maxBend, (float)rng.NextDouble());
                    result.Add(new GrassBladeData(new Vector3(x, 0f, z), height, bend));
                }
            }

            return result;
        }
    }
}
