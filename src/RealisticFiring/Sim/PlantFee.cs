using System.Linq;
using DV.Simulation.Cars;
using DV.ServicePenalty;
using DV.ThingTypes;
using HarmonyLib;

namespace RealisticFiring.Sim
{
 internal static class PlantFee
    {
        public const float CoalPerKgSteam = 0.105f;
        public const float CoalPerKgHotWater = 0.011f;

        // Fuel oil for the same heat as a kilogram of coal: 32 against 42 MJ/kg.
        private const float FuelPerCoal = 0.76f;

        /// <summary>Billed this often, s: a few grams a tick are too small for a bill's figures.</summary>
        public const float BillEvery = 2f;

        private static readonly AccessTools.FieldRef<SimController, SimulatedCarDebtTracker> Debt =
            AccessTools.FieldRefAccess<SimController, SimulatedCarDebtTracker>("debt");

        /// <summary>
        /// Bills <paramref name="engine"/> for this much of the plant's steam
        /// and hot water. Returns the water and fuel billed, kg, and the fuel's kind (null where the car has no bill for it).
        /// </summary>
        public static (float Water, float Fuel, ResourceType? FuelType) Bill(TrainCar engine, float steamKg, float hotWaterKg)
        {
            var water = steamKg + hotWaterKg;
            var coal = steamKg * CoalPerKgSteam + hotWaterKg * CoalPerKgHotWater;
            var billedWater = Add(engine, ResourceType.Water, water) ? water : 0f;
            if (Add(engine, ResourceType.Coal, coal))
                return (billedWater, coal, ResourceType.Coal);
            var fuel = coal * FuelPerCoal;
            return Add(engine, ResourceType.Fuel, fuel) ? (billedWater, fuel, ResourceType.Fuel) : (billedWater, 0f, (ResourceType?)null);
        }

        // Adds amount to what the engine, or its tender, is billed as having
        // used of type: its bill counts from what it held as the bill began,
        // so it is moved up by what the plant gave.
        private static bool Add(TrainCar engine, ResourceType type, float amount)
        {
            if (!(amount > 0f))
                return false;
            foreach (var car in new[] { engine, Coupled(engine, true), Coupled(engine, false) })
            {
                var sim = car != null ? car.GetComponent<SimController>() : null;
                var component = sim != null ? Debt(sim)?.GetDebtData()?.GetTrackedDebts()?.FirstOrDefault(c => c != null && c.Type == type) : null;
                if (component == null)
                    continue;
                component.UpdateStartValue(component.StartValue + amount);
                return true;
            }
            return false;
        }

        private static TrainCar Coupled(TrainCar car, bool rear)
        {
            var coupler = car == null ? null : rear ? car.rearCoupler : car.frontCoupler;
            return coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;
        }
    }
}
