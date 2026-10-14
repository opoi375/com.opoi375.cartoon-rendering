// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{

    /// <summary>体积云调试视图（CartoonProceduralSky.volCloudDebug）。</summary>
    public enum CloudDebugView
    {
        Off = 0,
        /// <summary>累积不透明度：应该有成片的云块；整屏黑 = 密度模型没吃到东西。</summary>
        Coverage = 1,
        /// <summary>射线与云层的相交区间：黑 = 这段区间不存在或被裁掉。</summary>
        MarchRange = 2,
        /// <summary>云层中部的原始噪声值：整屏均匀灰 = 噪声被 mip 抹平了。</summary>
        Field = 3,
        /// <summary>高度剖面 hg：黑 = 云被垂直剖面整个裁掉。</summary>
        Profile = 4,
        /// <summary>选用的噪声 mip：远处偏高说明细节全被抹掉。</summary>
        Lod = 5,
        /// <summary>2D 天气场：应该有大片深浅分区；整屏一个灰度 = 天气图没接上。</summary>
        Weather = 6,
    }
    /// <summary>
    /// Data container describing a procedural cartoon sky. Stored as a
    /// ScriptableObject because URP 17 removed the Volume-based
    /// <c>SkySettings</c> API entirely.
    ///
    /// Create instances via:
    ///   Assets > Create > Cartoon Rendering > Procedural Sky
    /// </summary>
    [CreateAssetMenu(
        fileName = "CartoonProceduralSky",
        menuName = "Cartoon Rendering/Procedural Sky",
        order = 320)]
    public sealed class CartoonProceduralSky : ScriptableObject
    {
        // ----- Sun ---------------------------------------------------------

        [Header("Sun")]
        [ColorUsage(showAlpha: true, hdr: true)]
        public Color sunColor = new Color(1.0f, 0.95f, 0.85f, 1.0f);

        [Range(0.0f, 0.5f), Tooltip("Apparent angular size of the sun disc.")]
        public float sunSize = 0.04f;

        [Range(0.0f, 1.0f), Tooltip("Soft edge inner bound for the sun disc.")]
        public float sunInnerBound = 0.2f;

        [Range(0.0f, 1.0f), Tooltip("Soft edge outer bound for the sun disc.")]
        public float sunOuterBound = 0.8f;

        // ----- Stars -------------------------------------------------------

        [Header("Stars")]
        [ColorUsage(showAlpha: true, hdr: true)]
        public Color starColor = new Color(0.9f, 0.95f, 1.0f, 1.0f);

        [Range(0.0f, 5.0f), Tooltip("Star brightness multiplier.")]
        public float starIntensity = 1.5f;

        [Range(0.0f, 2.0f), Tooltip("Relative size of each star point.")]
        public float starSize = 0.5f;

        [Range(0.0f, 4.0f), Tooltip("Starfield UV scale (larger = denser grid).")]
        public float starScale = 1.0f;

        [Range(0.0f, 1.0f), Tooltip("Fraction of grid cells that host a star.")]
        public float starDensity = 0.5f;

        [Tooltip("Optional noise texture used to place stars instead of the " +
                 "procedural hash lattice. Bright pixels become stars. " +
                 "Leave empty to use procedural noise-sampled points.")]
        public Texture2D starTexture;

        // ----- Clouds ------------------------------------------------------

        [Header("Clouds")]
        [Tooltip("Cloud tint. Darkened automatically at night, warmed at sunset.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color cloudColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        [Range(5.0f, 300.0f), Tooltip("Height of the virtual cloud plane above the camera.")]
        public float cloudHeight = 60.0f;

        [Range(0.0f, 1.0f), Tooltip("Noise threshold - higher = fewer, smaller clouds.")]
        public float cloudThreshold = 0.45f;

        [Range(0.1f, 8.0f), Tooltip("Cloud UV scale (larger = smaller clouds).")]
        public float cloudScale = 1.0f;

        [Range(0.0f, 2.0f), Tooltip("Cloud visibility. 0 disables clouds.")]
        public float cloudIntensity = 1.0f;

        [Tooltip("Optional cloud texture sampled on the cloud plane. Red channel " +
                 "is used as coverage. Leave empty to use procedural FBM noise.")]
        public Texture2D cloudTexture;

        // ----- Volumetric clouds ------------------------------------------
        // Stylised raymarched cloud layer rendered at half resolution by
        // CartoonSkyboxFeature (CartoonVolumetricClouds shader) and
        // composited over the procedural sky.

        [Header("Volumetric Clouds")]
        [Tooltip("Enable the raymarched volumetric cloud layer. The flat " +
                 "billboard clouds above remain as the distant horizon band.")]
        public bool volumetricCloudsEnabled = false;

        [Tooltip("Tileable 64^3 Perlin-Worley noise volume (R=base, GBA=detail). " +
                 "Bake it via: Cartoon Rendering > Bake Cloud Noise 3D")]
        public Texture3D volCloudNoiseTexture;

        [Tooltip("2D weather / domain-warp map (R=large weather, G=mid weather, BA=warp). " +
                 "Bake it via: Tools > Cloud > Bake Cloud Shape Maps.\n" +
                 "没有它时整片天空共用一个覆盖度常数，云只会均匀铺满 —— 像磨砂玻璃，不像天气。" +
                 "留空则退回旧行为。")]
        public Texture2D volCloudShapeTexture;

        [Range(0.00001f, 0.0005f), Tooltip("Weather map world scale: 1 metre -> map tiles. " +
            "Smaller = bigger weather systems. 0.00004 -> one map tile spans 25 km.")]
        public float volCloudWeatherScale = 0.00016f;

        [Range(0.0f, 2.0f), Tooltip("Domain warp strength in noise tiles - curls the " +
            "cloud outlines instead of round cauliflower blobs.")]
        public float volCloudWarp = 0.5f;

        [Range(0.0f, 1.5f), Tooltip("How much the weather map pushes coverage locally " +
            "around the authored value. 0 = uniform sky (old behaviour), " +
            "1 = strong clumping (clear gaps next to solid banks).")]
        public float volCloudWeatherContrast = 0.55f;

        [Range(0.0f, 1.0f), Tooltip("Blend from the large-scale weather channel to the " +
            "mid-scale one: 0 = only big systems, 1 = broken into individual clouds.")]
        public float volCloudWeatherDetail = 0.22f;

        [Range(0.0f, 1.0f), Tooltip("Sky coverage: 0 = clear, 1 = overcast. With a weather " +
            "map this stays the global average - the map moves it up and down locally.")]
        public float volCloudCoverage = 0.26f;

        [Range(100.0f, 4000.0f), Tooltip("Cloud slab base height - absolute world Y (m).")]
        public float volCloudBaseHeight = 800.0f;

        [Range(50.0f, 2000.0f), Tooltip("Cloud slab thickness (m).")]
        public float volCloudThickness = 300.0f;

        [Range(0.0001f, 0.01f), Tooltip("World UV scale - larger = smaller, denser clouds.")]
        public float volCloudScale = 0.00048f;

        [Range(0.1f, 4.0f), Tooltip("Density / absorption - higher = thicker, darker clouds.")]
        public float volCloudDensity = 1.7f;

        [Range(8, 48), Tooltip("View ray march steps. Higher = smoother, slower.")]
        public int volCloudMarchSteps = 32;

        [Range(2, 8), Tooltip("Sun light march steps. Higher = softer shading.")]
        public int volCloudLightSteps = 5;

        [Range(0.0f, 1.0f), Tooltip("Detail erosion strength - carves cauliflower edges.")]
        public float volCloudDetail = 0.7f;

        [Range(0.0f, 100.0f), Tooltip("Wind drift speed.")]
        public float volCloudWindSpeed = 7.0f;

        [Range(1.0f, 6.0f), Tooltip("Cel band count for the cloud light response.")]
        public float volCloudShadeSteps = 3.0f;

        [Range(0.0f, 0.5f), Tooltip("Ease-in-out softness of the cel band edges.")]
        public float volCloudShadeSmooth = 0.35f;

        [Range(0.0f, 2.0f), Tooltip("Silver-lining (back-lit edge) intensity.")]
        public float volCloudSilverIntensity = 0.3f;

        [Range(1.0f, 16.0f), Tooltip("Silver-lining tightness around the sun.")]
        public float volCloudSilverPower = 8.0f;

        [Tooltip("Temporal accumulation (TAA-style smoothing) for volumetric " +
            "clouds: blends each frame with reprojected history so marching " +
            "banding/dither converge away over a few frames. Turn OFF for " +
            "crisp single-frame clouds (also freezes the march jitter so it " +
            "doesn't shimmer) at the cost of visible dither grain.")]
        public bool volCloudTemporalEnabled = true;

        [Tooltip("体积云调试视图 —— 用来区分「没有云」和「云画了但看不见」。\n" +
                 "Coverage = 累积覆盖度，MarchRange = 射线与云层的相交区间，\n" +
                 "Field = 云层中点的原始噪声值，Profile = 高度剖面，Lod = 选用的 mip。\n" +
                 "红色 = 射线根本没穿过云层。")]
        public CloudDebugView volCloudDebug = CloudDebugView.Off;

        [Range(0.0f, 0.9f), Tooltip("Temporal accumulation weight. Higher = smoother " +
            "(marching banding/dither converge away over a few frames) but more ghosting " +
            "on fast camera moves. 0 disables history blending.")]
        public float volCloudTemporal = 0.8f;

        // ----- Sky gradient (5 colour stops) -------------------------------
        // The five stops below describe the DAY sky. Sunset and night
        // palettes follow; the shader blends between the three palettes
        // based on the sun elevation derived from the directional light.

        [Header("Sky Gradient - Day")]
        [Tooltip("Top-of-sky colour. Sampled at the zenith.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color topColor = new Color(0.15f, 0.35f, 0.75f, 1.0f);

        [Tooltip("Mid-sky colour. Sampled at the upper hemisphere mid-height.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color middleColor = new Color(0.55f, 0.75f, 0.95f, 1.0f);

        [Tooltip("Bottom-of-sky colour. Sampled at the lower hemisphere.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color bottomColor = new Color(0.85f, 0.75f, 0.60f, 1.0f);

        [Tooltip("Horizon line colour.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color horizonColor = new Color(0.95f, 0.80f, 0.65f, 1.0f);

        [Tooltip("Fallback background colour used where the sun is occluded.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color backgroundColor = new Color(0.05f, 0.10f, 0.20f, 1.0f);

        [Range(0.0f, 1.0f), Tooltip("Horizontal width of each gradient band.")]
        public float gradientSoftness = 0.15f;

        // ----- Sunset palette ----------------------------------------------

        [Header("Sky Gradient - Sunset")]
        [Tooltip("Zenith colour when the sun is near the horizon.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color sunsetTopColor = new Color(0.20f, 0.15f, 0.40f, 1.0f);

        [Tooltip("Mid-sky colour when the sun is near the horizon.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color sunsetMiddleColor = new Color(0.75f, 0.35f, 0.25f, 1.0f);

        [Tooltip("Horizon colour when the sun is near the horizon (deep orange-red).")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color sunsetHorizonColor = new Color(1.0f, 0.45f, 0.20f, 1.0f);

        // ----- Night palette ------------------------------------------------

        [Header("Sky Gradient - Night")]
        [Tooltip("Zenith colour when the sun is below the horizon.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color nightTopColor = new Color(0.01f, 0.02f, 0.06f, 1.0f);

        [Tooltip("Mid-sky colour when the sun is below the horizon.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color nightMiddleColor = new Color(0.03f, 0.05f, 0.12f, 1.0f);

        [Tooltip("Horizon colour when the sun is below the horizon.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color nightHorizonColor = new Color(0.06f, 0.08f, 0.16f, 1.0f);

        [Tooltip("Below-horizon background colour at night.")]
        [ColorUsage(showAlpha: false, hdr: true)]
        public Color nightBackgroundColor = new Color(0.01f, 0.01f, 0.03f, 1.0f);

        [Range(0.0f, 0.5f), Tooltip("Sun elevation (0..1) where night starts blending into sunset colours.")]
        public float sunsetBlendStart = 0.05f;

        [Range(0.0f, 0.9f), Tooltip("Sun elevation (0..1) where sunset finishes blending into day colours.")]
        public float dayBlendEnd = 0.35f;

        // Optional gradient ramp - if assigned, the procedural gradient
        // evaluation in CartoonSkybox.shader samples this gradient instead
        // of mixing the discrete colour stops. Useful when an artist wants
        // a hand-authored 24-hour palette in a single asset.
        [Tooltip("Optional gradient texture (1xN) sampled vertically to drive the sky colour. " +
                 "Leave empty to use the discrete top/middle/bottom/horizon colours.")]
        public Texture2D gradientRamp;

        // ----- Intensity ---------------------------------------------------

        [Header("Overall")]
        [Range(0.0f, 8.0f), Tooltip("Master multiplier applied to the sky colour.")]
        public float intensity = 1.0f;

        [Tooltip("If true the sun disc is drawn in addition to the sky gradient.")]
        public bool includeSunDisc = true;
    }
}
