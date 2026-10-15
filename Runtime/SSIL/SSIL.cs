// ============================================================================
// SSIL.cs
// ----------------------------------------------------------------------------
// 屏幕空间间接光照（SSILVB）的 Volume 参数。
//
// 算法来自论文 "Screen Space Indirect Lighting with Visibility Bitmask"
// （Therrien / Levesque / Gilet, arXiv:2301.11376）：GTAO 的升级版，用 32 位
// 可见性位掩码替代"两个地平线角"，一次追踪同时产出：
//   - 环境光遮蔽（AO）—— 比 SSAO / GTAO 伪影更少，薄物体（栅栏、栏杆）不再
//     被过度压暗（表面带固定厚度，光可以从背后绕过去）
//   - 间接光照（GI）—— 沿采样方向收集邻居物体反弹过来的颜色
//
// intensity 是总开关（默认 0 = 关闭），渲染通道每帧从 Volume 栈里读。
// 挂在 Global Volume 上全场景生效；也可以挂在 Local Volume 上按区域开关。
//
// 详细用法见 Shaders/PostProcessing/SSIL.shader 头部注释。
// ============================================================================

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>SSIL 的调试可视化模式。</summary>
    public enum SSILDebug
    {
        /// <summary>正常渲染。</summary>
        Off = 0,
        /// <summary>显示可见性（白 = 无遮挡，黑 = 全遮挡）。</summary>
        Visibility = 1,
        /// <summary>显示间接光贡献（放大后调强度用）。</summary>
        Indirect = 2,
    }

    /// <summary>枚举型 Volume 参数（Volume 框架没有内置枚举参数）。</summary>
    [Serializable]
    public sealed class SSILDebugParameter : VolumeParameter<SSILDebug>
    {
        public SSILDebugParameter(SSILDebug value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Screen Space Indirect Light")]
    public sealed class SSIL : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("总强度。0 = 完全关闭（默认）。\n\n" +
                 "和包内其他 Volume 组件一致，默认是关的：VolumeManager 对没被任何 " +
                 "Volume 覆盖的组件也会返回一个默认实例，默认非 0 会变成「没放 " +
                 "Volume 也生效」。所以要在 Volume 里把它调起来。")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 8f);

        [Tooltip("环境光遮蔽强度。1 = 完整的 AO，0 = 只留间接光。")]
        public ClampedFloatParameter aoStrength = new ClampedFloatParameter(1f, 0f, 1f);

        [Tooltip("间接光照强度。间接光默认是乘性叠加（场景色 × (1 + 间接光)），" +
                 "所以即使开很大也不会像加雾一样糊成一片。")]
        public ClampedFloatParameter indirectStrength = new ClampedFloatParameter(1f, 0f, 4f);

        [Tooltip("间接光染色。白色 = 忠实还原反弹光的颜色；想做风格化可以往暖 / 冷偏。")]
        public ColorParameter indirectTint = new ColorParameter(Color.white, true);

        [Header("采样")]
        [Tooltip("采样半径（世界米）。半径内的物体才会产生遮挡 / 反弹光。" +
                 "越大越柔和也越容易糊细节，采样数不够时建议同步调大采样数。")]
        public ClampedFloatParameter radius = new ClampedFloatParameter(2f, 0.1f, 10f);

        [Tooltip("表面厚度（世界米）。固定厚度是本算法的核心：光可以从薄物体背后" +
                 "绕过去，栅栏 / 栏杆不再压黑一片。太大会让厚物体边缘漏光。")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(0.5f, 0.01f, 5f);

        [Tooltip("每个切片（屏幕方向）向外的采样次数。4 是质量与开销的平衡点；" +
                 "半径调大时建议同步调大，否则会漏掉细遮挡物。")]
        public ClampedIntParameter sampleCount = new ClampedIntParameter(4, 1, 32);

        [Tooltip("切片（屏幕方向）数量，每个切片内做一次 32 扇区的位掩码追踪。" +
                 "4~6 够用，8 接近收敛。")]
        public ClampedIntParameter sliceCount = new ClampedIntParameter(4, 1, 16);

        [Tooltip("采样抖动强度。打散采样图案，配合模糊去掉条带；" +
                 "抖动是静态的（按像素坐标取种子），静止画面不会闪。")]
        public ClampedFloatParameter jitter = new ClampedFloatParameter(1f, 0f, 1f);

        [Header("范围")]
        [Tooltip("效果最远距离（米）。远处像素的采样密度不够，出噪点不如不画。")]
        public ClampedFloatParameter maxDistance = new ClampedFloatParameter(60f, 1f, 500f);

        [Tooltip("从最远距离的百分之几开始淡出，避免在最远距离上一刀切。")]
        public ClampedFloatParameter distanceFade = new ClampedFloatParameter(0.7f, 0f, 0.99f);

        [Header("质量")]
        [Tooltip("模糊步长（低分辨率纹素）。追踪在半分辨率跑，必须模糊才有平滑结果；" +
                 "1 = 5 抽头高斯，调大等于加一层柔化。")]
        public ClampedFloatParameter blurRadius = new ClampedFloatParameter(1f, 0.25f, 4f);

        [Header("风格化")]
        [Tooltip("卡通分层：把 AO 可见性量化成 N 档，得到漫画式的硬边阴影。" +
                 "0 = 关闭（平滑），2~4 是比较好用的档数。")]
        public ClampedIntParameter banding = new ClampedIntParameter(0, 0, 12);

        [Header("调试")]
        [Tooltip("可视化中间结果。Visibility 看遮挡，Indirect 看间接光贡献。")]
        public SSILDebugParameter debug = new SSILDebugParameter(SSILDebug.Off);

        /// <inheritdoc/>
        public bool IsActive()
        {
            return intensity.value > 0.001f &&
                   (aoStrength.value > 0.001f || indirectStrength.value > 0.001f);
        }

        /// <inheritdoc/>
        public bool IsTileCompatible()
        {
            return false;
        }
    }
}
