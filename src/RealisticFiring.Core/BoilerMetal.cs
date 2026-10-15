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
        /// steel takes it from the water.
        /// </summary>
        public float Exchange(float dt, float waterTemperature)
        {
            if (!(dt > 0f) || HeatCapacity <= 0f)
                return 0f;
            var share = 1f - (float)Math.Exp(-dt / FollowTime);
            var heat = HeatCapacity * (Temperature - waterTemperature) * share;
            Temperature -= heat / HeatCapacity;
            return heat;
        }
    }
}
