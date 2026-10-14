// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEditor;
using UnityEngine;

namespace CartoonRendering.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="CartoonProceduralSky"/>. Groups
    /// fields using <see cref="UnityEditor.Foldout"/> so the layout matches
    /// the original DanbaidongRP aesthetic without depending on the
    /// proprietary DanbaidongGUI library.
    /// </summary>
    [CustomEditor(typeof(CartoonProceduralSky))]
    public sealed class CartoonProceduralSkyEditor : UnityEditor.Editor
    {
        // ----- Persistent foldout state ------------------------------------

        private bool _foldoutSun       = true;
        private bool _foldoutStars     = true;
        private bool _foldoutVolClouds = true;
        private bool _foldoutGradient  = true;
        private bool _foldoutSunset    = true;
        private bool _foldoutNight     = true;
        private bool _foldoutOverall   = true;

        // ----- Serialised properties --------------------------------------

        private SerializedProperty _sunColor;
        private SerializedProperty _sunSize;
        private SerializedProperty _sunInnerBound;
        private SerializedProperty _sunOuterBound;

        private SerializedProperty _starColor;
        private SerializedProperty _starIntensity;
        private SerializedProperty _starSize;
        private SerializedProperty _starScale;
        private SerializedProperty _starDensity;
        private SerializedProperty _starTexture;


        // 体积云字段按名字前缀自动收集，不逐个列（v1.8.0 重写后字段集变动频繁，
        // 列表式的写法每次都要同步改三处，已经漏过一次）。
        private SerializedProperty _cloudColor;

        private SerializedProperty _topColor;
        private SerializedProperty _middleColor;
        private SerializedProperty _bottomColor;
        private SerializedProperty _horizonColor;
        private SerializedProperty _backgroundColor;
        private SerializedProperty _gradientSoftness;

        private SerializedProperty _sunsetTopColor;
        private SerializedProperty _sunsetMiddleColor;
        private SerializedProperty _sunsetHorizonColor;

        private SerializedProperty _nightTopColor;
        private SerializedProperty _nightMiddleColor;
        private SerializedProperty _nightHorizonColor;
        private SerializedProperty _nightBackgroundColor;

        private SerializedProperty _sunsetBlendStart;
        private SerializedProperty _dayBlendEnd;

        private SerializedProperty _intensity;
        private SerializedProperty _includeSunDisc;
        private SerializedProperty _gradientRamp;

        private void OnEnable()
        {
            _sunColor         = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunColor));
            _sunSize          = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunSize));
            _sunInnerBound    = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunInnerBound));
            _sunOuterBound    = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunOuterBound));

            _starColor        = serializedObject.FindProperty(nameof(CartoonProceduralSky.starColor));
            _starIntensity    = serializedObject.FindProperty(nameof(CartoonProceduralSky.starIntensity));
            _starSize         = serializedObject.FindProperty(nameof(CartoonProceduralSky.starSize));
            _starScale        = serializedObject.FindProperty(nameof(CartoonProceduralSky.starScale));
            _starDensity      = serializedObject.FindProperty(nameof(CartoonProceduralSky.starDensity));
            _starTexture      = serializedObject.FindProperty(nameof(CartoonProceduralSky.starTexture));


            _cloudColor = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudColor));

            _topColor         = serializedObject.FindProperty(nameof(CartoonProceduralSky.topColor));
            _middleColor      = serializedObject.FindProperty(nameof(CartoonProceduralSky.middleColor));
            _bottomColor      = serializedObject.FindProperty(nameof(CartoonProceduralSky.bottomColor));
            _horizonColor     = serializedObject.FindProperty(nameof(CartoonProceduralSky.horizonColor));
            _backgroundColor  = serializedObject.FindProperty(nameof(CartoonProceduralSky.backgroundColor));
            _gradientSoftness = serializedObject.FindProperty(nameof(CartoonProceduralSky.gradientSoftness));

            _sunsetTopColor     = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunsetTopColor));
            _sunsetMiddleColor  = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunsetMiddleColor));
            _sunsetHorizonColor = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunsetHorizonColor));

            _nightTopColor        = serializedObject.FindProperty(nameof(CartoonProceduralSky.nightTopColor));
            _nightMiddleColor     = serializedObject.FindProperty(nameof(CartoonProceduralSky.nightMiddleColor));
            _nightHorizonColor    = serializedObject.FindProperty(nameof(CartoonProceduralSky.nightHorizonColor));
            _nightBackgroundColor = serializedObject.FindProperty(nameof(CartoonProceduralSky.nightBackgroundColor));

            _sunsetBlendStart = serializedObject.FindProperty(nameof(CartoonProceduralSky.sunsetBlendStart));
            _dayBlendEnd      = serializedObject.FindProperty(nameof(CartoonProceduralSky.dayBlendEnd));

            _intensity        = serializedObject.FindProperty(nameof(CartoonProceduralSky.intensity));
            _includeSunDisc   = serializedObject.FindProperty(nameof(CartoonProceduralSky.includeSunDisc));
            _gradientRamp     = serializedObject.FindProperty(nameof(CartoonProceduralSky.gradientRamp));
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            _foldoutSun = DrawSection("Sun", _foldoutSun, () =>
            {
                EditorGUILayout.PropertyField(_sunColor);
                EditorGUILayout.PropertyField(_sunSize);
                EditorGUILayout.PropertyField(_sunInnerBound);
                EditorGUILayout.PropertyField(_sunOuterBound);
            });

            _foldoutStars = DrawSection("Stars", _foldoutStars, () =>
            {
                EditorGUILayout.PropertyField(_starColor);
                EditorGUILayout.PropertyField(_starIntensity);
                EditorGUILayout.PropertyField(_starSize);
                EditorGUILayout.PropertyField(_starDensity);
                EditorGUILayout.PropertyField(_starScale);
                EditorGUILayout.PropertyField(_starTexture);
            });

            _foldoutVolClouds = DrawSection("Clouds", _foldoutVolClouds, () =>
            {
                EditorGUILayout.PropertyField(_cloudColor);

                // 自动收集：打开折叠时先让 _foldoutEnabled 之类的状态属性写回，
                // 再遍历可见属性，凡名字是 volumetricCloudsEnabled / volCloud* 的都画。
                var prop = serializedObject.GetIterator();
                bool enterChildren = true;
                while (prop.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    string n = prop.name;
                    if (n == nameof(CartoonProceduralSky.volumetricCloudsEnabled)
                        || n.StartsWith("volCloud", System.StringComparison.Ordinal))
                        EditorGUILayout.PropertyField(prop, true);
                }
            });

            _foldoutGradient = DrawSection("Sky Gradient - Day", _foldoutGradient, () =>
            {
                EditorGUILayout.PropertyField(_topColor);
                EditorGUILayout.PropertyField(_middleColor);
                EditorGUILayout.PropertyField(_bottomColor);
                EditorGUILayout.PropertyField(_horizonColor);
                EditorGUILayout.PropertyField(_backgroundColor);
                EditorGUILayout.PropertyField(_gradientSoftness);
                EditorGUILayout.PropertyField(_gradientRamp);
            });

            _foldoutSunset = DrawSection("Sky Gradient - Sunset", _foldoutSunset, () =>
            {
                EditorGUILayout.PropertyField(_sunsetTopColor);
                EditorGUILayout.PropertyField(_sunsetMiddleColor);
                EditorGUILayout.PropertyField(_sunsetHorizonColor);
                EditorGUILayout.PropertyField(_sunsetBlendStart);
                EditorGUILayout.PropertyField(_dayBlendEnd);
            });

            _foldoutNight = DrawSection("Sky Gradient - Night", _foldoutNight, () =>
            {
                EditorGUILayout.PropertyField(_nightTopColor);
                EditorGUILayout.PropertyField(_nightMiddleColor);
                EditorGUILayout.PropertyField(_nightHorizonColor);
                EditorGUILayout.PropertyField(_nightBackgroundColor);
            });

            _foldoutOverall = DrawSection("Overall", _foldoutOverall, () =>
            {
                EditorGUILayout.PropertyField(_intensity);
                EditorGUILayout.PropertyField(_includeSunDisc);
            });

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Apply To RenderSettings (if Updater present)"))
                {
                    if (CartoonProceduralSkyUpdater.Instance != null)
                    {
                        CartoonProceduralSkyUpdater.Instance.ApplyImmediate();
                    }
                    else
                    {
                        Debug.LogWarning(
                            "[CartoonRendering] No CartoonProceduralSkyUpdater found in the active scene. " +
                            "Add one to drive this asset at runtime.");
                    }
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static bool DrawSection(string title, bool state, System.Action body)
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                state = EditorGUILayout.Foldout(state, title, true, EditorStyles.foldoutHeader);
                if (state)
                {
                    EditorGUI.indentLevel++;
                    body();
                    EditorGUI.indentLevel--;
                }
            }
            return state;
        }
    }
}
