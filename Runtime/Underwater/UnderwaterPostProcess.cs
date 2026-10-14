// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>水下后处理的调试可视化模式。</summary>
    public enum UnderwaterDebug
    {
        /// <summary>正常渲染。</summary>
        Off = 0,
        /// <summary>显示线性视深（按 60 米归一化）。灰度必须有梯度，否则深度没接上。</summary>
        Depth = 1,
        /// <summary>显示雾混合因子 f（白 = 完全被雾吃掉）。这是"效果跟距离有没有关系"的直接读数。</summary>
        Fog = 2,
        /// <summary>只显示焦散图案与其深度衰减。</summary>
        Caustics = 3,
        /// <summary>显示逐通道吸收率 T（红先没、绿其次、蓝最后 —— 蓝绿就是这么来的）。</summary>
        Absorption = 4,
        /// <summary>显示没有场景深度的像素（天空 / 只被透明物体覆盖）。整屏白 = 深度图没读到。</summary>
        SkyMask = 5,
    }

    /// <summary>枚举型 Volume 参数（Volume 框架没有内置枚举参数）。</summary>
    [Serializable]
    public sealed class UnderwaterDebugParameter : VolumeParameter<UnderwaterDebug>
    {
        public UnderwaterDebugParameter(UnderwaterDebug value, bool overrideState = false)
            : base(value, overrideState) { }
    }

    /// <summary>
    /// Volume settings for the underwater post process.
    ///
    /// <see cref="intensity"/> is the master switch (default 0 = off). A LOCAL
    /// Volume zone (trigger collider under the water surface) carries the
    /// override; the Volume system fades the effect in when the camera enters
    /// and out when it leaves.
    ///
    /// The look is built from two independent terms, which is what makes it
    /// read as water instead of a flat tint:
    ///   * per-channel absorption <see cref="absorptionRate"/> ×
    ///     <see cref="absorptionColor"/> removes radiance with distance
    ///     (red first → the scene drifts blue-green on its own);
    ///   * in-scattering <see cref="fogDensity"/> toward <see cref="color"/> /
    ///     <see cref="deepColor"/> adds the water's own colour back.
    /// Everything below the surface parameters is measured against
    /// <see cref="waterSurfaceY"/>, so depth-dependent extras (caustics,
    /// vignette) die out with depth instead of covering the whole screen.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Underwater Post Process")]
    public sealed class UnderwaterPostProcess : VolumeComponent, IPostProcessComponent
    {
        // ---- 主控 ---------------------------------------------------------

        [Tooltip("Master intensity, 0 = off. The volume zone fades this in/out " +
                 "via the volume weight; it also scales every other term.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 4f, true);

        [Tooltip("Fog / water colour the scene converges to in the distance " +
                 "(shallow water). Keep it darker than you think: it is added " +
                 "on top of an already lit scene.")]
        public ColorParameter color = new ColorParameter(new Color(0.08f, 0.42f, 0.55f, 1f), true);

        [Tooltip("Deep-water fog colour, blended in by how far below " +
                 "waterSurfaceY the pixel is. Light dies with depth, so this " +
                 "should be darker and cooler than the shallow colour.")]
        public ColorParameter deepColor = new ColorParameter(new Color(0.012f, 0.075f, 0.12f, 1f), true);

        [Tooltip("Distance below the surface at which the fog colour is fully " +
                 "the deep colour (metres).")]
        public MinFloatParameter depthToFullColor = new MinFloatParameter(9f, 0.01f, true);

        [Tooltip("Upper bound of the in-scattering mix. 1 = distant geometry " +
                 "disappears completely into the fog; 0.7 keeps a third of the " +
                 "scene visible even far away.")]
        public ClampedFloatParameter tintStrength = new ClampedFloatParameter(0.85f, 0f, 1f, true);

        // ---- 雾与吸收 -----------------------------------------------------

        [Tooltip("In-scattering growth per metre. 0.03-0.06 is the usual range " +
                 "for a pool-sized body of water; 0.2+ is muddy.")]
        public MinFloatParameter fogDensity = new MinFloatParameter(0.05f, 0f, true);

        [Tooltip("Per-channel absorption weights (NOT a colour - no sRGB " +
                 "conversion is applied). Red absorbs fastest in real water, so " +
                 "R > G > B. (1, 0.45, 0.22) is a typical clear-water ratio.")]
        public ColorParameter absorptionColor = new ColorParameter(new Color(1f, 0.45f, 0.22f, 1f), true);

        [Tooltip("Absorption scale per metre. The per-channel coefficient is " +
                 "absorptionRate * absorptionColor. 0.05-0.12 reads well.")]
        public MinFloatParameter absorptionRate = new MinFloatParameter(0.075f, 0f, true);

        // ---- 水面以下的"位置感" ------------------------------------------

        [Tooltip("World Y of the water surface. Everything depth-dependent " +
                 "(deep colour, caustics) is measured against this, so it must " +
                 "match the actual water plane.")]
        public FloatParameter waterSurfaceY = new FloatParameter(0f, true);

        [Tooltip("Caustic light bands projected onto surfaces. 0 = off.")]
        public ClampedFloatParameter causticsIntensity = new ClampedFloatParameter(0.7f, 0f, 4f, true);

        [Tooltip("Caustic pattern frequency (cycles per metre). pool-sized bands are " +
                 "0.4-0.8; below ~0.2 they smear into soft blobs.")]
        public MinFloatParameter causticsScale = new MinFloatParameter(0.55f, 0.001f, true);

        [Tooltip("Caustic drift speed (radians per second). Keep it slow - " +
                 "fast caustics look like TV static.")]
        public FloatParameter causticsSpeed = new FloatParameter(0.9f, true);

        [Tooltip("How far below the surface caustics survive (metres). Sunlight " +
                 "does not reach the bottom of a deep pool, so this is what " +
                 "stops the floor from looking lit by a lamp.")]
        public MinFloatParameter causticsDepthFade = new MinFloatParameter(12f, 0.01f, true);

        // ---- 波动扭曲 -----------------------------------------------------

        [Tooltip("Screen-space refraction wobble applied to opaque pixels " +
                 "(UV units). 0.004-0.012 is subtle; above ~0.02 the scene " +
                 "looks like jelly.")]
        public ClampedFloatParameter waveStrength = new ClampedFloatParameter(0.007f, 0f, 0.05f, true);

        [Tooltip("Wave frequency across the screen (multiplies the UV).")]
        public MinFloatParameter waveScale = new MinFloatParameter(1f, 0.01f, true);

        [Tooltip("Wave animation speed.")]
        public FloatParameter waveSpeed = new FloatParameter(1.2f, true);

        // ---- 风格化 -------------------------------------------------------

        [Tooltip("Corner darkening, scaled by the local fog amount so it only " +
                 "appears where the water is actually murky.")]
        public ClampedFloatParameter vignette = new ClampedFloatParameter(0.35f, 0f, 2f, true);

        [Tooltip("Cartoon banding: quantise the fog mix into N steps for hard " +
                 "stylized depth bands. < 1.5 = off (smooth).")]
        public FloatParameter banding = new FloatParameter(0f, true);

        [Tooltip("Debug views. Fog is the fastest way to tell \"depth-driven\" " +
                 "apart from \"flat tint\"; SkyMask all-white means the depth " +
                 "texture never reached this pass.")]
        public UnderwaterDebugParameter debug = new UnderwaterDebugParameter(UnderwaterDebug.Off);

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
