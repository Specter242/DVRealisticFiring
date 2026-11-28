using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DV.JObjectExtstensions;
using DV.Simulation.Controllers;
using DV.ThingTypes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class S282OilConversion
    {
        internal const string Livery = "LocoS282A";
        internal const string TenderLivery = "LocoS282B";

        /// <summary>The oil feed's sim component, standing where a CCL oil burner has its stoker.</summary>
        internal const string FeedId = "rfOilFeed";

        /// <summary>The firing valve: the burner's own cab control, as the S282 has no stoker's to take. Shut by default.</summary>
        internal static readonly StokerJetControls.Valve FiringValve = new StokerJetControls.Valve("rfFiringValve", "Firing valve", null, 0f);

        /// <summary>
        /// Fuel oil's heat, J a litre: Bunker C, about 18,500 Btu/lb (43 MJ/kg)
        /// at 0.97 kg/L (period handbooks give 18,000 to 19,000 Btu/lb; the
        /// density inferred).
        /// </summary>
        internal const float OilHeatPerLitre = 41.7e6f;

        /// <summary>
        /// Litres of oil from the tender per firebox kg: the fire's kg are the
        /// stock coal's (<see cref="FireboxParameters.CoalHeatContent"/>), so a
        /// litre goes about 1.3 times as far as a kg of coal. The tender's
        /// coal container holds the oil, a litre a unit.
        /// </summary>
        internal static float LitresPerFireboxKg => FireboxParameters.CoalHeatContent / OilHeatPerLitre;

        private sealed class Decision
        {
            public bool Oil;
        }

        private static readonly ConditionalWeakTable<TrainCar, Decision> Built = new ConditionalWeakTable<TrainCar, Decision>();

        // The GUID of the car being built as a save loads: TrainCar.Awake
        // builds its sim before the game gives it its GUID.
        private static string spawningGuid;

        /// <summary>
        /// Whether <paramref name="car"/> is an S282 built as an oil burner.
        /// Decided the first time it is asked, as the engine's sim is built,
        /// and kept for the engine's life.
        /// </summary>
        internal static bool IsOil(TrainCar car)
        {
            if (car == null || car.carLivery == null || car.carLivery.id != Livery)
                return false;
            if (Built.TryGetValue(car, out var known))
                return known.Oil;
            var oil = false;
            try
            {
                oil = Converted(Upgrades.Fittings.CarGuidOf(car) ?? spawningGuid);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException($"{car.name}: whether the S282 is converted to oil couldn't be read; built as coal", e);
            }
            Built.Add(car, new Decision { Oil = oil });
            if (oil)
                Main.Mod?.Logger.Log($"S282 {(spawningGuid != null ? spawningGuid : car.name)}: converted to oil, built as an oil burner");
            return oil;
        }

        // Whether the S282 with this GUID (null: one not from a save) is converted now.
        private static bool Converted(string guid) =>
            Main.Session != null && Main.Session.S282OilBurner && Upgrades.Fittings.HasForGuid(UpgradeMask.S282OilBurner, guid);

        /// <summary>Whether <paramref name="car"/>, an S282, is converted now: bought, or its rule fits it.</summary>
        internal static bool ConvertedNow(TrainCar car) =>
            car != null && car.carLivery != null && car.carLivery.id == Livery
            && Main.Session != null && Main.Session.S282OilBurner && Upgrades.Fittings.HasOilBurner(car);

        /// <summary>
        /// Whether <paramref name="car"/>, an S282, is converted (or no longer)
        /// since it was built: it comes out of the shops so when the game next loads.
        /// </summary>
        internal static bool Pending(TrainCar car) =>
            car != null && car.carLivery != null && car.carLivery.id == Livery && ConvertedNow(car) != IsOil(car);

        /// <summary>The engine coupled ahead of <paramref name="tender"/>, an S282 tender; null without one.</summary>
        internal static TrainCar EngineAhead(TrainCar tender)
        {
            var engine = tender != null && tender.frontCoupler != null && tender.frontCoupler.coupledTo != null ? tender.frontCoupler.coupledTo.train : null;
            return engine != null && engine.carLivery != null && engine.carLivery.id == Livery ? engine : null;
        }

        /// <summary>The tender behind <paramref name="engine"/>, an S282's; null without one.</summary>
        internal static TrainCar TenderBehind(TrainCar engine)
        {
            var tender = engine != null && engine.rearCoupler != null && engine.rearCoupler.coupledTo != null ? engine.rearCoupler.coupledTo.train : null;
            return tender != null && tender.carLivery != null && tender.carLivery.id == TenderLivery ? tender : null;
        }

        /// <summary>Whether <paramref name="tender"/> is an S282 tender coupled behind an S282 built to burn oil: it carries oil.</summary>
        internal static bool IsOilTender(TrainCar tender) =>
            tender != null && tender.carLivery != null && tender.carLivery.id == TenderLivery && IsOil(EngineAhead(tender));

        /// <summary>The oil the engine's feed draws: the tender's coal pile, which holds it, a litre a unit.</summary>
        internal static CoalPileSimController TenderPile(TrainCar engine)
        {
            var tender = TenderBehind(engine);
            return tender != null && tender.SimController != null ? tender.SimController.coalPile : null;
        }

        // The car's GUID while a save's car is built.
        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.SpawnLoadedCar))]
        private static class SpawningLoaded
        {
            private static void Prefix(string carGuid) => spawningGuid = carGuid;
            private static Exception Finalizer(Exception __exception)
            {
                spawningGuid = null;
                return __exception;
            }
        }

        // As above, for a caboose brought back from the last session.
        [HarmonyPatch(typeof(CarSpawner), nameof(CarSpawner.SpawnLoadedCarAtNearestAvailableSpace))]
        private static class SpawningNearest
        {
            private static void Prefix(string carGuid) => spawningGuid = carGuid;
            private static Exception Finalizer(Exception __exception)
            {
                spawningGuid = null;
                return __exception;
            }
        }

        // A unique car brought back as it was deleted (an owned engine from its
        // garage): the game restores its GUID after building it.
        [HarmonyPatch(typeof(CarSpawner), "BaseSpawn")]
        private static class SpawningUnique
        {
            private static readonly AccessTools.FieldRef<CarSpawner, Dictionary<TrainCarLivery, JObject>> Deleted =
                AccessTools.FieldRefAccess<CarSpawner, Dictionary<TrainCarLivery, JObject>>("deletedUniqueCarLiveryToLastCarState");

            private static void Prefix(CarSpawner __instance, GameObject carToSpawn, bool uniqueCar)
            {
                spawningGuid = null;
                if (!uniqueCar || carToSpawn == null)
                    return;
                var livery = carToSpawn.GetComponentInChildren<TrainCar>(true)?.carLivery;
                if (livery != null && livery.id == Livery && Deleted(__instance).TryGetValue(livery, out var state) && state != null)
                    spawningGuid = state.GetString("carGuid");
            }

            private static Exception Finalizer(Exception __exception)
            {
                spawningGuid = null;
                return __exception;
            }
        }
    }
}
