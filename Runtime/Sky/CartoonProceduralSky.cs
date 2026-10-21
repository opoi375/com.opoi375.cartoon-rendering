// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{

    /// <summary>体积云调试视图（CartoonProceduralSky.volCloudDebug）。</summary>
    /// <remarks>
    /// 体积云重写后（v1.8.0）的诊断手段：把密度模型的中间量直接画出来。
    /// "天上没有云"和"云画了但看不见"用眼睛分不出来 —— 只能看中间量。
    /// </remarks>
    public enum CloudDebugView
    {
        Off = 0,
        /// <summary>覆盖率：应看到【离散的云团】之间留着天空；整屏黑 = 密度模型没吃到东西。</summary>
        Coverage = 1,
        /// <summary>存在性：每个 cell 的存活权重。这一视图专门用来验证"云是离散的"。</summary>
        Instance = 2,
        /// <summary>归一化高度：0=云底 1=云顶。应看到竖向渐变而非纯色。</summary>
        Height = 3,
        /// <summary>边带权重：应是云轮廓上的一圈线，证明轮廓是被啃出来的。</summary>
        Edge = 4,
        /// <summary>射线与云层的相交起点：红色 = 射线根本没穿过云层。</summary>
        Depth = 5,
        /// <summary>散射响应：同一角度下应随密度有明暗变化。</summary>
        Scatter = 6,
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
        // Stylised raymarched cloud layer rendered at half resolution by
        // CartoonSkyboxFeature (CartoonVolumetricClouds shader) and
        // composited over the procedural sky.
        //
        // 【密度模型 = 离散云团实例】
        // 世界平面按 volCloudSpacing 切成 cell，每个 cell 最多住一朵云（独立
        // 哈希决定它存不存在、多大、多厚），密度取各候选椭球体的最大值。
        //
        // 这与"把连续噪声按覆盖率阈值化"有本质区别：连续随机场有个渗流阈值
        // （约 0.3），覆盖率一过它整块场就【拓扑连通】，天空必然是一整团/一整块
        // 甲板 —— 那是拓扑问题，调噪声和侵蚀都救不了。cell 模型则天然保证
        // 朵与朵之间有留白，而且每朵的半径/厚度都不同，读起来才像"天上有几朵云"。
        //
        // （2D 平流云层已在 v1.8.0 删除：它是一张贴到虚拟平面上的贴图，没有体积、
        //   没有明暗、没有自阴影，与体积云叠在一起只会互相打架。cloudColor 保留，
        //   继续作为体积云的色调。）

        [Header("Clouds")]
        [Tooltip("体积云的色调（乘在云的散射色上）。")]
        public Color cloudColor = new Color(1.0f, 0.99f, 0.97f, 1.0f);

        [Tooltip("启用体积云（天上的云由它一家负责）。")]
        public bool volumetricCloudsEnabled = false;

        [Header("· 云团摆放")]
        [Range(400f, 5000f), Tooltip("云团 cell 间距（米）。朵与朵之间的留白由它决定：\n" +
            "越大越稀疏、分得开；越小越挤成一片。")]
        public float volCloudSpacing = 950f;

        [Range(0.0f, 1.0f), Tooltip("覆盖率：这个 cell 是否住着云。\n" +
            "平滑淡入（调它不会让云逐个 pop），同时被大尺度天气场上下推动。")]
        public float volCloudCoverage = 0.62f;

        [Range(0.2f, 1.4f), Tooltip("云团半径占 cell 的比例。\n" +
            "大体印象：该值 × 2 ≈ 两朵云之间最窄处，>1.4 会挤到相接。")]
        public float volCloudSize = 0.55f;

        [Range(0.0f, 1.0f), Tooltip("半径随机度：0 = 每朵一样大，1 = 大小差异拉满。")]
        public float volCloudSizeJitter = 0.6f;

        [Range(0.0f, 1.0f), Tooltip("大尺度天气调制：天上有云系分区（这边成片、那边一片空），\n" +
            "而不是均匀撒糖。0 = 完全随机散布。")]
        public float volCloudWeather = 0.4f;

        [Header("· 单朵云形状")]
        [Range(100f, 3000f), Tooltip("云底高度（绝对世界 Y，米）。")]
        public float volCloudBaseHeight = 750f;

        [Range(60f, 2000f), Tooltip("单朵云的最大厚度（米）。每朵按 Thickness Jitter 变薄变厚。")]
        public float volCloudThickness = 420f;

        [Range(0.0f, 1.0f), Tooltip("厚度随机度：0 = 每朵一样高。")]
        public float volCloudHeightJitter = 0.6f;

        [Range(0.2f, 0.8f), Tooltip("圆顶指数：0.25 = 宽而扁（层云/高积云），0.7 = 圆墩（积云）。\n" +
            "底永远是平的（凝结面），这个值只管顶部收得快不快。")]
        public float volCloudDome = 0.5f;

        [Range(0.0f, 1.0f), Tooltip("轮廓域扭曲：把采样点推开，云缘变成卷的而不是圆的。")]
        public float volCloudWarp = 0.55f;

        [Range(0.0002f, 0.02f), Tooltip("扭曲 / 侵蚀噪声的频率（每米循环数）。\n" +
                 "0.006 ≈ 170m 一个瓣，配 500m 的云正好是大瓣套小咬口；\n" +
                 "太小退化成一大块光斑（0.002 就是），太碎变成粉刺。")]
        public float volCloudNoiseFreq = 0.006f;

        [Range(0.0f, 1.0f), Tooltip("边缘侵蚀：0 = 光滑椭球，1 = 重度椰菜花咬口。\n" +
            "只啃表面带，云芯实心不动（Remap 形式，覆盖率语义不被破坏）。")]
        public float volCloudDetail = 0.8f;

        [Header("· 密度与步进")]
        [Range(0.2f, 4.0f), Tooltip("密度 / 消光。1.5~2.5 是厚云，>3 近乎不透明。")]
        public float volCloudDensity = 1.9f;

        [Range(8, 64), Tooltip("视线步进次数（质量 / 性能开关）。")]
        public int volCloudMarchSteps = 32;

        [Range(1, 8), Tooltip("朝太阳的自阴影步进次数。")]
        public int volCloudLightSteps = 4;

        [Header("· 散射")]
        [Range(0.0f, 0.95f), Tooltip("前叶各向异性 g。云滴强烈前向散射，0.6~0.85 是物理区间；\n" +
            "0 = 各向同性，云会发平发假。")]
        public float volCloudGForward = 0.75f;

        [Range(-0.95f, 0.0f), Tooltip("后叶 g：决定背朝太阳那一侧的整体明暗形状。")]
        public float volCloudGBackward = -0.25f;

        [Range(0.0f, 1.0f), Tooltip("前叶权重：越高，朝太阳方向越炸亮。")]
        public float volCloudPhaseForward = 0.65f;

        [Range(1, 4), Tooltip("多重散射倍频数。1 = 只做单次散射（云芯烧成死灰），\n" +
            "3~4 才有发亮的内部。")]
        public int volCloudScatterOctaves = 3;

        [Range(0.0f, 1.0f), Tooltip("云的大气透视：远处云按「消光 + 内散射」染进天空色，\n" +
            "而不是只淡出 alpha（只淡 alpha = 一块悬在半空的白斑）。")]
        public float volCloudAerialPerspective = 0.8f;

        [Range(0.0f, 3.0f), Tooltip("直射阳光强度。云冠亮度与暗底的对比全靠它 —— \n" +
            "压到 1 以下云会发灰，抬到 1.4~1.8 才有「日光下的白团」。")]
        public float volCloudSunIntensity = 1.5f;

        [Range(0.0f, 3.0f), Tooltip("环境光（天空漫射）强度。云底暗不下来多半是它太高。")]
        public float volCloudAmbientIntensity = 1.0f;

        [Header("· 风")]
        [Range(0.0f, 40.0f), Tooltip("风速（米/秒）。整片云团网格随风平移。")]
        public float volCloudWindSpeed = 7.0f;

        [Header("· 风格化")]
        [Range(0.0f, 6.0f), Tooltip("cel 明暗分区数。0 = 关闭（连续渐变，写实默认）；卡通硬边分区才开到 2~4，开着会把云内部的层次压平。")]
        public float volCloudShadeSteps = 0.0f;

        [Range(0.0f, 0.5f), Tooltip("分区边界的软化。0 = 硬边，0.5 = 几乎连续。")]
        public float volCloudShadeSmooth = 0.35f;

        [Range(0.0f, 2.0f), Tooltip("逆光银边强度（按体素密度门控，不会在轮廓外撒点）。")]
        public float volCloudSilverIntensity = 0.3f;

        [Range(1.0f, 16.0f), Tooltip("银边聚拢度：越大只有正对太阳的云缘才亮。")]
        public float volCloudSilverPower = 8.0f;

        [Header("· 时间累积")]
        [Tooltip("时序累积（TAA 式平滑）：逐帧把步进抖动收敛掉。关掉不分配历史缓冲、\n" +
            "抖动冻结成静态图案（适合单帧截图或完全不想看拖影的场景）。")]
        public bool volCloudTemporalEnabled = true;

        [Range(0.0f, 0.9f), Tooltip("累积权重：越高越平滑，但快速运镜拖影更明显。0 = 关闭混合。")]
        public float volCloudTemporal = 0.8f;

        [Header("· 调试")]
        [Tooltip("体积云调试视图。\n" +
                 "Coverage = 应看到【离散云团】之间留着天空；Instance = 每个 cell 的存活权重；\n" +
                 "Height = 竖向渐变；Edge = 轮廓边带；Depth = 相交起点（红 = 没穿过云层）；\n" +
                 "Scatter = 散射响应。整屏黑说明密度模型没吃到东西，不是「看得见看不见」的问题。")]
        public CloudDebugView volCloudDebug = CloudDebugView.Off;

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
