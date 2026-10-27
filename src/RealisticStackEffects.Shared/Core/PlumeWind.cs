using System;

namespace RealisticStackEffects.Core
{
 public static class PlumeWind
    {
        /// <summary>A steady breeze at the stack's height on an ordinary day, in m/s.</summary>
        public const float Breeze = 2f;

        /// <summary>The calmest spells blow this share of the breeze.</summary>
        public const float CalmestSpell = 0.15f;

        /// <summary>Full rain adds this much, and a full thunderstorm this much, in m/s.</summary>
        public const float RainWind = 3f;
        public const float StormWind = 5f;

        /// <summary>Gusts blow this share above or below the wind.</summary>
        public const float Gusts = 0.3f;

        /// <summary>
        /// The wind's speed, in m/s: from the weather's rain and thunder (0 to
        /// 1), a slow noise for the spell (0 calm to 1 breeze, over minutes),
        /// a quick one for gusts (0 to 1), and the setting's scale.
        /// </summary>
        public static float Speed(float rain, float thunder, float spell, float gust, float scale)
        {
            var breeze = Breeze * (CalmestSpell + (1f - CalmestSpell) * Clamp01(spell));
            var wind = breeze + RainWind * Clamp01(rain) + StormWind * Clamp01(thunder);
            return Math.Max(0f, wind * (1f + Gusts * (2f * Clamp01(gust) - 1f)) * scale);
        }

        /// <summary>
        /// The way the wind blows, on the ground (x east, z north), from the
        /// weather's wind direction in degrees: the way its clouds drift.
        /// </summary>
        public static (float X, float Z) Toward(float degrees)
        {
            var radians = degrees * Math.PI / 180.0;
            return ((float)Math.Sin(radians), (float)Math.Cos(radians));
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
