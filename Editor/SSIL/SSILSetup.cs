// ============================================================================
// SSILSetup.cs
// ----------------------------------------------------------------------------
// 一键把 SSILFeature 写进当前激活的 URP 渲染器。
//
// 渲染器是从 GraphicsSettings.currentRenderPipeline 上找的，不写死资产路径 ——
// 这样包放进任何工程都能用。

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor
{
    public static class SSILSetup
    {
        [MenuItem("Tools/SSIL/Setup In Renderer")]
        public static void Setup()
        {
            if (EnsureFeature(out var rendererData))
            {
                Selection.activeObject = rendererData;
                EditorGUIUtility.PingObject(rendererData);
                Debug.Log($"[SSIL] 已把 Screen Space Indirect Light 加进渲染器：{rendererData.name}\n" +
                          "再建一个 Global Volume 挂上 Screen Space Indirect Light 组件，把 Intensity 调到 1 即可看到效果。");
            }
        }

        /// <summary>
        /// 确保当前渲染器上有 SSIL Feature。返回 true 表示这次真的加了。
        /// </summary>
        public static bool EnsureFeature(out UniversalRendererData rendererData)
        {
            rendererData = VolumetricLightSetup.FindActiveRenderer();
            if (rendererData == null)
            {
                Debug.LogWarning("[SSIL] 找不到激活的 URP 渲染器（GraphicsSettings.currentRenderPipeline）。" +
                                 "请在 Renderer Features 里手动 Add \"Screen Space Indirect Light\"。");
                return false;
            }

            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing is SSILFeature)
                    return false;      // 已经有了
            }

            var feature = ScriptableObject.CreateInstance<SSILFeature>();
            feature.name = "SSILFeature";
            feature.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            feature.resolution = SSILFeature.ResolutionDivisor.Half;

            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
