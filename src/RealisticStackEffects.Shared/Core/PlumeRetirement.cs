using System;

namespace RealisticStackEffects.Core
{
    /// <summary>A retiring parcel completes its fade even after crossing the faintness threshold.</summary>
    public struct PlumeRetirement
    {
        private bool retiring;
        private float started;

        public float Fade(float now, bool retire, float duration)
        {
            if (!retiring && retire)
            {
                retiring = true;
                started = now;
            }
            if (!retiring) return 1f;
            if (!(duration > 0f)) return 0f;
            var left = Math.Max(0f, Math.Min(1f, 1f - (now - started) / duration));
            // The shader strengthens thin edges. Ease the supplied alpha
            // down to zero, rather than killing a still visible faint parcel.
            return left * left;
        }

        public static bool ShouldRetire(float age, float fadeIn, float opacity, float faint, bool formed, bool survives) =>
            !survives || age > fadeIn && formed && opacity < Math.Max(faint, PlumeRise.Invisible);
    }
}
