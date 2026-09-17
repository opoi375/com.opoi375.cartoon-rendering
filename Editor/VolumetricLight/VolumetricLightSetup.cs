// ============================================================================
// VolumetricLightSetup.cs
// ----------------------------------------------------------------------------
// 一键把 VolumetricLightFeature 写进当前激活的 URP 渲染器。
//
// 渲染器是从 GraphicsSettings.currentRenderPipeline 上找的，不写死资产路径 ——
// 这样包放进任何工程都能用。

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering.Editor
{
    public static class VolumetricLightSetup
    {
        [MenuItem("Tools/Volumetric Light/Setup In Renderer")]
        public static void Setup()
        {
            if (EnsureFeature(out var rendererData))
            {
                Selection.activeObject = rendererData;
                EditorGUIUtility.PingObject(rendererData);
                Debug.Log($"[VolumetricLight] 已把 Volumetric Light 加进渲染器：{rendererData.name}");
            }
        }

        /// <summary>
        /// 确保当前渲染器上有体积光 Feature。返回 true 表示这次真的加了。
        /// 演示场景构建器也调它，保证一键生成出来的场景能直接看到效果。
        /// </summary>
        public static bool EnsureFeature(out UniversalRendererData rendererData)
        {
            rendererData = FindActiveRenderer();
            if (rendererData == null)
            {
                Debug.LogWarning("[VolumetricLight] 找不到激活的 URP 渲染器（GraphicsSettings.currentRenderPipeline）。" +
                                 "请在 Renderer Features 里手动 Add \"Volumetric Light\"。");
                return false;
            }

            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing is VolumetricLightFeature)
                    return false;      // 已经有了
            }

            var feature = ScriptableObject.CreateInstance<VolumetricLightFeature>();
            feature.name = "VolumetricLightFeature";
            feature.renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            feature.resolution = VolumetricLightFeature.ResolutionDivisor.Half;

            rendererData.rendererFeatures.Add(feature);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssets();
            return true;
        }

        /// <summary>从当前管线资产上取第 0 个渲染器。</summary>
        public static UniversalRendererData FindActiveRenderer()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (pipeline == null)
                pipeline = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;

            if (pipeline != null)
            {
                var list = pipeline.rendererDataList;
                for (int i = 0; i < list.Length; i++)
                {
                    if (list[i] is UniversalRendererData data)
                        return data;
                }
            }

            // 兜底：直接扫一遍工程里的渲染器数据资产
            var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path) is { } data)
                    return data;
            }
            return null;
        }
    }
}
