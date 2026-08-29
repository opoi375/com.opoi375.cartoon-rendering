// Copyright (c) 2026 CartoonRendering. MIT License.

using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CartoonRendering.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="CartoonProceduralSkyUpdater"/>.
    /// Adds convenience buttons that:
    ///   - Locate (or create) an Updater in the active scene
    ///   - Locate the CartoonProceduralSky asset in the project
    ///   - Apply the sky asset + material to RenderSettings
    ///   - Show the live time-of-day derived from the main light
    /// </summary>
    [CustomEditor(typeof(CartoonProceduralSkyUpdater))]
    public sealed class CartoonProceduralSkyUpdaterEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var updater = (CartoonProceduralSkyUpdater)target;

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Live Preview", EditorStyles.boldLabel);

                Light light = updater.sunLight != null ? updater.sunLight : FindAnyObjectByType<Light>();
                if (light == null)
                {
                    EditorGUILayout.HelpBox(
                        "No directional light assigned and Light.main is null. " +
                        "Tag a directional light as the main light or assign one explicitly.",
                        MessageType.Warning);
                }
                else
                {
                    float timeOfDay = TimeOfDaySystem.GetTimeOfDayFromLight(light);
                    Vector3 sunDir  = TimeOfDaySystem.GetSunDirection(light);

                    EditorGUILayout.LabelField("Time of day",   timeOfDay.ToString("0.000"));
                    EditorGUILayout.LabelField("Sun direction", sunDir.ToString("0.000"));
                }

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Apply To RenderSettings Now"))
                    {
                        updater.ApplyImmediate();
                    }
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Helpers", EditorStyles.boldLabel);

                // ----------------------------------------------------------------
                // Auto-find Updater in scene. Per Spec: editor should auto-locate
                // the Updater so a user pressing "Apply to RenderSettings" can
                // work even if their inspector selection is unrelated.
                // ----------------------------------------------------------------
                if (GUILayout.Button("Find Updater In Active Scene"))
                {
                    var found = FindUpdaterInActiveScene();
                    if (found == null)
                    {
                        if (EditorUtility.DisplayDialog(
                                "No Updater Found",
                                "No CartoonProceduralSkyUpdater was found in the active scene. " +
                                "Create one in a new GameObject now?",
                                "Create", "Cancel"))
                        {
                            var go = new GameObject("CartoonProceduralSky");
                            Undo.RegisterCreatedObjectUndo(go, "Create Cartoon Sky Updater");
                            var newUpdater = Undo.AddComponent<CartoonProceduralSkyUpdater>(go);
                            Selection.activeGameObject = go;
                            EditorGUIUtility.PingObject(newUpdater);
                        }
                    }
                    else
                    {
                        Selection.activeGameObject = found.gameObject;
                        EditorGUIUtility.PingObject(found);
                    }
                }

                if (GUILayout.Button("Find Cartoon Sky Asset In Project"))
                {
                    CartoonProceduralSky asset = FindSkyAsset();

                    if (asset == null)
                    {
                        if (EditorUtility.DisplayDialog(
                                "Create Sky Asset",
                                "No CartoonProceduralSky asset was found in the project. " +
                                "Create one now?",
                                "Create", "Cancel"))
                        {
                            asset = CreateDefaultSkyAsset();
                            if (asset != null)
                            {
                                Undo.RecordObject(updater, "Assign Cartoon Sky Asset");
                                updater.skyAsset = asset;
                                EditorUtility.SetDirty(updater);
                                EditorGUIUtility.PingObject(asset);
                            }
                        }
                    }
                    else
                    {
                        Undo.RecordObject(updater, "Assign Cartoon Sky Asset");
                        updater.skyAsset = asset;
                        EditorUtility.SetDirty(updater);
                    }
                }

                if (GUILayout.Button("Find Cartoon Sky Material In Project"))
                {
                    Material mat = FindSkyMaterial();

                    if (mat == null)
                    {
                        if (EditorUtility.DisplayDialog(
                                "Create Sky Material",
                                "No Cartoon Skybox material was found in the project. " +
                                "Create one now (Assets/CartoonRendering/Materials/CartoonSky.mat)?",
                                "Create", "Cancel"))
                        {
                            mat = CreateDefaultSkyMaterial();
                        }
                    }

                    if (mat != null)
                    {
                        Undo.RecordObject(updater, "Assign Cartoon Sky Material");
                        updater.skyMaterial = mat;
                        EditorUtility.SetDirty(updater);
                        updater.ApplyImmediate();
                    }
                }
            }

            // Keep the inspector live.
            if (Application.isPlaying || updater.isActiveAndEnabled)
            {
                Repaint();
            }
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        private static CartoonProceduralSkyUpdater FindUpdaterInActiveScene()
        {
            // Singleton lookup first (cheapest).
            var instance = CartoonProceduralSkyUpdater.Instance;
            if (instance != null) return instance;

            // Fallback: scan the active scene.
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return null;

            var roots = scene.GetRootGameObjects();
            foreach (var go in roots)
            {
                var found = go.GetComponentsInChildren<CartoonProceduralSkyUpdater>(true)
                    .FirstOrDefault();
                if (found != null) return found;
            }
            return null;
        }

        private static CartoonProceduralSky FindSkyAsset()
        {
            string[] guids = AssetDatabase.FindAssets("t:CartoonProceduralSky");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<CartoonProceduralSky>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static Material FindSkyMaterial()
        {
            string[] guids = AssetDatabase.FindAssets("t:Material CartoonSky");
            foreach (var guid in guids)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null && mat.shader != null && mat.shader.name == "Skybox/Cartoon/Procedural")
                    return mat;
            }
            return null;
        }

        private static CartoonProceduralSky CreateDefaultSkyAsset()
        {
            const string folder = "Assets/CartoonRendering/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/CartoonRendering", "Materials");

            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/CartoonProceduralSky.asset");
            var asset = ScriptableObject.CreateInstance<CartoonProceduralSky>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static Material CreateDefaultSkyMaterial()
        {
            const string folder = "Assets/CartoonRendering/Materials";
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder("Assets/CartoonRendering", "Materials");

            Shader shader = Shader.Find("Skybox/Cartoon/Procedural");
            if (shader == null)
            {
                Debug.LogError("[CartoonRendering] Shader 'Skybox/Cartoon/Procedural' not found. " +
                               "Make sure the shader compiled successfully before creating a material.");
                return null;
            }

            string path = AssetDatabase.GenerateUniqueAssetPath(folder + "/CartoonSky.mat");
            var mat = new Material(shader) { name = "CartoonSky" };
            AssetDatabase.CreateAsset(mat, path);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
