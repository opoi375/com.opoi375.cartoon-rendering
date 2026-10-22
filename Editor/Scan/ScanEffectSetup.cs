// Copyright (c) 2026 CartoonRendering. MIT License.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace CartoonRendering.EditorTools
{
    /// <summary>
    /// 一键接入扫描特效：把 Feature 加进当前渲染器、在 Volume 上补一个 ScanEffect
    /// 组件、必要时补一个控制器。
    ///
    /// 为什么要有这个菜单：URP 的 Renderer Feature 是资产里的一个子对象，手工加
    /// 容易漏（尤其是 m_RendererFeatureMap 那句），漏了就是"什么都没发生而且不报错"。
    /// </summary>
    public static class ScanEffectSetup
    {
        private const string kFeatureMenu = "Tools/Scan/Setup Scan Effect";
        private const string kControllerMenu = "Tools/Scan/Add Controller To Scene";
        private const string kToggleMenu = "Tools/Scan/Toggle Effect In Volume";

        // ------------------------------------------------------------------

        [MenuItem(kFeatureMenu, priority = 10)]
        public static void SetupScanEffect()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null)
            {
                EditorUtility.DisplayDialog("Scan Effect",
                    "当前渲染管线不是 URP，无法接入扫描特效。", "好");
                return;
            }

            var renderer = urp.scriptableRenderer as UniversalRenderer;
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("Scan Effect",
                    "拿不到 UniversalRenderer，请在 Graphics Settings 里确认管线资产。", "好");
                return;
            }

            var so = new SerializedObject(urp);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null || list.arraySize == 0)
            {
                EditorUtility.DisplayDialog("Scan Effect", "管线资产里没有 Renderer Data。", "好");
                return;
            }

            int changed = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is not ScriptableRendererData data)
                    continue;

                if (EnsureFeature(data))
                    changed++;

                // 【必须无条件刷新】之前只在"新增"分支里刷，导致 Feature 已经在
                // 列表里但 map 是旧的 —— 再跑一次菜单也修不好，运行时依旧不生效。
                RefreshRendererFeatureMap(data);
                EditorUtility.SetDirty(data);
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();

            int volumes = EnsureVolumeComponents();

            // 【必须】把刚补的 Volume 组件落盘。上面那次 SaveAssets 发生在这之前，
            // 所以 AddObjectToAsset 出来的组件只在内存里 —— 重开编辑器就没了，
            // YAML 里也搜不到（实证：components[7] 查得到、文件里却没有）。
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ScanEffect] 已接入 {changed} 个 Renderer Data，补了 {volumes} 个 Volume 组件。" +
                      "按 F5 触发（或勾上控制器上的 Auto Demo）。");
        }

        // ------------------------------------------------------------------

        static void RefreshRendererFeatureMap(ScriptableRendererData data)
        {
            const System.Reflection.BindingFlags kFlags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public;

            var t = typeof(ScriptableRendererData);
            var validate = t.GetMethod("ValidateRendererFeatures", kFlags);
            if (validate != null && validate.ReturnType == typeof(bool))
            {
                validate.Invoke(data, null);
                return;
            }

            var updateMap = t.GetMethod("UpdateMap", kFlags);
            if (updateMap != null)
            {
                updateMap.Invoke(data, null);
                return;
            }

            Debug.LogWarning("[ScanEffect] 没能刷新 Renderer Feature Map（URP 内部 API 变了）。" +
                             "请到 Renderer 资产的 Inspector 上手动改一下任意 Feature 以触发刷新。");
        }

        static bool EnsureFeature(ScriptableRendererData data)
        {
            foreach (var existing in data.rendererFeatures)
            {
                if (existing is ScanEffectFeature)
                    return false;
            }

            var feature = ScriptableObject.CreateInstance<ScanEffectFeature>();
            feature.name = "Scan Effect";

            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            data.rendererFeatures.Add(feature);

            // m_RendererFeatureMap 是 URP 用来按 localId 索引子资产的那本账
            // （ScriptableRendererData.runtime.cs 里检查 mapValid =
            //  map.Count == features.Count）。只往列表里塞、不刷新这本账，
            // 运行时就是"Feature 在列表里但根本没跑"，而且不报错 —— 实测就是这样。
            // 刷新逻辑在 private 的 UpdateMap() 里，由 internal 的
            // ValidateRendererFeatures() 调用，只能反射够到。
            data.SetDirty();

            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ScanEffect] 已把 Scan Effect Feature 加到 {data.name}");
            return true;
        }

        static int EnsureVolumeComponents()
        {
            int count = 0;

            // 1) 场景里的 Volume（优先 Global）
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
            foreach (var vol in volumes)
            {
                if (vol.sharedProfile == null)
                    continue;
                if (EnsureProfileHasScan(vol.sharedProfile))
                {
                    EditorUtility.SetDirty(vol.sharedProfile);
                    EditorSceneManager.MarkSceneDirty(vol.gameObject.scene);
                    count++;
                }
            }

            // 0) 场景完全没有 Volume 时先建一个（否则后面补组件也白搭）
            count += EnsureSceneHasGlobalVolume();

            // 2) 工程里的 Volume Profile 资产（场景里没有 Volume 时也能先配好）
            if (count == 0)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:VolumeProfile"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.StartsWith("Assets/"))
                        continue;
                    if (!path.ToLowerInvariant().Contains("cartoon"))
                        continue;

                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
                    if (profile != null && EnsureProfileHasScan(profile))
                    {
                        EditorUtility.SetDirty(profile);
                        count++;
                    }
                }
                AssetDatabase.SaveAssets();
            }

            return count;
        }

        /// <summary>
        /// 往 profile 里补 ScanEffect。
        ///
        /// 注意不能用 profile.Add&lt;ScanEffect&gt;() 然后只 SetDirty —— 那样序列化
        /// 出来会是 fileID: 0 的空组件，运行时 GetComponent 拿到 null，效果静默失效
        /// （本包水下后期踩过这个坑）。这里显式 CreateInstance + AddObjectToAsset。
        /// </summary>
        public static bool EnsureProfileHasScan(VolumeProfile profile)
        {
            if (profile.TryGet<ScanEffect>(out _))
                return false;

            var comp = ScriptableObject.CreateInstance<ScanEffect>();
            comp.name = "Scan Effect";
            comp.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;

            AssetDatabase.AddObjectToAsset(comp, profile);
            profile.components.Add(comp);
            EditorUtility.SetDirty(profile);
            return true;
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// 场景里根本没有 Volume 时，建一个 Global Volume + 带 ScanEffect 的 profile。
        ///
        /// 为什么必须有：只有 Feature、没有 Volume，运行时
        /// VolumeManager.stack.GetComponent&lt;ScanEffect&gt;() 就是 null，通道永远不入图，
        /// 屏幕上什么迹象都没有 —— 这是本轮排查耗时最长的一处。
        /// 场景里已经有 Volume 就不动它，只在它的 profile 上补组件。
        /// </summary>
        static int EnsureSceneHasGlobalVolume()
        {
            if (Object.FindObjectsByType<Volume>(FindObjectsInactive.Include).Length > 0)
                return 0;

            const string folder = "Assets/CartoonRendering/Scan";
            const string profilePath = folder + "/ScanGlobalVolumeProfile.asset";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                if (!AssetDatabase.IsValidFolder("Assets/CartoonRendering"))
                    AssetDatabase.CreateFolder("Assets", "CartoonRendering");
                AssetDatabase.CreateFolder("Assets/CartoonRendering", "Scan");
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, profilePath);
            }
            EnsureProfileHasScan(profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Global Volume");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 0f;
            vol.sharedProfile = profile;
            Undo.RegisterCreatedObjectUndo(go, "Create Global Volume");
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[ScanEffect] 场景里没有 Volume，已新建 Global Volume 并挂上 {profilePath}");
            return 1;
        }

        [MenuItem("Tools/Scan/Setup Scan Effect", true)]
        static bool ValidateSetup() => !EditorApplication.isPlaying;

        [MenuItem(kControllerMenu, priority = 11)]
        public static void AddControllerToScene()
        {
            var existing = Object.FindAnyObjectByType<ScanEffectController>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ScanEffect] 场景里已经有 ScanEffectController，已选中它。");
                return;
            }

            var go = new GameObject("ScanEffectController");
            var ctrl = go.AddComponent<ScanEffectController>();

            // 默认用主相机的朝向，自动演示打开，进来就能看到效果
            ctrl.centerMode = ScanCenter.Camera;
            if (Camera.main != null)
                ctrl.centerTarget = Camera.main.transform;
            ctrl.autoDemo = true;

            Undo.RegisterCreatedObjectUndo(go, "Add ScanEffectController");
            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log("[ScanEffect] 已创建 ScanEffectController（Auto Demo 打开，" +
                      "按 F5 手动触发 / F6 开关）。");
        }

        [MenuItem(kToggleMenu, priority = 12)]
        public static void ToggleInVolume()
        {
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include);
            int toggled = 0;
            bool nowOn = false;

            foreach (var vol in volumes)
            {
                if (vol.sharedProfile == null)
                    continue;
                if (!vol.sharedProfile.TryGet<ScanEffect>(out var scan))
                    continue;

                scan.enabled.value = !scan.enabled.value;
                nowOn = scan.enabled.value;
                EditorUtility.SetDirty(vol.sharedProfile);
                EditorSceneManager.MarkSceneDirty(vol.gameObject.scene);
                toggled++;
            }

            if (toggled == 0)
                Debug.Log("[ScanEffect] 没有找到带 Scan Effect 的 Volume，先执行 " + kFeatureMenu);
            else
                Debug.Log($"[ScanEffect] 已{(nowOn ? "打开" : "关闭")}扫描特效（{toggled} 个 Volume）。" +
                          (nowOn ? "" : "关闭后该通道完全不进渲染图。"));
        }

        // ------------------------------------------------------------------

        [MenuItem(kControllerMenu, validate = true)]
        static bool ValidateNotPlaying() => !EditorApplication.isPlaying;
    }
}
