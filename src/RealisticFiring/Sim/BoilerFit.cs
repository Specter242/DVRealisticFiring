using LocoSim.Definitions;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class BoilerFit
    {
        /// <summary>
        /// Sizes <paramref name="definition"/>'s fire to its engine's
        /// prototype, before the fire is built from it. Returns the maker's
        /// burn time to put back if the fire cannot be built, or null when
        /// the engine keeps its maker's.
        /// </summary>
        public static float? Apply(FireboxDefinition definition, TrainCar car, bool oil)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (definition == null || !TryBurnTime(livery, definition.maxCoalCapacity, definition.efficiencyAtMaxCombustion, oil, out var burnTime))
                return null;
            var maker = definition.burnTime;
            definition.burnTime = burnTime;
            return maker;
        }

        /// <summary>
        /// The burn time that sizes a box of <paramref name="maxCoalCapacity"/>
        /// kg at <paramref name="efficiencyAtMaxCombustion"/> to
        /// <paramref name="livery"/>'s prototype, if it is listed.
        /// </summary>
        internal static bool TryBurnTime(string livery, float maxCoalCapacity, float efficiencyAtMaxCombustion, bool oil, out float burnTime)
        {
            burnTime = 0f;
            float steam;
            if (LocomotiveProfiles.TryGet(livery, out var profile) && profile.PrototypeEvaporation is float builtIn)
                steam = builtIn;
            else if (!MakerFitRegistry.TryGetMaxEvaporation(livery, out steam))
                return false;
            burnTime = FireboxParameters.BurnTimeFor(maxCoalCapacity, efficiencyAtMaxCombustion,
                steam * AutoFiremanBase.SteamHeat, oil ? OilFire.MaxFeedFactor : 1f);
            return true;
        }
    }
}
