// Copyright (c) 2026 CartoonRendering. MIT License.
//
// SDF 核心算法（CPU 参考实现）——Saito 两遍法，参考知乎专栏 p/702637242：
//   第一遍：每个像素沿横向扫描，记录到最近"障碍"像素的距离平方；
//   第二遍：每个像素沿纵向扫描，用 temp[j] + 纵向距离平方 开根取最小，得到欧氏距离。
// 分别把白色（>0.5）和黑色（<0.5）当作障碍各算一遍，正向距离减负向距离，
// 映射 (pos - neg) * 0.5 + 0.5 并钳制到 [0,1]：0.5 为分界线，外部 > 0.5，内部 < 0.5。

using System;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// 黑白遮罩图 -&gt; 归一化 SDF 图的纯 C# 参考实现。
    /// 与 GPU Compute Shader（SdfGenerator.compute 的 kernel 0-3）算法完全一致，
    /// 既作为 GPU 路径正确性的对照，也可在没有 GPU 的环境（如批处理 CI）下使用。
    /// </summary>
    public static class SdfGeneratorCore
    {
        /// <summary>找不到障碍像素时使用的"无穷远"距离平方。</summary>
        private const float FarDistanceSqr = 1e29f;

        /// <summary>
        /// 由黑白遮罩生成 SDF 图。
        /// </summary>
        /// <param name="mask">行主序遮罩，长度 = width * height；白色(&gt;0.5) 为内部/障碍，黑色为背景。</param>
        /// <param name="width">遮罩宽度（像素）。</param>
        /// <param name="height">遮罩高度（像素）。</param>
        /// <param name="scaleDown">距离缩放系数，距离会先除以它再映射到 [0,1]，通常取贴图最大边长。</param>
        /// <returns>行主序 SDF 图，值域 [0,1]，0.5 为分界线。</returns>
        public static float[] GenerateSdf(float[] mask, int width, int height, float scaleDown)
        {
            Validate(mask, width, height, scaleDown);

            float[] positive = ComputeDistance(mask, width, height, invert: false);
            float[] negative = ComputeDistance(mask, width, height, invert: true);

            var output = new float[mask.Length];
            for (int i = 0; i < output.Length; i++)
            {
                float pos = positive[i] / scaleDown;
                float neg = negative[i] / scaleDown;
                output[i] = Mathf.Clamp01((pos - neg) * 0.5f + 0.5f);
            }
            return output;
        }

        private static void Validate(float[] mask, int width, int height, float scaleDown)
        {
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            if (width <= 0) throw new ArgumentException("宽度必须为正数", nameof(width));
            if (height <= 0) throw new ArgumentException("高度必须为正数", nameof(height));
            if (mask.Length != width * height)
                throw new ArgumentException($"遮罩长度 {mask.Length} 与 {width}x{height} 不符", nameof(mask));
            if (scaleDown <= 0f) throw new ArgumentException("ScaleDown 必须为正数", nameof(scaleDown));
        }

        /// <summary>
        /// 计算每个像素到最近障碍像素的欧氏距离。
        /// </summary>
        /// <param name="invert">false：白色为障碍（外部正距离）；true：黑色为障碍（内部负距离）。</param>
        private static float[] ComputeDistance(float[] mask, int width, int height, bool invert)
        {
            // 第一遍：横向最小距离平方（kernel 0 / 2）
            var temp = new float[mask.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    if (IsObstacle(mask[row + x], invert))
                    {
                        temp[row + x] = 0f;
                        continue;
                    }

                    float minDistanceSqr = FarDistanceSqr;
                    for (int i = 0; i < width; i++)
                    {
                        if (!IsObstacle(mask[row + i], invert)) continue;
                        float d = (i - x) * (i - x);
                        if (d < minDistanceSqr) minDistanceSqr = d;
                    }
                    temp[row + x] = minDistanceSqr;
                }
            }

            // 第二遍：纵向合成欧氏距离（kernel 1 / 3 的距离部分）
            var distance = new float[mask.Length];
            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    float minDistance = Mathf.Sqrt(FarDistanceSqr);
                    for (int j = 0; j < height; j++)
                    {
                        float d = (j - y) * (j - y) + temp[x + j * width];
                        if (d < 0f) d = 0f; // 防止浮点误差导致 sqrt 负数
                        float candidate = Mathf.Sqrt(d);
                        if (candidate < minDistance) minDistance = candidate;
                    }
                    distance[x + y * width] = minDistance;
                }
            }
            return distance;
        }

        private static bool IsObstacle(float color, bool invert)
            => invert ? color < 0.5f : color > 0.5f;
    }
}
