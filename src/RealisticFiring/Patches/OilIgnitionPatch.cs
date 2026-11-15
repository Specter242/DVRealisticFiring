using DV.Simulation.Cars;
using HarmonyLib;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// RF fireboxes take physical starter fuel; the lighter lights the rag.
    /// With the setting on, the lighter lights laid coal on a coal firebox as
    /// in the stock game (<see cref="RealisticFirebox.LighterLightsCoal"/>).
    /// </summary>
    [HarmonyPatch(typeof(Fire), nameof(Fire.IgnitionAllowed), MethodType.Getter)]
    internal static class OilIgnitionPatch
    {
        private static void Postfix(Fire __instance, ref bool __result, FireboxSimController ___fireboxController)
        {
            if (!___fireboxController) return;
            var car = TrainCar.Resolve(__instance.gameObject);
            // RF cold fires now take physical burning waste; other engines stay stock.
            if (RealisticOilFirebox.TryGet(car, out _))
                __result = false;
            else if (FireboxRegistry.TryGet(car, out var coal))
                __result = __result && coal.LighterLightsCoal;
        }
    }
}
