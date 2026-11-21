using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// Optional chimney geometry hint, in metres, supplied by a host or another
    /// mod when a locomotive's stack mesh cannot be read in game (or when an
    /// explicit dimension is preferred). Any null or invalid field falls back
    /// to the mesh measurement or the plume's defaults.
    /// </summary>
    public struct StackGeometryHint
    {
        public const float DefaultBoreRadius = 0.25f;
        public const float DefaultOutsideRadius = 0.32f;
        public const float MinBoreRadius = 0.08f;
        public const float MinOutsideRadius = 0.10f;
        public const float MaxOutsideRadius = 0.50f;
        public const float MaxLipHeight = 3.0f;

        /// <summary>Height of the stack's lip above the smoke emitter along the stack's axis, m (0 to 3).</summary>
        public float? LipHeight;

        /// <summary>Inside bore radius at the stack's lip, m (0.08 to 0.5).</summary>
        public float? BoreRadius;

        /// <summary>Outside radius at the stack's lip, m (0.1 to 0.5).</summary>
        public float? OutsideRadius;

        /// <summary>Whether at least one dimension is set.</summary>
        public bool HasAny => LipHeight.HasValue || BoreRadius.HasValue || OutsideRadius.HasValue;

        /// <summary>
        /// Returns a copy whose fields are finite and clamped to physical
        /// bounds, dropping NaN, infinite, negative, or non-positive radii,
        /// and ensuring <see cref="BoreRadius"/> never exceeds <see cref="OutsideRadius"/>.
        /// </summary>
        public StackGeometryHint Sanitize()
        {
            var lip = SanitizeLip(LipHeight);
            var outside = SanitizeRadius(OutsideRadius, MinOutsideRadius, MaxOutsideRadius);
            var bore = SanitizeRadius(BoreRadius, MinBoreRadius, MaxOutsideRadius);
            if (bore.HasValue && outside.HasValue && bore.Value > outside.Value)
                bore = outside.Value;
            return new StackGeometryHint
            {
                LipHeight = lip,
                BoreRadius = bore,
                OutsideRadius = outside,
            };
        }

        /// <summary>
        /// Applies the sanitized fields of this hint onto the baseline
        /// <paramref name="lipHeight"/>, <paramref name="boreRadius"/>, and
        /// <paramref name="outsideRadius"/>, keeping <c>MinBoreRadius &lt;= boreRadius &lt;= outsideRadius &lt;= MaxOutsideRadius</c>.
        /// </summary>
        public void Apply(ref float? lipHeight, ref float boreRadius, ref float outsideRadius)
        {
            var clean = Sanitize();
            if (!clean.HasAny)
                return;
            if (clean.LipHeight.HasValue)
                lipHeight = clean.LipHeight.Value;
            if (clean.OutsideRadius.HasValue)
                outsideRadius = clean.OutsideRadius.Value;
            if (clean.BoreRadius.HasValue)
            {
                boreRadius = clean.BoreRadius.Value;
                if (!clean.OutsideRadius.HasValue && boreRadius > outsideRadius)
                    outsideRadius = Clamp(boreRadius, MinOutsideRadius, MaxOutsideRadius);
            }
            outsideRadius = Clamp(outsideRadius, MinOutsideRadius, MaxOutsideRadius);
            boreRadius = Clamp(boreRadius, MinBoreRadius, outsideRadius);
        }

        private static float? SanitizeLip(float? value)
        {
            if (!value.HasValue)
                return null;
            var v = value.Value;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
                return null;
            return v > MaxLipHeight ? MaxLipHeight : v;
        }

        private static float? SanitizeRadius(float? value, float min, float max)
        {
            if (!value.HasValue)
                return null;
            var v = value.Value;
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0f)
                return null;
            return Clamp(v, min, max);
        }

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
