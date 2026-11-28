using System;

namespace RealisticFiring.Core
{
 public static class BoilerResize
    {
        /// <summary>The volume must change by more than this share to be a resize.</summary>
        public const float VolumeChange = 1e-3f;

        /// <summary>The water's share of the boiler, kg per L, may differ by this share.</summary>
        public const float ShareTolerance = 0.02f;

        /// <summary>And its temperature by this much, °C.</summary>
        public const float TemperatureTolerance = 2f;

        /// <summary>
        /// Whether a new boiler (<paramref name="newMass"/> kg of water at
        /// <paramref name="newTemperature"/> °C in <paramref name="newVolume"/> L)
        /// is the old one resized: a different volume holding the old water at
        /// its temperature and share of the boiler.
        /// </summary>
        public static bool CarriesWater(float oldMass, float oldVolume, float oldTemperature,
            float newMass, float newVolume, float newTemperature)
        {
            if (!Finite(oldMass) || !Finite(oldVolume) || !Finite(newMass) || !Finite(newVolume)
                || oldVolume <= 0f || newVolume <= 0f || oldMass < 0f || newMass < 0f)
                return false;
            if (Math.Abs(newVolume - oldVolume) <= VolumeChange * oldVolume)
                return false;
            if (oldMass <= 0f || newMass <= 0f)
                return oldMass <= 0f && newMass <= 0f;
            if (!Finite(oldTemperature) || !Finite(newTemperature) || Math.Abs(newTemperature - oldTemperature) > TemperatureTolerance)
                return false;
            var oldShare = oldMass / oldVolume;
            var newShare = newMass / newVolume;
            return Math.Abs(newShare - oldShare) <= ShareTolerance * oldShare;
        }

        /// <summary>
        /// How much of each thing carried in the water to keep for each kg in
        /// the old: the new water over the old, so its concentration stands.
        /// </summary>
        public static float Carried(float oldMass, float newMass) =>
            Finite(oldMass) && Finite(newMass) && oldMass > 0f && newMass >= 0f ? newMass / oldMass : 1f;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
