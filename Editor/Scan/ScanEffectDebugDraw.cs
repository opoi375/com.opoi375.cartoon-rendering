// Copyright (c) 2026 CartoonRendering. MIT License.
//
// 诊断工具：绕过 URP 直接把材质画进 RenderTexture 并回读像素。
//
// 它回答两个问题，而且是左右对照的：
//   1) 同一张材质用 DrawProcedural 画不出，是不是【着色器】的问题？
//      → 同时用 Graphics.Blit 画一遍（Blit 不走我们的顶点着色器）
//   2) 是不是【只有我的 shader】有问题？
//      → 同时拿包内水下后期材质当对照（同一套 HLSLINCLUDE + DrawProcedural 写法）
//
// 两组 x 两种画法 = 四个数据点，一次跑完就能定位。

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonRendering.EditorTools
{
    public static class ScanEffectDebugDraw
    {
        const int Size = 64;

        [MenuItem("Tools/Scan/Diagnose Draw (RenderTexture)")]
        public static void Run()
        {
            var scan = Shader.Find("CartoonRendering/PostProcessing/ScanEffect");
            var uw = Shader.Find("CartoonRendering/PostProcessing/UnderwaterPostProcess");

            Debug.Log($"[ScanDrawTest] scan={(scan != null)} supported={scan != null && scan.isSupported} | " +
                      $"uw={(uw != null)} supported={uw != null && uw.isSupported}");

            if (scan != null)
                Probe("ScanEffect", scan, forceRed: true);
            if (uw != null)
                Probe("Underwater", uw, forceRed: false);
        }

        static void Probe(string label, Shader shader, bool forceRed)
        {
            var mat = new Material(shader) { name = "Probe_" + label };

            if (forceRed)
                mat.SetFloat("_ScanDiag", 1f);

            // 水下材质给几个必需的量，免得整屏判成天空
            mat.SetFloat("_UWIntensity", 1f);
            mat.SetFloat("_UWTintStrength", 1f);
            mat.SetFloat("_UWFogDensity", 0.05f);
            mat.SetFloat("_UWSurfaceY", 0f);

            float procedural = DrawProcedural(mat);
            float blit = Blit(mat);

            Debug.Log($"[ScanDrawTest] {label,-12} passCount={mat.passCount} " +
                      $"DrawProcedural.R={procedural:F3} Blit.R={blit:F3} -> " +
                      $"{(procedural > 0.01f ? "procedural OK" : "procedural FAIL")}, " +
                      $"{(blit > 0.01f ? "blit OK" : "blit FAIL")}");

            Object.DestroyImmediate(mat);
        }

        static float DrawProcedural(Material mat)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGBHalf);
            var cb = new CommandBuffer { name = "ProbeProcedural" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.black);
            cb.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 3, 1);
            Graphics.ExecuteCommandBuffer(cb);
            GL.Flush();                          // 回读前先把命令刷下去
            float v = ReadCenter(rt);
            cb.Release();
            RenderTexture.ReleaseTemporary(rt);
            return v;
        }

        static float Blit(Material mat)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 24, RenderTextureFormat.ARGBHalf);
            var cb = new CommandBuffer { name = "ProbeBlit" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, Color.black);
            cb.Blit((RenderTexture)null, rt, mat);
            Graphics.ExecuteCommandBuffer(cb);
            GL.Flush();                          // 回读前先把命令刷下去
            float v = ReadCenter(rt);
            cb.Release();
            RenderTexture.ReleaseTemporary(rt);
            return v;
        }

        static float ReadCenter(RenderTexture rt)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBAHalf, false, true);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0, false);
            tex.Apply();
            RenderTexture.active = prev;

            Color c = tex.GetPixel(Size / 2, Size / 2);
            Object.DestroyImmediate(tex);
            return c.r + c.g + c.b;
        }
    }
}
