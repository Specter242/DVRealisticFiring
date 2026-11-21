namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Bounds and safe clamping for the player-facing chimney plume sliders
    /// (steam visibility and wind strength), so hand-edited or corrupted
    /// Settings.xml values cannot produce NaN, infinity, or out-of-range
    /// multipliers.
    /// </summary>
    public static class PlumeSettings
    {
        public const float DefaultSteamVisibility = 1f;
        public const float MinSteamVisibility = 0.5f;
        public const float MaxSteamVisibility = 2f;

        public const float DefaultWindStrength = 1f;
        public const float MinWindStrength = 0f;
        public const float MaxWindStrength = 2f;

        /// <summary>Clamps <paramref name="value"/> to [<see cref="MinSteamVisibility"/>, <see cref="MaxSteamVisibility"/>], falling back to <see cref="DefaultSteamVisibility"/> when NaN or infinite.</summary>
        public static float ClampSteamVisibility(float value) =>
            Clamp(value, MinSteamVisibility, MaxSteamVisibility, DefaultSteamVisibility);

        /// <summary>Clamps <paramref name="value"/> to [<see cref="MinWindStrength"/>, <see cref="MaxWindStrength"/>], falling back to <see cref="DefaultWindStrength"/> when NaN or infinite.</summary>
        public static float ClampWindStrength(float value) =>
            Clamp(value, MinWindStrength, MaxWindStrength, DefaultWindStrength);

        /// <summary>Scales a weather whiteness value by the clamped steam visibility multiplier; preserves physics identically at 1.0.</summary>
        public static float ScaleSteamWhiteness(float whiteness, float steamVisibility)
        {
            if (float.IsNaN(whiteness) || float.IsInfinity(whiteness) || whiteness <= 0f)
                return 0f;
            return whiteness * ClampSteamVisibility(steamVisibility);
        }

        private static float Clamp(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return fallback;
            return value < min ? min : value > max ? max : value;
        }
    }
}
