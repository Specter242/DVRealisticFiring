using System;
using System.Linq;
using System.Reflection;
using DV.Simulation.Cars;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Sim;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// On locos whose firebox the mod replaces, Custom Car Loader's mechanical
    /// stoker becomes the mod's <see cref="RealisticStoker"/>. CCL's types are
    /// found by name, so the mod runs without CCL installed; the patch then
    /// does nothing.
    /// </summary>
    [HarmonyPatch]
    internal static class StokerDefinitionPatch
    {
        private const string DefinitionTypeName = "CCL.Importer.Components.Simulation.SteamMechanicalStokerDefinitionInternal";

        private static Type definitionType;

        private static bool Prepare()
        {
            definitionType = definitionType ?? AccessTools.TypeByName(DefinitionTypeName);
            return definitionType != null;
        }

        private static MethodBase TargetMethod() => AccessTools.Method(definitionType, "InstantiateImplementation");

        private static bool Prefix(SimComponentDefinition __instance, ref SimComponent __result)
        {
            var car = __instance.GetComponentInParent<TrainCar>();
            if (!Main.ReplacementEnabled || !FireboxRegistry.IsSupported(car))
                return true;

            try
            {
                var values = Read(__instance);
                var simController = car.SimController != null ? car.SimController : car.GetComponentInChildren<SimController>(true);
                var fireboxController = simController != null ? simController.firebox : null;
                var multiplier = fireboxController != null && fireboxController.coalConsumptionMultiplier > 0f
                    ? fireboxController.coalConsumptionMultiplier
                    : 1f;
                var connections = simController != null ? simController.connectionsDefinition : null;
                var steamPort = $"{values.Id}.{values.SteamConsumptionReadOut.ID}";
                var steamWired = connections != null && connections.portReferenceConnections != null
                                 && connections.portReferenceConnections.Any(r => r.portId == steamPort);
                __result = new RealisticStoker(values, car, multiplier, steamWired);
                return false;
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException($"Realistic stoker failed on {car.carLivery.id}; keeping CCL's stoker", e);
                return true;
            }
        }

        internal static StokerDefinitionValues Read(SimComponentDefinition def) => new StokerDefinitionValues
        {
            Id = def.ID,
            MaxTransferRate = Field<float>(def, "MaxTransferRate"),
            MaxWorkingPressure = Field<float>(def, "MaxWorkingPressure"),
            SmoothTime = Field<float>(def, "SmoothTime"),
            SteamConsumptionReadOut = Field<PortDefinition>(def, "SteamConsumptionReadOut"),
            StokingNormalizedReadOut = Field<PortDefinition>(def, "StokingNormalizedReadOut"),
            Control = Field<PortReferenceDefinition>(def, "Control"),
            SteamPressure = Field<PortReferenceDefinition>(def, "SteamPressure"),
            FireboxCoalLevel = Field<PortReferenceDefinition>(def, "FireboxCoalLevel"),
            FireboxCoalCapacity = Field<PortReferenceDefinition>(def, "FireboxCoalCapacity"),
            FireboxCoalControl = Field<PortReferenceDefinition>(def, "FireboxCoalControl"),
            CoalAmount = Field<PortReferenceDefinition>(def, "CoalAmount"),
            CoalConsume = Field<PortReferenceDefinition>(def, "CoalConsume"),
        };

        private static T Field<T>(object instance, string name)
        {
            var field = AccessTools.Field(instance.GetType(), name)
                        ?? throw new MissingFieldException(instance.GetType().FullName, name);
            return (T)field.GetValue(instance);
        }
    }
}
