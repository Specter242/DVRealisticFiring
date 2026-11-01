using DV.Simulation.Cars;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>The stock lighter requires coal; an oil pan takes lit waste before oil.</summary>
    [HarmonyPatch(typeof(Fire), nameof(Fire.IgnitionAllowed), MethodType.Getter)]
    internal static class OilIgnitionPatch
    {
        private static void Postfix(Fire __instance, ref bool __result, FireboxSimController ___fireboxController)
        {
            if (__result || !___fireboxController)
                return;
            var car = TrainCar.Resolve(__instance.gameObject);
            if (RealisticOilFirebox.TryGet(car, out _))
                __result = OilFire.CanOfferTorch(__instance.Ignited, ___fireboxController.FireboxDoorOpening,
                    LevelInfo.IsUnderWater(__instance.transform.position));
        }
    }
}
