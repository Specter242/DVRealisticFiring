using System;

namespace RealisticFiring.Core
{
 public readonly struct SteamDraft
    {
        /// <summary>Air entrained per kg of blower steam.</summary>
        public const float BlowerEntrainment = 5f;

        /// <summary>The share of the draft a shut damper leaves, as in the game.</summary>
        public const float ShutDamper = 0.1f;

        /// <param name="maxBlower">Blower steam wide open at working pressure, kg/s.</param>
        /// <param name="natural">The draft with no steam, kg/s of air.</param>
        /// <param name="exhaustEntrainment">Air per kg of exhaust steam (the engine's own).</param>
        /// <param name="workingPressure">The boiler's working pressure, absolute bar.</param>
        public SteamDraft(float maxBlower, float natural, float exhaustEntrainment, float workingPressure)
        {
            MaxBlower = Math.Max(0f, maxBlower);
            Natural = Math.Max(0f, natural);
            ExhaustEntrainment = Math.Max(0f, exhaustEntrainment);
            WorkingPressure = Math.Max(1.5f, workingPressure);
        }

        public float MaxBlower { get; }
        public float Natural { get; }
        public float ExhaustEntrainment { get; }
        public float WorkingPressure { get; }

        /// <summary>The blower's steam at <paramref name="pressure"/> absolute bar with its valve at <paramref name="valve"/>, kg/s.</summary>
        public float BlowerSteam(float pressure, float valve)
        {
            var share = Clamp01((pressure - 1f) / (WorkingPressure - 1f));
            return MaxBlower * Clamp01(valve) * share;
        }

        /// <summary>The air drawn through the fire, kg/s.</summary>
        public float Air(float exhaustSteam, float blowerSteam, float damper) =>
            (Natural + ExhaustEntrainment * Math.Max(0f, exhaustSteam) + BlowerEntrainment * Math.Max(0f, blowerSteam))
            * (ShutDamper + (1f - ShutDamper) * Clamp01(damper));

        /// <summary>The most steam and air the stack passes, kg/s, as the game sums them: the engine at full flow, damper open.</summary>
        public float MaxTotal(float engineMaxFlow) => engineMaxFlow + Natural + ExhaustEntrainment * engineMaxFlow;

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
