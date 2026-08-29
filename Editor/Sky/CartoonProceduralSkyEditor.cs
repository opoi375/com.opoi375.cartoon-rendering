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
        private bool _foldoutClouds    = true;
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

        private SerializedProperty _cloudColor;
        private SerializedProperty _cloudHeight;
        private SerializedProperty _cloudThreshold;
        private SerializedProperty _cloudScale;
        private SerializedProperty _cloudIntensity;
        private SerializedProperty _cloudTexture;

        private SerializedProperty _volEnabled;
        private SerializedProperty _volNoiseTex;
        private SerializedProperty _volCoverage;
        private SerializedProperty _volBaseHeight;
        private SerializedProperty _volThickness;
        private SerializedProperty _volScale;
        private SerializedProperty _volDensity;
        private SerializedProperty _volMarchSteps;
        private SerializedProperty _volLightSteps;
        private SerializedProperty _volDetail;
        private SerializedProperty _volWindSpeed;
        private SerializedProperty _volShadeSteps;
        private SerializedProperty _volShadeSmooth;
        private SerializedProperty _volSilverIntensity;
        private SerializedProperty _volSilverPower;
        private SerializedProperty _volTemporal;

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

            _cloudColor       = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudColor));
            _cloudHeight      = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudHeight));
            _cloudThreshold   = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudThreshold));
            _cloudScale       = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudScale));
            _cloudIntensity   = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudIntensity));
            _cloudTexture     = serializedObject.FindProperty(nameof(CartoonProceduralSky.cloudTexture));

            _volEnabled         = serializedObject.FindProperty(nameof(CartoonProceduralSky.volumetricCloudsEnabled));
            _volNoiseTex        = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudNoiseTexture));
            _volCoverage        = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudCoverage));
            _volBaseHeight      = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudBaseHeight));
            _volThickness       = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudThickness));
            _volScale           = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudScale));
            _volDensity         = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudDensity));
            _volMarchSteps      = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudMarchSteps));
            _volLightSteps      = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudLightSteps));
            _volDetail          = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudDetail));
            _volWindSpeed       = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudWindSpeed));
            _volShadeSteps      = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudShadeSteps));
            _volShadeSmooth     = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudShadeSmooth));
            _volSilverIntensity = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudSilverIntensity));
            _volSilverPower     = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudSilverPower));
            _volTemporal        = serializedObject.FindProperty(nameof(CartoonProceduralSky.volCloudTemporal));

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

            _foldoutClouds = DrawSection("Clouds", _foldoutClouds, () =>
            {
                EditorGUILayout.PropertyField(_cloudIntensity);
                EditorGUILayout.PropertyField(_cloudColor);
                EditorGUILayout.PropertyField(_cloudHeight);
                EditorGUILayout.PropertyField(_cloudThreshold);
                EditorGUILayout.PropertyField(_cloudScale);
                EditorGUILayout.PropertyField(_cloudTexture);
            });

            _foldoutVolClouds = DrawSection("Volumetric Clouds", _foldoutVolClouds, () =>
            {
                EditorGUILayout.PropertyField(_volEnabled);
                using (new EditorGUI.DisabledScope(!_volEnabled.boolValue))
                {
                    EditorGUILayout.PropertyField(_volNoiseTex);
                    EditorGUILayout.PropertyField(_volCoverage);
                    EditorGUILayout.PropertyField(_volBaseHeight);
                    EditorGUILayout.PropertyField(_volThickness);
                    EditorGUILayout.PropertyField(_volScale);
                    EditorGUILayout.PropertyField(_volDensity);
                    EditorGUILayout.PropertyField(_volMarchSteps);
                    EditorGUILayout.PropertyField(_volLightSteps);
                    EditorGUILayout.PropertyField(_volDetail);
                    EditorGUILayout.PropertyField(_volWindSpeed);
                    EditorGUILayout.PropertyField(_volShadeSteps);
                    EditorGUILayout.PropertyField(_volShadeSmooth);
                    EditorGUILayout.PropertyField(_volSilverIntensity);
                    EditorGUILayout.PropertyField(_volSilverPower);
                    EditorGUILayout.PropertyField(_volTemporal);

                    if (_volNoiseTex.objectReferenceValue == null)
                    {
                        EditorGUILayout.HelpBox(
                            "No 3D noise texture assigned. Bake one via:\n" +
                            "Cartoon Rendering > Bake Cloud Noise 3D",
                            MessageType.Warning);
                    }
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
