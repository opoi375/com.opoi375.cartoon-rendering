// Copyright (c) 2026 CartoonRendering. MIT License.
//
// GPU 派发封装：把 SdfGenerator.compute 的 5 个 kernel 包装成简单的静态方法。
// 遮罩 -> SDF 走 kernel 0-3；多帧 SDF -> 渐变贴图走 kernel 4，
// 帧区间与权重由 SdfGradientComposer.EnumerateLevels 提供，与 CPU 路径保持一致。

using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// SdfGenerator.compute 的 GPU 派发器。
    /// </summary>
    public static class SdfComputeBaker
    {
        /// <summary>Compute Shader 在工程内的固定路径。</summary>
        public const string ComputeShaderPath = "Assets/CartoonRendering/Editor/SDF/SdfGenerator.compute";

        /// <summary>
        /// 由 CPU 端的遮罩数据生成 SDF 并读回（主要用于测试与验证）。
        /// </summary>
        public static float[] BakeSdf(ComputeShader compute, float[] mask, int width, int height, float scaleDown)
        {
            if (compute == null) throw new ArgumentNullException(nameof(compute));

            var original = new Texture2D(width, height, TextureFormat.RFloat, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            original.SetPixelData(mask, 0);
            original.Apply();

            try
            {
                RenderTexture output = BakeSdfToTexture(compute, original, width, height, scaleDown);
                try
                {
                    return ReadBack(output);
                }
                finally
                {
                    output.Release();
                    Object.DestroyImmediate(output);
                }
            }
            finally
            {
                Object.DestroyImmediate(original);
            }
        }

        /// <summary>
        /// 在 GPU 上由遮罩贴图生成 SDF，结果保留在 RenderTexture（RFloat）中，由调用方负责释放。
        /// </summary>
        public static RenderTexture BakeSdfToTexture(ComputeShader compute, Texture original,
            int width, int height, float scaleDown)
        {
            if (compute == null) throw new ArgumentNullException(nameof(compute));
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (scaleDown <= 0f) throw new ArgumentException("ScaleDown 必须为正数", nameof(scaleDown));

            int kH1 = compute.FindKernel("SetHorizontalMinDist");
            int kV1 = compute.FindKernel("CalculateSDF");
            int kH2 = compute.FindKernel("SetHorizontalMinDist2");
            int kV2 = compute.FindKernel("CalculateSDF2");

            var temp = CreateFloatRT(width, height, "SDF_Temp");
            var output = CreateFloatRT(width, height, "SDF_Output");

            compute.SetFloat("_ScaleDown", scaleDown);
            compute.SetVector("_TexSize", new Vector2(width, height));

            // kernel 0/2 读取原图并写临时图；kernel 1/3 读临时图、写输出图
            foreach (int k in new[] { kH1, kH2 }) compute.SetTexture(k, "_OriginalTex", original);
            foreach (int k in new[] { kH1, kV1, kH2, kV2 }) compute.SetTexture(k, "_TempTex", temp);
            foreach (int k in new[] { kV1, kV2 }) compute.SetTexture(k, "_OutputTex", output);

            int groupsX = Mathf.CeilToInt(width / 8f);
            int groupsY = Mathf.CeilToInt(height / 8f);
            compute.Dispatch(kH1, groupsX, groupsY, 1);
            compute.Dispatch(kV1, groupsX, groupsY, 1);
            compute.Dispatch(kH2, groupsX, groupsY, 1);
            compute.Dispatch(kV2, groupsX, groupsY, 1);

            temp.Release();
            Object.DestroyImmediate(temp);
            return output;
        }

        /// <summary>
        /// 在 GPU 上把多帧 SDF 贴图合成为渐变贴图（kernel 4），由调用方负责释放返回的 RT。
        /// </summary>
        /// <param name="frames">帧号 -&gt; SDF 贴图（任意格式，采样 R 通道）。</param>
        public static RenderTexture ComposeGradientToTexture(ComputeShader compute,
            IReadOnlyDictionary<int, Texture> frames, int width, int height)
        {
            if (compute == null) throw new ArgumentNullException(nameof(compute));
            if (frames == null) throw new ArgumentNullException(nameof(frames));

            int kernel = compute.FindKernel("CombineSDF");
            var output = CreateFloatRT(width, height, "SDF_Gradient");

            compute.SetVector("_TexSize", new Vector2(width, height));
            compute.SetTexture(kernel, "_OutputTex", output);

            int[] keys = SdfGradientComposer.SortFrameNumbers(frames.Keys);
            int groupsX = Mathf.CeilToInt(width / 8f);
            int groupsY = Mathf.CeilToInt(height / 8f);

            foreach (var (_, prevFrame, nextFrame, weight) in SdfGradientComposer.EnumerateLevels(keys))
            {
                compute.SetFloat("_Weight", weight);
                compute.SetTexture(kernel, "_SDF1", frames[prevFrame]);
                compute.SetTexture(kernel, "_SDF2", frames[nextFrame]);
                compute.Dispatch(kernel, groupsX, groupsY, 1);
            }
            return output;
        }

        /// <summary>
        /// 把 RFloat RenderTexture 读回 CPU 浮点数组（行主序）。
        /// </summary>
        public static float[] ReadBack(RenderTexture rt)
        {
            if (rt == null) throw new ArgumentNullException(nameof(rt));

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RFloat, false);
            try
            {
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                var data = tex.GetPixelData<float>(0);
                var result = new float[data.Length];
                data.CopyTo(result);
                return result;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(tex);
            }
        }

        private static RenderTexture CreateFloatRT(int width, int height, string name)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            rt.Create();
            return rt;
        }
    }
}
