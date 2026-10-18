using System;
using System.Diagnostics;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
 internal static class Ambient
    {
        // Water stays liquid: a tender's and a cold boiler's no colder than
        // this, °C (a water tower's is kept from freezing; a boiler left in
        // the frost would be drained).
        private const float LeastTenderWater = 2f;
        private const float LeastBoilerWater = 1f;

        // The tender's water and oil without DV Seasons, °C.
        private const float GameTender = 15f;

        private const double ReadEvery = 0.25;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static double readAt = double.NegativeInfinity;
        private static float? seasons;

        /// <summary>DV Seasons' outside temperature, °C, or null when it does not run.</summary>
        public static float? Seasons
        {
            get
            {
                var now = Clock.Elapsed.TotalSeconds;
                if (now - readAt >= ReadEvery)
                {
                    readAt = now;
                    seasons = SeasonsMod.TryRead(out var celsius, out _) ? celsius : (float?)null;
                }
                return seasons;
            }
        }

        /// <summary>The outside air's temperature, °C: DV Seasons', else the game's.</summary>
        public static float Air => Seasons ?? SimConsts.OUTSIDE_TEMPERATURE;

        /// <summary>The tender's oil, °C: the air's with DV Seasons, else 15 °C.</summary>
        public static float TenderOil => Seasons ?? GameTender;

        /// <summary>The tender's water, °C: the air's with DV Seasons (kept liquid), else 15 °C.</summary>
        public static float TenderWater => Math.Max(LeastTenderWater, Seasons ?? GameTender);

        /// <summary>A cold boiler's water, °C: the air's, kept liquid.</summary>
        public static float BoilerWater => Math.Max(LeastBoilerWater, Air);

        /// <summary>Where the air's temperature comes from, for the logs.</summary>
        public static string Describe() => SeasonsMod.TryRead(out var celsius, out var season)
            ? $"{celsius:F1} °C from {season}"
            : $"{SimConsts.OUTSIDE_TEMPERATURE:F0} °C, the game's";
    }
}
