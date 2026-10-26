using System;

namespace RealisticFiring.Core
{
 public sealed class BoilerMetal
    {
        /// <summary>Steel per kg of the water the boiler carries (inferred).</summary>
        public const float SteelPerWater = 2.5f;

        /// <summary>Steel's specific heat, J/(kg K).</summary>
        public const float SteelHeat = 490f;

        /// <summary>
        /// Water's specific heat as the game's steam tables take it, J/(kg K):
        /// its temperature is (h + 24) / 4.39 for h in kJ/kg.
        /// </summary>
        public const float WaterHeat = 4390f;

        /// <summary>
        /// How long the steel takes to follow the water's temperature, s: heat
        /// through an inch of shell plate takes about a minute, a firebox
        /// sheet a few seconds, a tube under one.
        /// </summary>
        public const float FollowTime = 20f;

        /// <param name="waterMass">The water the boiler carries, kg.</param>
        /// <param name="temperature">The steel's temperature, °C: the water's.</param>
        public BoilerMetal(float waterMass, float temperature)
        {
            HeatCapacity = SteelPerWater * Math.Max(0f, waterMass) * SteelHeat;
            Temperature = temperature;
        }

        /// <summary>The steel's heat capacity, J/K.</summary>
        public float HeatCapacity { get; }

        /// <summary>The steel's temperature, °C.</summary>
        public float Temperature { get; private set; }

        /// <summary>Puts the steel at <paramref name="temperature"/>, as a new or reloaded boiler's.</summary>
        public void Settle(float temperature) => Temperature = temperature;

        /// <summary>
        /// The heat the water takes from the steel over <paramref name="dt"/>
        /// s at <paramref name="waterTemperature"/>, J; negative when the
        /// steel takes it from the water. No more than brings the two to the
        /// temperature they would settle at together: the first water into a
        /// hot boiler blown dry, a few kilograms of
        /// <paramref name="waterHeatCapacity"/> J/K, warms to the steel and
        /// no further, where the full share swung it thousands of degrees
        /// each step until the boiler burst (2026-10-26).
        /// </summary>
        public float Exchange(float dt, float waterTemperature, float waterHeatCapacity)
        {
            if (!(dt > 0f) || HeatCapacity <= 0f || !(waterHeatCapacity > 0f))
                return 0f;
            var share = 1f - (float)Math.Exp(-dt / FollowTime);
            var together = HeatCapacity * waterHeatCapacity / (HeatCapacity + waterHeatCapacity);
            var heat = Math.Min(HeatCapacity * share, together) * (Temperature - waterTemperature);
            Temperature -= heat / HeatCapacity;
            return heat;
        }
    }
}
