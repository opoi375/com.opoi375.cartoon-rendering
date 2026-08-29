// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// One-click setup for the underwater effect using a LOCAL Volume zone:
    ///   - adds the UnderwaterPostProcessFeature to the CartoonRP renderer asset
    ///   - creates an "Underwater Zone" GameObject: BoxCollider + Volume
    ///     (isGlobal = false, blendDistance for a smooth edge) whose profile
    ///     carries the UnderwaterPostProcess override at full intensity
    ///   - cleans up the obsolete UnderwaterMode component if present
    ///
    /// The camera fades the effect in while inside the collider and out when
    /// it leaves (the Volume system computes the blend from the collider).
    /// Move/resize the zone to cover the water body.
    /// </summary>
    public static class UnderwaterSetup
    {
        private const string RendererAssetPath = "Assets/Settings/CartoonRP_Renderer.asset";
        private const string ProfileDir = "Assets/CartoonRendering/Data/Underwater";
        private const string ProfilePath = ProfileDir + "/UnderwaterZoneProfile.asset";

        [MenuItem("Tools/Underwater/Setup Underwater Zone")]
        public static void Setup()
        {
            // 1. Renderer feature.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
            if (rendererData != null && !HasFeature(rendererData))
            {
                var feature = ScriptableObject.CreateInstance<UnderwaterPostProcessFeature>();
                feature.name = "UnderwaterPostProcessFeature";
                rendererData.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, rendererData);
                EditorUtility.SetDirty(rendererData);
                AssetDatabase.SaveAssets();
            }

            // 2. Cleanup: remove the obsolete waterline trigger if still present
            //    (its class was deleted, so look it up by name).
            var camera = Camera.main;
            if (camera != null)
            {
                var obsolete = camera.GetComponent("UnderwaterMode");
                if (obsolete != null)
                {
                    Object.DestroyImmediate(obsolete);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                }
            }

            // 3. Volume profile asset (reused if it already exists).
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                if (!AssetDatabase.IsValidFolder(ProfileDir))
                    AssetDatabase.CreateFolder("Assets/CartoonRendering/Data", "Underwater");
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            // Reset the underwater override to the working defaults (tunable
            // afterwards in the Volume panel).
            var underwater = profile.TryGet<UnderwaterPostProcess>(out var existing)
                ? existing
                : profile.Add<UnderwaterPostProcess>(overrides: true);
            underwater.intensity.value = 1f;
            underwater.tintStrength.value = 0.7f;
            underwater.color.value = new Color(0.08f, 0.42f, 0.55f, 1f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 4. Zone GameObject (BoxCollider + local Volume).
            var go = new GameObject("Underwater Zone");
            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(20f, 8f, 20f);

            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.blendDistance = 3f;
            volume.profile = profile;

            Selection.activeGameObject = go;
            Debug.Log("[Underwater] Zone created. Move/resize it over the water and " +
                      "tune the effect in the Volume profile: " + ProfilePath);
        }

        private static bool HasFeature(UniversalRendererData rendererData)
        {
            foreach (var existing in rendererData.rendererFeatures)
            {
                if (existing is UnderwaterPostProcessFeature)
                    return true;
            }
            return false;
        }
    }
}
