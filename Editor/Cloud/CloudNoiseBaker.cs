// Copyright (c) 2026 CartoonRendering. MIT License.
//
// Tools > Clouds > Bake Noise — 体积云用的三张噪声图。
//   Textures/CloudShape3D.asset   128³  Perlin-Worley + Worley FBM
//   Textures/CloudDetail3D.asset   32³  Worley FBM（高频侵蚀）
//   Textures/CloudWeather2D.asset 512²  覆盖率 / 云系（天气图）
//
// 构造严格照 Schneider《Real-time Volumetric Cloudscapes of HZ:D》与
// Nubis（Guerrilla）的生产实现：
//
//   R = Perlin-Worley = Remap(perlinFBM, 0, 1, worleyLowFBM, 1)   ← 用 Worley 膨胀 Perlin
//   G = Worley FBM @ f4      B = @ f8      A = @ f16
//
// 着色器端再做一次膨胀（这是关键，之前在烘焙里就用 CDF 拉伸把它做坏了）：
//   fbmW = dot(GBA, (0.625, 0.25, 0.125))
//   base = remap(R, -(1 - fbmW), 1, 0, 1)
//
// 注意：不要对通道做直方图均衡（CDF）。均匀分布看着"对比更强"，但它会抹平
// Perlin-Worley 的团块结构，云就散成一层均匀的棉絮 —— 之前踩过这个坑。

using UnityEditor;
using UnityEngine;

namespace CartoonRendering.EditorTools
{
    public static class CloudNoiseBaker
    {
        const int SHAPE = 128;
        const int DETAIL = 32;
        const int WEATHER = 512;

        const string PATH_SHAPE   = "Packages/com.opoi375.cartoon-rendering/Textures/CloudShape3D.asset";
        const string PATH_DETAIL  = "Packages/com.opoi375.cartoon-rendering/Textures/CloudDetail3D.asset";
        const string PATH_WEATHER = "Packages/com.opoi375.cartoon-rendering/Textures/CloudWeather2D.asset";

        [MenuItem("Tools/Clouds/Bake Noise")]
        public static void BakeAll()
        {
            BakeShape();
            BakeDetail();
            BakeWeather();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- shape
        [MenuItem("Tools/Clouds/Bake Shape Noise 3D")]
        public static void BakeShape()
        {
            var data = new Color32[SHAPE * SHAPE * SHAPE];
            int i = 0;
            for (int z = 0; z < SHAPE; z++)
            for (int y = 0; y < SHAPE; y++)
            for (int x = 0; x < SHAPE; x++, i++)
            {
                float u = x / (float)SHAPE, v = y / (float)SHAPE, w = z / (float)SHAPE;

                float perlin = PerlinFbm(u, v, w, 4, 5);
                float w0 = WorleyFbm(u, v, w, 4);
                float w1 = WorleyFbm(u, v, w, 8);
                float w2 = WorleyFbm(u, v, w, 16);

                // Schneider 的 Perlin-Worley：把 Perlin 的团块用低频 Worley 撑开，
                // 得到椰菜花状、彼此连通的大团块。
                float pw = Remap(perlin, 0f, 1f, w0, 1f);

                data[i] = new Color32(ToByte(pw), ToByte(w0), ToByte(w1), ToByte(w2));
            }
            Write3D(PATH_SHAPE, SHAPE, data, "CloudShape3D");
            Debug.Log($"[CloudNoiseBaker] shape {SHAPE}^3 (Perlin-Worley + Worley FBM) -> {PATH_SHAPE}");
        }

        // --------------------------------------------------------------- detail
        [MenuItem("Tools/Clouds/Bake Detail Noise 3D")]
        public static void BakeDetail()
        {
            var data = new Color32[DETAIL * DETAIL * DETAIL];
            int i = 0;
            for (int z = 0; z < DETAIL; z++)
            for (int y = 0; y < DETAIL; y++)
            for (int x = 0; x < DETAIL; x++, i++)
            {
                float u = x / (float)DETAIL, v = y / (float)DETAIL, w = z / (float)DETAIL;
                // 高频 Worley：只负责在云的边缘啃出细碎咬口
                float w0 = WorleyFbm(u, v, w, 8);
                float w1 = WorleyFbm(u, v, w, 16);
                float w2 = WorleyFbm(u, v, w, 32);
                data[i] = new Color32(ToByte(w0), ToByte(w1), ToByte(w2), 255);
            }
            Write3D(PATH_DETAIL, DETAIL, data, "CloudDetail3D");
            Debug.Log($"[CloudNoiseBaker] detail {DETAIL}^3 (Worley FBM) -> {PATH_DETAIL}");
        }

        // -------------------------------------------------------------- weather
        [MenuItem("Tools/Clouds/Bake Weather Map 2D")]
        public static void BakeWeather()
        {
            var data = new Color32[WEATHER * WEATHER];
            int i = 0;
            for (int y = 0; y < WEATHER; y++)
            for (int x = 0; x < WEATHER; x++, i++)
            {
                float u = x / (float)WEATHER, v = y / (float)WEATHER;

                // R = 覆盖率（大尺度云系：这边成片、那边一片空）
                float coverage = PerlinFbm2(u, v, 2, 4);
                // G = 云型（0 = 层云摊平，1 = 积云竖厚）
                float type = PerlinFbm2(u + 0.37f, v + 0.71f, 3, 4);
                // BA = 域扭曲
                float wx = PerlinFbm2(u + 0.11f, v + 0.53f, 5, 3);
                float wy = PerlinFbm2(u + 0.83f, v + 0.19f, 5, 3);

                data[i] = new Color32(ToByte(coverage), ToByte(type), ToByte(wx), ToByte(wy));
            }

            var tex = new Texture2D(WEATHER, WEATHER, TextureFormat.RGBA32, true, linear: true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                name = "CloudWeather2D"
            };
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);
            SaveOrReplace(tex, PATH_WEATHER);
            Debug.Log($"[CloudNoiseBaker] weather {WEATHER}^2 -> {PATH_WEATHER}");
        }

        // ====================================================================
        //  工具
        // ====================================================================
        static byte ToByte(float v) { return (byte)(Mathf.Clamp01(v) * 255f + 0.5f); }

        /// Schneider 的 Remap：把 v 从 [a,b] 线性映射到 [c,d]，并钳制。
        static float Remap(float v, float a, float b, float c, float d)
        {
            return c + (v - a) / Mathf.Max(b - a, 1e-6f) * (d - c);
        }

        static void Write3D(string path, int n, Color32[] data, string name)
        {
            var tex = new Texture3D(n, n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                name = name
            };
            tex.SetPixelData(data, 0);
            tex.Apply(true, false);      // 生成完整 mip 链（远处按足迹选 mip）
            SaveOrReplace(tex, path);
        }

        static void SaveOrReplace(Object tex, string path)
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
        }

        static void EnsureFolder(string assetPath)
        {
            string dir = System.IO.Path.GetDirectoryName(assetPath).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(dir)) return;
            string parent = System.IO.Path.GetDirectoryName(dir).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
        }

        // ====================================================================
        //  可平铺 Perlin（3D）
        // ====================================================================
        static readonly Vector3[] kGrads =
        {
            new Vector3(1,1,0), new Vector3(-1,1,0), new Vector3(1,-1,0), new Vector3(-1,-1,0),
            new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(1,0,-1), new Vector3(-1,0,-1),
            new Vector3(0,1,1), new Vector3(0,-1,1), new Vector3(0,1,-1), new Vector3(0,-1,-1)
        };

        static int Hash3(int x, int y, int z, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + z * 1274126177 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        static float Hash01(int x, int y, int z, int seed)
        {
            return (Hash3(x, y, z, seed) & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }

        static int Wrap(int c, int period)
        {
            int m = c % period;
            return m < 0 ? m + period : m;
        }

        static float Perlin3(float x, float y, float z, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y), zi = Mathf.FloorToInt(z);
            float xf = x - xi, yf = y - yi, zf = z - zi;
            float u = Fade(xf), v = Fade(yf), w = Fade(zf);

            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
            int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);
            int z0 = Wrap(zi, period), z1 = Wrap(zi + 1, period);

            float n000 = GradDot(Hash3(x0,y0,z0,0), xf,     yf,     zf);
            float n100 = GradDot(Hash3(x1,y0,z0,0), xf - 1, yf,     zf);
            float n010 = GradDot(Hash3(x0,y1,z0,0), xf,     yf - 1, zf);
            float n110 = GradDot(Hash3(x1,y1,z0,0), xf - 1, yf - 1, zf);
            float n001 = GradDot(Hash3(x0,y0,z1,0), xf,     yf,     zf - 1);
            float n101 = GradDot(Hash3(x1,y0,z1,0), xf - 1, yf,     zf - 1);
            float n011 = GradDot(Hash3(x0,y1,z1,0), xf,     yf - 1, zf - 1);
            float n111 = GradDot(Hash3(x1,y1,z1,0), xf - 1, yf - 1, zf - 1);

            float nx00 = Mathf.Lerp(n000, n100, u), nx10 = Mathf.Lerp(n010, n110, u);
            float nx01 = Mathf.Lerp(n001, n101, u), nx11 = Mathf.Lerp(n011, n111, u);
            float nxy0 = Mathf.Lerp(nx00, nx10, v), nxy1 = Mathf.Lerp(nx01, nx11, v);
            return Mathf.Clamp01(Mathf.Lerp(nxy0, nxy1, w) * 0.7f + 0.5f);
        }

        static float GradDot(int hash, float x, float y, float z)
        {
            Vector3 g = kGrads[(hash & 0x7FFFFFFF) % kGrads.Length];
            return g.x * x + g.y * y + g.z * z;
        }

        static float PerlinFbm(float u, float v, float w, int baseFreq, int octaves)
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

        // ====================================================================
        //  可平铺 Worley（3D，反向 F1）
        // ====================================================================
        static float Worley3(float x, float y, float z, int period)
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

        static float WorleyFbm(float u, float v, float w, int baseFreq)
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

        // ====================================================================
        //  可平铺 Perlin（2D，天气图）
        // ====================================================================
        static readonly Vector2[] kGrads2 =
        {
            new Vector2(1,0), new Vector2(-1,0), new Vector2(0,1), new Vector2(0,-1),
            new Vector2(0.7071f,0.7071f), new Vector2(-0.7071f,0.7071f),
            new Vector2(0.7071f,-0.7071f), new Vector2(-0.7071f,-0.7071f)
        };

        static int Hash2(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return h ^ (h >> 16);
            }
        }

        static float Perlin2(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = Fade(xf), v = Fade(yf);
            int x0 = Wrap(xi, period), x1 = Wrap(xi + 1, period);
            int y0 = Wrap(yi, period), y1 = Wrap(yi + 1, period);

            Vector2 g00 = kGrads2[(Hash2(x0,y0,seed) & 0x7FFFFFFF) % 8];
            Vector2 g10 = kGrads2[(Hash2(x1,y0,seed) & 0x7FFFFFFF) % 8];
            Vector2 g01 = kGrads2[(Hash2(x0,y1,seed) & 0x7FFFFFFF) % 8];
            Vector2 g11 = kGrads2[(Hash2(x1,y1,seed) & 0x7FFFFFFF) % 8];

            float n00 = g00.x * xf        + g00.y * yf;
            float n10 = g10.x * (xf - 1f) + g10.y * yf;
            float n01 = g01.x * xf        + g01.y * (yf - 1f);
            float n11 = g11.x * (xf - 1f) + g11.y * (yf - 1f);
            return Mathf.Clamp01(Mathf.Lerp(Mathf.Lerp(n00, n10, u),
                                            Mathf.Lerp(n01, n11, u), v) * 0.9f + 0.5f);
        }

        static float PerlinFbm2(float u, float v, int baseFreq, int octaves)
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
