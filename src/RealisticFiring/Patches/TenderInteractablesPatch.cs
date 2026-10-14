using System;
using HarmonyLib;
using RealisticFiring.Visuals;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Runs as a car's outside interactables load, as a tender's do when the
    /// player comes near: puts the slides on its water valve handles.
    /// </summary>
    [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.LoadExternalInteractables))]
    internal static class TenderInteractablesPatch
    {
        private static void Postfix(TrainCar __instance)
        {
            try
            {
                var interactables = __instance.loadedExternalInteractables;
                var note = TenderWaterValves.AttachControls(__instance, interactables != null ? interactables.transform : null);
                if (note != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{__instance.ID}: {note}");
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Tender water valve setup failed", e);
            }
        }
    }
}
