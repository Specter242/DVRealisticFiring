using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Tunables of the game's stock firebox (LocoSim FireboxDefinition) and the
    /// maximums the stock model derives from them. Defaults match the
    /// definition's field initializers; locomotive prefabs may override them.
    /// </summary>
    public sealed class FireboxParameters
    {
        /// <summary>Coal heat content used by the stock model, in J/kg.</summary>
        public const float CoalHeatContent = 32_000_000f;

        /// <summary>Coal the firebox can hold, in kg.</summary>
        public float MaxCoalCapacity { get; }

        /// <summary>Seconds a full firebox lasts at the maximum combustion rate.</summary>
        public float BurnTime { get; }

        /// <summary>Fraction of coal heat delivered when burning at the maximum rate.</summary>
        public float EfficiencyAtMaxCombustion { get; }

        /// <summary>Coal removed per second by the fire dump at full flow, in kg/s.</summary>
        public float CoalDumpRate { get; }

        /// <summary>Smoothing time of the combustion rate and smoke, in s.</summary>
        public float CombustionRateSmoothTime { get; }

        /// <summary>Smoothing time of the firebox temperature, in s.</summary>
        public float TemperatureSmoothTime { get; }

        public FireboxParameters(
            float maxCoalCapacity = 80f,
            float burnTime = 120f,
            float efficiencyAtMaxCombustion = 0.5f,
            float coalDumpRate = 10f,
            float combustionRateSmoothTime = 5f,
            float temperatureSmoothTime = 15f)
        {
            if (!(maxCoalCapacity > 0f))
                throw new ArgumentOutOfRangeException(nameof(maxCoalCapacity), maxCoalCapacity, "Must be positive.");
            if (!(burnTime > 0f))
                throw new ArgumentOutOfRangeException(nameof(burnTime), burnTime, "Must be positive.");
            if (!(efficiencyAtMaxCombustion > 0f && efficiencyAtMaxCombustion <= 1f))
                throw new ArgumentOutOfRangeException(nameof(efficiencyAtMaxCombustion), efficiencyAtMaxCombustion, "Must be in (0, 1].");
            if (!(coalDumpRate >= 0f))
                throw new ArgumentOutOfRangeException(nameof(coalDumpRate), coalDumpRate, "Must not be negative.");
            if (!(combustionRateSmoothTime >= 0f))
                throw new ArgumentOutOfRangeException(nameof(combustionRateSmoothTime), combustionRateSmoothTime, "Must not be negative.");
            if (!(temperatureSmoothTime >= 0f))
                throw new ArgumentOutOfRangeException(nameof(temperatureSmoothTime), temperatureSmoothTime, "Must not be negative.");

            MaxCoalCapacity = maxCoalCapacity;
            BurnTime = burnTime;
            EfficiencyAtMaxCombustion = efficiencyAtMaxCombustion;
            CoalDumpRate = coalDumpRate;
            CombustionRateSmoothTime = combustionRateSmoothTime;
            TemperatureSmoothTime = temperatureSmoothTime;
        }

        /// <summary>Highest combustion rate the stock model allows, in kg/s.</summary>
        public float MaxCombustionRate => MaxCoalCapacity / BurnTime;

        /// <summary>Heat delivered at the maximum combustion rate, in W.</summary>
        public float MaxHeatOutput => MaxCombustionRate * CoalHeatContent * EfficiencyAtMaxCombustion;
    }
}
