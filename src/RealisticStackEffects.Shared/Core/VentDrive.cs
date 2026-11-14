using System;
using System.Collections.Generic;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// How hard a stock source of steam around the engine blows, from the
    /// game's own value that drives its effect (pnwr1854, Nexus, 2026-11-13:
    /// the FEF-3's blowdown blew the same at 10 % as at 100 %). The game's
    /// particle controller names, for each effect, the port that switches it
    /// and the properties it scales: the blowdown, whistle, safety valves,
    /// dynamo, and cylinder crack by their normalized flows, 0 to 1. Sizing a
    /// source against the fastest start speed seen in the session made the
    /// first opening look full, whatever it was.
    /// </summary>
    public static class VentDrive
    {
        // The game's ParticlesPortReadersController.ParticlePortReader.ParticleProperty
        // values that scale an effect, as opposed to switching it on (0) or setting
        // how long its particles live (1, 100).
        private const int StartSize = 2, StartSpeed = 3, MaxParticles = 4, RateOverTime = 5;
        private const int StartSizeMultiplier = 101, StartSpeedMultiplier = 102, MaxParticlesMultiplier = 103, RateOverTimeMultiplier = 104;

        /// <summary>Whether a property of the game's particle controller scales an effect's steam.</summary>
        public static bool Sizes(int property) =>
            property == StartSize || property == StartSpeed || property == MaxParticles || property == RateOverTime
            || property == StartSizeMultiplier || property == StartSpeedMultiplier || property == MaxParticlesMultiplier
            || property == RateOverTimeMultiplier;

        /// <summary>
        /// Which of a reader's updaters, each given by the properties it sets,
        /// drives how much steam there is: the first that scales the effect.
        /// -1 for none: an effect only switched on and off, or set by a value
        /// that is not its flow (the cylinder cocks' drips), keeps no driver.
        /// </summary>
        public static int Driver(IReadOnlyList<IReadOnlyList<int>> updaters)
        {
            if (updaters == null)
                return -1;
            for (var i = 0; i < updaters.Count; i++)
            {
                var properties = updaters[i];
                if (properties == null)
                    continue;
                foreach (var property in properties)
                    if (Sizes(property))
                        return i;
            }
            return -1;
        }

        /// <summary>
        /// A port's value taken as the game's reader takes it (its input
        /// modifier), as a source's strength, 0 to 1.
        /// </summary>
        public static float Strength(float value, float multiplier, float offset, bool absoluteInput, bool absoluteResult)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            if (absoluteInput)
                value = Math.Abs(value);
            value = value * multiplier + offset;
            if (absoluteResult)
                value = Math.Abs(value);
            if (float.IsNaN(value))
                return 0f;
            return Math.Max(0f, Math.Min(1f, value));
        }
    }
}
