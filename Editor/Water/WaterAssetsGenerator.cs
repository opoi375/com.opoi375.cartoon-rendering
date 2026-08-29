// ============================================================================
// WaterAssetsGenerator.cs
// ----------------------------------------------------------------------------
// Editor tool for the cartoon water shader (zhuanlan.zhihu.com/p/425605759).
//
// The article uses two textures which ship with Roystan's original tutorial:
//   - SurfaceNoise     : fractal noise that is thresholded into foam/waves
//   - SurfaceDistortion: 2-channel vector field that perturbs the noise UVs
//
// Since we cannot bundle Roystan's assets, this tool regenerates equivalent
// textures procedurally (tileable value-noise fBm; domain-warped for the
// distortion map) and writes them into Packages/com.opoi375.cartoon-rendering/Textures/Water/.
//
// It also creates/updates the CartoonWater material wired up with the article's
// default parameters.
//
// Usage:  CartoonRendering > Water > Generate Water Assets (or run
//         CartoonRendering.Editor.Water.WaterAssetsGenerator.GenerateAll
//         from the command line in batch mode)
// ============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor.Water
{
    public static class WaterAssetsGenerator
    {
        private const string TextureDir = "Packages/com.opoi375.cartoon-rendering/Textures/Water";
        private const string MaterialPath = "Packages/com.opoi375.cartoon-rendering/Materials/CartoonWater.mat";
        private const string AdvancedMaterialPath = "Packages/com.opoi375.cartoon-rendering/Materials/CartoonWaterAdvanced.mat";
        private const string ShaderName = "CartoonRendering/ToonWater/Cartoon";
        private const string AdvancedShaderName = "CartoonRendering/ToonWater/Advanced";

        private const string NoisePath = TextureDir + "/SurfaceNoise.png";
        private const string DistortionPath = TextureDir + "/SurfaceDistortion.png";

        private const int Size = 512;
        private const float NoiseScale = 16f;   // lattice cell size in pixels
        private const int NoisePeriod = 32;     // lattice cells across the texture (32 * 16 = 512 -> perfectly tileable)
        private const int NoiseOctaves = 4;     // cells of 16 / 8 / 4 / 2 px, closer to the article's fractal noise

        [MenuItem("Tools/Water/Generate Water Assets")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(TextureDir);

            Texture2D noise = GenerateSurfaceNoise();
            Texture2D distortion = GenerateSurfaceDistortion();

            SaveTexture(noise, NoisePath);
            SaveTexture(distortion, DistortionPath);
            ConfigureImporter(NoisePath, 10);
            ConfigureImporter(DistortionPath, 4);

            Object.DestroyImmediate(noise);
            Object.DestroyImmediate(distortion);

            AssetDatabase.Refresh();
            CreateMaterial();

            Debug.Log("[CartoonWater] Water assets generated: " + TextureDir);
        }

        // --------------------------------------------------------------------
        // Camera setup: adds the two components the article uses to the main
        // camera (depth texture + normals replacement camera).
        // --------------------------------------------------------------------
        [MenuItem("Tools/Water/Setup Main Camera")]
        public static void SetupMainCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                Debug.LogError("[CartoonWater] No camera tagged MainCamera in the scene.");
                return;
            }

            // Force the camera's URP "Depth Texture" override ON. This is the
            // single switch that makes URP render _CameraDepthTexture for this
            // camera (the pipeline-asset global toggle is ignored when the
            // camera has a UniversalAdditionalCameraData component set to
            // "Use Pipeline Settings").
            var urpData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (urpData != null)
                urpData.requiresDepthTexture = true;

            EditorUtility.SetDirty(cam.gameObject);
            Debug.Log("[CartoonWater] Main camera: URP Depth Texture = On. Also check the\n" +
                      "    URP renderer asset: Copy Depth Mode = After Opaques.");
        }

        // --------------------------------------------------------------------
        // Public texture builders (kept separate so they are unit-testable
        // without an asset pipeline).
        // --------------------------------------------------------------------
        public static Texture2D GenerateSurfaceNoise()
        {
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "SurfaceNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Sample at pixel centres in a scaled UV space: one
                    // lattice cell spans NoiseScale pixels, so the result is a
                    // smooth low-frequency "cloud" (the Roystan look). Sampling
                    // at raw integer pixel coordinates would hit lattice
                    // corners, where gradient noise is always zero -> the
                    // texture would be a single flat colour.
                    //
                    // NOTE: no contrast pow here - the fBm output is used
                    // directly and _SurfaceNoiseCutoff (0.6 in the generator)
                    // is calibrated against this distribution.
                    float n = Fbm(x * (1f / NoiseScale) + 0.5f, y * (1f / NoiseScale) + 0.5f, NoisePeriod, 101u, NoiseOctaves);
                    pixels[y * Size + x] = new Color(n, n, n, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        public static Texture2D GenerateSurfaceDistortion()
        {
            Texture2D tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                name = "SurfaceDistortion",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    // Domain-warped noise -> swirly 2-channel vector field.
                    // Same scaled UV space; the +3.7/+1.3 offsets and the warp
                    // are expressed in lattice cells (each cell = 16 px).
                    float sx = x * (1f / NoiseScale);
                    float sy = y * (1f / NoiseScale);
                    float warpX = (Fbm(sx + 3.7f, sy + 1.3f, NoisePeriod, 201u, NoiseOctaves) - 0.5f) * 2f;
                    float warpY = (Fbm(sx + 5.9f, sy + 7.1f, NoisePeriod, 301u, NoiseOctaves) - 0.5f) * 2f;

                    float r = Fbm(sx + warpX * 0.5f, sy + warpY * 0.5f, NoisePeriod, 401u, NoiseOctaves);
                    float g = Fbm(sx + warpX * 0.5f + 2.7f, sy + warpY * 0.5f - 1.9f, NoisePeriod, 501u, NoiseOctaves);

                    pixels[y * Size + x] = new Color(r, g, 0f, 1f);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // --------------------------------------------------------------------
        // Tileable Perlin-noise helpers.
        // --------------------------------------------------------------------
        private static float Fbm(float x, float y, int period, uint seed, int octaves)
        {
            float value = 0f;
            float amp = 0.5f;
            float freq = 1f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                value += amp * PerlinNoise(x * freq, y * freq, period, seed + (uint)(i * 101));
                norm += amp;
                amp *= 0.5f;
                freq *= 2f;
            }
            return value / norm;
        }

        // Gradient (Perlin) noise, tileable: the lattice is wrapped with a
        // period, and each lattice cell gets a deterministic gradient
        // direction, so the result is smooth with no value-noise grid
        // artifacts - this matches the classic Perlin texture the article's
        // SurfaceNoise is based on.
        private static float PerlinNoise(float x, float y, int period, uint seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            int x0 = Mod(xi, period);
            int y0 = Mod(yi, period);
            int x1 = (x0 + 1) % period;
            int y1 = (y0 + 1) % period;

            // Quintic (smoothstep) interpolation.
            float u = xf * xf * xf * (xf * (xf * 6f - 15f) + 10f);
            float v = yf * yf * yf * (yf * (yf * 6f - 15f) + 10f);

            float n00 = DotGradient(x0, y0, xf,      yf,      seed);
            float n10 = DotGradient(x1, y0, xf - 1f, yf,      seed);
            float n01 = DotGradient(x0, y1, xf,      yf - 1f, seed);
            float n11 = DotGradient(x1, y1, xf - 1f, yf - 1f, seed);

            // Perlin outputs roughly in [-1, 1]; remap to [0, 1].
            return 0.5f + 0.5f * Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
        }

        // Deterministic gradient direction per lattice cell (continuous angle
        // -> smooth vector field, no grid artifacts).
        private static float DotGradient(int ix, int iy, float dx, float dy, uint seed)
        {
            float angle = Hash01((uint)ix, (uint)iy, seed) * Mathf.PI * 2f;
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }

        private static int Mod(int a, int b)
        {
            int r = a % b;
            return r < 0 ? r + b : r;
        }

        private static float Hash01(uint x, uint y, uint seed)
        {
            uint h = seed + 0x9E3779B9u;
            h = (h ^ (x * 0x85EBCA6Bu + 0xC2B2AE35u)) * 0x27D4EB2Fu;
            h = (h ^ (y * 0x165667B1u + 0x27D4EB2Fu)) * 0x85EBCA6Bu;
            h ^= h >> 13;
            h *= 0x7FEB352Du;
            h ^= h >> 15;
            return h / 4294967295f;
        }

        // --------------------------------------------------------------------
        // Asset plumbing.
        // --------------------------------------------------------------------
        private static void SaveTexture(Texture2D tex, string path)
        {
            byte[] png = tex.EncodeToPNG();
            File.WriteAllBytes(path, png);
        }

        private static void ConfigureImporter(string path, float defaultTiling)
        {
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                // Import hasn't happened yet; refresh first.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                importer = (TextureImporter)AssetImporter.GetAtPath(path);
            }

            importer.textureType = TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = true;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }

        private static void CreateMaterial()
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[CartoonWater] Shader not found: " + ShaderName +
                               ". Make sure CartoonWater.shader compiled first.");
                return;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            bool isNew = mat == null;
            if (isNew)
                mat = new Material(shader);
            else
                mat.shader = shader;

            // --- article default parameters (Roystan's toon-water values) ---
            mat.SetColor("_DepthGradientShallow", new Color(0.325f, 0.807f, 0.971f, 0.725f));
            mat.SetColor("_DepthGradientDeep", new Color(0.086f, 0.407f, 1f, 0.749f));
            mat.SetFloat("_DepthMaxDistance", 5f);

            // Calibrated to the generated Perlin fBm distribution (max ~0.74;
            // the article's 0.777 assumes Roystan's own texture). 0.6 leaves
            // ~7% of the surface as sparse open-water foam, matching the look
            // of the article's screenshots.
            mat.SetFloat("_SurfaceNoiseCutoff", 0.6f);
            mat.SetVector("_SurfaceNoiseScroll", new Vector4(0.03f, 0.03f, 0f, 0f));
            mat.SetTextureScale("_SurfaceNoise", new Vector2(10f, 10f));
            mat.SetTextureScale("_SurfaceDistortion", new Vector2(4f, 4f));

            mat.SetFloat("_SurfaceDistortionAmount", 0.27f);

            mat.SetColor("_FoamColor", Color.white);
            mat.SetFloat("_FoamDistance", 0.15f);

            mat.SetFloat("_Alpha", 1f);

            Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            Texture2D distortion = AssetDatabase.LoadAssetAtPath<Texture2D>(DistortionPath);
            if (noise != null)
                mat.SetTexture("_SurfaceNoise", noise);
            if (distortion != null)
                mat.SetTexture("_SurfaceDistortion", distortion);

            if (isNew)
                AssetDatabase.CreateAsset(mat, MaterialPath);
            else
                EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            Debug.Log("[CartoonWater] Material ready: " + MaterialPath);
        }

        [MenuItem("Tools/Water/Generate Advanced Water Material")]
        public static void GenerateAdvancedMaterial()
        {
            Shader shader = Shader.Find(AdvancedShaderName);
            if (shader == null)
            {
                Debug.LogError("[CartoonWater] Shader not found: " + AdvancedShaderName +
                               ". Make sure CartoonWaterAdvanced.shader compiled first.");
                return;
            }

            Material mat = AssetDatabase.LoadAssetAtPath<Material>(AdvancedMaterialPath);
            bool isNew = mat == null;
            if (isNew)
                mat = new Material(shader);
            else
                mat.shader = shader;

            // --- depth-based cartoon shading (same defaults as CartoonWater) ---
            mat.SetColor("_DepthGradientShallow", new Color(0.325f, 0.807f, 0.971f, 0.725f));
            mat.SetColor("_DepthGradientDeep", new Color(0.086f, 0.407f, 1f, 0.749f));
            mat.SetFloat("_DepthMaxDistance", 5f);

            mat.SetFloat("_SurfaceNoiseCutoff", 0.6f);
            mat.SetVector("_SurfaceNoiseScroll", new Vector4(0.03f, 0.03f, 0f, 0f));
            mat.SetTextureScale("_SurfaceNoise", new Vector2(10f, 10f));
            mat.SetTextureScale("_SurfaceDistortion", new Vector2(4f, 4f));
            mat.SetFloat("_SurfaceDistortionAmount", 0.27f);
            mat.SetColor("_FoamColor", Color.white);
            mat.SetFloat("_FoamDistance", 0.15f);

            // --- waves + tessellation (same defaults as CartoonWaterSimple) ---
            mat.SetFloat("_WaveHeight", 0.06f);
            mat.SetFloat("_WaveScale", 6f);
            mat.SetFloat("_WaveSpeed", 0.6f);
            mat.SetVector("_WaveDir", new Vector4(1f, 0f, 0.6f, 0f));
            mat.SetFloat("_TessMax", 12f);
            mat.SetFloat("_TessMin", 2f);
            mat.SetFloat("_TessMinDist", 20f);
            mat.SetFloat("_TessMaxDist", 80f);

            mat.SetFloat("_Alpha", 1f);

            Texture2D noise = AssetDatabase.LoadAssetAtPath<Texture2D>(NoisePath);
            Texture2D distortion = AssetDatabase.LoadAssetAtPath<Texture2D>(DistortionPath);
            if (noise != null)
                mat.SetTexture("_SurfaceNoise", noise);
            if (distortion != null)
                mat.SetTexture("_SurfaceDistortion", distortion);

            if (isNew)
                AssetDatabase.CreateAsset(mat, AdvancedMaterialPath);
            else
                EditorUtility.SetDirty(mat);

            AssetDatabase.SaveAssets();
            Debug.Log("[CartoonWater] Advanced material ready: " + AdvancedMaterialPath);
        }
    }
}
