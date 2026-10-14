// ============================================================================
// UnderwaterSetup.cs
// ----------------------------------------------------------------------------
// 一键把水下后处理装进当前激活的 URP 渲染器，并在水池里放一个局部 Volume 区域。
//
// 渲染器从 GraphicsSettings.currentRenderPipeline 上找，不写死资产路径 ——
// 这样包放进任何工程都能用（原来写死了 Assets/Settings/CartoonRP_Renderer.asset，
// 换工程就点不出来）。
//
// profile 里的覆盖项必须用 VolumeProfileFactory 写成子资产：原来的
// profile.Add<UnderwaterPostProcess>() 只建内存实例，序列化出来是 fileID: 0，
// 于是包里发出去的 Data/Underwater/UnderwaterZoneProfile.asset 其实是个【空】
// profile —— 场景重开以后 Zone 里什么都没有，效果根本不会激活。
// ============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// One-click setup for the underwater post process:
    ///   - installs <see cref="UnderwaterPostProcessFeature"/> on the active URP renderer
    ///   - creates an "Underwater Zone" GameObject (trigger BoxCollider + local Volume)
    ///     whose profile carries the override at full intensity
    ///   - removes the obsolete waterline trigger component if still present
    ///
    /// Move/resize the zone so it fills the water body: the camera fades the
    /// effect in while inside the collider and out when it leaves.
    /// </summary>
    public static class UnderwaterSetup
    {
        private const string ProfileDir = "Packages/com.opoi375.cartoon-rendering/Data/Underwater";
        private const string ProfilePath = ProfileDir + "/UnderwaterZoneProfile.asset";

        [MenuItem("Tools/Underwater/Setup Underwater Post Process")]
        public static void Setup()
        {
            if (!EnsureFeature(out var rendererData))
                Debug.Log($"[Underwater] 渲染器上已有水下 Feature：{rendererData?.name}");

            CleanupObsoleteWaterline();
            EnsureProfile();
            CreateZone();

            Debug.Log("[Underwater] 已安装 Feature 并创建 Underwater Zone。" +
                      "把 Zone 的移动/缩放到水池范围，参数在 Volume 面板里调：" + ProfilePath);
        }

        /// <summary>
        /// 确保当前渲染器上有水下 Feature。返回 true 表示这次真的加了。
        /// 演示场景构建器也调它，保证一键生成出来的场景能直接看到效果。
        /// </summary>
        public static bool EnsureFeature(out UniversalRendererData rendererData)
        {
            rendererData = FindActiveRenderer();
            if (rendererData == null)
            {
                Debug.LogWarning("[Underwater] 找不到激活的 URP 渲染器" +
                                 "（GraphicsSettings.currentRenderPipeline）。" +
                                 "请在 Renderer Features 里手动 Add \"Underwater Post Process\"。");
                return false;
            }

            foreach (var existing in rendererData.rendererFeatures)
                if (existing is UnderwaterPostProcessFeature)
                    return false;      // 已经有了

            var feature = ScriptableObject.CreateInstance<UnderwaterPostProcessFeature>();
            feature.name = "UnderwaterPostProcessFeature";
            // 注入点：透明之后、后处理之前。水面本身是透明物体，只有跑到它后面
            // 才可能被雾盖住；而 Feature.Create() 当年把事件点硬写成
            // BeforeRenderingTransparents，Inspector 里改了也没用。
            feature.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>从当前管线资产上取第一个通用渲染器，兜底扫工程。</summary>
        public static UniversalRendererData FindActiveRenderer()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
                pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

            if (pipeline != null)
            {
                var list = pipeline.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                    if (list[i] is UniversalRendererData data)
                        return data;
            }

            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path) is { } data)
                    return data;
            }
            return null;
        }

        /// <summary>
        /// 拿到（或建出）包里的 Zone profile 资产，并把覆盖项写成持久化的子资产。
        /// 每次都按代码里的默认值重置，这样"改代码重跑"的结果是确定的。
        /// </summary>
        public static VolumeProfile EnsureProfile()
        {
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder(ProfileDir))
                    AssetDatabase.CreateFolder(
                        "Packages/com.opoi375.cartoon-rendering/Data", "Underwater");
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // 清掉旧的（包括历史上被写成 fileID: 0 的空引用）
            foreach (var comp in profile.components.ToArray())
                if (comp != null) Object.DestroyImmediate(comp, true);
            profile.components.Clear();

            PopulateProfile(profile);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        /// <summary>往 profile 里填水下覆盖项（公开给演示场景构建器复用）。</summary>
        public static void PopulateProfile(VolumeProfile profile)
        {
            var underwater = VolumeProfileFactory.CreateVolumeComponent<UnderwaterPostProcess>(
                profile, overrides: true, saveAsset: false);
            // 新建的覆盖项已经全部 overrideState = true，下面直接把值写定就行，
            // 不需要（也没有）“重置全部覆盖”的 API。
            ApplyZoneDefaults(underwater);
        }

        /// <summary>
        /// Zone 的推荐参数。颜色是 sRGB，进线性空间后会暗很多 ——
        /// 水下的雾本来就比场景暗，写太亮会变成一层奶白。
        /// 吸收与内散射是两个独立的项：前者把红先吃掉（场景自己往蓝绿偏），
        /// 后者把水体自己的颜色加回来。只调后者就是“平涂”。
        /// </summary>
        public static void ApplyZoneDefaults(UnderwaterPostProcess underwater)
        {
            underwater.intensity.Override(1f);
            underwater.color.Override(new Color(0.08f, 0.42f, 0.55f, 1f));
            underwater.deepColor.Override(new Color(0.012f, 0.075f, 0.12f, 1f));
            underwater.depthToFullColor.Override(9f);
            underwater.tintStrength.Override(0.85f);

            underwater.fogDensity.Override(0.05f);
            underwater.absorptionColor.Override(new Color(1f, 0.45f, 0.22f, 1f));
            underwater.absorptionRate.Override(0.075f);

            underwater.waterSurfaceY.Override(0f);
            underwater.causticsIntensity.Override(0.7f);
            underwater.causticsScale.Override(0.55f);
            underwater.causticsSpeed.Override(0.9f);
            underwater.causticsDepthFade.Override(12f);

            underwater.waveStrength.Override(0.007f);
            underwater.waveScale.Override(1f);
            underwater.waveSpeed.Override(1.2f);

            underwater.vignette.Override(0.35f);
            underwater.banding.Override(0f);
            underwater.debug.Override(UnderwaterDebug.Off);
        }

        static void CreateZone()
        {
            var profile = EnsureProfile();

            var go = new GameObject("Underwater Zone");
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(20f, 8f, 20f);
            collider.center = new Vector3(0f, -4f, 0f);   // 默认沉到水面以下

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.blendDistance = 3f;
            volume.sharedProfile = profile;

            Selection.activeGameObject = go;
            EditorGUIUtility.PingObject(go);
        }

        /// <summary>清理早期版本的水面触发器（类已删除，只能按名字找）。</summary>
        static void CleanupObsoleteWaterline()
        {
            var camera = Camera.main;
            if (camera == null) return;

            var obsolete = camera.GetComponent("UnderwaterMode");
            if (obsolete != null)
            {
                Object.DestroyImmediate(obsolete);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
            }
        }
    }
}
