using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DV.Customization.Paint;
using DV.Simulation.Brake;
using DV.Utils;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Patches
{
 [HarmonyPatch]
    internal static class CarPoolGuard
    {
        private static readonly FieldInfo PoolInitialized = AccessTools.Field(typeof(CarSpawner), "poolInitialized");

        private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CarSpawner), "InitPoolCoro"));

        private static Exception Finalizer(Exception __exception, object __instance, ref bool __result)
        {
            if (__exception == null)
                return null;
            try
            {
                var spawner = SingletonBehaviour<CarSpawner>.Instance;
                var kept = 0;
                var dropped = new List<string>();
                // The coroutine's wagons not yet returned to the pool.
                foreach (var field in AccessTools.GetDeclaredFields(__instance.GetType()).Where(f => f.FieldType == typeof(List<TrainCar>)))
                {
                    if (!(field.GetValue(__instance) is List<TrainCar> cars))
                        continue;
                    foreach (var car in cars.Where(c => c != null))
                    {
                        if (car.rb != null && car.interior != null && spawner != null)
                        {
                            spawner.ReturnToPool(car);
                            kept++;
                        }
                        else
                        {
                            dropped.Add(car.carLivery != null ? car.carLivery.id : car.name);
                            Object.Destroy(car.gameObject);
                        }
                    }
                    cars.Clear();
                }
                if (spawner != null)
                    PoolInitialized?.SetValue(spawner, true);
                __result = false;
                Main.Mod.Logger.Warning($"The game's wagon pool failed while loading ({__exception.GetType().Name}: {__exception.Message}); " +
                                        $"ended it so the load goes on: {kept} wagons pooled, broken ones removed ({string.Join(", ", dropped)}). " +
                                        "Wagons it lacks are built fresh as they are needed. " + TrainCarAwakeReport.Last);
                return null;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not recover the game's wagon pool", e);
                return __exception;
            }
        }
    }

 [HarmonyPatch(typeof(TrainCar), "Awake")]
    internal static class TrainCarAwakeReport
    {
        private const int MaxReports = 5;
        private static int reports;

        /// <summary>The last failure, in a line, or empty.</summary>
        public static string Last { get; private set; } = "";

        private static Exception Finalizer(Exception __exception, TrainCar __instance)
        {
            if (__exception == null || reports >= MaxReports)
                return __exception;
            reports++;
            try
            {
                var livery = __instance != null ? __instance.carLivery : null;
                var parent = livery != null ? livery.parentType : null;
                string Is(object o) => o == null || (o is Object u && u == null) ? "missing" : "present";
                Last = $"TrainCar.Awake failed on '{(__instance != null ? __instance.name : "?")}' ({(livery != null ? livery.id : "no livery")}): " +
                       $"{__exception.GetType().Name}: {__exception.Message}; livery {Is(livery)}, its type {Is(parent)}, brakes setup {Is(parent?.brakes)}, " +
                       $"car spawner {Is(SingletonBehaviour<CarSpawner>.Instance)}, globals {Is(DV.Globals.G)}, " +
                       $"brake system {Is(__instance != null ? __instance.GetComponent<BrakeSystem>() : null)}, rigidbody {Is(__instance != null ? __instance.rb : null)}, " +
                       $"interior {Is(__instance != null ? __instance.interior : null)}, paint parts {(__instance != null ? __instance.GetComponents<TrainCarPaint>().Length : 0)}";
                Main.Mod.Logger.Warning(Last + Environment.NewLine + __exception);
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Could not describe a failed TrainCar.Awake", e);
            }
            return __exception;
        }
    }
}
