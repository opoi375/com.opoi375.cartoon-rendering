// Copyright (c) 2026 CartoonRendering. MIT License.
//
// 多帧 SDF 图合成渐变贴图（CPU 参考实现），参考知乎专栏 p/702637242：
//   256 个明度层级，ratio = 最大帧号 / 255；第 i 个层级在相邻两帧 SDF 之间按
//   weight = (ratio * i - 前一帧号) / (后一帧号 - 前一帧号) 插值；
//   插值结果仍处于内部（<= 0.5）的层级，向输出累加 1/255 的亮度。
// 输出记录每个像素"从第几个明度层级开始进入内部"，用于溶解/燃烧/生长类效果。

using System;
using System.Collections.Generic;
using System.Linq;

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// 把一组带帧号的 SDF 图合成为一张渐变贴图。
    /// 与 GPU Compute Shader（SdfGenerator.compute 的 kernel 4）算法一致。
    /// </summary>
    public static class SdfGradientComposer
    {
        /// <summary>渐变贴图使用的明度层级数。</summary>
        public const int LevelCount = 256;

        /// <summary>
        /// 帧号按升序排序（乱序输入的安全入口）。
        /// </summary>
        public static int[] SortFrameNumbers(IEnumerable<int> frames)
            => frames.OrderBy(f => f).ToArray();

        /// <summary>
        /// 计算第 <paramref name="level"/> 个明度层级在 [prevFrame, nextFrame] 之间的插值权重：
        /// weight = (maxFrame / 255 * level - prevFrame) / (nextFrame - prevFrame)。
        /// </summary>
        public static float ComputeWeight(int prevFrame, int nextFrame, int maxFrame, int level)
        {
            float ratio = maxFrame / (float)(LevelCount - 1);
            return (ratio * level - prevFrame) / (float)(nextFrame - prevFrame);
        }

        /// <summary>
        /// 枚举每个明度层级所需的帧区间与权重（CPU 合成与 GPU 派发共用，保证两条路径一致）。
        /// </summary>
        public static IEnumerable<(int level, int prevFrame, int nextFrame, float weight)> EnumerateLevels(
            IReadOnlyList<int> sortedFrames)
        {
            if (sortedFrames == null) throw new ArgumentNullException(nameof(sortedFrames));
            if (sortedFrames.Count < 2)
                throw new ArgumentException("至少需要两帧 SDF 图才能合成渐变贴图", nameof(sortedFrames));

            int keyIdx = 1;
            int maxFrame = sortedFrames[sortedFrames.Count - 1];
            float ratio = maxFrame / (float)(LevelCount - 1);

            for (int level = 0; level < LevelCount; level++)
            {
                while (ratio * level > sortedFrames[keyIdx] && keyIdx < sortedFrames.Count - 1)
                    keyIdx++;

                int prev = sortedFrames[keyIdx - 1];
                int next = sortedFrames[keyIdx];
                float weight = ComputeWeight(prev, next, maxFrame, level);
                yield return (level, prev, next, weight);
            }
        }

        /// <summary>
        /// CPU 合成渐变贴图。
        /// </summary>
        /// <param name="frames">帧号 -&gt; 行主序 SDF 数据（值域 [0,1]，&lt;=0.5 为内部）。</param>
        /// <param name="width">贴图宽度。</param>
        /// <param name="height">贴图高度。</param>
        /// <returns>行主序渐变图。注意始终处于内部的像素会累加全部 256 级，理论最大值为 256/255。</returns>
        public static float[] ComposeGradient(IReadOnlyDictionary<int, float[]> frames, int width, int height)
        {
            if (frames == null) throw new ArgumentNullException(nameof(frames));
            if (width <= 0 || height <= 0)
                throw new ArgumentException("宽高必须为正数");

            int pixelCount = width * height;
            foreach (var pair in frames)
            {
                if (pair.Value == null || pair.Value.Length != pixelCount)
                    throw new ArgumentException($"帧 {pair.Key} 的数据长度与 {width}x{height} 不符", nameof(frames));
            }

            int[] keys = SortFrameNumbers(frames.Keys);
            var output = new float[pixelCount];

            foreach (var (_, prevFrame, nextFrame, weight) in EnumerateLevels(keys))
            {
                float[] sdfPrev = frames[prevFrame];
                float[] sdfNext = frames[nextFrame];
                for (int p = 0; p < pixelCount; p++)
                {
                    float blended = sdfPrev[p] * weight + sdfNext[p] * (1f - weight);
                    if (blended <= 0.5f)
                        output[p] += 1f / (LevelCount - 1);
                }
            }
            return output;
        }
    }
}
