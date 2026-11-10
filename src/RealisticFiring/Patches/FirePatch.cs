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
        // Each fitting on its own (SetupGuard): one that fails on an odd
        // firebox or beside another mod leaves the rest. Under one catch, a
        // failed coal bed took the stoker's valves, the injector's steam
        // levers, and the boiler's alarms with it (audits 2026-10-10 and
        // 2026-11-09).
        private static void Postfix(Fire __instance)
        {
            TrainCar car = null;
            SetupGuard.Run(null, "firebox interior", () => car = TrainCar.Resolve(__instance.gameObject));
            if (car == null)
                return;
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            // Every coal-fired loco, custom ones included, so new locos
            // can be measured before the mod supports them.
            if (Main.Settings.DumpFireboxGeometry)
                SetupGuard.Run(car, "firebox geometry dump", () => FireboxGeometryDump.DumpOnce(car, __instance));
            if (RealisticOilFirebox.TryGet(car, out var oil))
                SetupGuard.Logged(car, "oil fire's look", () => OilFireLook.Attach(__instance, car, oil));
            if (FireboxRegistry.TryGet(car, out var firebox))
            {
                SetupGuard.Logged(car, "coal bed", () => ZonedCoalBed.Attach(__instance, car, firebox));
                SetupGuard.Run(car, "stoker's spray", () => StokerSpray.Attach(__instance.GetComponent<ZonedCoalBed>(), firebox));
                SetupGuard.Logged(car, "flames", () => FireboxFlames.Attach(__instance, __instance.GetComponent<ZonedCoalBed>(), firebox));
                SetupGuard.Logged(car, "fire volume", () => FireboxVolume.Attach(__instance, __instance.GetComponent<ZonedCoalBed>(), firebox));
                SetupGuard.Logged(car, "coal lumps", () => CoalLumps.Attach(__instance, __instance.GetComponent<ZonedCoalBed>()));
                SetupGuard.Logged(car, "firebox lighting", () =>
                {
                    var lighting = FireboxLighting.Attach(__instance, __instance.GetComponent<ZonedCoalBed>(), firebox);
                    return lighting != null ? $"firebox {lighting}" : null;
                });
            }
            SetupGuard.Logged(car, "stoker's cab controls", () => StokerCab.Attach(car, interior));
            SetupGuard.Run(car, "S282 interaction areas", () => S282InteractionAreas.Attach(car, interior));
            SetupGuard.Logged(car, "feed pump on the backhead", () => FeedPumpCab.AttachOnBackhead(car, interior));
            SetupGuard.Logged(car, "booster's cab controls", () => BoosterCab.Attach(car, interior));
            SetupGuard.Logged(car, "drop grate lever", () => DropGrateCab.Attach(car, interior));
            SetupGuard.Logged(car, "feed pump's lever", () => FeedPumpCab.RebindInjectorLever(car, interior));
            // After the feed pump takes the fireman's lever on a cab with two.
            SetupGuard.Logged(car, "injector steam levers", () => InjectorSteamCab.Attach(car, interior));
            SetupGuard.Logged(car, "boiler alarms", () => BoilerAlarms.Attach(car, __instance));
            if (InjectorRig.TryGet(car, out var rig))
            {
                SetupGuard.Logged(car, "injector overflow", () =>
                {
                    var roots = new[] { car.transform, __instance.transform.root, interior };
                    return InjectorOverflow.Attach(car, rig, roots.Where(t => t != null).Distinct());
                });
            }
        }
    }
}
