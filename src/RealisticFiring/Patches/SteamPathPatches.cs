using HarmonyLib;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    [HarmonyPatch(typeof(ReciprocatingSteamEngine), "SimulateSteamChest")]
    internal static class SteamPathChestPatch
    {
        private static bool Prefix(ReciprocatingSteamEngine __instance, float steamLossMassRate,
            int numChuffs, float delta, float ___steamMassPerChuff) =>
            !EngineSteamPath.TryGet(__instance, out var path) || !path.Step(steamLossMassRate, numChuffs, ___steamMassPerChuff, delta);
    }

    [HarmonyPatch(typeof(ReciprocatingSteamEngine), nameof(ReciprocatingSteamEngine.GetSaveStateData))]
    internal static class SteamPathSavePatch
    {
        private static void Postfix(ReciprocatingSteamEngine __instance, JObject __result) => EngineSteamPath.Save(__instance, __result);
    }

    [HarmonyPatch(typeof(ReciprocatingSteamEngine), nameof(ReciprocatingSteamEngine.SetSaveStateData))]
    internal static class SteamPathLoadPatch
    {
        private static void Postfix(ReciprocatingSteamEngine __instance, JObject savedData) => EngineSteamPath.Load(__instance, savedData);
    }
}
