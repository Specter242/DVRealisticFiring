using System;

namespace RealisticStackEffects.Core
{
 public enum SmokeQuality
    {
        High,
        Medium,
        Low,
    }

    public static class SmokeDetail
    {
        /// <summary>Whether <paramref name="quality"/> draws the plume at half resolution where it can.</summary>
        public static bool HalfResolution(SmokeQuality quality) => quality != SmokeQuality.High;

        /// <summary>
        /// The share of the plume's particles thrown at <paramref name="quality"/>,
        /// with the plume drawn at half resolution or not.
        /// </summary>
        public static float ParticleShare(SmokeQuality quality, bool halfResolution)
        {
            if (quality == SmokeQuality.High)
                return 1f;
            var low = quality == SmokeQuality.Low;
            return halfResolution ? (low ? 0.5f : 1f) : (low ? 0.25f : 0.5f);
        }

 public static float Faint(SmokeQuality quality) =>
            quality == SmokeQuality.Low ? 0.05f : quality == SmokeQuality.Medium ? 0.03f : PlumeRise.Invisible;

        /// <summary>
        /// The particles to throw for <paramref name="wanted"/> at
        /// <paramref name="share"/>: <paramref name="due"/> carries the
        /// fraction left over to the next call, so on average the share of
        /// what is wanted, and at least <paramref name="least"/> (a puff's
        /// core) whenever any is wanted.
        /// </summary>
        public static int Scaled(int wanted, float share, ref float due, int least = 0)
        {
            if (wanted <= 0)
                return 0;
            if (share >= 1f)
                return wanted;
            due += wanted * Math.Max(0f, share);
            var count = (int)Math.Floor(due);
            due -= count;
            return Math.Min(wanted, Math.Max(least, count));
        }
    }
}
