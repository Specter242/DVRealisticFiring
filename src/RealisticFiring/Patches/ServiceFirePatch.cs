using System.Linq;
using DV.Damage;
using DV.ServicePenalty;
using HarmonyLib;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
 [HarmonyPatch(typeof(SimulatedCarDebtTracker), nameof(SimulatedCarDebtTracker.TurnOffDebtSources))]
    internal static class ServiceFirePatch
    {
        private static readonly AccessTools.FieldRef<SimulatedCarDebtTracker, DamageController> Damage =
            AccessTools.FieldRefAccess<SimulatedCarDebtTracker, DamageController>("dmgController");

        private static void Postfix(SimulatedCarDebtTracker __instance)
        {
            try
            {
                var car = Damage(__instance) != null ? Damage(__instance).GetComponent<TrainCar>() : null;
                if (car == null)
                    return;
                var cars = car.trainset != null ? car.trainset.cars.ToList() : new System.Collections.Generic.List<TrainCar> { car };
                foreach (var each in cars)
                {
                    var flow = each != null && each.SimController != null ? each.SimController.SimulationFlow : null;
                    if (flow == null)
                        continue;
                    var kept = flow.OrderedSimComps.OfType<RealisticFirebox>().Count(f => f.KeepFire())
                               + flow.OrderedSimComps.OfType<RealisticOilFirebox>().Count(f => f.KeepFire());
                    if (kept > 0)
                        Main.Mod.Logger.Log($"{each.LogName()}: serviced from the career manager with its fire kept in");
                }
            }
            catch (System.Exception e)
            {
                Faults.Report("keeping the fire through a service", null, e);
            }
        }
    }
}
