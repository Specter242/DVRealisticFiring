using System.Collections.Generic;
using LocoSim.Definitions;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class BoilerFit
    {
        private const float LbPerHour = 0.45359237f / 3600f;

        // The prototypes' most evaporation, actual, kg/s.
        private static readonly Dictionary<string, float> Steam = new Dictionary<string, float>
        {
            // LMS Class 5 at the front-end limit, 23,050 lb/h (Cox, British
            // Railways Standard Steam Locomotives, 1966): the 8F "was never
            // subjected to similar constant speed tests, but the corresponding
            // Class 5 4-6-0 was most thoroughly tested", its firebox identical.
            ["em_lms_8f_loc_black"] = 23050f * LbPerHour,
            ["em_lms_8f_loc_grey"] = 23050f * LbPerHour,
            // NYC J-3a, 85,000 lb/h with the standard front end, Selkirk
            // standing tests (Railway Age, 18 Jan 1941, p.181).
            ["S464-730"] = 85000f * LbPerHour,
            // UP FEF-1, 68,700 lb/h by Cole's ratios (Brandt, RME Mar 1940
            // p.104), times the tested engines' mean margin over Cole's,
            // 1.22 (the J-3a 1.40, the Santa Fe 3765s 1.03): no FEF was tested.
            ["FEF-3"] = 83600f * LbPerHour,
            ["S484-840A"] = 83600f * LbPerHour,
            // Santa Fe 3766, 82,000 lb/h (AT&SF Test Report 87312, in
            // Stagner, Trains, Feb 1987); no 2900 was tested.
            ["A.T.&S.F. 2901-A"] = 82000f * LbPerHour,
        };

        /// <summary>
        /// Sizes <paramref name="definition"/>'s fire to its engine's
        /// prototype, before the fire is built from it. Returns the maker's
        /// burn time to put back if the fire cannot be built, or null when
        /// the engine keeps its maker's.
        /// </summary>
        public static float? Apply(FireboxDefinition definition, TrainCar car, bool oil)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (definition == null || livery == null || !Steam.TryGetValue(livery, out var steam))
                return null;
            var maker = definition.burnTime;
            definition.burnTime = FireboxParameters.BurnTimeFor(definition.maxCoalCapacity, definition.efficiencyAtMaxCombustion,
                steam * AutoFiremanBase.SteamHeat, oil ? OilFire.MaxFeedFactor : 1f);
            return maker;
        }
    }
}
