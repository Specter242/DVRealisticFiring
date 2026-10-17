using HarmonyLib;
using LocoSim.Implementations;
using LocoSim.Resources;
using Newtonsoft.Json.Linq;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Saves the rain's water in an engine's coal beside its coal container's
    /// stock key (<see cref="CoalStore"/>), which the stock load ignores.
    /// </summary>
    [HarmonyPatch(typeof(ResourceContainer), nameof(ResourceContainer.GetSaveStateData))]
    internal static class CoalSavePatch
    {
        private static void Postfix(ResourceContainer __instance, JObject __result)
        {
            if (__instance.resourceType == ResourceContainerType.COAL)
                CoalStore.Save(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(ResourceContainer), nameof(ResourceContainer.SetSaveStateData))]
    internal static class CoalLoadPatch
    {
        private static void Postfix(ResourceContainer __instance, JObject savedData)
        {
            if (__instance.resourceType == ResourceContainerType.COAL)
                CoalStore.Load(__instance, savedData);
        }
    }
}
