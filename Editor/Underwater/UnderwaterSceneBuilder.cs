// ============================================================================
// UnderwaterSceneBuilder.cs
// ----------------------------------------------------------------------------
// 生成水下后处理的演示场景：一个水池，相机在水面以下平视，同样的球和柱子按
// 4 / 10 / 16 / 22 / 28 米排开。
//
// 为什么是这个结构：
//   1. 水下效果最容易出的破绽是【雾跟深度没关系】—— 那就退化成整屏平涂一层
//      颜色，近处的球和 28 米外的柱子一样清楚，一眼就能看出来。所以场景里
//      必须摆一排【同材质、不同距离】的物体，把"距离"这个变量单独隔离出来。
//   2. 水面必须是透明物体。水下效果要能盖住水面本身（透过水面看天空的那层），
//      注入点如果在透明之前，水面就永远是干净的 —— 这也是本工具要检查的一项。
//   3. 相机平视而不是俯视。俯视时几乎所有像素的深度都落在地板那一个距离上，
//      看不出距离梯度。
//
// 菜单：
//   Tools/Underwater/Create Demo Scene       新建并保存演示场景
//   Tools/Underwater/Build In Current Scene  只在当前场景重建
//   Tools/Underwater/Dump State              打印渲染器 / 相机 / Volume / shader 状态
// ============================================================================

using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor
{
    public static class UnderwaterSceneBuilder
    {
        const string RootName    = "UNDERWATER_SHOWCASE";
        const string ShaderName  = "CartoonRendering/PostProcessing/UnderwaterPostProcess";
        const string GenDir      = "Assets/CartoonRendering/Underwater";
        const string MatDir      = GenDir + "/Materials";
        const string ProfilePath = GenDir + "/Underwater_ShowcaseVolume.asset";
        const string ScenePath   = "Assets/Scenes/UnderwaterDemo.unity";
        const string WaterMatPath = "Packages/com.opoi375.cartoon-rendering/Materials/CartoonWaterSimple.mat";

        // ---- 水池尺寸：水面 y=0，地板 y=-6.5，进深到 z=30 ----
        public const float SurfaceY = 0f;
        const float FloorY      = -6.5f;
        const float TankHalfX   = 22f;
        const float BackWallZ   = 30f;
        const float FrontZ      = -16f;

        // 相机：站在水池前端、水面下 4.6 米，几乎水平看出去。
        // 为什么贴得深：相机越深，“抬头看水面”的射线越斜、水柱越长，水面区域
        // 自然被雾压暗，只留头顶一块 Snell 窗口；相机浅了整个上半屏都是亮天空，
        // 雾、吸收、焦散全都看不出来。
        static readonly Vector3 CamPos  = new Vector3(0f, -4.6f, -12f);
        // 俯视一点：水面那层亮片（水面 shader 自己的事）占掉上半屏的话，
        // 雾与吸收就没东西可比了。压到顶部一条，剩下的全是水下几何。
        static readonly Vector3 CamLook = new Vector3(0f, -13f, 24f);
        const float CamFov = 60f;

        /// <summary>演示用的等距球排（同材质、只有距离不同），Dump State 按这个报距离。</summary>
        static readonly float[] RowZ = { 2f, 8f, 14f, 20f, 26f };

        static readonly Color SunColor      = new Color(1.00f, 0.93f, 0.80f);
        const float SunIntensity = 2.4f;
        // 太阳在相机背后往 +Z 照：地板、后墙的内面、左墙的内面都吃到光。
        // （以前给的是 (42,150)，光往 -Z 跑，于是看到的整圈墙全是法线背光的死黑，
        //  画面黑到看不出雾有没有生效。）
        static readonly Quaternion SunRot = Quaternion.Euler(48f, -25f, 0f);

        static readonly Color Ambient   = new Color(0.100f, 0.140f, 0.170f);
        static readonly Color SandTint  = new Color(0.42f, 0.36f, 0.26f);
        static readonly Color RockTint  = new Color(0.26f, 0.27f, 0.30f);
        static readonly Color PillarTint = new Color(0.34f, 0.30f, 0.24f);
        static readonly Color BallTint  = new Color(0.72f, 0.42f, 0.20f);   // 暖色：蓝绿雾里最容易看出被吸收掉

        // ------------------------------------------------------------------
        [MenuItem("Tools/Underwater/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            if (!PrepareForNewScene()) return;

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();

            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(active, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[UnderwaterSceneBuilder] 演示场景已生成：{ScenePath}");
        }

        [MenuItem("Tools/Underwater/Build In Current Scene")]
        public static void BuildInCurrentScene()
        {
            Build();
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        /// <summary>
        /// 【不要】用 SaveCurrentModifiedScenesIfUserWantsTo()：从脚本 / 自动化调用会弹模态框，
        /// 没人点就把主线程堵死（体积光演示场景构建器踩过同样的坑）。
        /// </summary>
        static bool PrepareForNewScene()
        {
            if (EditorSceneManager.SaveOpenScenes())
                return true;

            Debug.LogError("[UnderwaterSceneBuilder] 有未保存且没有文件路径的场景，" +
                           "无法静默保存，已中止。请先手动保存或另存该场景后再生成演示场景。");
            return false;
        }

        /// <summary>
        /// 演示球排的（标签, 世界坐标）。BuildDepthRow 与 Dump State 共用同一个算法，
        /// 两边各自算一遍很容易算歪。
        /// </summary>
        public static System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Vector3>> RowPositions()
        {
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Vector3>>();
            for (int i = 0; i < RowZ.Length; i++)
                list.Add(new System.Collections.Generic.KeyValuePair<string, Vector3>($"z{RowZ[i]:F0}", BallPosition(i)));
            return list;
        }

        static Vector3 BallPosition(int index) => new Vector3(-6f + index * 3f, FloorY + 0.6f, RowZ[index]);

        /// <summary>诊断用：把决定效果能不能出来的几个前提全打出来。</summary>
        [MenuItem("Tools/Underwater/Dump State")]
        public static void DumpState()
        {
            var rendererData = UnderwaterSetup.FindActiveRenderer();
            bool hasFeature = false;
            foreach (var f in rendererData == null ? (System.Collections.Generic.IList<ScriptableRendererFeature>)System.Array.Empty<ScriptableRendererFeature>() : rendererData.rendererFeatures)
                if (f is UnderwaterPostProcessFeature) hasFeature = true;

            var cam = Camera.main;
            var extra = cam != null ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            string camInfo = cam == null
                ? "找不到 MainCamera"
                : $"eye={(cam.transform.position.y):F2} post={extra?.renderPostProcessing.ToString() ?? "?"} " +
                  $"depth={extra?.requiresDepthTexture.ToString() ?? "?"} " +
                  $"opaque={extra?.requiresColorTexture.ToString() ?? "?"} far={cam.farClipPlane:F0}";

            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            string pipeInfo = pipeline == null
                ? "管线资产 = null（不是 URP？）"
                : $"管线={pipeline.name} 深度图={pipeline.supportsCameraDepthTexture} 不透明图={pipeline.supportsCameraOpaqueTexture}";

            var uw = VolumeManager.instance.stack.GetComponent<UnderwaterPostProcess>();
            string volumeInfo = uw == null
                ? "Volume 栈里没有 UnderwaterPostProcess（Zone 没覆盖到相机，或 profile 里没有覆盖项）"
                : $"intensity={uw.intensity.value:F3} fog={uw.fogDensity.value:F3} " +
                  $"absorb={uw.absorptionRate.value:F3}×({uw.absorptionColor.value.r:F2},{uw.absorptionColor.value.g:F2},{uw.absorptionColor.value.b:F2}) " +
                  $"tint={uw.tintStrength.value:F2} surfaceY={uw.waterSurfaceY.value:F1} " +
                  $"caustics={uw.causticsIntensity.value:F2} wave={uw.waveStrength.value:F4} " +
                  $"debug={uw.debug.value} 激活={uw.IsActive()}";

            var shader = Shader.Find(ShaderName);

            // 相机到那一排球的距离 —— 用来核对"距离梯度"是不是真的存在
            string rowInfo = "";
            if (cam != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var kv in RowPositions())
                    sb.Append($" {kv.Key}={Vector3.Distance(cam.transform.position, kv.Value):F1}m");
                rowInfo = $"\n  球排距离（同材质）：{sb}";
            }

            Debug.Log($"[水下后期诊断] 渲染器={rendererData?.name ?? "null"} feature={(hasFeature ? "已安装" : "缺失！")}\n"
                      + $"  相机：{camInfo}\n  {pipeInfo}\n  Volume：{volumeInfo}\n"
                      + $"  shader={(shader == null ? "找不到 " + ShaderName : $"已找到 pass={shader.passCount}")}"
                      + rowInfo);
        }

        // ------------------------------------------------------------------
        static void Build()
        {
            Clean();
            EnsureFolders();

            // 一键生成出来的场景必须直接能看到效果：Feature 得在渲染器上
            UnderwaterSetup.EnsureFeature(out _);

            var root = new GameObject(RootName);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
            RenderSettings.reflectionIntensity = 0.1f;
            // 我们自己做水下雾，URP 的距离雾会把近处的效果一起洗淡
            RenderSettings.fog = false;

            var sand   = GetOrCreateMaterial(MatDir + "/UW_Sand.mat",   m => TuneLit(m, SandTint, 0.05f));
            var rock   = GetOrCreateMaterial(MatDir + "/UW_Rock.mat",   m => TuneLit(m, RockTint, 0.03f));
            var pillar = GetOrCreateMaterial(MatDir + "/UW_Pillar.mat", m => TuneLit(m, PillarTint, 0.04f));
            var ball   = GetOrCreateMaterial(MatDir + "/UW_Ball.mat",   m => TuneLit(m, BallTint, 0.25f));

            BuildTank(root.transform, sand, rock);
            BuildPillars(root.transform, pillar);
            BuildDepthRow(root.transform, ball);
            BuildWaterSurface(root.transform);
            BuildLighting(root.transform);
            BuildCamera(root.transform);
            BuildZone(root.transform);

            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
        }

        // ------------------------------------------------------------------
        //  调试视图（排查时不用去 Volume 面板里翻）
        // ------------------------------------------------------------------
        [MenuItem("Tools/Underwater/Debug/Off", priority = 100)]
        static void DebugOff() => SetDebugView(UnderwaterDebug.Off);

        [MenuItem("Tools/Underwater/Debug/Depth", priority = 101)]
        static void DebugDepth() => SetDebugView(UnderwaterDebug.Depth);

        [MenuItem("Tools/Underwater/Debug/Fog", priority = 102)]
        static void DebugFog() => SetDebugView(UnderwaterDebug.Fog);

        [MenuItem("Tools/Underwater/Debug/Caustics", priority = 103)]
        static void DebugCaustics() => SetDebugView(UnderwaterDebug.Caustics);

        [MenuItem("Tools/Underwater/Debug/Absorption", priority = 104)]
        static void DebugAbsorption() => SetDebugView(UnderwaterDebug.Absorption);

        [MenuItem("Tools/Underwater/Debug/Sky Mask", priority = 105)]
        static void DebugSkyMask() => SetDebugView(UnderwaterDebug.SkyMask);

        /// <summary>
        /// Sky Mask 是第一个该看的：整屏白 = 深度图根本没进这个通道，
        /// 水下效果会静默地什么都不画（本功能以前就是这个状态）。
        /// Fog 是第二个：必须有从近到远的灰度梯度，没梯度就是平涂。
        /// </summary>
        static void SetDebugView(UnderwaterDebug mode)
        {
            UnderwaterPostProcess uw = null;
            foreach (var volume in Object.FindObjectsByType<Volume>())
            {
                if (volume.sharedProfile == null) continue;
                if (volume.sharedProfile.TryGet<UnderwaterPostProcess>(out var found))
                {
                    uw = found;
                    break;
                }
            }

            if (uw == null)
            {
                Debug.LogWarning("[Underwater] 当前场景里没找到带 UnderwaterPostProcess 覆盖项的 Volume。");
                return;
            }

            uw.debug.Override(mode);
            EditorUtility.SetDirty(uw);
            AssetDatabase.SaveAssets();

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            UnityEditor.SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();

            string hint = mode == UnderwaterDebug.SkyMask
                ? "  —— 整屏白 = 深度图没接上，水下效果根本不会画"
                : mode == UnderwaterDebug.Fog
                    ? "  —— 必须有从近到远的灰度梯度，否则就是平涂"
                    : string.Empty;
            Debug.Log($"[Underwater] 调试视图 → {mode}{hint}");
        }

        static void BuildTank(Transform parent, Material sand, Material rock)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(0f, FloorY, (FrontZ + BackWallZ) * 0.5f);
            floor.transform.localScale = new Vector3(6f, 1f, 6f);   // Plane 原始 10×10
            floor.GetComponent<MeshRenderer>().sharedMaterial = sand;

            // 墙要高过“射线能跑出去”的范围：否则画面上半屏会露出一圈
            // 背景空洞（看起来像水下有个黑洞），干扰对雾的判断。
            MakeSlab(parent, "Back Wall", rock,
                new Vector3(0f, FloorY + 8f, BackWallZ + 0.5f),
                new Vector3(TankHalfX * 2f, 16f, 1f));
            MakeSlab(parent, "Side Wall L", rock,
                new Vector3(-TankHalfX - 0.5f, FloorY + 8f, (FrontZ + BackWallZ) * 0.5f),
                new Vector3(1f, 16f, BackWallZ - FrontZ));
            MakeSlab(parent, "Side Wall R", rock,
                new Vector3(TankHalfX + 0.5f, FloorY + 8f, (FrontZ + BackWallZ) * 0.5f),
                new Vector3(1f, 16f, BackWallZ - FrontZ));
        }

        /// <summary>成对的柱子：左右各一排，按距离排开，用来读“距离越远越糊”。</summary>
        static void BuildPillars(Transform parent, Material mat)
        {
            foreach (var z in RowZ)
            {
                MakeCylinder(parent, $"Pillar L z{z:F0}", mat, new Vector3(-11f, FloorY, z), 1.1f, 6.5f);
                MakeCylinder(parent, $"Pillar R z{z:F0}", mat, new Vector3(11f, FloorY, z), 1.1f, 6.5f);
            }
        }

        /// <summary>
        /// 同材质、只有距离不同的球。X 也一起摄开 —— 五个球如果在同一根视线上，
        /// 近的就挡了远的，验证“雾跟深度有关”根本测不了。
        /// </summary>
        static void BuildDepthRow(Transform parent, Material mat)
        {
            for (int i = 0; i < RowZ.Length; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = $"Ball z{RowZ[i]:F0}";
                s.transform.SetParent(parent, false);
                s.transform.localPosition = BallPosition(i);
                s.transform.localScale = Vector3.one * 1.2f;
                s.GetComponent<MeshRenderer>().sharedMaterial = mat;
                Object.DestroyImmediate(s.GetComponent<Collider>());
            }

            // 一块白底参照板在最后一排球后面：给“最远处应该基本只剩雾色”一个参照
            MakeSlab(parent, "Far Reference", mat,
                new Vector3(0f, FloorY + 1.2f, RowZ[RowZ.Length - 1] + 3f),
                new Vector3(2.4f, 2.4f, 0.4f));
        }

        static void BuildWaterSurface(Transform parent)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
            if (mat == null)
            {
                Debug.LogWarning($"[UnderwaterSceneBuilder] 找不到 {WaterMatPath}，水面留空。");
                return;
            }

            var go = new GameObject("Water Surface");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, SurfaceY, (FrontZ + BackWallZ) * 0.5f);

            var filter = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            // 网格必须比水池还大：边缘一旦落进画面，从水下看就是一条锯齿状的
            // "水面断面"，非常出戏。
            filter.sharedMesh = BuildGridMesh(64f, 88);
            renderer.sharedMaterial = mat;

            // 水面不能挡射线：它是给眼睛看的，不是给物理用的
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = filter.sharedMesh;
        }

        /// <summary>在世界单位下生成 XZ 网格（不缩放），供水面 shader 的顶点位移使用。</summary>
        static Mesh BuildGridMesh(float size, int segments)
        {
            int res = segments + 1;
            var vertices = new Vector3[res * res];
            var normals = new Vector3[res * res];
            var uvs = new Vector2[res * res];
            var triangles = new int[segments * segments * 6];

            float half = size * 0.5f;
            float step = size / segments;

            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int i = z * res + x;
                vertices[i] = new Vector3(-half + x * step, 0f, -half + z * step);
                normals[i] = Vector3.up;
                uvs[i] = new Vector2((float)x / segments, (float)z / segments);
            }

            int t = 0;
            for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                int i0 = z * res + x, i1 = i0 + 1, i2 = i0 + res, i3 = i2 + 1;
                triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
            }

            var mesh = new Mesh { name = "UnderwaterDemo_Water" };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
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
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.05f, 0.07f);

            var extra = go.AddComponent<UniversalAdditionalCameraData>();
            extra.renderPostProcessing = true;
            // 水下通道要读深度图与不透明图，显式打开（管线资产上也开着，双保险）
            // 注意：相机上的这个开关叫 requiresColorTexture（URP 内部才改名 requiresOpaqueTexture）
            extra.requiresDepthTexture = true;
            extra.requiresColorTexture = true;
        }

        /// <summary>
        /// 局部 Volume 区域：BoxCollider 盖住水面以下，相机进去才生效 ——
        /// 这正是水下后处理的使用方式（Volume 系统自己按 blendDistance 淡入淡出）。
        /// </summary>
        static void BuildZone(Transform parent)
        {
            var go = new GameObject("Underwater Zone");
            go.transform.SetParent(parent, false);

            float depth = 6.6f;
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.center = new Vector3(0f, SurfaceY - depth * 0.5f, (FrontZ + BackWallZ) * 0.5f);
            collider.size = new Vector3(TankHalfX * 2f, depth, BackWallZ - FrontZ);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // 生成物：每次重跑都把覆盖项清干净再按代码里的值填，保证"改常量重跑"结果一致
            foreach (var comp in profile.components.ToArray())
                if (comp != null) Object.DestroyImmediate(comp, true);
            profile.components.Clear();
            PopulateProfile(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.priority = 10f;
            volume.blendDistance = 2.5f;
            volume.sharedProfile = profile;
        }

        static void PopulateProfile(VolumeProfile profile)
        {
            var uw = AddComponent<UnderwaterPostProcess>(profile);
            UnderwaterSetup.ApplyZoneDefaults(uw);

            // 演示场景比“随便一个 Zone”稍微重一点：焦散要给足，
            // 否则静态截图里看不出“亮带贴在表面上”这个卖点。
            uw.causticsIntensity.Override(0.45f);   // 0.8 的时候地板像结了张蜘蛛网
            uw.fogDensity.Override(0.06f);
            uw.tintStrength.Override(0.9f);
            uw.waveStrength.Override(0.005f);       // 0.009 会在物体边缘留下明显的重影
            uw.waveStrength.Override(0.009f);
            uw.waterSurfaceY.Override(SurfaceY);
        }

        /// <summary>
        /// 把覆盖项加进 profile 并【写成子资产】。
        ///
        /// 必须用 VolumeProfileFactory：profile.Add&lt;T&gt;() 只建一个内存实例，它不在任何
        /// 资产里，序列化时变成 fileID: 0 —— 当前会话看着是好的，场景一重开覆盖项全没。
        /// 包里的 Data/Underwater/UnderwaterZoneProfile.asset 就是这么变成空 profile 的。
        /// </summary>
        static T AddComponent<T>(VolumeProfile profile) where T : VolumeComponent
        {
            return VolumeProfileFactory.CreateVolumeComponent<T>(profile, overrides: true, saveAsset: false);
        }

        // ------------------------------------------------------------------
        static void MakeSlab(Transform parent, string name, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void MakeCylinder(Transform parent, string name, Material mat, Vector3 basePos, float radius, float height)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = basePos + new Vector3(0f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(radius, height * 0.5f, radius);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
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
                tune(mat);
                EditorUtility.SetDirty(mat);
                return mat;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogError($"[UnderwaterSceneBuilder] 找不到 URP/Lit，材质 {path} 不可用");
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
            CreateFolder("Assets/CartoonRendering", "Underwater");
            CreateFolder("Assets/CartoonRendering/Underwater", "Materials");
            CreateFolder("Assets", "Scenes");
        }

        static void CreateFolder(string parent, string name)
        {
            string full = name == null ? parent : parent + "/" + name;
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

            foreach (var cam in Object.FindObjectsByType<Camera>())
            {
                if (cam.transform.parent == null && cam.name == "Main Camera")
                    Object.DestroyImmediate(cam.gameObject);
            }
        }
    }
}
