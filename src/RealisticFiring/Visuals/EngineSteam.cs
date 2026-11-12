using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticStackEffects;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class EngineSteam
    {
        // A booster's exhaust at its hardest takes this much steam, kg/s.
        private const float BoosterFullSteam = 0.5f;

        // The trailing truck from the cab's standing point, m in the car's
        // frame: forward of it and at the frame's height, out to each side.
        private const float TruckAhead = 1.2f;
        private const float TruckHeight = 0.45f;
        private const float TruckOut = 1.0f;

        private const float CheckEvery = 1f;
        private static float nextCheck;
        private static readonly Dictionary<RealisticBooster, object> Boosters = new Dictionary<RealisticBooster, object>();

        public static void Update()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + CheckEvery;
            foreach (var gone in Boosters.Keys.Where(b => b.Car == null).ToList())
                Boosters.Remove(gone);
            foreach (var booster in RealisticBooster.All)
            {
                var car = booster.Car;
                if (car == null || !EngineVents.TryGet(car, out var steam, out var add))
                    continue;
                if (Boosters.TryGetValue(booster, out var had) && ReferenceEquals(had, steam))
                    continue;
                Boosters[booster] = steam;
                Add(booster, car, add);
            }
        }

        private static void Add(RealisticBooster booster, TrainCar car, EngineVents.Adder add)
        {
            // Where the game stands a player in the cab, as the injector's overflow finds it.
            var point = car.transform.Find(TrainCarColliders.CAB_NAME);
            if (point == null && car.interior != null)
                point = car.interior.Find(TrainCarColliders.CAB_NAME);
            var cab = point != null ? car.transform.InverseTransformPoint(point.position).z : 0f;
            var at = new Vector3(0f, TruckHeight, cab + TruckAhead);
            foreach (var side in new[] { -1f, 1f })
            {
                add($"booster cock {(side < 0f ? "L" : "R")}", VentKind.CylinderCock,
                    at + new Vector3(side * TruckOut, 0f, 0f), new Vector3(side, -0.15f, 0f),
                    () => booster.Flow.CocksOpen && booster.Flow.SteamUsed > 0.01f ? 0.5f : 0f);
            }
            add("booster exhaust", VentKind.BoosterExhaust,
                at + new Vector3(-TruckOut, 0.3f, -0.6f), new Vector3(-0.5f, 1f, -0.2f),
                () => booster.Flow.State == BoosterState.CutOut ? 0f : Mathf.Clamp01(booster.Flow.SteamUsed / BoosterFullSteam));
            Main.Mod.Logger.Log($"{car.ID}: booster steam drawn with the engine's: two cocks and an exhaust at the trailing truck");
        }
    }
}
