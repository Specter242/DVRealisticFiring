using System;
using System.Linq;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Runs after a car's simulation graph is built and its controllers are
    /// initialized, when every port can be looked up by ID.
    /// </summary>
    [HarmonyPatch(typeof(SimController), nameof(SimController.Initialize))]
    internal static class SimControllerPatch
    {
        // Coal-fired: the sim graph has a firebox, as on custom steam locos too.
        private static bool HasFirebox(SimController controller) =>
            controller.connectionsDefinition != null && controller.connectionsDefinition.executionOrder != null
            && controller.connectionsDefinition.executionOrder.OfType<FireboxDefinition>().Any();

        private static void Postfix(SimController __instance, TrainCar trainCar)
        {
            if (Main.Settings.DumpSimGraphs)
                SimGraphDump.DumpOnce(trainCar, __instance.connectionsDefinition);
            if (Main.Settings.DumpFireboxGeometry && HasFirebox(__instance))
            {
                // Diagnostics must never break the car's setup.
                try
                {
                    SmokeDump.DumpOnce(trainCar, __instance.particlesController);
                }
                catch (Exception e)
                {
                    Main.Mod.Logger.LogException("Chimney smoke dump failed", e);
                }
            }

            if (Main.RealisticInjectorsEnabled && __instance.simFlow != null)
            {
                var injectors = InjectorRig.Attach(trainCar, __instance.simFlow);
                if (injectors != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{trainCar.carLivery.id}: {injectors}");
            }

            if (!FireboxRegistry.TryGet(trainCar, out var firebox) || __instance.simFlow == null)
                return;

            // The door is not a firebox port in the stock graph. Read the door
            // port FireboxSimController already uses for the shovel check.
            var doorPortId = __instance.firebox != null ? __instance.firebox.fireboxDoorPortId : null;
            if (__instance.simFlow.TryGetPort(doorPortId, out var doorPort, canBeNullOrEmpty: true))
                firebox.AttachDoorPort(doorPort);
            else
                Main.Mod.Logger.Warning($"{trainCar.carLivery.id}: no firebox door port; door air stays closed");
        }
    }
}
