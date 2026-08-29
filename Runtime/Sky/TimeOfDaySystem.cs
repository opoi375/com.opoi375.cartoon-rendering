// Copyright (c) 2026 CartoonRendering. MIT License.

using UnityEngine;

namespace CartoonRendering
{
    /// <summary>
    /// Static helper that derives a normalized "time of day" value (0..1)
    /// from a directional light. The math matches the convention used by
    /// DanbaidongRP's original TimeOfDaySystem so that the sky shader
    /// gradient sampling stays consistent.
    /// </summary>
    public static class TimeOfDaySystem
    {
        /// <summary>
        /// Get time of day from a directional light.
        /// </summary>
        /// <param name="light">The directional light representing the sun.</param>
        /// <param name="dayLength">Multiplier applied to the result.</param>
        /// <returns>A value in <c>[0, dayLength]</c>. 0.5 means solar noon.</returns>
        public static float GetTimeOfDayFromLight(Light light, float dayLength = 1.0f)
        {
            if (light == null)
                return 0.5f;

            // Convention: light.transform.forward points FROM the light source
            // TOWARDS the scene. Y = +1 means the light is pointing straight
            // down (noon), Y = -1 means straight up (midnight).
            float y = light.transform.forward.y;

            // Map the vertical component to a [0, 1] range with 0.5 at noon.
            // forward.y > 0  -> y in [0, 1] -> (1 - y) in [0, 1] (morning)
            // forward.y < 0  -> y in [-1, 0] -> (1 + y) in [0, 1] (afternoon)
            // We split the range into two halves and shift by 3 so that the
            // night side wraps to the upper half of the 0..1 gradient strip.
            float timeOfDay = y > 0 ? -y + 1.0f : y + 3.0f;
            timeOfDay /= 4.0f;

            return Mathf.Clamp01(timeOfDay) * dayLength;
        }

        /// <summary>
        /// Returns a world-space sun direction derived from the light's
        /// transform (the direction the light is shining).
        /// </summary>
        public static Vector3 GetSunDirection(Light light)
        {
            if (light == null)
                return new Vector3(0.0f, -1.0f, 0.0f);

            return light.transform.forward.normalized;
        }
    }
}
