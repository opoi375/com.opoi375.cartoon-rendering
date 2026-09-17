// ============================================================================
// LedDotMatrixSceneBuilder.cs
// ----------------------------------------------------------------------------
// 「LED 点阵文字屏」演示场景构建器。
//
// 菜单：Tools > LED > Create Demo Scene
//       Tools > LED > Build In Current Scene
//       Tools > LED > Dump Mask PNG
//
// 设计要点：
//  - 所有摆场参数集中在下面的常量区，改数字重跑即可，不用在编辑器里手点
//  - 重跑会先清掉上一次生成的根节点，不会堆垃圾
//  - 材质 / VolumeProfile 是生成物，每次重跑都会重新上参数，保证结果一致

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor
{
    public static class LedDotMatrixSceneBuilder
    {
        const string RootName   = "LED_SHOWCASE";
        const string ShaderName = "CartoonRendering/LED/DotMatrix Text";
        const string GenDir     = "Assets/CartoonRendering/LED";
        const string MatDir     = GenDir + "/Materials";
        const string MatPath    = MatDir + "/LED_SystemOnline.mat";
        const string FloorMatPath = MatDir + "/LED_Floor.mat";
        const string BackdropMatPath = MatDir + "/LED_Backdrop.mat";
        const string ProfilePath = GenDir + "/LED_ShowcaseVolume.asset";
        const string ScenePath   = "Assets/Scenes/LEDDotMatrix.unity";

        // ---- 屏幕 ----
        const float ScreenWidth    = 12f;    // 弧长（世界单位）
        const float ScreenHeight   = 3f;
        const float ScreenArc      = 26f;    // 总张角（度），正数 = 凹面朝向相机
        const int   ScreenSegments = 64;
        const float ScreenY        = 2.6f;

        const float LedCols = 132f;

        const int    MaskWidth  = 2048;
        const float  MaskFitPadding = 0.06f;   // 文字四周留白，越小字越大
        const string MaskText   = "SYSTEM ONLINE";

        // ---- 环境 ----
        static readonly Color Ambient   = new Color(0.022f, 0.026f, 0.034f);
        static readonly Color FogColor  = new Color(0.010f, 0.012f, 0.017f);
        static readonly Color FloorTint = new Color(0.011f, 0.013f, 0.017f);
        const float FogDensity = 0.018f;

        // ---- 相机 ----
        static readonly Vector3 CamPos  = new Vector3(-0.8f, 1.72f, -10.5f);
        static readonly Vector3 CamLook = new Vector3(0f, 2.85f, 0f);
        const float CamFov = 42f;

        [MenuItem("Tools/LED/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(active, ScenePath);
            AssetDatabase.Refresh();

            // Refresh 会重新导入 .mat，运行期赋上去的 _MainTex 会丢，必须再烘一次
            RebakeAll();

            Debug.Log($"[LedDotMatrixSceneBuilder] 演示场景已生成：{ScenePath}");
        }

        static void RebakeAll()
        {
            foreach (var baker in Object.FindObjectsByType<LedTextMaskBaker>())
                baker.Bake();
        }

        [MenuItem("Tools/LED/Build In Current Scene")]
        public static void BuildInCurrentScene()
        {
            Build();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        /// <summary>诊断用：先报告当前状态，再把遮罩导成 PNG。</summary>
        [MenuItem("Tools/LED/Dump Mask PNG")]
        public static void DumpMask()
        {
            var baker = Object.FindAnyObjectByType<LedTextMaskBaker>();
            if (baker == null)
            {
                Debug.LogWarning("[LedDotMatrixSceneBuilder] 当前场景里没有 LedTextMaskBaker。");
                return;
            }

            LogLedState("烘焙前", baker);
            baker.Bake();
            LogLedState("烘焙后", baker);

            var mask = baker.Mask;
            if (mask == null)
            {
                Debug.LogError("[LedDotMatrixSceneBuilder] 遮罩为 null，烘焙根本没跑成功。");
                return;
            }

            Debug.Log($"[LED 诊断] 字体图集 = {baker.LastAtlasInfo}");

            const string dir = GenDir;
            if (!AssetDatabase.IsValidFolder(dir))
            {
                CreateFolder("Assets", "CartoonRendering");
                CreateFolder("Assets/CartoonRendering", "LED");
            }
            string file = $"{dir}/Mask_dump.png";
            System.IO.File.WriteAllBytes(file, mask.EncodeToPNG());
            AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[LED 诊断] 遮罩已导出：{file}", baker);
        }

        // ------------------------------------------------------------------
        static void Build()
        {
            Clean();
            EnsureFolders();

            var root = new GameObject(RootName);

            BuildEnvironment(root.transform);
            var panel = BuildScreen(root.transform);
            BuildFloor(root.transform);
            BuildLighting(root.transform);
            BuildCamera(root.transform);
            BuildVolume(root.transform);

            Selection.activeGameObject = panel;
            EditorGUIUtility.PingObject(panel);
        }

        // ------------------------------------------------------------------
        static GameObject BuildScreen(Transform parent)
        {
            var mat = GetOrCreateMaterial(MatPath, ShaderName, TuneLedMaterial);

            var go = new GameObject("LED Panel");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, ScreenY, 0f);

            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var screen = go.AddComponent<LedCurvedScreen>();
            screen.Rebuild();

            var baker = go.AddComponent<LedTextMaskBaker>();
            SetPrivate(baker, "text", MaskText);
            SetPrivate(baker, "textureWidth", MaskWidth);
            SetPrivate(baker, "padding", MaskFitPadding);
            SetPrivate(baker, "fontStyle", (int)FontStyle.Bold);
            SetPrivate(baker, "matchScreenAspect", true);
            baker.Bake();

            return go;
        }

        static void BuildEnvironment(Transform parent)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = FogColor;
            RenderSettings.fogDensity = FogDensity;
            RenderSettings.reflectionIntensity = 0.15f;

            var mat = GetOrCreateMaterial(BackdropMatPath, "Universal Render Pipeline/Unlit");
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", new Color(0.012f, 0.014f, 0.018f));

            var backdrop = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backdrop.name = "Backdrop";
            backdrop.transform.SetParent(parent, false);
            backdrop.transform.localPosition = new Vector3(0f, 5f, 16f);
            backdrop.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            backdrop.transform.localScale = new Vector3(90f, 44f, 1f);
            Object.DestroyImmediate(backdrop.GetComponent<Collider>());
            backdrop.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void BuildFloor(Transform parent)
        {
            var floorMat = GetOrCreateMaterial(FloorMatPath, "Universal Render Pipeline/Lit", TuneFloorMaterial);

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(parent, false);
            floor.transform.localScale = new Vector3(9f, 1f, 9f);   // Plane 原始尺寸 10x10
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMat;
        }

        static void BuildLighting(Transform parent)
        {
            var key = new GameObject("Key Light");
            key.transform.SetParent(parent, false);
            key.transform.localRotation = Quaternion.Euler(38f, -145f, 0f);

            var l = key.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(0.62f, 0.72f, 0.92f);
            l.intensity = 0.32f;
            l.shadows = LightShadows.Soft;
        }

        static void BuildCamera(Transform parent)
        {
            var go = new GameObject("Main Camera");
            go.tag = "MainCamera";
            go.transform.SetParent(parent, false);
            go.transform.position = CamPos;
            go.transform.rotation = Quaternion.LookRotation((CamLook - CamPos).normalized, Vector3.up);

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = CamFov;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 300f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.008f, 0.010f, 0.014f);

            var extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        }

        static void BuildVolume(Transform parent)
        {
            var go = new GameObject("Global Volume");
            go.transform.SetParent(parent, false);

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // 和材质一样，这是生成物：每次重跑都把覆盖项重置回代码里的值
            foreach (var comp in profile.components.ToArray())
            {
                if (comp != null) Object.DestroyImmediate(comp, true);
            }
            profile.components.Clear();
            PopulateProfile(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 10f;
            vol.sharedProfile = profile;
        }

        static void PopulateProfile(VolumeProfile profile)
        {
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.85f);
            bloom.intensity.Override(1.15f);
            bloom.scatter.Override(0.78f);
            bloom.tint.Override(new Color(0.86f, 0.93f, 1f));

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(0.05f);
            ca.contrast.Override(14f);
            ca.saturation.Override(-14f);

            var vig = profile.Add<Vignette>(true);
            vig.intensity.Override(0.38f);
            vig.smoothness.Override(0.5f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium1);
            grain.intensity.Override(0.22f);
            grain.response.Override(0.7f);
        }

        // ------------------------------------------------------------------
        //  资产
        // ------------------------------------------------------------------
        static Material GetOrCreateMaterial(string path, string shaderName, System.Action<Material> tune = null)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                // 这些材质都是生成物，每次都重新上参数，保证「改常量重跑」的结果一致
                if (tune != null)
                {
                    tune(mat);
                    EditorUtility.SetDirty(mat);
                }
                return mat;
            }

            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"[LedDotMatrixSceneBuilder] 找不到 shader：{shaderName}");
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            tune?.Invoke(mat);

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void TuneLedMaterial(Material m)
        {
            m.SetFloat("_Cols", LedCols);
            m.SetFloat("_DotRadius", 0.38f);
            m.SetFloat("_DotSoftness", 0.05f);
            m.SetFloat("_MaskThreshold", 0.5f);
            m.SetFloat("_MaskSoftness", 0.35f);
            m.SetVector("_MaskTransform", new Vector4(1f, 1f, 0f, 0f));
            // 默认不滚。设成 0.03 之类的话，编辑模式下 _Time 一直在跑，字会直接滚出屏幕
            m.SetVector("_Scroll", new Vector4(0f, 0f, 0f, 0f));
            m.SetFloat("_FlickerAmount", 0.42f);
            m.SetFloat("_FlickerSpeed", 5.5f);
            m.SetFloat("_FlickerRatio", 4.7f);
            m.SetFloat("_ScanIntensity", 0.18f);
            m.SetFloat("_ScanSpeed", 0.45f);
            m.SetFloat("_ScanWidth", 0.14f);
            m.SetColor("_OnColor", Color.white);
            m.SetFloat("_OnIntensity", 3.8f);
            m.SetColor("_OffColor", new Color(0.105f, 0.115f, 0.135f, 1f));
            m.SetColor("_PanelColor", new Color(0.018f, 0.021f, 0.026f, 1f));
        }

        static void TuneFloorMaterial(Material m)
        {
            m.SetColor("_BaseColor", FloorTint);
            // 要还原参考图里地板反射 LED 屏的效果，得另外放一个 Realtime Reflection Probe；
            // 没有探针时默认反射源是天空盒，会在地板上涂一层灰，所以这里把光泽压到最低。
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.14f);
        }

        // ------------------------------------------------------------------
        static void LogLedState(string phase, LedTextMaskBaker baker)
        {
            var mr = baker.GetComponent<MeshRenderer>();
            var mat = mr != null ? mr.sharedMaterial : null;
            var mf = baker.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;

            var mask = baker.Mask;
            string maskInfo = "null";
            if (mask != null)
            {
                var px = mask.GetPixelData<byte>(0);
                int max = 0, lit = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i] > max) max = px[i];
                    if (px[i] > 200) lit++;
                }
                maskInfo = $"{mask.width}x{mask.height} format={mask.format} 最大值={max} 实心={lit}";
            }

            string matInfo = mat == null
                ? "材质 = null"
                : $"材质={mat.name} shader={mat.shader.name} "
                  + $"_MainTex={(mat.GetTexture("_MainTex") == null ? "null" : mat.GetTexture("_MainTex").name)} "
                  + $"_Aspect={mat.GetFloat("_Aspect"):F3} _Cols={mat.GetFloat("_Cols"):F1} "
                  + $"_MaskThreshold={mat.GetFloat("_MaskThreshold"):F3} "
                  + $"_Scroll={mat.GetVector("_Scroll")} _MaskTransform={mat.GetVector("_MaskTransform")}";

            string meshInfo = mesh == null
                ? "mesh = null"
                : $"mesh={mesh.name} verts={mesh.vertexCount} uv={(mesh.uv != null && mesh.uv.Length > 0 ? mesh.uv[mesh.uv.Length / 2].ToString("F3") : "n/a")}";

            Debug.Log($"[LED 诊断/{phase}] mask={maskInfo} | {matInfo} | {meshInfo}");
        }

        static void EnsureFolders()
        {
            CreateFolder("Assets", "CartoonRendering");
            CreateFolder("Assets/CartoonRendering", "LED");
            CreateFolder(GenDir, "Materials");
            CreateFolder("Assets", "Scenes");
        }

        static void CreateFolder(string parent, string name)
        {
            string full = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, name);
        }

        static void SetPrivate(Object target, string property, object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property);
            if (prop == null) return;

            switch (value)
            {
                case string s: prop.stringValue = s; break;
                case int i: prop.intValue = i; break;
                case bool b: prop.boolValue = b; break;
                case float f: prop.floatValue = f; break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void Clean()
        {
            var existing = GameObject.Find(RootName);
            while (existing != null)
            {
                Object.DestroyImmediate(existing);
                existing = GameObject.Find(RootName);
            }

            // 新建的空场景里可能还留着上一次跑出来的独立相机
            foreach (var cam in Object.FindObjectsByType<Camera>())
            {
                if (cam.transform.parent == null && cam.name == "Main Camera")
                    Object.DestroyImmediate(cam.gameObject);
            }
        }
    }
}
