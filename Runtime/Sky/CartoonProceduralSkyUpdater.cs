// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// Pushes a <see cref="CartoonProceduralSky"/> asset to the global shader
    /// uniforms expected by <c>Skybox/Cartoon/Procedural</c>, and assigns the
    /// supplied <see cref="Material"/> to <c>RenderSettings.skybox</c> so that
    /// URP's <c>DrawSkyboxPass</c> renders it.
    ///
    /// Place exactly one instance in the scene (or use the singleton
    /// <see cref="Instance"/> accessor). The component reads the main
    /// directional light via <c>Light.main</c> and converts its forward
    /// direction to a <c>time-of-day</c> value with
    /// <see cref="TimeOfDaySystem"/>.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("Cartoon Rendering/Cartoon Procedural Sky Updater")]
    [DisallowMultipleComponent]
    public sealed class CartoonProceduralSkyUpdater : MonoBehaviour
    {
        // ----- Shader property IDs (cached) ------------------------------

        private static readonly int kSunDirection       = Shader.PropertyToID("_CartoonSunDirection");
        private static readonly int kTimeOfDay          = Shader.PropertyToID("_CartoonTimeOfDay");
        private static readonly int kSunColor           = Shader.PropertyToID("_CartoonSunColor");
        private static readonly int kSunParams          = Shader.PropertyToID("_CartoonSunParams"); // x=size, y=inner, z=outer
        private static readonly int kStarColor          = Shader.PropertyToID("_CartoonStarColor");
        private static readonly int kStarParams         = Shader.PropertyToID("_CartoonStarParams"); // x=intensity, y=randomColor, z=scale
        private static readonly int kTopColor           = Shader.PropertyToID("_CartoonTopColor");
        private static readonly int kMiddleColor        = Shader.PropertyToID("_CartoonMiddleColor");
        private static readonly int kBottomColor        = Shader.PropertyToID("_CartoonBottomColor");
        private static readonly int kHorizonColor       = Shader.PropertyToID("_CartoonHorizonColor");
        private static readonly int kBackgroundColor    = Shader.PropertyToID("_CartoonBackgroundColor");
        private static readonly int kGradientSoftness   = Shader.PropertyToID("_CartoonGradientSoftness");
        private static readonly int kIntensity          = Shader.PropertyToID("_CartoonIntensity");
        private static readonly int kIncludeSunDisc     = Shader.PropertyToID("_CartoonIncludeSunDisc");
        private static readonly int kGradientRamp       = Shader.PropertyToID("_CartoonGradientRamp");
        private static readonly int kHasGradientRamp    = Shader.PropertyToID("_CartoonHasGradientRamp");
        private static readonly int kSunsetTop          = Shader.PropertyToID("_CartoonSunsetTopColor");
        private static readonly int kSunsetMiddle       = Shader.PropertyToID("_CartoonSunsetMiddleColor");
        private static readonly int kSunsetHorizon      = Shader.PropertyToID("_CartoonSunsetHorizonColor");
        private static readonly int kNightTop           = Shader.PropertyToID("_CartoonNightTopColor");
        private static readonly int kNightMiddle        = Shader.PropertyToID("_CartoonNightMiddleColor");
        private static readonly int kNightHorizon       = Shader.PropertyToID("_CartoonNightHorizonColor");
        private static readonly int kNightBackground    = Shader.PropertyToID("_CartoonNightBackgroundColor");
        private static readonly int kBlendParams        = Shader.PropertyToID("_CartoonBlendParams"); // x=sunsetBlendStart, y=dayBlendEnd
        private static readonly int kStarMap            = Shader.PropertyToID("_CartoonStarMap");
        private static readonly int kHasStarMap         = Shader.PropertyToID("_CartoonHasStarMap");
        private static readonly int kCloudColor         = Shader.PropertyToID("_CartoonCloudColor");
        private static readonly int kCloudParams        = Shader.PropertyToID("_CartoonCloudParams"); // x=height, y=threshold, z=scale, w=intensity
        private static readonly int kCloudMap           = Shader.PropertyToID("_CartoonCloudMap");
        private static readonly int kHasCloudMap        = Shader.PropertyToID("_CartoonHasCloudMap");

        // ----- Inspector --------------------------------------------------

        [Tooltip("ScriptableObject describing the cartoon sky colours.")]
        public CartoonProceduralSky skyAsset;

        [Tooltip("Material using the Skybox/Cartoon/Procedural shader. Assigned to RenderSettings.skybox on enable.")]
        public Material skyMaterial;

        [Tooltip("Optional directional light. If null, the component looks up Light.main every frame.")]
        public Light sunLight;

        [Tooltip("If true, RenderSettings.skybox is set automatically. Disable to drive it manually.")]
        public bool autoApplyToRenderSettings = true;

        // ----- Singleton (optional) --------------------------------------

        private static CartoonProceduralSkyUpdater s_Instance;
        public static CartoonProceduralSkyUpdater Instance
        {
            get
            {
                // Lazy re-acquire: if the previous instance was destroyed
                // while another updater is alive in the scene (e.g. a
                // temporary rig was deleted), recover instead of leaving
                // the singleton null until the next domain reload.
                if (s_Instance == null)
                    s_Instance = FindAnyObjectByType<CartoonProceduralSkyUpdater>();
                return s_Instance;
            }
        }

        private void OnEnable()
        {
            s_Instance = this;
#if UNITY_EDITOR
            // In edit mode LateUpdate does not run continuously, so hook the
            // editor tick instead. This makes the sky follow the directional
            // light LIVE while the user rotates it in the editor.
            UnityEditor.EditorApplication.update += OnEditorTick;
#endif
            ApplyImmediate();
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= OnEditorTick;
#endif
            if (s_Instance == this) s_Instance = null;
        }

#if UNITY_EDITOR
        private void OnEditorTick()
        {
            // Play mode is driven by LateUpdate; only push values here while
            // editing so the sky reacts immediately to light changes.
            if (!UnityEditor.EditorApplication.isPlaying)
                ApplyImmediate();
        }
#endif

        private void LateUpdate()
        {
            ApplyImmediate();
        }

        // OnValidate is invoked by Unity every time a serialised field is
        // changed in the Inspector, when the component is loaded, and when
        // the user re-assigns the skyAsset via drag-and-drop. Calling
        // ApplyImmediate here is what lets the editor Game view react
        // immediately to user edits - LateUpdate is not enough because
        // Unity does not repaint the Game view when MonoBehaviour
        // properties change unless something else requests a repaint.
        private void OnValidate()
        {
#if UNITY_EDITOR
            // Defer to next editor tick so the asset reference has actually
            // been updated by the time we read it.
            UnityEditor.EditorApplication.delayCall += () => ApplyImmediate();
#else
            ApplyImmediate();
#endif
        }

        /// <summary>
        /// Pushes the asset's values to the sky material and (optionally)
        /// assigns the sky material to <c>RenderSettings.skybox</c>.
        ///
        /// NOTE: the values are written to the MATERIAL itself, not via
        /// Shader.SetGlobal*. URP 17 renders the skybox through a cached
        /// RendererList whose constant buffer is built from the material's
        /// own property sheet - global uniforms are not guaranteed to reach
        /// that draw call, which previously produced a flat grey sky.
        /// </summary>
        public void ApplyImmediate()
        {
            if (skyAsset == null) return;

            Light light = sunLight != null ? sunLight : GameObject.FindAnyObjectByType<Light>();
            Vector3 sunDir = TimeOfDaySystem.GetSunDirection(light);
            float timeOfDay = TimeOfDaySystem.GetTimeOfDayFromLight(light);

            Material mat = skyMaterial;
            if (mat == null)
            {
                // Nothing to write to - fall back to globals so the shader
                // still has *some* data if someone assigns the material later.
                ApplyGlobalsOnly(sunDir, timeOfDay);
                return;
            }

            mat.SetVector(kSunDirection, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0.0f));
            mat.SetFloat(kTimeOfDay, timeOfDay);

            mat.SetColor(kSunColor, skyAsset.sunColor);
            mat.SetVector(kSunParams,
                new Vector4(skyAsset.sunSize, skyAsset.sunInnerBound, skyAsset.sunOuterBound, 0.0f));

            mat.SetColor(kStarColor, skyAsset.starColor);
            mat.SetVector(kStarParams,
                new Vector4(skyAsset.starIntensity, skyAsset.starSize, skyAsset.starScale, skyAsset.starDensity));
            if (skyAsset.starTexture != null)
            {
                mat.SetTexture(kStarMap, skyAsset.starTexture);
                mat.SetFloat(kHasStarMap, 1.0f);
            }
            else
            {
                mat.SetFloat(kHasStarMap, 0.0f);
            }

            mat.SetColor(kCloudColor, skyAsset.cloudColor);
            mat.SetVector(kCloudParams,
                new Vector4(skyAsset.cloudHeight, skyAsset.cloudThreshold, skyAsset.cloudScale, skyAsset.cloudIntensity));
            if (skyAsset.cloudTexture != null)
            {
                mat.SetTexture(kCloudMap, skyAsset.cloudTexture);
                mat.SetFloat(kHasCloudMap, 1.0f);
            }
            else
            {
                mat.SetFloat(kHasCloudMap, 0.0f);
            }

            mat.SetColor(kTopColor, skyAsset.topColor);
            mat.SetColor(kMiddleColor, skyAsset.middleColor);
            mat.SetColor(kBottomColor, skyAsset.bottomColor);
            mat.SetColor(kHorizonColor, skyAsset.horizonColor);
            mat.SetColor(kBackgroundColor, skyAsset.backgroundColor);

            mat.SetColor(kSunsetTop, skyAsset.sunsetTopColor);
            mat.SetColor(kSunsetMiddle, skyAsset.sunsetMiddleColor);
            mat.SetColor(kSunsetHorizon, skyAsset.sunsetHorizonColor);
            mat.SetColor(kNightTop, skyAsset.nightTopColor);
            mat.SetColor(kNightMiddle, skyAsset.nightMiddleColor);
            mat.SetColor(kNightHorizon, skyAsset.nightHorizonColor);
            mat.SetColor(kNightBackground, skyAsset.nightBackgroundColor);
            mat.SetVector(kBlendParams,
                new Vector4(skyAsset.sunsetBlendStart, skyAsset.dayBlendEnd, 0.0f, 0.0f));

            mat.SetFloat(kGradientSoftness, skyAsset.gradientSoftness);
            mat.SetFloat(kIntensity, skyAsset.intensity);
            mat.SetFloat(kIncludeSunDisc, skyAsset.includeSunDisc ? 1.0f : 0.0f);

            if (skyAsset.gradientRamp != null)
            {
                mat.SetTexture(kGradientRamp, skyAsset.gradientRamp);
                mat.SetFloat(kHasGradientRamp, 1.0f);
            }
            else
            {
                mat.SetFloat(kHasGradientRamp, 0.0f);
            }

            // Also mirror the sun direction / time-of-day to globals so any
            // other shader (e.g. rim light, water) can consume them.
            Shader.SetGlobalVector(kSunDirection, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0.0f));
            Shader.SetGlobalFloat(kTimeOfDay, timeOfDay);

            if (autoApplyToRenderSettings)
            {
                // Re-assigning skybox every time (even if the same reference)
                // is what makes the editor Game view re-render the sky. Unity
                // only repaints the skybox cuboid when RenderSettings.skybox
                // changes, so without this toggle the editor view will keep
                // showing the cached frame even though the material is updating.
                if (RenderSettings.skybox != mat)
                {
                    RenderSettings.skybox = mat;
                    // Keep ambient in sync so the sky contribution is visible.
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                    DynamicGI.UpdateEnvironment();
                }
#if UNITY_EDITOR
                else
                {
                    // Force the editor to repaint so the user sees the new
                    // gradient as soon as they tweak a field.
                    UnityEditor.SceneView.RepaintAll();
                    UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
                }
#endif
            }
        }

        /// <summary>
        /// Fallback path used when no sky material is assigned: push the
        /// uniforms globally so the shader still has plausible data.
        /// </summary>
        private void ApplyGlobalsOnly(Vector3 sunDir, float timeOfDay)
        {
            Shader.SetGlobalVector(kSunDirection, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0.0f));
            Shader.SetGlobalFloat(kTimeOfDay, timeOfDay);

            Shader.SetGlobalColor(kSunColor, skyAsset.sunColor);
            Shader.SetGlobalVector(kSunParams,
                new Vector4(skyAsset.sunSize, skyAsset.sunInnerBound, skyAsset.sunOuterBound, 0.0f));

            Shader.SetGlobalColor(kStarColor, skyAsset.starColor);
            Shader.SetGlobalVector(kStarParams,
                new Vector4(skyAsset.starIntensity, skyAsset.starSize, skyAsset.starScale, skyAsset.starDensity));
            if (skyAsset.starTexture != null)
            {
                Shader.SetGlobalTexture(kStarMap, skyAsset.starTexture);
                Shader.SetGlobalFloat(kHasStarMap, 1.0f);
            }
            else
            {
                Shader.SetGlobalFloat(kHasStarMap, 0.0f);
            }

            Shader.SetGlobalColor(kCloudColor, skyAsset.cloudColor);
            Shader.SetGlobalVector(kCloudParams,
                new Vector4(skyAsset.cloudHeight, skyAsset.cloudThreshold, skyAsset.cloudScale, skyAsset.cloudIntensity));
            if (skyAsset.cloudTexture != null)
            {
                Shader.SetGlobalTexture(kCloudMap, skyAsset.cloudTexture);
                Shader.SetGlobalFloat(kHasCloudMap, 1.0f);
            }
            else
            {
                Shader.SetGlobalFloat(kHasCloudMap, 0.0f);
            }

            Shader.SetGlobalColor(kTopColor, skyAsset.topColor);
            Shader.SetGlobalColor(kMiddleColor, skyAsset.middleColor);
            Shader.SetGlobalColor(kBottomColor, skyAsset.bottomColor);
            Shader.SetGlobalColor(kHorizonColor, skyAsset.horizonColor);
            Shader.SetGlobalColor(kBackgroundColor, skyAsset.backgroundColor);

            Shader.SetGlobalColor(kSunsetTop, skyAsset.sunsetTopColor);
            Shader.SetGlobalColor(kSunsetMiddle, skyAsset.sunsetMiddleColor);
            Shader.SetGlobalColor(kSunsetHorizon, skyAsset.sunsetHorizonColor);
            Shader.SetGlobalColor(kNightTop, skyAsset.nightTopColor);
            Shader.SetGlobalColor(kNightMiddle, skyAsset.nightMiddleColor);
            Shader.SetGlobalColor(kNightHorizon, skyAsset.nightHorizonColor);
            Shader.SetGlobalColor(kNightBackground, skyAsset.nightBackgroundColor);
            Shader.SetGlobalVector(kBlendParams,
                new Vector4(skyAsset.sunsetBlendStart, skyAsset.dayBlendEnd, 0.0f, 0.0f));

            Shader.SetGlobalFloat(kGradientSoftness, skyAsset.gradientSoftness);
            Shader.SetGlobalFloat(kIntensity, skyAsset.intensity);
            Shader.SetGlobalFloat(kIncludeSunDisc, skyAsset.includeSunDisc ? 1.0f : 0.0f);

            if (skyAsset.gradientRamp != null)
            {
                Shader.SetGlobalTexture(kGradientRamp, skyAsset.gradientRamp);
                Shader.SetGlobalFloat(kHasGradientRamp, 1.0f);
            }
            else
            {
                Shader.SetGlobalFloat(kHasGradientRamp, 0.0f);
            }
        }
    }
}
