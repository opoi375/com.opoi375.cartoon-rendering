// ============================================================================
// VolumetricLight.cs
// ----------------------------------------------------------------------------
// 体积光（上帝光 / 光柱）的 Volume 参数。
//
// intensity 是总开关（默认 0 = 关闭），渲染通道每帧从 Volume 栈里读。
// 挂在 Global Volume 上就是全场景生效；也可以挂在带碰撞体的 Local Volume 上，
// 让相机进出洞穴 / 走廊 / 水下时淡入淡出（Volume 系统按 blendDistance 算权重）。
//
// 只支持主方向光：平行光的散射在屏幕空间最好实现，也是"上帝光"的典型场景。
// 聚光灯 / 点光需要遍历附加光并采样它们的阴影切片，本包暂不覆盖。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>体积光的调试可视化模式。</summary>
    public enum VolumetricLightDebug
    {
        /// <summary>正常渲染。</summary>
        Off = 0,
        /// <summary>显示主光阴影采样结果：能看到明暗分区才说明阴影关键字接上了。</summary>
        Shadow = 1,
        /// <summary>显示步数热力图。</summary>
        Steps = 2,
        /// <summary>显示场景距离（按最远步进距离归一化）。</summary>
        SceneDepth = 3,
    }

    /// <summary>枚举型 Volume 参数（Volume 框架没有内置枚举参数）。</summary>
    [Serializable]
    public sealed class VolumetricLightDebugParameter : VolumeParameter<VolumetricLightDebug>
    {
        public VolumetricLightDebugParameter(VolumetricLightDebug value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Volumetric Light")]
    public sealed class VolumetricLight : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("总强度。0 = 完全关闭（默认）。\n\n" +
                 "和包内其他 Volume 组件一致，默认是关的：VolumeManager 对没被任何 " +
                 "Volume 覆盖的组件也会返回一个默认实例，默认非 0 的话会变成「没放 " +
                 "Volume 也生效」。所以要在 Volume 里把它调起来。")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 8f);

        [Tooltip("散射着色，一般取光源颜色。亮部会被 Bloom 拉出光晕，所以建议配合 HDR 光色。")]
        public ColorParameter tint = new ColorParameter(new Color(1f, 0.88f, 0.68f, 1f), true);

        [Tooltip("散射系数（每米的消光量）。0.01~0.05 是常见区间，太大整个画面会被雾糊住。")]
        public ClampedFloatParameter density = new ClampedFloatParameter(0.02f, 0f, 0.5f);

        [Tooltip("Henyey-Greenstein 相位函数的 g。正数 = 前向散射（迎着光看最亮，即经典光柱），" +
                 "0 = 各向同性，负数 = 背向散射。")]
        public ClampedFloatParameter anisotropy = new ClampedFloatParameter(0.55f, -0.95f, 0.95f);

        [Header("范围")]
        [Tooltip("最远步进距离（米）。远处够不到的部分不做步进，是主要的性能旋钮之一。")]
        public ClampedFloatParameter maxDistance = new ClampedFloatParameter(70f, 1f, 500f);

        [Tooltip("从最远距离的百分之几处开始淡出，避免在最远距离上出现一刀切的硬边。")]
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(0.7f, 0f, 0.99f);

        [Header("高度雾")]
        [Tooltip("高度雾起始高度（世界 Y，一般填地面高度）。")]
        public FloatParameter heightStart = new FloatParameter(0f, true);

        [Tooltip("高度雾衰减系数：海拔每升高 1/该值 米，密度衰减到 1/e。0.02 ≈ 50 米。" +
                 "填 0 关闭高度雾。")]
        public ClampedFloatParameter heightFalloff = new ClampedFloatParameter(0.02f, 0f, 1f);

        [Header("阴影")]
        [Tooltip("阴影对散射的压制比例。1 = 阴影里完全没有散射（光柱最锐利），" +
                 "0 = 忽略阴影（变成均匀体积雾）。")]
        public ClampedFloatParameter shadowStrength = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("阴影采样沿指向光源方向的偏移（米）。步进采样点常常紧贴几何体，" +
                 "不偏会出摩尔纹式闪烁；太大光柱边缘会离地。")]
        public ClampedFloatParameter shadowBias = new ClampedFloatParameter(0.05f, 0f, 2f);

        [Header("质量")]
        [Tooltip("步进次数。24 是质量与开销的平衡点；低于 12 条带会明显，" +
                 "高于 40 收益很小。")]
        public ClampedIntParameter stepCount = new ClampedIntParameter(24, 4, 64);

        [Tooltip("步进起点抖动强度，用来打散步进条带。静态抖动所以不会闪烁，靠双边上采样抹平。")]
        public ClampedFloatParameter jitter = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("双边上采样的抽头间距。1 = 正好取相邻四个低分辨率纹素；调大等于加一层模糊。")]
        public ClampedFloatParameter softness = new ClampedFloatParameter(1f, 0.25f, 4f);

        [Header("尘埃噪声")]
        [Tooltip("噪声强度。让光柱有絮状 / 尘埃感，0 = 均匀介质（更干净但更「塑料」）。")]
        public ClampedFloatParameter noiseStrength = new ClampedFloatParameter(0.3f, 0f, 1f);

        [Tooltip("噪声频率（每米）。0.05~0.2 之间比较像空气尘埃。")]
        public ClampedFloatParameter noiseScale = new ClampedFloatParameter(0.1f, 0.001f, 2f);

        [Tooltip("噪声漂移速度（米/秒）。0 = 完全静止。")]
        public ClampedFloatParameter noiseSpeed = new ClampedFloatParameter(0.05f, 0f, 5f);

        [Header("风格化")]
        [Tooltip("卡通分层：把散射亮度量化成 N 档，得到漫画式的硬边光带。" +
                 "0 = 关闭，4~6 是比较好用的档数。")]
        public ClampedIntParameter banding = new ClampedIntParameter(0, 0, 12);

        [Header("调试")]
        [Tooltip("可视化中间结果。Shadow 模式用来确认主光阴影有没有接上 —— " +
                 "如果整屏是纯白，说明阴影关键字没生效，效果会退化成均匀雾。")]
        public VolumetricLightDebugParameter debug =
            new VolumetricLightDebugParameter(VolumetricLightDebug.Off);

        /// <inheritdoc/>
        public bool IsActive()
        {
            return intensity.value > 0.001f;
        }

        /// <inheritdoc/>
        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
