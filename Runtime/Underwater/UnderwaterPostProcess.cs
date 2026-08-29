// Copyright (c) 2026 CartoonRendering. MIT License.

using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CartoonRendering
{
    /// <summary>
    /// Volume settings for the underwater post process (colour-only stage).
    ///
    /// The render pass reads these values from the volume stack every frame.
    /// <see cref="intensity"/> is the master switch (default 0 = off). A LOCAL
    /// Volume zone (Underwater Setup menu) carries the override at full
    /// intensity; the Volume system fades the effect in when the camera enters
    /// the zone's collider and out when it leaves.
    /// </summary>
    [Serializable]
    [VolumeComponentMenu("CartoonRendering/Underwater Post Process")]
    public sealed class UnderwaterPostProcess : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Master intensity. 0 = no effect. The volume zone fades this " +
                 "in/out via the volume weight.")]
        public FloatParameter intensity = new FloatParameter(0f, true);

        [Tooltip("Underwater tint colour. The scene blends toward this colour.")]
        public ColorParameter color = new ColorParameter(new Color(0.08f, 0.42f, 0.55f, 1f), true);

        [Tooltip("How far the scene blends toward the tint colour at full " +
                 "intensity (0..1). 1 = the whole screen becomes the tint.")]
        public FloatParameter tintStrength = new FloatParameter(0.7f, true);

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
