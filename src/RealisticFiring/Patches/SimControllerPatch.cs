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
                SetupGuard.Logged(trainCar, "tender water valves", () => Visuals.TenderWaterValves.Attach(trainCar, __instance.simFlow));

            if (LeftStock.Is(trainCar))
            {
                LeftStock.Keep(__instance.simFlow);
                if (Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{trainCar.carLivery.id}: left stock, not part of the mod");
                return;
            }

            if (FireboxRegistry.TryGet(trainCar, out _) || RealisticOilFirebox.TryGet(trainCar, out _))
            {
                // The valves first: the blower and the injectors take the lift as working pressure.
                SetupGuard.Logged(trainCar, "safety valves", () => SafetyValveFit.Attach(trainCar, __instance.simFlow));
                SetupGuard.Logged(trainCar, "blower and air pumps", () => BlowerFit.Attach(trainCar, __instance.simFlow));
                SetupGuard.Logged(trainCar, "shed's blower line connection", () => Shed.ShedConnections.Attach(trainCar, __instance.simFlow));
            }

            if (Main.RealisticInjectorsEnabled && __instance.simFlow != null)
            {
                SetupGuard.Logged(trainCar, "injectors", () => InjectorRig.Attach(trainCar, __instance.simFlow));
                SetupGuard.Logged(trainCar, "injector steam handles",
                    () => InjectorRig.TryGet(trainCar, out var rig) ? Visuals.InjectorSteamCab.NotchSteamHandles(trainCar, rig) : null);
            }

            if (__instance.simFlow != null)
            {
                SetupGuard.Logged(trainCar, "superheater", () => EngineSuperheater.Attach(trainCar, __instance));
                SetupGuard.Logged(trainCar, "boiler water", () => BoilerWaterRig.Attach(trainCar, __instance.simFlow));
            }

            if (__instance.simFlow != null && RealisticOilFirebox.TryGet(trainCar, out var oil))
            {
                SetupGuard.Run(trainCar, "oil burner's controls", () =>
                {
                    var doorId = __instance.firebox != null ? __instance.firebox.fireboxDoorPortId : null;
                    __instance.simFlow.TryGetPort(doorId, out var door, canBeNullOrEmpty: true);
                    __instance.simFlow.TryGetPort(OilBurnerFit.Atomizer.PortId, out var atomizer, canBeNullOrEmpty: true);
                    __instance.simFlow.TryGetPort(OilBurnerFit.Heater.PortId, out var heater, canBeNullOrEmpty: true);
                    __instance.simFlow.TryGetPort(OilBurnerFit.ShopSteam.PortId, out var shopSteam, canBeNullOrEmpty: true);
                    __instance.simFlow.TryGetPort(OilBurnerFit.PeepHolePortId, out var peepHole, canBeNullOrEmpty: true);
                    oil.AttachPorts(door, atomizer, heater, shopSteam, peepHole);
                });
                SetupGuard.Logged(trainCar, "oil fireman", () => FiremanRig.AttachOil(trainCar, __instance, oil));
            }

            if (!FireboxRegistry.TryGet(trainCar, out var firebox) || __instance.simFlow == null)
                return;

            SetupGuard.Run(trainCar, "firebox's door, drop grate, and ashpan", () =>
            {
                // The door is not a firebox port in the stock graph. Read the door
                // port FireboxSimController already uses for the shovel check.
                var doorPortId = __instance.firebox != null ? __instance.firebox.fireboxDoorPortId : null;
                if (__instance.simFlow.TryGetPort(doorPortId, out var doorPort, canBeNullOrEmpty: true))
                    firebox.AttachDoorPort(doorPort);
                else
                    Main.Mod.Logger.Warning($"{trainCar.carLivery.id}: no firebox door port; door air stays closed");
                if (__instance.simFlow.TryGetPort(RealisticFirebox.DropGrateControl.PortId, out var dropGratePort, canBeNullOrEmpty: true))
                    firebox.AttachDropGratePort(dropGratePort);
                if (__instance.simFlow.TryGetPort(RealisticFirebox.AshpanControl.PortId, out var ashpanPort, canBeNullOrEmpty: true))
                {
                    firebox.AttachAshpanPort(ashpanPort);
                    Visuals.AshpanLever.Attach(trainCar, firebox, ashpanPort);
                }
            });
            SetupGuard.Run(trainCar, "ashpan's glow", () => Visuals.AshpanGlow.Attach(trainCar, firebox));
            SetupGuard.Logged(trainCar, "fireman", () => FiremanRig.Attach(trainCar, __instance, firebox));
        }
    }
}
