using System.Collections.Generic;
using DV.PitStops;
using DV.ThingTypes;
using HarmonyLib;
using RealisticFiring.Visuals;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// An S282 tender carrying oil (<see cref="S282OilTender.IsConverted"/>)
    /// is refilled at a pit stop's fuel pump, as the 2901's oil is: the game's
    /// pit stop lists its coal container, which holds the oil, a litre a unit,
    /// as Fuel, and a refill of fuel fills it. Its record is redone when the
    /// tender changes between coal and oil (<see cref="Refit"/>).
    /// </summary>
    internal static class OilTenderPitStopPatch
    {
        private static readonly AccessTools.FieldRef<CarPitStopParametersBase, Dictionary<ResourceType, LocoParameterData>> Parameters =
            AccessTools.FieldRefAccess<CarPitStopParametersBase, Dictionary<ResourceType, LocoParameterData>>("carPitStopParameters");

        private static readonly AccessTools.FieldRef<SimulatedCarPitStopParameters, List<ResourceType>> Existing =
            AccessTools.FieldRefAccess<SimulatedCarPitStopParameters, List<ResourceType>>("existingResources");

        private static bool Oil(SimulatedCarPitStopParameters parameters)
        {
            var car = parameters != null ? TrainCar.Resolve(parameters.gameObject) : null;
            return car != null && S282OilTender.IsConverted(car);
        }

        /// <summary>Redoes <paramref name="tender"/>'s pit stop record, after it changed between coal and oil.</summary>
        internal static void Refit(TrainCar tender)
        {
            var parameters = tender != null ? tender.GetComponentInChildren<SimulatedCarPitStopParameters>(true) : null;
            if (parameters != null && Parameters(parameters) != null)
                AccessTools.Method(typeof(SimulatedCarPitStopParameters), "InitPitStopParameters").Invoke(parameters, null);
        }

        // Its coal, listed as fuel.
        [HarmonyPatch(typeof(SimulatedCarPitStopParameters), "InitPitStopParameters")]
        private static class Listed
        {
            private static void Postfix(SimulatedCarPitStopParameters __instance)
            {
                var parameters = Parameters(__instance);
                if (parameters == null || !Oil(__instance) || !parameters.TryGetValue(ResourceType.Coal, out var coal) || parameters.ContainsKey(ResourceType.Fuel))
                    return;
                parameters.Remove(ResourceType.Coal);
                parameters[ResourceType.Fuel] = coal;
                var existing = Existing(__instance);
                var at = existing != null ? existing.IndexOf(ResourceType.Coal) : -1;
                if (at >= 0)
                    existing[at] = ResourceType.Fuel;
            }
        }

        // The game's refresh reads the container as coal: lent the fuel's entry under that name for it.
        [HarmonyPatch(typeof(SimulatedCarPitStopParameters), "RefreshParameters")]
        private static class Refreshed
        {
            private static void Prefix(SimulatedCarPitStopParameters __instance, out bool __state)
            {
                __state = false;
                var parameters = Parameters(__instance);
                // Lent whenever the coal is listed as fuel, coal or oil now: unlent, the game's own read throws.
                if (parameters == null || parameters.ContainsKey(ResourceType.Coal) || !parameters.TryGetValue(ResourceType.Fuel, out var fuel))
                    return;
                parameters[ResourceType.Coal] = fuel;
                __state = true;
            }

            private static void Finalizer(SimulatedCarPitStopParameters __instance, bool __state)
            {
                if (__state)
                    Parameters(__instance)?.Remove(ResourceType.Coal);
            }
        }

        // Fuel bought fills the container that holds it.
        [HarmonyPatch(typeof(SimulatedCarPitStopParameters), nameof(SimulatedCarPitStopParameters.UpdateCarPitStopParameter))]
        private static class Refilled
        {
            private static void Prefix(SimulatedCarPitStopParameters __instance, ref ResourceType parameter)
            {
                if (parameter == ResourceType.Fuel && Oil(__instance))
                    parameter = ResourceType.Coal;
            }
        }
    }
}
