using System;

namespace RealisticFiring.Core
{
 public static class ColdWater
    {
        private static readonly float[] Volumes = { 1.0002f, 1.0003f, 1.0018f, 1.0044f, 1.0078f, 1.0121f, 1.0171f, 1.0228f, 1.0290f, 1.0359f, 1.0435f };

        /// <summary>A kg of water's volume at <paramref name="temperature"/> °C, L (0 to 100 °C).</summary>
        public static float SpecificVolume(float temperature)
        {
            var t = Math.Max(0f, Math.Min(100f, temperature)) / 10f;
            var i = Math.Min(Volumes.Length - 2, (int)t);
            return Volumes[i] + (Volumes[i + 1] - Volumes[i]) * (t - i);
        }

        /// <summary>The share of its volume at boiling that water at <paramref name="temperature"/> °C has lost, 0 at 100 °C.</summary>
        public static float Shrink(float temperature) => 1f - SpecificVolume(temperature) / Volumes[Volumes.Length - 1];
    }
}
