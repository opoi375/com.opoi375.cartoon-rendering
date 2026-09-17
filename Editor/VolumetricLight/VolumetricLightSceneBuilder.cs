// ============================================================================
// VolumetricLightSceneBuilder.cs
// ----------------------------------------------------------------------------
// 生成体积光演示场景：一间暗房间，天花板上开了一整排竖直窄缝，低角度太阳把
// 光柱斜着射进来，相机在房间里侧向观看。
//
// 为什么是这个结构：
//   1. 体积光最容易出的破绽是【阴影没接上】—— 那样光柱会退化成一整片均匀的
//      雾。窄缝投出的平行光柱有非常明确的形状与边缘，接错了一眼就能看出来，
//      所以它同时也是最好的自检场景。
//   2. 相机必须在阴影里。如果相机本身处在受光的位置，整条视线上的雾都是亮的，
//      光柱和背景的亮度就没有差别，画面只会是一片奶白。
//   3. 相机要【侧向】看光柱。顺着光看会得到强烈的 HG 前向散射峰值（一片惨白），
//      横着看才是根根分明的光柱。
//
// 菜单：
//   Tools/Volumetric Light/Create Demo Scene     新建并保存演示场景
//   Tools/Volumetric Light/Build In Current Scene 只在当前场景重建
//   Tools/Volumetric Light/Dump State             打印渲染器 / 主光 / Volume / 材质状态
//   Tools/Volumetric Light/Debug/*                切换调试视图
// ============================================================================

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor
{
    public static class VolumetricLightSceneBuilder
    {
        const string RootName      = "VOLUMETRIC_LIGHT_SHOWCASE";
        const string ShaderName    = "CartoonRendering/PostProcessing/VolumetricLight";
        const string GenDir        = "Assets/CartoonRendering/VolumetricLight";
        const string MatDir        = GenDir + "/Materials";
        const string FloorMatPath  = MatDir + "/VL_Floor.mat";
        const string WallMatPath   = MatDir + "/VL_Wall.mat";
        const string ProfilePath   = GenDir + "/VolumetricLight_ShowcaseVolume.asset";
        const string ScenePath     = "Assets/Scenes/VolumetricLight.unity";

        // ---- 房间尺寸 ----
        const float SlitWallZ    = 8f;     // 开缝的那面墙（光从这边进来）
        const float RoomFarZ     = -9f;    // 房间深处那面墙
        const float RoomHalfX    = 16f;
        const float CeilingY     = 9f;     // 天花板上表面
        const float SlabThick    = 0.6f;

        // ---- 缝与垛 ----
        const float SlitPitch    = 3.4f;
        const float SlitBarWidth = 1.4f;
        const int   SlitBarCount = 10;

        // ---- 光照与相机 ----
        // 仰角 30°：缝高 9 米的光束落地在墙前 9/tan(30°) ≈ 15.6 米处，比房间深度略大，
        // 所以光柱从窗口一直铺到房间深处
        static readonly Quaternion SunRot = Quaternion.Euler(30f, 158f, 0f);
        static readonly Color SunColor    = new Color(1.00f, 0.86f, 0.62f);
        const float SunIntensity = 3.0f;

        // 相机在房间对角：视线方向和光束方向大致成 115° —— 既不顺着光看（那样只剩
        // 一片惨白的前向散射峰值），也不完全横切（那样背景全是亮窗），而是斜着穿过
        // 光柱，背后是暗的侧墙。
        static readonly Vector3 CamPos  = new Vector3(-13.5f, 2.2f, -8f);
        static readonly Vector3 CamLook = new Vector3(3f, 4.0f, 5f);
        const float CamFov = 58f;

        // 注意：这些颜色是 sRGB，进线性空间后大约剩 0.1 / 0.04 / 0.008 —— 写小了
        // 整张画面会黑到看不出几何体。
        static readonly Color Ambient    = new Color(0.070f, 0.080f, 0.105f);
        static readonly Color Backdrop   = new Color(0.018f, 0.022f, 0.030f);
        static readonly Color FloorTint  = new Color(0.30f, 0.28f, 0.25f);
        static readonly Color WallTint   = new Color(0.17f, 0.17f, 0.195f);
        static readonly Color OutsideTint = new Color(0.55f, 0.47f, 0.34f);   // 缝外面那层背景

        // ------------------------------------------------------------------
        [MenuItem("Tools/Volumetric Light/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!PrepareForNewScene()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(active, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[VolumetricLightSceneBuilder] 演示场景已生成：{ScenePath}");
        }

        /// <summary>
        /// 新建演示场景前的准备工作。
        ///
        /// 【不要】用 EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()：
        /// 它会在有脏场景时弹出模态对话框，而本方法经常是从脚本 / 自动化
        /// （UnitySkills 的 ExecuteMenuItem、CI 等）里调的 —— 模态框没人点，
        /// 编辑器主线程就被堵死了。这里改成静默存盘，存不了（没有文件路径的
        /// 未命名场景）就中止并报错。
        /// </summary>
        static bool PrepareForNewScene()
        {
            if (EditorSceneManager.SaveOpenScenes())
                return true;

            Debug.LogError("[VolumetricLightSceneBuilder] 有未保存且没有文件路径的场景，" +
                           "无法静默保存，已中止。请先手动保存或另存该场景后再生成演示场景。");
            return false;
        }

        [MenuItem("Tools/Volumetric Light/Build In Current Scene")]
        public static void BuildInCurrentScene()
        {
            Build();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        /// <summary>诊断用：把决定效果能不能出来的几个前提全打出来。</summary>
        [MenuItem("Tools/Volumetric Light/Dump State")]
        public static void DumpState()
        {
            var rendererData = VolumetricLightSetup.FindActiveRenderer();
            bool hasFeature = false;
            if (rendererData != null)
            {
                foreach (var f in rendererData.rendererFeatures)
                    if (f is VolumetricLightFeature) hasFeature = true;
            }

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            var sun = FindMainDirectionalLight();
            string lightInfo = sun == null
                ? "找不到方向光"
                : $"{sun.name} type={sun.type} shadows={sun.shadows} 强度={sun.intensity:F2} " +
                  $"颜色=({sun.color.r:F2},{sun.color.g:F2},{sun.color.b:F2}) " +
                  $"forward={sun.transform.forward.ToString("F3")}";

            string pipelineInfo = pipeline == null
                ? "管线资产 = null（不是 URP？）"
                : $"管线={pipeline.name} 阴影距离={pipeline.shadowDistance} " +
                  $"级联={pipeline.shadowCascadeCount} 主光阴影={pipeline.supportsMainLightShadows} " +
                  $"阴影图={pipeline.mainLightShadowmapResolution} 深度图={pipeline.supportsCameraDepthTexture}";

            var volume = VolumeManager.instance.stack.GetComponent<VolumetricLight>();
            string volumeInfo = volume == null
                ? "Volume 栈里没有 VolumetricLight（场景里没有 Global Volume？）"
                : $"intensity={volume.intensity.value:F3} density={volume.density.value:F4} " +
                  $"maxDist={volume.maxDistance.value:F1} g={volume.anisotropy.value:F2} " +
                  $"steps={volume.stepCount.value} debug={volume.debug.value} 激活={volume.IsActive()}";

            var shader = Shader.Find(ShaderName);
            string shaderInfo = shader == null
                ? $"找不到 shader：{ShaderName}"
                : $"shader 已找到 pass 数={shader.passCount}";

            Debug.Log($"[体积光诊断] 渲染器={rendererData?.name ?? "null"} feature={(hasFeature ? "已安装" : "缺失！")}\n"
                      + $"  主光：{lightInfo}\n"
                      + $"  {pipelineInfo}\n"
                      + $"  Volume：{volumeInfo}\n"
                      + $"  {shaderInfo}");
        }

        // ------------------------------------------------------------------
        //  调试视图（排查时不用去 Volume 面板里翻）
        // ------------------------------------------------------------------
        [MenuItem("Tools/Volumetric Light/Debug/Off", priority = 100)]
        static void DebugOff() => SetDebugView(VolumetricLightDebug.Off);

        [MenuItem("Tools/Volumetric Light/Debug/Shadow", priority = 101)]
        static void DebugShadow() => SetDebugView(VolumetricLightDebug.Shadow);

        [MenuItem("Tools/Volumetric Light/Debug/Steps", priority = 102)]
        static void DebugSteps() => SetDebugView(VolumetricLightDebug.Steps);

        [MenuItem("Tools/Volumetric Light/Debug/Scene Depth", priority = 103)]
        static void DebugSceneDepth() => SetDebugView(VolumetricLightDebug.SceneDepth);

        static void SetDebugView(VolumetricLightDebug mode)
        {
            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume == null || volume.sharedProfile == null)
            {
                Debug.LogWarning("[VolumetricLight] 当前场景里没有找到挂 profile 的 Volume。");
                return;
            }

            if (!volume.sharedProfile.TryGet<VolumetricLight>(out var light))
            {
                Debug.LogWarning($"[VolumetricLight] {volume.sharedProfile.name} 里没有 VolumetricLight 覆盖项。");
                return;
            }

            light.debug.Override(mode);
            EditorUtility.SetDirty(volume.sharedProfile);
            AssetDatabase.SaveAssets();

            // 让 Scene / Game 视图立刻重画，不用切窗口
            UnityEditor.SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();

            string hint = mode == VolumetricLightDebug.Shadow
                ? "  —— 整屏纯白 = 主光阴影没接上（光柱会退化成均匀雾）"
                : string.Empty;
            Debug.Log($"[VolumetricLight] 调试视图 → {mode}{hint}");
        }

        // ------------------------------------------------------------------
        static void Build()
        {
            Clean();
            EnsureFolders();

            // 场景能直接看到效果的前提：渲染器上得有这个 Feature
            VolumetricLightSetup.EnsureFeature(out _);

            var root = new GameObject(RootName);

            BuildEnvironment(root.transform);
            var mats = BuildMaterials();
            BuildFloor(root.transform, mats.floor);
            BuildSlitWall(root.transform, mats.wall);
            BuildRoomShell(root.transform, mats.wall);
            BuildOutside(root.transform);
            BuildLighting(root.transform);
            BuildCamera(root.transform);
            BuildVolume(root.transform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        struct Mats
        {
            public Material floor;
            public Material wall;
        }

        static Mats BuildMaterials()
        {
            Mats m;
            m.floor = GetOrCreateMaterial(FloorMatPath, TuneFloor);
            m.wall  = GetOrCreateMaterial(WallMatPath, TuneWall);
            return m;
        }

        static void BuildEnvironment(Transform parent)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.reflectionIntensity = 0.1f;
            // 体积光本身就是"雾"，再叠一层 URP 的距离雾只会把光柱洗掉
            RenderSettings.fog = false;
        }

        static void BuildFloor(Transform parent, Material mat)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(parent, false);
            floor.transform.localScale = new Vector3(24f, 1f, 24f);   // Plane 原始尺寸 10x10
            floor.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// <summary>房间的一侧：一整排墙垛，垛与垛之间就是透光的窄缝。</summary>
        static void BuildSlitWall(Transform parent, Material mat)
        {
            var wall = new GameObject("Slit Wall");
            wall.transform.SetParent(parent, false);

            float startX = -(SlitBarCount - 1) * 0.5f * SlitPitch;
            for (int i = 0; i < SlitBarCount; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                bar.name = $"Bar_{i:00}";
                bar.transform.SetParent(wall.transform, false);
                bar.transform.localPosition = new Vector3(startX + i * SlitPitch, CeilingY * 0.5f, SlitWallZ);
                bar.transform.localScale = new Vector3(SlitBarWidth, CeilingY, SlabThick);
                bar.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        /// <summary>天花板 + 深处那面墙 + 两侧墙，把相机包在一个暗房间里。</summary>
        static void BuildRoomShell(Transform parent, Material mat)
        {
            var shell = new GameObject("Room Shell");
            shell.transform.SetParent(parent, false);

            float depth = SlitWallZ - RoomFarZ;

            // 天花板：挡住从上方直射下来的太阳，房间内部才暗得下来
            MakeSlab(shell.transform, "Ceiling", mat,
                new Vector3(0f, CeilingY + SlabThick * 0.5f, (SlitWallZ + RoomFarZ) * 0.5f),
                new Vector3(RoomHalfX * 2f, SlabThick, depth));

            // 深处那面墙（相机主要看的暗背景）
            MakeSlab(shell.transform, "Far Wall", mat,
                new Vector3(0f, CeilingY * 0.5f, RoomFarZ - SlabThick * 0.5f),
                new Vector3(RoomHalfX * 2f, CeilingY, SlabThick));

            // 两侧墙，防止侧面漏进天空盒把对比度冲淡
            MakeSlab(shell.transform, "Side Wall L", mat,
                new Vector3(-RoomHalfX - SlabThick * 0.5f, CeilingY * 0.5f, (SlitWallZ + RoomFarZ) * 0.5f),
                new Vector3(SlabThick, CeilingY, depth));
            MakeSlab(shell.transform, "Side Wall R", mat,
                new Vector3(RoomHalfX + SlabThick * 0.5f, CeilingY * 0.5f, (SlitWallZ + RoomFarZ) * 0.5f),
                new Vector3(SlabThick, CeilingY, depth));
        }

        /// <summary>缝外面那层亮背景：让窄缝看起来是"过曝的窗外"。</summary>
        static void BuildOutside(Transform parent)
        {
            var mat = GetOrCreateMaterial(MatDir + "/VL_Outside.mat", TuneOutside);

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Outside";
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 7f, SlitWallZ + 12f);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            go.transform.localScale = new Vector3(120f, 60f, 1f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void MakeSlab(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void BuildLighting(Transform parent)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = SunRot;

            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = SunColor;
            l.intensity = SunIntensity;
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
            cam.farClipPlane = 400f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;

            var extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            extra.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            // 体积光要读深度图，显式打开（管线资产上也开着，双保险）
            extra.requiresDepthTexture = true;
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

            // 和材质一样是生成物：每次重跑都把覆盖项重置回代码里的值
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
            var light = AddComponent<VolumetricLight>(profile);
            light.intensity.Override(1.1f);
            light.tint.Override(new Color(1f, 0.86f, 0.66f, 1f));
            light.density.Override(0.016f);
            light.anisotropy.Override(0.5f);
            light.maxDistance.Override(46f);
            light.distanceFade.Override(0.6f);
            light.heightStart.Override(0f);
            light.heightFalloff.Override(0.015f);
            light.shadowStrength.Override(1f);
            light.shadowBias.Override(0.05f);
            light.stepCount.Override(48);
            light.jitter.Override(0.9f);
            light.softness.Override(1.6f);
            light.noiseStrength.Override(0.25f);
            light.noiseScale.Override(0.09f);
            light.noiseSpeed.Override(0.05f);
            light.banding.Override(0);
            light.debug.Override(VolumetricLightDebug.Off);

            var bloom = AddComponent<Bloom>(profile);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.9f);
            bloom.scatter.Override(0.72f);
            bloom.tint.Override(new Color(1f, 0.93f, 0.82f));

            var tone = AddComponent<Tonemapping>(profile);
            tone.mode.Override(TonemappingMode.ACES);

            var ca = AddComponent<ColorAdjustments>(profile);
            ca.contrast.Override(10f);
            ca.saturation.Override(-4f);

            var vig = AddComponent<Vignette>(profile);
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.5f);
        }

        /// <summary>
        /// 把一个 Volume 覆盖项加进 profile 并【写成子资产】。
        ///
        /// 必须用 VolumeProfileFactory：直接调 profile.Add&lt;T&gt;() 只会建一个内存
        /// 实例，它不在任何资产里，序列化时会变成 fileID: 0 —— 当前会话里看着是好的，
        /// 场景一重开 Volume 覆盖项就全没了。
        /// </summary>
        static T AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return VolumeProfileFactory.CreateVolumeComponent<T>(profile, overrides: true, saveAsset: false);
        }

        // ------------------------------------------------------------------
        static Light FindMainDirectionalLight()
        {
            foreach (var l in Object.FindObjectsByType<Light>())
            {
                if (l.type == LightType.Directional && l.isActiveAndEnabled)
                    return l;
            }
            return null;
        }

        static void TuneFloor(Material m)  => TuneLit(m, FloorTint, 0.06f);
        static void TuneWall(Material m)   => TuneLit(m, WallTint, 0.04f);
        static void TuneOutside(Material m)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", OutsideTint);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0f);
            // 缝外面那层只负责"过曝"，不要吃阴影也不要挡光
            if (m.HasProperty("_ReceiveShadows")) m.SetFloat("_ReceiveShadows", 0f);
        }

        static void TuneLit(Material m, Color tint, float smoothness)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        }

        static Material GetOrCreateMaterial(string path, System.Action<Material> tune)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
            {
                // 生成物：每次重跑都重新上参数，保证「改常量重跑」的结果一致
                tune(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[VolumetricLightSceneBuilder] 找不到 URP/Lit，材质 {path} 会不可用");
                return null;
            }

            mat = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
            tune(mat);

            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static void EnsureFolders()
        {
            CreateFolder("Assets", "CartoonRendering");
            CreateFolder("Assets/CartoonRendering", "VolumetricLight");
            CreateFolder(GenDir, "Materials");
            CreateFolder("Assets", "Scenes");
        }

        static void CreateFolder(string parent, string name)
        {
            string full = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(full))
                AssetDatabase.CreateFolder(parent, name);
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
