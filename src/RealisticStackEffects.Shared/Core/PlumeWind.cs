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

        /// <summary>Reference height above the stack lip for surface-layer wind shear, in m.</summary>
        public const float ShearReferenceHeight = 10f;

        /// <summary>Power-law exponent for open-country surface-layer wind shear aloft.</summary>
        public const float ShearExponent = 0.16f;

        /// <summary>Maximum finite wind shear multiplier aloft.</summary>
        public const float MaxShearFactor = 2f;

        /// <summary>Diurnal thermal inversion ceiling under calm dawn stability, in m above the stack.</summary>
        public const float InversionLowCeiling = 25f;

        /// <summary>Diurnal thermal inversion ceiling as stability weakens before vanishing, in m above the stack.</summary>
        public const float InversionHighCeiling = 55f;

        // Wind speeds (m/s) and rain (0 to 1) over which mechanical and convective mixing break up an inversion.
        private const float CalmInversionWind = 0.5f;
        private const float MixedInversionWind = 3f;
        private const float MixedInversionRain = 0.35f;

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
        /// Surface-layer wind shear multiplier at <paramref name="height"/> m
        /// above the stack: 1 at or below the lip (or for NaN), increasing
        /// smoothly aloft up to <see cref="MaxShearFactor"/> so near-stack
        /// tuning holds while tall plumes bend naturally.
        /// </summary>
        public static float ShearFactor(float height)
        {
            if (float.IsPositiveInfinity(height))
                return MaxShearFactor;
            if (!(height > 0f))
                return 1f;
            return Math.Min(MaxShearFactor, (float)Math.Pow(1.0 + height / ShearReferenceHeight, ShearExponent));
        }

        /// <summary>
        /// The wind speed <paramref name="height"/> m above the stack, in m/s,
        /// strengthening smoothly from <paramref name="surfaceSpeed"/> at the lip.
        /// Returns 0 for non-positive or non-finite surface speeds.
        /// </summary>
        public static float AtHeight(float surfaceSpeed, float height)
        {
            if (!IsFinite(surfaceSpeed) || !(surfaceSpeed > 0f))
                return 0f;
            return surfaceSpeed * ShearFactor(height);
        }

        /// <summary>
        /// Strength of the diurnal thermal inversion layer, 0 (none: midday,
        /// breezy, or rainy) to 1 (strong calm dawn/pre-dawn radiative stability),
        /// from the local <paramref name="hour"/> (0 to 24), surface
        /// <paramref name="windSpeed"/> (m/s), and <paramref name="rain"/> (0 to 1).
        /// </summary>
        public static float InversionStrength(float hour, float windSpeed, float rain)
        {
            var time = DiurnalStability(hour);
            if (!(time > 0f))
                return 0f;
            var wind = 1f - SmoothStep(CalmInversionWind, MixedInversionWind, Math.Max(0f, windSpeed));
            if (!(wind > 0f))
                return 0f;
            var dry = 1f - SmoothStep(0f, MixedInversionRain, Clamp01(rain));
            return time * wind * dry;
        }

        /// <summary>
        /// Height of the diurnal thermal inversion ceiling above the stack, in
        /// m (~25 to 55 m during calm dawn/night hours), or
        /// <see cref="float.PositiveInfinity"/> when no inversion applies.
        /// </summary>
        public static float InversionCeiling(float hour, float windSpeed, float rain)
        {
            var strength = InversionStrength(hour, windSpeed, rain);
            if (!(strength > 0f))
                return float.PositiveInfinity;
            return InversionHighCeiling - (InversionHighCeiling - InversionLowCeiling) * strength;
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

        // Radiative boundary-layer stability by clock hour (0 to 24): strongest
        // before and around dawn (03:00 to 06:30), burning off through morning
        // (06:30 to 09:00), absent by day (09:00 to 19:00), and rebuilding
        // gently after sunset (19:00 to 03:00).
        private static float DiurnalStability(float hour)
        {
            var h = ((hour % 24f) + 24f) % 24f;
            if (h >= 9f && h <= 19f)
                return 0f;
            if (h >= 3f && h <= 6.5f)
                return 1f;
            if (h > 6.5f && h < 9f)
                return 1f - SmoothStep(6.5f, 9f, h);
            if (h > 19f && h <= 23f)
                return 0.75f * SmoothStep(19f, 23f, h);
            var sinceLate = h > 23f ? h - 23f : h + 1f;
            return 0.75f + 0.25f * SmoothStep(0f, 4f, sinceLate);
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            var t = Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
