// Copyright (c) 2026 CartoonRendering. MIT License.
//
// 正式测试：GPU SDF 生成工具（参考知乎专栏 p/702637242《写个GPU上运行的SDF生成器》）。
// 约定：白色像素（>0.5）为"障碍/内部"，黑色像素为"背景/外部"；
// 输出 SDF 归一化到 [0,1]，0.5 为分界线，外部 > 0.5，内部 < 0.5。

using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CartoonRendering.EditorTools;

namespace CartoonRendering.Editor.Tests
{
    /// <summary>
    /// 遮罩图 -> SDF 图（Saito 两遍算法，CPU 参考实现）。
    /// </summary>
    [TestFixture]
    public class SdfGeneratorCoreTests
    {
        private const int W = 32;
        private const int H = 32;

        private static float[] HalfMask(int w, int h)
        {
            // 左半纯黑、右半纯白
            var mask = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mask[x + y * w] = x >= w / 2 ? 1f : 0f;
            return mask;
        }

        private static float[] SquareMask(int w, int h, int min, int max)
        {
            // 中心白色方块 [min..max]（含两端），其余黑色
            var mask = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool inside = x >= min && x <= max && y >= min && y <= max;
                mask[x + y * w] = inside ? 1f : 0f;
            }
            return mask;
        }

        // Given 一张左半纯黑、右半纯白的遮罩图
        // When 生成 SDF 图
        // Then 黑白分界处的像素值约等于 0.5
        [Test]
        public void GenerateSdf_BoundaryPixelsAreNearHalf()
        {
            const float scaleDown = 32f;
            float[] sdf = SdfGeneratorCore.GenerateSdf(HalfMask(W, H), W, H, scaleDown);

            int y = H / 2;
            float lastBlack = sdf[(W / 2 - 1) + y * W];
            float firstWhite = sdf[(W / 2) + y * W];
            float tolerance = 0.5f / scaleDown + 1e-4f;
            Assert.That(lastBlack, Is.EqualTo(0.5f).Within(tolerance), "分界左侧（黑色）像素应约等于 0.5");
            Assert.That(firstWhite, Is.EqualTo(0.5f).Within(tolerance), "分界右侧（白色）像素应约等于 0.5");
        }

        // Given 一张左半纯黑、右半纯白的遮罩图
        // When 生成 SDF 图
        // Then 黑色背景区域的像素值大于 0.5 且白色内部区域的像素值小于 0.5
        [Test]
        public void GenerateSdf_BackgroundAboveHalfAndInteriorBelowHalf()
        {
            float[] sdf = SdfGeneratorCore.GenerateSdf(HalfMask(W, H), W, H, 32f);

            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                if (x < W / 2)
                    Assert.Greater(sdf[x + y * W], 0.5f, $"黑色背景像素 ({x},{y}) 应大于 0.5");
                else
                    Assert.Less(sdf[x + y * W], 0.5f, $"白色内部像素 ({x},{y}) 应小于 0.5");
            }
        }

        // Given 一张中心有白色方块、四周为黑色的遮罩图
        // When 生成 SDF 图
        // Then 黑色背景像素离方块越远其 SDF 值越大（单调递增）
        [Test]
        public void GenerateSdf_DistanceIncreasesWithDistanceFromObstacle()
        {
            float[] sdf = SdfGeneratorCore.GenerateSdf(SquareMask(W, H, 14, 17), W, H, 32f);

            int y = H / 2; // 中心行，向左远离方块
            for (int x = 0; x < 13; x++)
            {
                Assert.Greater(sdf[x + y * W], sdf[(x + 1) + y * W],
                    $"离方块更远的像素 x={x} 的 SDF 值应大于 x={x + 1}");
            }
        }

        // Given 一张中心有白色方块的遮罩图
        // When 生成 SDF 图
        // Then 白色方块内部像素离边缘越深其 SDF 值越小（负距离越深越小）
        [Test]
        public void GenerateSdf_InteriorValueDecreasesWithDepth()
        {
            float[] sdf = SdfGeneratorCore.GenerateSdf(SquareMask(W, H, 12, 19), W, H, 32f);

            int y = H / 2; // 中心行，从方块左边缘向内部深入
            for (int x = 12; x < 15; x++)
            {
                Assert.Less(sdf[(x + 1) + y * W], sdf[x + y * W],
                    $"方块内部更深的像素 x={x + 1} 的 SDF 值应小于 x={x}");
            }
        }

        // Given 任意遮罩图与任意合法的 ScaleDown 参数
        // When 生成 SDF 图
        // Then 输出的所有像素值都被限制在 [0,1] 区间内
        [Test]
        public void GenerateSdf_OutputIsClampedToUnitRange()
        {
            var rng = new System.Random(123);
            var mask = new float[W * H];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = rng.NextDouble() > 0.5 ? 1f : 0f;

            // 故意使用很小的 ScaleDown 以触发大距离值
            float[] sdf = SdfGeneratorCore.GenerateSdf(mask, W, H, 4f);

            foreach (float v in sdf)
            {
                Assert.GreaterOrEqual(v, 0f, "SDF 值不应小于 0");
                Assert.LessOrEqual(v, 1f, "SDF 值不应大于 1");
            }
        }

        // Given 一张遮罩图和它的水平镜像图
        // When 分别生成 SDF 图
        // Then 两张 SDF 图互为水平镜像
        [Test]
        public void GenerateSdf_MirroredInputProducesMirroredOutput()
        {
            const int w = 16, h = 16;
            var rng = new System.Random(7);
            var mask = new float[w * h];
            var mirror = new float[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = rng.NextDouble() > 0.5 ? 1f : 0f;
                mask[x + y * w] = v;
                mirror[(w - 1 - x) + y * w] = v;
            }

            float[] sdf = SdfGeneratorCore.GenerateSdf(mask, w, h, 16f);
            float[] sdfMirror = SdfGeneratorCore.GenerateSdf(mirror, w, h, 16f);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                Assert.That(sdfMirror[(w - 1 - x) + y * w], Is.EqualTo(sdf[x + y * w]).Within(1e-4f),
                    $"镜像输出在 ({x},{y}) 处应与原输出一致");
        }

        // Given 一张全黑的遮罩图（没有任何白色障碍像素）
        // When 生成 SDF 图
        // Then 不会抛出异常且输出全部为 1（距离最大）
        [Test]
        public void GenerateSdf_AllBlackMaskReturnsAllOne()
        {
            float[] sdf = SdfGeneratorCore.GenerateSdf(new float[W * H], W, H, 16f);

            foreach (float v in sdf)
                Assert.That(v, Is.EqualTo(1f).Within(1e-4f), "全黑遮罩的所有像素 SDF 值应为 1");
        }

        // Given 一张全白的遮罩图（没有任何黑色背景像素）
        // When 生成 SDF 图
        // Then 不会抛出异常且输出全部为 0（距离最小）
        [Test]
        public void GenerateSdf_AllWhiteMaskReturnsAllZero()
        {
            var mask = new float[W * H];
            for (int i = 0; i < mask.Length; i++) mask[i] = 1f;

            float[] sdf = SdfGeneratorCore.GenerateSdf(mask, W, H, 16f);

            foreach (float v in sdf)
                Assert.That(v, Is.EqualTo(0f).Within(1e-4f), "全白遮罩的所有像素 SDF 值应为 0");
        }
    }

    /// <summary>
    /// SDF 帧图的帧号解析（贴图名最后一个下划线分段为帧号，
    /// 例如 Substance_graph_output_SDF_177 表示第 177 帧）。
    /// </summary>
    [TestFixture]
    public class SdfFrameParserTests
    {
        // Given 一张名为 Substance_graph_output_SDF_177 的贴图
        // When 解析其帧号
        // Then 得到整数 177
        [Test]
        public void ParseFrameNumber_NameEndingWithDigitsReturnsFrame()
        {
            bool ok = SdfFrameParser.TryParseFrameNumber("Substance_graph_output_SDF_177", out int frame);

            Assert.IsTrue(ok, "合法帧号后缀应解析成功");
            Assert.AreEqual(177, frame);
        }

        // Given 一张名字不以下划线加数字结尾的贴图
        // When 解析其帧号
        // Then 解析失败并返回 false（不抛出异常）
        [Test]
        public void ParseFrameNumber_NameWithoutNumberSuffixFails()
        {
            Assert.IsFalse(SdfFrameParser.TryParseFrameNumber("SomeTexture", out _), "无数字后缀应解析失败");
            Assert.IsFalse(SdfFrameParser.TryParseFrameNumber("SDF_12a", out _), "非纯数字后缀应解析失败");
        }
    }

    /// <summary>
    /// 多帧 SDF 图合成渐变贴图。
    /// 256 个明度层级，ratio = 最大帧号 / 255，每个层级在相邻两帧 SDF 之间插值，
    /// 并将"插值结果仍位于内部（<=0.5）"的层级以 1/255 为步进累加到输出。
    /// </summary>
    [TestFixture]
    public class SdfGradientComposerTests
    {
        private const int W = 64;
        private const int H = 4;

        // Given 帧号 0 与帧号 255 两张 SDF 图，其中第 x 列像素在第 (255-4x) 个明度层级附近跨越 0.5 分界线
        // When 合成渐变贴图
        // Then 输出值随列号单调不增，形成空间渐变（列间相差 4 个层级，足以抵抗浮点误差）
        [Test]
        public void ComposeGradient_SpatialRampProducesMonotonicGradient()
        {
            // 构造：sdf0[x] = 0.5 + 4x/510（第 0 帧时在界外），
            //       sdf255[x] = 0.5 - (255-4x)/510（第 255 帧时在界内）。
            // 理论：第 x 列在 i <= 255-4x 的层级内处于内部，输出 = (256-4x)/255。
            var sdf0 = new float[W * H];
            var sdf255 = new float[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                sdf0[x + y * W] = 0.5f + 4f * x / 510f;
                sdf255[x + y * W] = 0.5f - (255f - 4f * x) / 510f;
            }
            var frames = new Dictionary<int, float[]> { [0] = sdf0, [255] = sdf255 };

            float[] gradient = SdfGradientComposer.ComposeGradient(frames, W, H);

            int row = H / 2;
            for (int x = 0; x < W - 1; x++)
            {
                Assert.Greater(gradient[x + row * W], gradient[(x + 1) + row * W],
                    $"输出应随列号严格单调递减（x={x}，列间相差 4/255，浮点误差不足以反转）");
            }
            Assert.That(gradient[0 + row * W], Is.EqualTo(256f / 255f).Within(2f / 255f),
                "最早进入内部的列输出应接近 1");
            Assert.That(gradient[(W - 1) + row * W], Is.EqualTo((256f - 4f * (W - 1)) / 255f).Within(2f / 255f),
                "最晚进入内部的列输出应接近 (256-252)/255");
        }

        // Given 一组乱序传入的多帧 SDF 贴图
        // When 合成渐变贴图
        // Then 内部按帧号升序排序后再进行插值合成
        [Test]
        public void ComposeGradient_UnorderedInputIsSortedByFrameNumber()
        {
            Assert.That(SdfGradientComposer.SortFrameNumbers(new[] { 255, 0, 128 }),
                Is.EqualTo(new[] { 0, 128, 255 }), "帧号应按升序排序");

            // 同一组帧以不同插入顺序合成，结果必须完全一致
            const int w = 8, h = 8;
            var rng = new System.Random(99);
            float[] MakeFrame()
            {
                var f = new float[w * h];
                for (int i = 0; i < f.Length; i++) f[i] = (float)rng.NextDouble();
                return f;
            }
            float[] f0 = MakeFrame(), f128 = MakeFrame(), f255 = MakeFrame();

            var ordered = new Dictionary<int, float[]> { [0] = f0, [128] = f128, [255] = f255 };
            var shuffled = new Dictionary<int, float[]> { [255] = f255, [0] = f0, [128] = f128 };

            float[] a = SdfGradientComposer.ComposeGradient(ordered, w, h);
            float[] b = SdfGradientComposer.ComposeGradient(shuffled, w, h);
            Assert.That(b, Is.EqualTo(a).Within(1e-6f), "乱序输入的合成结果应与有序输入一致");
        }

        // Given 帧号 0 和帧号 180 两张 SDF 图以及层级 i
        // When 计算第 i 个明度层级的插值权重
        // Then 权重等于 (180/255 * i - 0) / (180 - 0)
        [Test]
        public void ComposeGradient_WeightMatchesRatioFormula()
        {
            const float ratio = 180f / 255f;
            foreach (int level in new[] { 0, 1, 128, 255 })
            {
                float expected = (ratio * level - 0f) / (180f - 0f);
                Assert.That(SdfGradientComposer.ComputeWeight(0, 180, 180, level),
                    Is.EqualTo(expected).Within(1e-5f), $"层级 {level} 的权重应符合 ratio 公式");
            }
        }

        // Given 同一像素在两帧 SDF 中均小于 0.5（该像素在合成区间内始终为内部）
        // When 合成渐变贴图
        // Then 该像素在每个层级都贡献 1/255 的累加亮度
        [Test]
        public void ComposeGradient_InteriorPixelAccumulatesEveryLevel()
        {
            const int w = 8, h = 8;
            var sdf0 = new float[w * h];              // 全 0（内部）
            var sdf255 = new float[w * h];
            for (int i = 0; i < sdf255.Length; i++) sdf255[i] = 0.25f; // 全 0.25（内部）
            var frames = new Dictionary<int, float[]> { [0] = sdf0, [255] = sdf255 };

            float[] gradient = SdfGradientComposer.ComposeGradient(frames, w, h);

            foreach (float v in gradient)
                Assert.That(v, Is.EqualTo(256f / 255f).Within(1e-3f),
                    "始终处于内部的像素应在全部 256 个层级各累加 1/255");
        }
    }

    /// <summary>
    /// GPU（Compute Shader）路径与 CPU 参考实现的一致性。
    /// </summary>
    [TestFixture]
    public class SdfGpuConsistencyTests
    {
        private const string ComputePath = "Packages/com.opoi375.cartoon-rendering/Editor/SDF/SdfGenerator.compute";

        // Given 同一张随机噪声二值遮罩图
        // When 分别用 CPU 参考实现和 GPU Compute Shader 生成 SDF 图
        // Then 两条路径的输出在每个像素上的误差不超过 1/255
        [Test]
        public void GpuSdf_MatchesCpuReferenceWithinOneGrayLevel()
        {
            const int w = 64, h = 64;
            const float scaleDown = 64f;
            var rng = new System.Random(2024);
            var mask = new float[w * h];
            for (int i = 0; i < mask.Length; i++)
                mask[i] = rng.NextDouble() > 0.5 ? 1f : 0f;

            float[] cpu = SdfGeneratorCore.GenerateSdf(mask, w, h, scaleDown);

            var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputePath);
            Assert.IsNotNull(compute, $"Compute Shader 应存在于 {ComputePath}");
            float[] gpu = SdfComputeBaker.BakeSdf(compute, mask, w, h, scaleDown);

            Assert.AreEqual(cpu.Length, gpu.Length, "GPU 输出尺寸应与 CPU 一致");
            float tolerance = 1f / 255f + 1e-4f;
            for (int i = 0; i < cpu.Length; i++)
                Assert.That(gpu[i], Is.EqualTo(cpu[i]).Within(tolerance),
                    $"像素 {i} 处 GPU 与 CPU 结果应一致");
        }
    }
}
