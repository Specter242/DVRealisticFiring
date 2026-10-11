using System;
using System.Linq;
using HarmonyLib;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Runs when a loco's visual fire starts, after its cab interior has
    /// loaded; the interior loads later than the sim graph. Dumps the firebox
    /// geometry, builds the zoned coal bed on realistic fireboxes, and puts
    /// the injector's overflow beside the cab's injector lever.
    /// </summary>
    [HarmonyPatch(typeof(Fire), "Start")]
    internal static class FirePatch
    {
        private static void Postfix(Fire __instance)
        {
            try
            {
                var car = TrainCar.Resolve(__instance.gameObject);
                // Every coal-fired loco, custom ones included, so new locos
                // can be measured before the mod supports them.
                if (Main.Settings.DumpFireboxGeometry)
                    FireboxGeometryDump.DumpOnce(car, __instance);
                if (RealisticOilFirebox.TryGet(car, out var oil))
                {
                    var look = OilFireLook.Attach(__instance, car, oil);
                    if (look != null && Main.Settings.LogFireboxCreation)
                        Main.Mod.Logger.Log($"{car.ID}: {look}");
                }
                if (FireboxRegistry.TryGet(car, out var firebox))
                {
                    var result = ZonedCoalBed.Attach(__instance, car, firebox);
                    StokerSpray.Attach(__instance.GetComponent<ZonedCoalBed>(), firebox);
                    var flames = FireboxFlames.Attach(__instance, __instance.GetComponent<ZonedCoalBed>(), firebox);
                    if (Main.Settings.LogFireboxCreation)
                    {
                        Main.Mod.Logger.Log($"{car.ID}: {result}");
                        if (flames != null)
                            Main.Mod.Logger.Log($"{car.ID}: {flames}");
                    }
                }
                var stokerCab = StokerCab.Attach(car, car.loadedInterior != null ? car.loadedInterior.transform : null);
                if (stokerCab != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {stokerCab}");
                var boosterCab = BoosterCab.Attach(car, car.loadedInterior != null ? car.loadedInterior.transform : null);
                if (boosterCab != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {boosterCab}");
                var dropGrate = DropGrateCab.Attach(car, car.loadedInterior != null ? car.loadedInterior.transform : null);
                if (dropGrate != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {dropGrate}");
                var feedLever = FeedPumpCab.RebindInjectorLever(car, car.loadedInterior != null ? car.loadedInterior.transform : null);
                if (feedLever != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {feedLever}");
                // After the feed pump takes the fireman's lever on a cab with two.
                var steamLevers = InjectorSteamCab.Attach(car, car.loadedInterior != null ? car.loadedInterior.transform : null);
                if (steamLevers != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {steamLevers}");
                var guard = BoilerAlarms.Attach(car, __instance);
                if (guard != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {guard}");
                if (InjectorRig.TryGet(car, out var rig))
                {
                    var roots = new[] { car.transform, __instance.transform.root, car.loadedInterior != null ? car.loadedInterior.transform : null };
                    var overflow = InjectorOverflow.Attach(car, rig, roots.Where(t => t != null).Distinct());
                    if (overflow != null && Main.Settings.LogFireboxCreation)
                        Main.Mod.Logger.Log($"{car.ID}: {overflow}");
                }
            }
            catch (Exception e)
            {
                Main.Mod.Logger.LogException("Firebox interior setup failed", e);
            }
        }
    }
}
