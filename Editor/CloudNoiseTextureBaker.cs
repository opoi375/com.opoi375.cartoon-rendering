// Copyright (c) 2026 CartoonRendering. MIT License.
//
// Bakes for the stylised volumetric cloud layer.
//
//   Tools > Cloud > Bake Cloud Noise 3D     - tileable 128^3 shape volume
//   Tools > Cloud > Bake Cloud Shape Maps   - tileable 512^2 weather / warp map
//
// ----------------------------------------------------------------------------
// Why these exist at all (and why the old bake produced an empty sky):
//
// The cloud shader turns a noise value into density with a single threshold:
//
//     d = smoothstep(1 - coverage, 1 - coverage + soft, r)
//
// That only behaves if r is spread over the whole 0..1 range. The previous
// bake built its base shape with the Schneider Perlin-Worley combination
//
//     pw = (perlin - (1 - worley)) / worley
//
// which collapses to exactly 0 wherever worley is small - and a Worley FBM is
// small over most of its volume. Measured on the shipped asset, R had a mean
// of ~0.3 with a hard floor at 0, so a coverage of 0.5 (threshold 0.48) let
// almost nothing through: the sky rendered empty, and pushing coverage to 0.95
// produced a few blobs. Tuning the shader could not fix that - the field it
// was thresholding had no material in the upper range.
//
// Both bakes therefore end with the same step: a CDF (histogram) normalisation
// that remaps each channel onto a uniform 0..1 distribution. A monotone remap
// changes no spatial relationship, so all the clustering and connectivity of
// the underlying noise survives - it only guarantees that "coverage = 0.6"
// means "60% of the sky has cloud", which is what an artist expects from the
// slider.
//
// Channel layout, 3D volume (R = base shape, GBA = detail):
//   R  Perlin-Worley cloud masses, CDF-normalised
//   G  Worley FBM, detail erosion, CDF-normalised
//   B  same at 2x frequency
//   A  same at 4x frequency
//
// Channel layout, 2D shape map:
//   R  weather: large-scale coverage (5 octaves)  - decides WHERE clouds live
//   G  weather: mid-scale coverage (7 octaves)    - breaks the large blobs up
//   B  domain warp X (from a noise gradient)      - makes outlines squiggly
//   A  domain warp Y
//
// Both textures are fully tileable (periodic lattices), and both carry a full
// mip chain: the cloud shader picks the mip matching each march step's world
// footprint, and down-sampling a tileable texture stays tileable.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    public static class CloudNoiseTextureBaker
    {
        private const int N = 128;              // 3D volume resolution
        private const int M = 512;              // 2D weather map resolution
        private const string kPath3D   = "Packages/com.opoi375.cartoon-rendering/Textures/CloudNoise3D.asset";
        private const string kPath2D   = "Packages/com.opoi375.cartoon-rendering/Textures/CloudShape2D.asset";

        // ==================================================================
        //  3D shape volume
        // ==================================================================
        [MenuItem("Tools/Cloud/Bake Cloud Noise 3D")]
        public static void Bake()
        {
            var raw = new float[N * N * N, 4];
            int i = 0;
            for (int z = 0; z < N; z++)
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++, i++)
            {
                float u = x / (float)N, v = y / (float)N, w = z / (float)N;

                // 6 octaves from base freq 4: enough scale variety that cloud
                // masses stop reading as same-size round blobs.
                float per = PerlinFbm(u, v, w, 4, 6);
                float w1  = WorleyFbm(u, v, w, 4);
                float w2  = WorleyFbm(u, v, w, 8);
                float w3  = WorleyFbm(u, v, w, 16);

                // Schneider-style Perlin-Worley: dilate the Perlin masses by
                // the low-frequency Worley so they grow puffy cauliflower
                // lobes. Kept as the SHAPE source - its bias is exactly what
                // the CDF pass below removes.
                float pw = Clamp01((per - (1f - w1)) / Mathf.Max(w1, 1e-3f));

                // Schneider 那个除法在 w1 < 0.5 且 per < 1-w1 的地方会整片塌成
                // 0 —— 实测旧资产里约一半体素都是精确的 0。CDF 归一化救不了
                // "并列"：一半样本挤在同一个值上，归一化后仍然是一整块常数
                // （coverage 滑到那块的值时，天空会从 0% 直接跳到 50%）。
                // 所以先把 per 本身按小比例混回去，给死区留下排序信息，
                // 云团的连通结构不受影响。
                pw = Clamp01(pw * 0.88f + per * 0.12f);

                raw[i, 0] = pw;
                raw[i, 1] = w1;
                raw[i, 2] = w2;
                raw[i, 3] = w3;
            }

            NormaliseChannels(raw, N * N * N, new[] { true, true, true, true });
            var data = ToColor32(raw, N * N * N);
            SaveVolume(kPath3D, N, data, "CloudNoise3D");
            Debug.Log($"[CloudNoiseTextureBaker] Baked {N}^3 Perlin-Worley volume, CDF-normalised, mip chain -> {kPath3D}");
        }

        // ==================================================================
        //  2D weather / warp map
        // ==================================================================
        [MenuItem("Tools/Cloud/Bake Cloud Shape Maps")]
        public static void BakeShapeMaps()
        {
            var raw = new float[M * M, 4];
            int i = 0;
            for (int y = 0; y < M; y++)
            for (int x = 0; x < M; x++, i++)
            {
                float u = x / (float)M, v = y / (float)M;

                // Large-scale weather: where do cloud systems live. Few
                // octaves and a low base frequency so it produces big
                // connected regions with real gaps, not salt.
                raw[i, 0] = PerlinFbm2(u, v, 3, 5);
                // Mid-scale: breaks those regions into individual clouds.
                raw[i, 1] = PerlinFbm2(u, v, 9, 7);

                // Domain warp: the 2D offset applied to the 3D sample
                // position. Built from the same family of noise, centred on
                // 0.5 so it pushes coordinates both ways.
                raw[i, 2] = PerlinFbm2(u + 0.37f, v + 0.71f, 5, 4);
                raw[i, 3] = PerlinFbm2(u + 0.83f, v + 0.19f, 5, 4);
            }

            NormaliseChannels(raw, M * M, new[] { true, true, true, true });
            var data = ToColor32(raw, M * M);
            SaveTexture2D(kPath2D, M, data, "CloudShape2D");
            Debug.Log($"[CloudNoiseTextureBaker] Baked {M}^2 weather / warp map, CDF-normalised -> {kPath2D}");
        }

        // ==================================================================
        //  CDF normalisation
        // ==================================================================
        // Remap each channel onto a uniform distribution via its histogram.
        // Monotone, so spatial structure is untouched; afterwards the mean of
        // every channel is 0.5 and "value > t" covers exactly (1 - t) of the
        // domain - which is what makes the coverage slider behave.
        private static void NormaliseChannels(float[,] values, int count, bool[] perChannel)
        {
            const int BINS = 4096;
            int channels = values.GetLength(1);
            for (int c = 0; c < channels; c++)
            {
                if (!perChannel[c]) continue;

                var hist = new int[BINS];
                for (int i = 0; i < count; i++)
                {
                    int b = (int)(Clamp01(values[i, c]) * (BINS - 1));
                    hist[b]++;
                }

                // Cumulative distribution, mapped to 0..1. The half-bin bias
                // keeps the result centred instead of snapping the first
                // populated bin to exactly 0.
                var cdf = new float[BINS];
                int running = 0;
                for (int b = 0; b < BINS; b++)
                {
                    running += hist[b];
                    cdf[b] = (running - hist[b] * 0.5f) / Mathf.Max(count, 1);
                }

                // Empty bins inherit the previous value so the LUT stays
                // monotone (a dip would fold the mapping back on itself).
                for (int b = 1; b < BINS; b++)
                    if (hist[b] == 0) cdf[b] = cdf[b - 1];

                for (int i = 0; i < count; i++)
                {
                    int b = (int)(Clamp01(values[i, c]) * (BINS - 1));
                    values[i, c] = cdf[b];
                }
            }
        }

        private static Color32[] ToColor32(float[,] values, int count)
        {
            var data = new Color32[count];
            for (int i = 0; i < count; i++)
                data[i] = new Color32(
                    (byte)(Clamp01(values[i, 0]) * 255f),
                    (byte)(Clamp01(values[i, 1]) * 255f),
                    (byte)(Clamp01(values[i, 2]) * 255f),
                    (byte)(Clamp01(values[i, 3]) * 255f));
            return data;
        }

        // ==================================================================
        //  Asset writing
        // ==================================================================
        // CopySerialized into the existing asset when there is one: the GUID
        // (and therefore every reference from the sky presets) survives, and
        // git sees a content change instead of a deleted + recreated asset.
        private static void SaveVolume(string path, int size, Color32[] data, string name)
        {
            var tex = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                name = name
            };
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);
            SaveOrCreate(tex, path);
        }

        private static void SaveTexture2D(string path, int size, Color32[] data, string name)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear: true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = name
            };
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);
            SaveOrCreate(tex, path);
        }

        private static void SaveOrCreate(Object tex, string path)
        {
            EnsureFolder(path);
            var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex, existing);
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(tex, path);
            }
            AssetDatabase.SaveAssets();
        }

        private static void EnsureFolder(string assetPath)
        {
            string dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = System.IO.Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
        }

        private static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }

        // ==================================================================
        //  Tileable 3D Perlin / Worley
        // ==================================================================
        private static readonly Vector3[] kGrads =
        {
            new Vector3(1, 1, 0), new Vector3(-1, 1, 0), new Vector3(1, -1, 0), new Vector3(-1, -1, 0),
            new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1),
            new Vector3(0, 1, 1), new Vector3(0, -1, 1), new Vector3(0, 1, -1), new Vector3(0, -1, -1)
        };

        private static int Hash3(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        private static float Hash01(int x, int y, int z, int seed)
        {
            return (Hash3(x, y, z, seed) & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        private static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }

        private static int Wrap(int c, int period)
        {
            int m = c % period;
            return m < 0 ? m + period : m;
        }

        private static float Perlin3(float x, float y, float z, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);

            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
            int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
            int z0 = Wrap(zi, period), z1 = Wrap(zi + 1, period);

            float n000 = GradDot(Hash3(x0, y0, z0, 0), xf,     yf,     zf);
            float n100 = GradDot(Hash3(x1, y0, z0, 0), xf - 1, yf,     zf);
            float n010 = GradDot(Hash3(x0, y1, z0, 0), xf,     yf - 1, zf);
            float n110 = GradDot(Hash3(x1, y1, z0, 0), xf - 1, yf - 1, zf);
            float n001 = GradDot(Hash3(x0, y0, z1, 0), xf,     yf,     zf - 1);
            float n101 = GradDot(Hash3(x1, y0, z1, 0), xf - 1, yf,     zf - 1);
            float n011 = GradDot(Hash3(x0, y1, z1, 0), xf,     yf - 1, zf - 1);
            float n111 = GradDot(Hash3(x1, y1, z1, 0), xf - 1, yf - 1, zf - 1);

            float nx00 = Mathf.Lerp(n000, n100, u), nx10 = Mathf.Lerp(n010, n110, u);
            float nx01 = Mathf.Lerp(n001, n101, u), nx11 = Mathf.Lerp(n011, n111, u);
            // 注意第三重的权重是 v 不是 w —— 上一版这里写成了 w，插出来的不是正交三线性。
            float nxy0 = Mathf.Lerp(nx00, nx10, v), nxy1 = Mathf.Lerp(nx01, nx11, v);
            return Mathf.Clamp01(Mathf.Lerp(nxy0, nxy1, w) * 0.7f + 0.5f);
        }

        private static float GradDot(int hash, float x, float y, float z)
        {
            Vector3 g = kGrads[(hash & 0x7FFFFFFF) % kGrads.Length];
            return g.x * x + g.y * y + g.z * z;
        }

        private static float PerlinFbm(float u, float v, float w, int baseFreq, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Perlin3(u * freq, v * freq, w * freq, freq);
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return sum / norm;
        }

        private static float Worley3(float x, float y, float z, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float minD = 8f;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                int wx = Wrap(cx, period), wy = Wrap(cy, period), wz = Wrap(cz, period);
                float fx = cx + Hash01(wx, wy, wz, 1);
                float fy = cy + Hash01(wx, wy, wz, 2);
                float fz = cz + Hash01(wx, wy, wz, 3);
                float ddx = fx - x, ddy = fy - y, ddz = fz - z;
                float d = ddx * ddx + ddy * ddy + ddz * ddz;
                if (d < minD) minD = d;
            }
            return Mathf.Clamp01(1f - Mathf.Sqrt(minD));
        }

        private static float WorleyFbm(float u, float v, float w, int baseFreq)
        {
            float sum = 0f, amp = 0.55f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < 3; o++)
            {
                sum += amp * Worley3(u * freq, v * freq, w * freq, freq);
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return sum / norm;
        }

        // ==================================================================
        //  Tileable 2D Perlin (weather + warp)
        // ==================================================================
        private static readonly Vector2[] kGrads2 =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7071f, 0.7071f), new Vector2(-0.7071f, 0.7071f),
            new Vector2(0.7071f, -0.7071f), new Vector2(-0.7071f, -0.7071f)
        };

        private static int Hash2(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        private static float Perlin2(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = Fade(xf), v = Fade(yf);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
            int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);

            Vector2 g00 = kGrads2[(Hash2(x0, y0, seed) & 0x7FFFFFFF) % 8];
            Vector2 g10 = kGrads2[(Hash2(x1, y0, seed) & 0x7FFFFFFF) % 8];
            Vector2 g01 = kGrads2[(Hash2(x0, y1, seed) & 0x7FFFFFFF) % 8];
            Vector2 g11 = kGrads2[(Hash2(x1, y1, seed) & 0x7FFFFFFF) % 8];

            float n00 = g00.x * xf         + g00.y * yf;
            float n10 = g10.x * (xf - 1f)  + g10.y * yf;
            float n01 = g01.x * xf         + g01.y * (yf - 1f);
            float n11 = g11.x * (xf - 1f)  + g11.y * (yf - 1f);
            return Mathf.Clamp01(Mathf.Lerp(Mathf.Lerp(n00, n10, u),
                                            Mathf.Lerp(n01, n11, u), v) * 0.9f + 0.5f);
        }

        private static float PerlinFbm2(float u, float v, int baseFreq, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int freq = baseFreq;
            for (int o = 0; o < octaves; o++)
            {
                sum += amp * Perlin2(u * freq, v * freq, freq, o + 1);
                norm += amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return sum / norm;
        }
    }
}
