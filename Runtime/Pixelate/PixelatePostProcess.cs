// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace CartoonRendering
{
    /// <summary>
    /// Bayer 有序抖动矩阵尺寸。
    /// </summary>
    public enum PixelateBayerMode
    {
        /// <summary>4x4 矩阵，颗粒感更强。</summary>
        Bayer4x4 = 0,
        /// <summary>8x8 矩阵，过渡更细腻。</summary>
        Bayer8x8 = 1,
    }

    /// <summary>
    /// Volume settings for the pixelate post process.
    ///
    /// The render pass reads these values from the volume stack every frame.
    /// <see cref="intensity"/> is the master switch (default 0 = off): add the
    /// component to a Volume profile and raise Intensity to enable the effect.
    /// Local Volume zones can fade the effect in/out via the volume weight.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Pixelate Post Process")]
    public sealed class PixelatePostProcess : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Master intensity. 0 = no effect (original image), 1 = full pixelation. " +
                 "Volume weight fades this in/out.")]
        public ClampedFloatParameter intensity = new ClampedFloatParameter(0f, 0f, 1f, true);

        [Tooltip("Pixel block edge length in physical screen pixels. Larger = chunkier. " +
                 "Visual size is resolution-independent.")]
        public ClampedFloatParameter pixelSize = new ClampedFloatParameter(4f, 1f, 64f, true);

        [Tooltip("Color levels per channel. 8 or 16 recommended; 2 = two-tone posterize.")]
        public ClampedFloatParameter colorLevels = new ClampedFloatParameter(16f, 2f, 64f, true);

        [Tooltip("Bayer dither strength 0~1. 1 = one full quantization step, 0 = dither off.")]
        public ClampedFloatParameter ditherStrength = new ClampedFloatParameter(0.5f, 0f, 1f, true);

        [Tooltip("Bayer matrix size: 4x4 = grittier, 8x8 = finer transitions.")]
        public VolumeParameter<PixelateBayerMode> bayerMode = new VolumeParameter<PixelateBayerMode>
        {
            value = PixelateBayerMode.Bayer8x8,
            overrideState = true,
        };

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
