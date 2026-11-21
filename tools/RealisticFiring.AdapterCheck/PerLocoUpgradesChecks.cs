using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DV.Logic.Job;
using DV.ServicePenalty.UI;
using DV.ThingTypes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Upgrades;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
 internal static class PerLocoUpgradesChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            CheckJsonSaveMigration(check);
            CheckPerCarResolutionAndHostSync(check);
            CheckLateCarGuidCabFittings(check);
            CheckOwnedEngines(check);
            CheckUpgradesScreenOwnedList(check);
            CheckClientMirrorFittingsSection(check);
        }

        private static void CheckJsonSaveMigration(Action<bool, string> check)
        {
            // 1. Legacy save with fleet-wide flags only + an unknown key.
            var legacy = new JObject
            {
                ["brickArch"] = true,
                ["s282Stoker"] = true,
                ["futureFeature"] = 42,
            };
            var ledger = new UpgradeLedger();
            Fittings.ReadJson(legacy, ledger);
            check(ledger.FleetBought == (UpgradeMask.BrickArch | UpgradeMask.S282Stoker)
                  && ledger.LocomotiveBought.Count == 0,
                "a legacy fleet-wide upgrades save loads cleanly with no per-locomotive entries");

            // 2. Prior fleet-wide purchases stay fitted in PerLocomotive mode and are not duplicated;
            //    new per-locomotive purchases apply only to their own GUID and round-trip alongside legacy flags.
            var duplicateFleetBuy = ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, "guid-s282-042");
            ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.PerLocomotive, "guid-s282-042");
            ledger.Buy(UpgradeMask.S282Booster, UpgradeScope.PerLocomotive, "guid-s282-042");
            ledger.Buy(UpgradeMask.SniftingValves, UpgradeScope.PerLocomotive, "guid-s060-011");

            var saved = Fittings.WriteJson(legacy, ledger);
            var reloaded = new UpgradeLedger();
            Fittings.ReadJson(saved, reloaded);
            check(!duplicateFleetBuy
                  && saved["futureFeature"]?.Value<int>() == 42
                  && reloaded.FleetBought == (UpgradeMask.BrickArch | UpgradeMask.S282Stoker)
                  && reloaded.LocomotiveBought.Count == 2
                  && reloaded.LocomotiveBought["guid-s282-042"] == (UpgradeMask.OverFireJets | UpgradeMask.S282Booster)
                  && reloaded.LocomotiveBought["guid-s060-011"] == UpgradeMask.SniftingValves
                  && reloaded.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, "guid-s282-042")
                     == (UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.S282Stoker | UpgradeMask.S282Booster)
                  && reloaded.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, "guid-s060-011")
                     == (UpgradeMask.BrickArch | UpgradeMask.S282Stoker | UpgradeMask.SniftingValves),
                "per-locomotive upgrades save alongside legacy fleet-wide flags and unknown keys, keep prior fleet purchases fitted, and round-trip cleanly");

            // 3. Lenient booleans (hand-edited saves) are read cleanly, while malformed entries are skipped without throwing.
            var lenientAndMalformed = new JObject
            {
                ["brickArch"] = "not-a-bool",
                ["overFireJets"] = "true",
                ["s282Stoker"] = 1,
                ["s282FeedPump"] = 0,
                ["s282Booster"] = "false",
                ["sniftingValves"] = JValue.CreateNull(),
                ["driftingValve"] = new JObject(),
                ["byPassValves"] = new JArray(),
                ["locomotives"] = new JObject
                {
                    ["bad-entry"] = "wrong",
                    ["good-loco"] = new JObject
                    {
                        ["automaticFireDoor"] = "True",
                        ["driftingValve"] = 1,
                        ["s282Stoker"] = "nope",
                    },
                },
            };
            var safe = new UpgradeLedger();
            Fittings.ReadJson(lenientAndMalformed, safe);
            check(safe.FleetBought == (UpgradeMask.OverFireJets | UpgradeMask.S282Stoker)
                  && safe.LocomotiveBought.Count == 1
                  && safe.LocomotiveBought["good-loco"] == (UpgradeMask.AutomaticFireDoor | UpgradeMask.DriftingValve),
                "lenient hand-edited boolean tokens are accepted while malformed save entries are skipped without throwing");
        }

        private static void CheckPerCarResolutionAndHostSync(Action<bool, string> check)
        {
            var prevSettings = Main.Settings;
            try
            {
                Main.Settings = new Settings { UpgradeScope = UpgradeScope.PerLocomotive };

                var unnumbered = MakeCar("LocoS282A", null, null);
                var carA = MakeCar("LocoS282A", "S282-042", "guid-car-a");
                var carB = MakeCar("LocoS060", "S060-012", "guid-car-b");

                check(Fittings.CarGuidOf(unnumbered) == null && Fittings.CarGuidOf(carA) == "guid-car-a" && Fittings.CarGuidOf(carB) == "guid-car-b",
                    "CarGuidOf returns null before logicCar is assigned and the persistent carGuid once assigned");

                // Host per-locomotive authority: Car A has Arch+Jets+Stoker+FeedPump+Booster, Car B has AutoFireDoor+Snifting+ByPass.
                Fittings.ClearHost();
                Fittings.HostPerLocomotive = true;
                Fittings.ApplyHostPerCar(new[]
                {
                    (carA, UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.S282Stoker | UpgradeMask.S282FeedPump | UpgradeMask.S282Booster),
                    // Jets set without Arch on Car B: prerequisite enforcement must clear Jets on Car B.
                    (carB, UpgradeMask.OverFireJets | UpgradeMask.AutomaticFireDoor | UpgradeMask.SniftingValves | UpgradeMask.ByPassValves),
                }, hostVersion: 10, replaceAll: true);

                var carAOk = Fittings.HasArch(carA) && Fittings.HasJets(carA) && Fittings.HasStoker(carA)
                    && Fittings.HasFeedPump(carA) && Fittings.HasBooster(carA)
                    && !Fittings.HasAutoFireDoor(carA) && !Fittings.HasSnifting(carA) && !Fittings.HasDriftingValve(carA) && !Fittings.HasByPass(carA);
                var carBOk = !Fittings.HasArch(carB) && !Fittings.HasJets(carB) && !Fittings.HasStoker(carB)
                    && Fittings.HasAutoFireDoor(carB) && Fittings.HasSnifting(carB) && Fittings.HasByPass(carB) && !Fittings.HasDriftingValve(carB);
                var consumersOk = !DriftingFittings.Snifting(carA) && DriftingFittings.Snifting(carB)
                    && !DriftingFittings.ByPass(carA) && DriftingFittings.ByPass(carB)
                    && !AutoFireDoorFit.Has(carA) && AutoFireDoorFit.Has(carB);
                check(carAOk && carBOk && consumersOk,
                    "per-locomotive host masks resolve independently per TrainCar across Fittings, DriftingFittings, and AutoFireDoorFit, enforcing the arch prerequisite per car");

                // Two RealisticFireboxes on distinct S282s refit independently, and a firebox built before logicCar assignment refits as soon as logicCar arrives.
                var def = new LocoSim.Definitions.FireboxDefinition { ID = "firebox" };
                var boxA = new RealisticFirebox(def, carA);
                var boxB = new RealisticFirebox(def, carB);
                var lateCar = MakeCar("LocoS282A", null, null);
                var boxLate = new RealisticFirebox(def, lateCar);
                var beforeLateLogic = !boxLate.ArchFitted;
                var lateLogic = (Car)FormatterServices.GetUninitializedObject(typeof(Car));
                AccessTools.Field(typeof(Car), "carGuid").SetValue(lateLogic, "guid-late");
                AccessTools.Field(typeof(Car), "ID").SetValue(lateLogic, "S282-099");
                lateCar.logicCar = lateLogic;
                Fittings.SetCarFromHost(lateCar, UpgradeMask.BrickArch | UpgradeMask.OverFireJets, hostVersion: 10);
                boxA.RefreshFittings();
                boxB.RefreshFittings();
                boxLate.RefreshFittings();
                check(boxA.ArchFitted && boxA.JetsFitted && !boxB.ArchFitted && !boxB.JetsFitted
                      && beforeLateLogic && boxLate.ArchFitted && boxLate.JetsFitted,
                    "RealisticFirebox refits ArchFitted and JetsFitted independently per TrainCar and picks up upgrades once logicCar is assigned");

                // RealisticBooster and RealisticStoker evaluate Fitted per TrainCar.
                var carS282B = MakeCar("LocoS282A", "S282-088", "guid-s282-b");
                Fittings.SetCarFromHost(carS282B, UpgradeMask.None, hostVersion: 10);
                var boosterA = new RealisticBooster(new RealisticBoosterDefinition { ID = "rfBooster" }, carA);
                var boosterB = new RealisticBooster(new RealisticBoosterDefinition { ID = "rfBooster" }, carS282B);
                var stokerDef = new StokerDefinitionValues
                {
                    Id = "stoker",
                    MaxTransferRate = 2f,
                    MaxWorkingPressure = 14f,
                    SmoothTime = 1f,
                    SteamConsumptionReadOut = new LocoSim.Definitions.PortDefinition(LocoSim.Definitions.PortType.READONLY_OUT, LocoSim.Definitions.PortValueType.MASS_RATE, "STEAM"),
                    StokingNormalizedReadOut = new LocoSim.Definitions.PortDefinition(LocoSim.Definitions.PortType.READONLY_OUT, LocoSim.Definitions.PortValueType.STATE, "NORM"),
                    Control = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.CONTROL, "CTRL"),
                    SteamPressure = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.PRESSURE, "PRES"),
                    FireboxCoalLevel = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.COAL, "LVL"),
                    FireboxCoalCapacity = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.COAL, "CAP"),
                    FireboxCoalControl = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.COAL, "ADD", writeAllowed: true),
                    CoalAmount = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.COAL, "AMT"),
                    CoalConsume = new LocoSim.Definitions.PortReferenceDefinition(LocoSim.Definitions.PortValueType.COAL, "CON", writeAllowed: true),
                };
                var stokerA = new RealisticStoker(stokerDef, carA, 4f, false, upgrade: true);
                var stokerB = new RealisticStoker(stokerDef, carS282B, 4f, false, upgrade: true);
                check(boosterA.Fitted && !boosterB.Fitted && stokerA.Fitted && !stokerB.Fitted,
                    "RealisticBooster and RealisticStoker evaluate Fitted independently per S282 locomotive");

                // Stale ClientMirror update (hostVersion 9 < 10) must be ignored; newer (hostVersion 11) applies and bumps Version.
                // An out-of-order unreliable snapshot (V-1 = 10) arriving after V = 11 must be rejected so it never reverts a purchase or double-refits.
                var versionBefore = Fittings.Version;
                var staleApplied = Fittings.SetCarFromHost(carB, UpgradeMask.BrickArch, hostVersion: 9);
                var sameApplied = Fittings.SetCarFromHost(carA, Fittings.EffectiveMask(carA), hostVersion: 10);
                var freshApplied = Fittings.SetCarFromHost(carB, UpgradeMask.BrickArch | UpgradeMask.DriftingValve, hostVersion: 11);
                var lateReverted = Fittings.SetCarFromHost(carB, UpgradeMask.None, hostVersion: 10);
                // Even an older reliable batch at hostVersion 10 must not overwrite carB's newer version 11 state.
                Fittings.ApplyHostPerCar(new[] { (carA, Fittings.EffectiveMask(carA)), (carB, UpgradeMask.None) }, hostVersion: 10, replaceAll: false);
                check(!staleApplied && !sameApplied && freshApplied && !lateReverted && Fittings.Version == versionBefore + 1
                      && Fittings.HasArch(carB) && DriftingFittings.Valve(carB) && !DriftingFittings.Valve(carA),
                    "stale host per-car snapshots and out-of-order V-1 snapshots after V are rejected without reverting masks or bumping Version");

                // Wrapping 16-bit host version: 65535 -> 0 accepts 0 and rejects a late 65535 snapshot.
                var wrapCar = MakeCar("LocoS060", "S060-099", "guid-wrap");
                Fittings.ClearHost();
                Fittings.HostPerLocomotive = true;
                var wrapBefore = Fittings.Version;
                var wrapOld = Fittings.SetCarFromHost(wrapCar, UpgradeMask.BrickArch, hostVersion: 65535);
                var wrapNew = Fittings.SetCarFromHost(wrapCar, UpgradeMask.BrickArch | UpgradeMask.OverFireJets, hostVersion: 0);
                var wrapLate = Fittings.SetCarFromHost(wrapCar, UpgradeMask.BrickArch, hostVersion: 65535);
                check(wrapOld && wrapNew && !wrapLate && Fittings.Version == wrapBefore + 2
                      && Fittings.HasArch(wrapCar) && Fittings.HasJets(wrapCar),
                    "wrapping 16-bit per-car host versions accept 0 after 65535 and reject a late 65535 snapshot");

                // Payment screen quote scope validation: switching scope while the payment screen is open invalidates the quote.
                Fittings.ClearHost();
                Main.Settings = new Settings { UpgradeScope = UpgradeScope.PerLocomotive };
                var paying = (UpgradePayingScreen)FormatterServices.GetUninitializedObject(typeof(UpgradePayingScreen));
                paying.Sell(UpgradesScreen.Arch, UpgradeScope.PerLocomotive, carA, "S282-042");
                var matchesBefore = paying.QuoteScopeMatches;
                Main.Settings.UpgradeScope = UpgradeScope.FleetWide;
                var matchesAfter = paying.QuoteScopeMatches;
                check(matchesBefore && !matchesAfter,
                    "UpgradePayingScreen captures its quote scope and detects a mid-payment scope switch before charging");

                // Eligibility list is centralized in UpgradeKeys and shared by DriftingFittings and AutoFireDoorFit.
                var carOther = MakeCar("LocoS282B", "S282-T", "guid-tender");
                var carHudson = MakeCar("S464-730", "HUD-1", "guid-hudson");
                Main.Settings = new Settings { AutomaticFireDoor = UpgradeRule.BuyInCareer };
                Fittings.ClearHost();
                Fittings.HostPerLocomotive = true;
                Fittings.ApplyHostPerCar(new[] { (carA, UpgradeMask.None), (carB, UpgradeMask.AutomaticFireDoor) }, hostVersion: 1, replaceAll: true);
                check(UpgradeKeys.IsEligibleLivery("LocoS282A") == DriftingFittings.Upgradable("LocoS282A")
                      && UpgradeKeys.IsEligibleLivery("LocoS060") == DriftingFittings.Upgradable("LocoS060")
                      && !DriftingFittings.Upgradable("LocoS282B")
                      && !AutoFireDoorFit.Has(carA) && AutoFireDoorFit.Has(carB)
                      && !AutoFireDoorFit.Has(carOther) && AutoFireDoorFit.Has(carHudson),
                    "career upgrade livery eligibility is centralized in UpgradeKeys and shared by DriftingFittings and AutoFireDoorFit");

                Fittings.ClearHost();
                check(Fittings.HostPerLocomotive == null, "ClearHost resets per-locomotive host override state cleanly");
            }
            finally
            {
                Fittings.ClearHost();
                Main.Settings = prevSettings;
            }
        }

        private static void CheckLateCarGuidCabFittings(Action<bool, string> check)
        {
            var prevSettings = Main.Settings;
            var prevInCareer = Fittings.InCareerOverride;
            var lateCar = MakeCar("LocoS282A", null, null);
            var fakeLoadedInterior = (GameObject)FormatterServices.GetUninitializedObject(typeof(GameObject));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(fakeLoadedInterior, new IntPtr(++nextObjectId));
            AccessTools.PropertySetter(typeof(TrainCar), nameof(TrainCar.loadedInterior)).Invoke(lateCar, new object[] { fakeLoadedInterior });
            var fakeInterior = (Transform)FormatterServices.GetUninitializedObject(typeof(Transform));
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(fakeInterior, new IntPtr(++nextObjectId));

            var def = new LocoSim.Definitions.FireboxDefinition { ID = "firebox" };
            var driftEvents = new List<(TrainCar Car, Transform Interior, bool ShouldHave)>();
            var jetsEvents = new List<(TrainCar Car, Transform Interior, bool ShouldHave)>();
            try
            {
                Fittings.ClearHost();
                Fittings.InCareerOverride = true;
                Main.Settings = new Settings
                {
                    UpgradeScope = UpgradeScope.PerLocomotive,
                    BrickArch = UpgradeRule.BuyInCareer,
                    OverFireJets = UpgradeRule.BuyInCareer,
                    DriftingValve = UpgradeRule.BuyInCareer,
                };
                Fittings.LoadLocalForTests(UpgradeMask.None, new[]
                {
                    new KeyValuePair<string, UpgradeMask>("guid-late-cab", UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.DriftingValve),
                });

                var lateFirebox = new RealisticFirebox(def, lateCar);
                FireboxRegistry.Register(lateFirebox);

                DriftingValveCab.Valve.SyncHook = (c, i, fitted) => driftEvents.Add((c, i, fitted));
                JetsCab.Valve.SyncHook = (c, i, fitted) => jetsEvents.Add((c, i, fitted));

                // Cab interior loads BEFORE logicCar is assigned: valves cannot see the per-loco purchase yet.
                DriftingValveCab.Valve.TrackInterior(lateCar, fakeInterior);
                JetsCab.Valve.TrackInterior(lateCar, fakeInterior);
                StokerCab.TrackInterior(lateCar, fakeInterior);
                FeedPumpCab.TrackUpgradeCar(lateCar);
                BoosterCab.TrackInterior(lateCar, fakeInterior);

                var versionBefore = Fittings.Version;
                var driftBefore = DriftingFittings.Valve(lateCar);
                var jetsBefore = Fittings.HasJets(lateCar);
                DriftingValveCab.EnsureFitted();
                JetsCab.EnsureFitted();
                var noResyncBeforeGuid = driftEvents.Count == 0 && jetsEvents.Count == 0
                    && !StokerCab.AnyGuidChanged() && !FeedPumpCab.AnyGuidChanged() && !BoosterCab.AnyGuidChanged();

                // Assigning logicCar.carGuid later does NOT bump Fittings.Version, yet all cab fittings must detect it.
                var logic = (Car)FormatterServices.GetUninitializedObject(typeof(Car));
                AccessTools.Field(typeof(Car), "carGuid").SetValue(logic, "guid-late-cab");
                AccessTools.Field(typeof(Car), "ID").SetValue(logic, "S282-077");
                lateCar.logicCar = logic;

                var versionUnchanged = Fittings.Version == versionBefore;
                var otherCabsDetected = StokerCab.AnyGuidChanged() && FeedPumpCab.AnyGuidChanged() && BoosterCab.AnyGuidChanged();
                DriftingValveCab.EnsureFitted();
                JetsCab.EnsureFitted();
                // Second call with unchanged version and GUID must be a no-op.
                DriftingValveCab.EnsureFitted();
                JetsCab.EnsureFitted();

                check(!driftBefore && !jetsBefore && noResyncBeforeGuid && versionUnchanged && otherCabsDetected
                      && driftEvents.Count == 1 && driftEvents[0].Car == lateCar && driftEvents[0].Interior == fakeInterior && driftEvents[0].ShouldHave
                      && jetsEvents.Count == 1 && jetsEvents[0].Car == lateCar && jetsEvents[0].Interior == fakeInterior && jetsEvents[0].ShouldHave,
                    "cab fittings recheck and attach purchased valves when logicCar.carGuid is assigned after interior load without bumping Fittings.Version");
            }
            finally
            {
                DriftingValveCab.Valve.SyncHook = null;
                JetsCab.Valve.SyncHook = null;
                DriftingValveCab.Valve.Untrack(lateCar);
                JetsCab.Valve.Untrack(lateCar);
                StokerCab.Untrack(lateCar);
                FeedPumpCab.Untrack(lateCar);
                BoosterCab.Untrack(lateCar);
                ((Dictionary<TrainCar, RealisticFirebox>)AccessTools.Field(typeof(FireboxRegistry), "Fireboxes").GetValue(null)).Remove(lateCar);
                Fittings.ClearLocalForTests();
                Fittings.InCareerOverride = prevInCareer;
                Main.Settings = prevSettings;
            }
        }

        private static void CheckOwnedEngines(Action<bool, string> check)
        {
            // The game's Owned Vehicles: unique cars it lists, a restoration engine hidden (preventDebtDisplay) until serviced.
            var owned = MakeCar("LocoS282A", "S282-042", "guid-owned");
            var derelict = MakeCar("LocoS060", "S060-007", "guid-derelict");
            derelict.preventDebtDisplay = true;
            var company = MakeCar("LocoS282A", "S282-900", "guid-company");
            var restoredOnGuest = MakeCar("LocoS060", "S060-012", "guid-restored");
            var diesel = MakeCar("LocoDE2", "DE2-001", "guid-de2");
            var tender = MakeCar("LocoS282B", "S282-042T", "guid-tender");
            var unnumbered = MakeCar("LocoS282A", null, null);
            var listed = new HashSet<TrainCar> { owned, derelict, diesel, tender, unnumbered };
            var locos = new List<TrainCar> { owned, derelict, company, restoredOnGuest, diesel, unnumbered };
            var found = OwnedEngines.Select(locos, listed, car => car == restoredOnGuest);
            check(found.Count == 2 && found.Contains(owned) && found.Contains(restoredOnGuest),
                "the owned list holds the S282s and S060s the game shows as owned (and, on a guest, the restored ones), " +
                "once each; not a restoration engine still hidden, a company engine, a diesel, a tender, or a car without a GUID");
            var empty = OwnedEngines.Select(new List<TrainCar> { company }, new HashSet<TrainCar>(), car => false);
            check(empty.Count == 0, "a career owning no S282 or S060 lists none");
        }

        private static void CheckUpgradesScreenOwnedList(Action<bool, string> check)
        {
            var screen = (UpgradesScreen)FormatterServices.GetUninitializedObject(typeof(UpgradesScreen));
            var mainScreen = (CareerManagerMainScreen)FormatterServices.GetUninitializedObject(typeof(CareerManagerMainScreen));
            var payingScreen = (UpgradePayingScreen)FormatterServices.GetUninitializedObject(typeof(UpgradePayingScreen));
            var infoScreen = (CareerManagerInfoScreen)FormatterServices.GetUninitializedObject(typeof(CareerManagerInfoScreen));

            AccessTools.Field(typeof(UpgradesScreen), "main").SetValue(screen, mainScreen);
            AccessTools.Field(typeof(UpgradesScreen), "paying").SetValue(screen, payingScreen);
            AccessTools.Field(typeof(UpgradesScreen), "info").SetValue(screen, infoScreen);
            foreach (var name in new[] { "picker", "ownedCars", "entries" })
            {
                var field = AccessTools.Field(typeof(UpgradesScreen), name);
                field.SetValue(screen, Activator.CreateInstance(field.FieldType));
            }

            var s282 = MakeCar("LocoS282A", "S282-042", "guid-loco-a");
            var s060 = MakeCar("LocoS060", "S060-012", "guid-loco-b");

            // 1. A fresh visit from the main menu starts on the list, by car ID; nothing is picked for the player.
            var fresh = screen.PrepareForActivation(mainScreen);
            screen.ApplyOwned(new[] { s282, s060 });
            var picker = screen.Picker;
            var listed = fresh && picker.OnList && picker.Engines.Count == 2
                         && picker.Engines[0].Id == "S060-012" && picker.Engines[1].Id == "S282-042";

            // 2. Picking one opens its page; back from paying or its info the page stays.
            var picked = picker.Pick(1) && picker.Picked?.Guid == "guid-loco-a";
            var fromPaying = screen.PrepareForActivation(payingScreen);
            screen.ApplyOwned(new[] { s282, s060 });
            var keptAfterPaying = !fromPaying && picker.PickedGuid == "guid-loco-a";
            var fromInfo = screen.PrepareForActivation(infoScreen);
            screen.ApplyOwned(new[] { s060, s282 });
            var keptAfterInfo = !fromInfo && picker.PickedGuid == "guid-loco-a";

            // 3. Back goes to the list with the engine left highlighted; a fresh visit forgets the pick.
            var left = picker.Back();
            picker.Pick(0);
            var freshAgain = screen.PrepareForActivation(mainScreen);
            var freshForgets = freshAgain && picker.OnList;

            // 4. An engine no longer owned while picked: the list.
            picker.Pick(1);
            screen.ApplyOwned(new[] { s060 });
            var goneToList = picker.OnList && picker.Engines.Count == 1;

            check(listed && picked && keptAfterPaying && keptAfterInfo && left == 1 && freshForgets && goneToList,
                "the per-locomotive Upgrades screen starts on the owned list by car ID, keeps the picked engine's page back from paying or info, " +
                "returns to the list with the engine left, forgets the pick on a fresh visit, and drops an engine no longer owned");

            // 5. Each engine on the list shows how many of its upgrades it has.
            var prevSettings = Main.Settings;
            var prevInCareer = Fittings.InCareerOverride;
            try
            {
                Fittings.ClearHost();
                Fittings.InCareerOverride = true;
                Main.Settings = new Settings { UpgradeScope = UpgradeScope.PerLocomotive };
                Fittings.LoadLocalForTests(UpgradeMask.BrickArch, new[]
                {
                    new KeyValuePair<string, UpgradeMask>("guid-loco-a", UpgradeMask.SniftingValves | UpgradeMask.AutomaticFireDoor),
                });
                var has = AccessTools.Method(typeof(UpgradesScreen), "Has");
                var a = (string)has.Invoke(null, new object[] { new OwnedLocomotive("guid-loco-a", "S282-042", "LocoS282A"), s282 });
                var b = (string)has.Invoke(null, new object[] { new OwnedLocomotive("guid-loco-b", "S060-012", "LocoS060"), s060 });
                check(a == "3/9 fitted" && b == "1/6 fitted",
                    $"the owned list shows each engine's upgrades fitted of those it can take (S282 {a}, S060 {b})");
            }
            finally
            {
                Fittings.ClearLocalForTests();
                Fittings.InCareerOverride = prevInCareer;
                Main.Settings = prevSettings;
            }
        }

        private static void CheckClientMirrorFittingsSection(Action<bool, string> check)
        {
            var sent = UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.DriftingValve;
            var data = ClientMirror.Capture(null, null, null, null, null, null, null, carFittings: sent, fittingsVersion: 1234);
            UpgradeMask? gotMask = null;
            ushort gotVersion = 0;
            ClientMirror.Apply(data, null, null, null, null, null, "per-loco-guest", null, null, onFittings: (mask, version) =>
            {
                gotMask = mask;
                gotVersion = version;
            });
            check(gotMask == sent && gotVersion == 1234,
                "ClientMirror carries a locomotive's effective upgrade mask and host version in its Fittings section");
        }

        private static int nextObjectId = 100;

        private static TrainCar MakeCar(string liveryId, string id, string guid)
        {
            var car = (TrainCar)FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var carId = ++nextObjectId;
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(car, new IntPtr(carId));

            var livery = (TrainCarLivery)FormatterServices.GetUninitializedObject(typeof(TrainCarLivery));
            var liveryIdNum = ++nextObjectId;
            AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(livery, new IntPtr(liveryIdNum));
            livery.id = liveryId;
            car.carLivery = livery;
            if (guid != null)
            {
                var logic = (Car)FormatterServices.GetUninitializedObject(typeof(Car));
                AccessTools.Field(typeof(Car), "carGuid").SetValue(logic, guid);
                AccessTools.Field(typeof(Car), "ID").SetValue(logic, id ?? liveryId);
                car.logicCar = logic;
            }
            return car;
        }
    }
}
