// Copyright (c) 2026 CartoonRendering. MIT License.
//
// One-shot baker for the tileable 3D Perlin-Worley noise volume used by
// CartoonVolumetricClouds. Menu: Tools > Cloud > Bake Cloud Noise 3D.
//
//   R channel: Perlin-Worley base shape (cloud masses)
//   G channel: Worley FBM freq 4  (detail erosion)
//   B channel: Worley FBM freq 8
//   A channel: Worley FBM freq 16
//
// The texture is fully tileable on every axis (periodic lattice), so the
// shader can sample it with a Repeat sampler in unbounded world UVW space.

using UnityEditor;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    public static class CloudNoiseTextureBaker
    {
        private const int N = 128;
        private const string kPath = "Packages/com.opoi375.cartoon-rendering/Textures/CloudNoise3D.asset";

        [MenuItem("Tools/Cloud/Bake Cloud Noise 3D")]
        public static void Bake()
        {
            var data = new Color32[N * N * N];
            int i = 0;
            for (int z = 0; z < N; z++)
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++, i++)
            {
                float u = x / (float)N, v = y / (float)N, w = z / (float)N;
                // 5 octaves from base freq 4: enough scale variety that
                // cloud masses stop looking like same-size round blobs.
                float per = PerlinFbm(u, v, w, 4, 5);
                float w1  = WorleyFbm(u, v, w, 4);
                float w2  = WorleyFbm(u, v, w, 8);
                float w3  = WorleyFbm(u, v, w, 16);
                // Schneider-style Perlin-Worley: dilate the Perlin shape by
                // the inverted low-freq Worley -> puffy connected masses.
                float pw = Mathf.Clamp01((per - (1f - w1)) / Mathf.Max(w1, 1e-3f));
                // Consolidation curve: sink the low wisps and boost the
                // mids so masses read as a few big connected clouds rather
                // than scattered fragments.
                pw = Mathf.Clamp01((pw - 0.1f) * 1.3f);
                data[i] = new Color32(
                    (byte)(Mathf.Clamp01(pw) * 255f),
                    (byte)(Mathf.Clamp01(w1) * 255f),
                    (byte)(Mathf.Clamp01(w2) * 255f),
                    (byte)(Mathf.Clamp01(w3) * 255f));
            }

            var tex = new Texture3D(N, N, N, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "CloudNoise3D"
            };
            tex.SetPixelData(data, 0);
            tex.Apply();

            System.IO.Directory.CreateDirectory("Packages/com.opoi375.cartoon-rendering/Textures");
            var existing = AssetDatabase.LoadAssetAtPath<Texture3D>(kPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(tex, existing);
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(existing);
            }
            else
            {
                AssetDatabase.CreateAsset(tex, kPath);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[CloudNoiseTextureBaker] Baked {N}^3 Perlin-Worley volume -> {kPath}");
        }

        // ----- Tileable Perlin ---------------------------------------------

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

        // ----- Tileable Worley (inverted F1) --------------------------------

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
    }
}
