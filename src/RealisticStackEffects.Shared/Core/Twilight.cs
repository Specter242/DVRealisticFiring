using System;

namespace RealisticStackEffects.Core
{
 public static class Twilight
    {
        /// <summary>The sun's light is whole on the steam with the sun this far from overhead or less, degrees: 6 above the horizon.</summary>
        public const float SunWholeBy = 84f;

        /// <summary>And gone by this: 2 below the horizon, when the last of its disc and its glow have left low steam.</summary>
        public const float SunGoneBy = 92f;

        /// <summary>The game's light is the moon's from this far, degrees (astronomical dusk begins), wholly by the next.</summary>
        public const float MoonFrom = 96f;
        public const float MoonBy = 102f;

        /// <summary>
        /// The share of the game's directional light that falls on steam, 0
        /// to 1, with the sun <paramref name="zenith"/> degrees from
        /// overhead: all of it by day, none from sunset through dusk, and
        /// all of it again once it is the moon's.
        /// </summary>
        public static float DirectLight(float zenith)
        {
            if (float.IsNaN(zenith))
                return 1f;
            var sun = Clamp01((SunGoneBy - zenith) / (SunGoneBy - SunWholeBy));
            var moon = Clamp01((zenith - MoonFrom) / (MoonBy - MoonFrom));
            return Math.Max(Smooth(sun), Smooth(moon));
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
