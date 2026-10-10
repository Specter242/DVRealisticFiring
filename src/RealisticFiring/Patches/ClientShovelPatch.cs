using DV.Simulation.Cars;
using HarmonyLib;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// On a multiplayer client, a shovelful (thrown, or the keyboard's) into
    /// one of the mod's fireboxes goes to the host as the mod's own request,
    /// with the client's aim, instead of through the firebox's coal port,
    /// which the Multiplayer mod forwards without it. Elsewhere the stock
    /// transfer runs.
    /// </summary>
    [HarmonyPatch(typeof(FireboxSimController), nameof(FireboxSimController.TransferCoal))]
    internal static class ClientShovelPatch
    {
        private static bool Prefix(FireboxSimController __instance, float coalMass)
        {
            if (!Net.IsClient || !(coalMass > 0f))
                return true;
            var car = TrainCar.Resolve(__instance.gameObject);
            var multiplier = __instance.coalConsumptionMultiplier > 0f ? __instance.coalConsumptionMultiplier : 1f;
            if (FireboxRegistry.TryGet(car, out var firebox))
            {
                firebox.DeliverFromClient(coalMass / multiplier);
                return false;
            }
            if (RealisticOilFirebox.TryGet(car, out var oil))
            {
                oil.DeliverFromClient(coalMass);
                return false;
            }
            return true;
        }
    }
}
