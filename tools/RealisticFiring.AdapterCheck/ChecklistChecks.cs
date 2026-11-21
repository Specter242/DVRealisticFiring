using System;
using System.Collections.Generic;
using System.Linq;
using DV.Items;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Checks the firing checklists booklet (its printed steps against the live
    /// checklists, starting kit, shop shelf, and the old card's migration), the
    /// firing tablet &amp; Comms Tablet Check tab layout, and the Cab Control
    /// Spotlight helpers.
    /// </summary>
    internal static class ChecklistChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            CheckPrintedBooklet(check);
            CheckBookletStartingKit(check);
            CheckCardMigration(check);

            // Tablet 5th page (Check) save/load & button layout.
            check(FiringTabletGadget.Pages == 5 && FiringTabletGadget.ChecklistPage == 4
                  && FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = 4 }) == 4
                  && FiringTabletGadget.SavedPage(new JObject { ["rfTabletPage"] = 10 }) == 4,
                "firing tablet supports and saves the 5th page (Check)");

            var screen = new Rect(0, 0, 1080, 924);
            var checkAreas = Enumerable.Range(0, FiringTabletPanel.Pages)
                .Select(i => Scaled(FiringTabletPanel.ButtonArea(i), 2f))
                .Concat(Enumerable.Range(0, FiringTabletPanel.ChecklistActions).Select(i => Scaled(FiringTabletPanel.ChecklistButtonArea(i), 2f)))
                .ToList();
            check(checkAreas.All(a => a.xMin >= screen.xMin && a.yMin >= screen.yMin && a.xMax <= screen.xMax && a.yMax <= screen.yMax)
                  && checkAreas.SelectMany((a, i) => checkAreas.Skip(i + 1), (a, b) => !a.Overlaps(b)).All(apart => apart),
                "doubled for the Comms Tablet's screen, the five page tabs and four checklist action buttons fit inside it without overlap");

            // CabControlSpotlight overlap resolution & safe toggle with null car.
            var boxes = new List<CabControlSpotlight.LabelBox>
            {
                new CabControlSpotlight.LabelBox { X = 100, Y = 200, Width = 140, Height = 38, Label = "INJ STEAM" },
                new CabControlSpotlight.LabelBox { X = 110, Y = 210, Width = 140, Height = 38, Label = "JETS" },
                new CabControlSpotlight.LabelBox { X = 105, Y = 215, Width = 140, Height = 38, Label = "DRIFTING" },
            };
            CabControlSpotlight.ResolveOverlap(boxes, 6f, 44f, 900f);
            check(boxes[1].Y >= boxes[0].Y + boxes[0].Height + 5.9f
                  && boxes[2].Y >= boxes[1].Y + boxes[1].Height + 5.9f,
                "cab control spotlight separates overlapping floating screen labels cleanly");

            CabControlSpotlight.Dismiss();
            var noCarNotice = CabControlSpotlight.Toggle(null);
            check(!CabControlSpotlight.Active && !string.IsNullOrEmpty(noCarNotice),
                "cab control spotlight handles missing locomotive gracefully: " + noCarNotice);
            var isVrCallsVrManager = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(CabControlSpotlight), nameof(CabControlSpotlight.IsVr)))
                .Any(i => i.operand is System.Reflection.MethodBase m && m.DeclaringType == typeof(VRManager) && m.Name == nameof(VRManager.IsVREnabled));
            check(CabControlSpotlight.CoupledTender(null) == null && isVrCallsVrManager,
                "cab control spotlight guards null tender lookup and checks VRManager.IsVREnabled()");

            // Shop shelf offset keeps the booklet cleanly separated from clinometer / firing tablet.
            check(ChecklistBookletShop.ShelfOffset.magnitude >= 0.15f && ChecklistBookletShop.ShelfSize.x < ChecklistBookletShop.ShelfOffset.magnitude - 0.05f
                  && ChecklistBookletShop.ShelfSource == "Clinometer",
                $"the checklists' shelf copy ({ChecklistBookletShop.ShelfSize.x:F3} m wide) stands {ChecklistBookletShop.ShelfOffset.magnitude:F2} m from the clinometer, tags and colliders apart");

            // Dual-injector isolation in ChecklistReader: water on right + steam on left must NOT tick water+prime together.
            CheckDualInjectorReader(check);

            // Drop-grate port observation in ChecklistReader (works for multiplayer guests too).
            var firebox = new RealisticFirebox(new FireboxDefinition { ID = "firebox" }, null);
            var dgPort = new Port("rfDropGrate", new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, RealisticFirebox.DropGrateControl.Id));
            firebox.AttachDropGratePort(dgPort);
            dgPort.Value = 0f;
            var closedSnap = ChecklistReader.Snapshot(firebox, null, null, null, null);
            dgPort.Value = 1f;
            var openSnap = ChecklistReader.Snapshot(firebox, null, null, null, null);
            check(!closedSnap.DropGrateOpenedRecently && openSnap.DropGrateOpenedRecently,
                "checklist reader observes the drop-grate control port directly for both host and multiplayer guests");

            // Host-authoritative slice checklist sync across host, slicing guest, and second crew guest.
            CheckMultiplayerSliceSync(check);
        }

        /// <summary>
        /// The booklet prints one page per checklist, in the tablet's order,
        /// each step titled as the live checklist titles it in its fullest form.
        /// </summary>
        private static void CheckPrintedBooklet(Action<bool, string> check)
        {
            var entries = ChecklistBookletContents.Entries;
            var ids = (ChecklistId[])Enum.GetValues(typeof(ChecklistId));
            var parsed = entries.Select(e => Enum.TryParse<ChecklistId>(e.Checklist, out var id) ? id : (ChecklistId?)null).ToList();
            check(ChecklistBookletContents.PageCount == entries.Length + 1 && parsed.All(id => id.HasValue)
                  && parsed.Select(id => id.Value).SequenceEqual(ids)
                  && entries.Select(e => e.Page).SequenceEqual(Enumerable.Range(1, entries.Length)),
                $"the checklist booklet has a cover and then every checklist a page, in the tablet's order ({ChecklistBookletContents.PageCount} pages)");
            for (var i = 0; i < entries.Length; i++)
            {
                if (!parsed[i].HasValue) continue;
                var id = parsed[i].Value;
                var snap = new ChecklistSnapshot
                {
                    IsOil = id == ChecklistId.OilColdStart,
                    HasStoker = id == ChecklistId.StokerSetup,
                    HasTenderWaterValve = true,
                    HasSeparateSteamValve = true,
                };
                var live = FiringChecklists.Evaluate(snap, id);
                var printed = entries[i].Steps;
                check(live.Id == id && entries[i].Title == FiringChecklists.TitleOf(id) && printed.SequenceEqual(live.Steps.Select(st => st.Title)),
                    $"page {entries[i].Page} prints {entries[i].Title}'s {printed.Length} steps as the tablet names them"
                    + (printed.SequenceEqual(live.Steps.Select(st => st.Title)) ? "" : $" (live: {string.Join(" | ", live.Steps.Select(st => st.Title))})"));
            }
            check(PaperBooklet.SourcePrefab == "VehicleCatalog" && PaperBooklet.PageWidth == FiringManual.PageWidth
                  && PaperBooklet.PageHeight == FiringManual.PageHeight
                  && Math.Abs(PaperBooklet.PageWidth / PaperBooklet.PageHeight - (float)ChecklistBookletContents.Width / ChecklistBookletContents.Height) < 0.01f
                  && BookletAssets.BundleFile == "realisticfiring_manual",
                "the checklist booklet is printed on the manual's paper, from the manual's stock catalog and bundle");
        }

        private static InventoryItemSpec Spec(string name)
        {
            var spec = (InventoryItemSpec)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InventoryItemSpec));
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(spec, name);
            return spec;
        }

        private static StorageItemData Saved(string name, int slot = -1, JObject state = null) =>
            new StorageItemData(name, new Vector3(1f, 2f, 3f), Quaternion.identity, true, state: state, inventorySlotIndex: slot);

        /// <summary>Starting kit (once per save, respecting discard).</summary>
        private static void CheckBookletStartingKit(Action<bool, string> check)
        {
            var radio = new StartingItems.StartingItem { item = Spec("Radio"), preferredRelativeSlot = 0 };
            var shovel = new StartingItems.StartingItem { item = Spec("shovel"), preferredRelativeSlot = 3 };
            var catalog = new StartingItems.StartingItem { item = Spec(FiringManual.SourcePrefab), preferredRelativeSlot = 1 };
            var booklet = Spec(ChecklistBooklet.PrefabName);
            var basic = new List<StartingItems.StartingItem> { radio };
            var steam = new List<StartingItems.StartingItem> { radio, shovel };
            var catalogKit = new List<StartingItems.StartingItem> { radio, catalog };
            check(ReferenceEquals(basic, ChecklistBookletStartingItems.WithBooklet(basic, () => booklet)),
                "a starting kit without catalog or shovel gets no checklist booklet");
            var withSteam = ChecklistBookletStartingItems.WithBooklet(steam, () => booklet);
            var withCatalog = ChecklistBookletStartingItems.WithBooklet(catalogKit, () => booklet);
            check(withSteam.Count == 3 && withSteam.Last().ItemPrefabName == ChecklistBooklet.PrefabName && steam.Count == 2
                  && withCatalog.Count == 3 && withCatalog.Last().ItemPrefabName == ChecklistBooklet.PrefabName
                  && ReferenceEquals(withSteam, ChecklistBookletStartingItems.WithBooklet(withSteam, () => throw new Exception("duplicate")))
                  && ReferenceEquals(steam, ChecklistBookletStartingItems.WithBooklet(steam, () => null)),
                "catalog and steam starting kits each receive one checklist booklet without mutating the source list; a missing template is safe");
            var save = new SaveGameData();
            var first = ChecklistBookletStartingItems.WithBookletOnce(save, steam, () => booklet);
            var reloaded = SaveGameData.LoadFromString(save.GetJsonString());
            var second = ChecklistBookletStartingItems.WithBookletOnce(reloaded, steam, () => throw new Exception("already given"));
            check(first.Count == 3 && save.GetBool(ChecklistBookletStartingItems.GivenKey) == true
                  && ReferenceEquals(second, steam) && reloaded.GetBool(ChecklistBookletStartingItems.GivenKey) == true
                  && ChecklistBookletStartingItems.GivenKey != FiringManualStartingItems.GivenKey,
                "the checklist booklet is granted once per save under its own key and respects disposal after reload");
            var cardSave = new SaveGameData();
            cardSave.SetBool("RealisticFiring_FiringChecklistsGiven", true);
            check(ChecklistBookletStartingItems.WithBookletOnce(cardSave, steam, () => booklet).Count == 3,
                "a save that was given the old checklist card is still offered the booklet once");
        }

        private static List<StartingItems.StartingItem> bookletKit;
        private static InventoryItemSpec bookletSpec;

        private static bool BookletKitPrefix(SaveGameData data, ref List<StartingItems.StartingItem> __result)
        {
            __result = ChecklistBookletStartingItems.WithBookletOnce(data, bookletKit, () => bookletSpec);
            return false;
        }

        private static bool NoCountedItems(ref Dictionary<string, int> __result)
        {
            __result = new Dictionary<string, int>();
            return false;
        }

        /// <summary>A save holding the retired checklist card loads it as the booklet, wherever it was.</summary>
        private static void CheckCardMigration(Action<bool, string> check)
        {
            var card = ChecklistCardMigration.CardPrefabName;
            check(card == "RealisticFiring_FiringChecklists" && card != ChecklistBooklet.PrefabName,
                "the old card's saved item name is the one the preview builds saved, not the booklet's");
            var cardState = new JObject { ["gadgetData"] = new JObject { ["rfChecklistCard"] = 3 }, ["placedOn"] = "" };
            var inventory = new List<StorageItemData> { Saved(card, 14, (JObject)cardState.DeepClone()), Saved("shovel", 3) };
            var lostAndFound = new List<StorageItemData> { Saved(card) };
            var world = new List<StorageItemData> { Saved(card), Saved(FiringManual.PrefabName) };
            world[0].carGuid = "car-1";
            var gadgets = new List<StorageItemData> { Saved(card, state: new JObject { ["placedOn"] = "mount", ["gadgetData"] = new JObject() }), Saved("Clinometer") };
            var containers = new List<StorageItemData> { Saved(card) };
            containers[0].containerId = "box";
            containers[0].containerSlotIndex = 2;
            var moved = ChecklistCardMigration.Migrate(inventory, lostAndFound, world, gadgets, containers);
            check(moved == 5 && inventory[0].itemPrefabName == ChecklistBooklet.PrefabName && inventory[0].inventorySlotIndex == 14 && inventory[0].state == null
                  && world[0].itemPrefabName == ChecklistBooklet.PrefabName && world[0].carGuid == "car-1" && world[0].ItemPosition == new Vector3(1f, 2f, 3f)
                  && containers[0].itemPrefabName == ChecklistBooklet.PrefabName && containers[0].containerId == "box" && containers[0].containerSlotIndex == 2
                  && lostAndFound[0].itemPrefabName == ChecklistBooklet.PrefabName,
                "a saved card in the inventory, world, lost and found, or a container becomes the booklet in the same place, opening on its cover");
            check(gadgets.Count == 1 && gadgets[0].itemPrefabName == "Clinometer" && lostAndFound.Count == 2
                  && lostAndFound[1].itemPrefabName == ChecklistBooklet.PrefabName && lostAndFound[1].belongsToPlayer
                  && lostAndFound[1].ItemPosition == StorageItemTransformController.IGNORE_POSITION_VALUE && lostAndFound[1].state == null,
                "a card fitted to a gadget mount goes to lost and found as the booklet, leaving other gadgets mounted");
            check(inventory[1].itemPrefabName == "shovel" && world[1].itemPrefabName == FiringManual.PrefabName
                  && ChecklistCardMigration.Migrate(inventory, lostAndFound, world, gadgets, containers) == 0
                  && ChecklistCardMigration.Migrate(null, null, null, null, null) == 0,
                "other items stay as saved, a second pass changes nothing, and missing lists are safe");

            // Through the game's own loading: the card is a booklet before the starting items are resolved.
            var combine = AccessTools.DeclaredMethod(typeof(StartingItemsController), "CombineAndResolveIllegalDuplicates");
            var counters = AccessTools.GetDeclaredMethods(typeof(StartingItemsController)).FirstOrDefault(m => m.Name.Contains("GenerateItemCounters"));
            var safeguard = AccessTools.DeclaredMethod(typeof(StartingItemsController), "StartingItemsSafeguard");
            var kit = AccessTools.DeclaredMethod(typeof(StartingItemsController), "GetStartingItemsPrefabs");
            check(combine != null && counters != null && safeguard != null && kit != null
                  && combine.GetParameters().Select(p => p.Name).SequenceEqual(new[] { "itemsInventory", "itemsLostAndFound", "itemsWorld", "itemsInstalledGadgets", "itemsItemContainers" }),
                "the game combines the saved inventory, lost and found, world, gadgets, and containers where the migration expects");
            if (combine == null || counters == null || safeguard == null || kit == null)
                return;
            var harmony = new Harmony("RealisticFiring.AdapterCheck.ChecklistCardMigration");
            try
            {
                bookletKit = new List<StartingItems.StartingItem> { new StartingItems.StartingItem { item = Spec("shovel"), preferredRelativeSlot = 3 } };
                bookletSpec = Spec(ChecklistBooklet.PrefabName);
                harmony.Patch(counters, prefix: new HarmonyMethod(typeof(ChecklistChecks), nameof(NoCountedItems)));
                harmony.Patch(kit, prefix: new HarmonyMethod(typeof(ChecklistChecks), nameof(BookletKitPrefix)));
                harmony.CreateClassProcessor(typeof(ChecklistCardMigration)).Patch();
                harmony.CreateClassProcessor(typeof(ChecklistBookletStartingItems)).Patch();
                var controller = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(StartingItemsController));
                List<StorageItemData> Load(List<StorageItemData> inv, List<StorageItemData> lf, List<StorageItemData> w, List<StorageItemData> g) =>
                    (List<StorageItemData>)combine.Invoke(controller, new object[] { inv, lf, w, g, new List<StorageItemData>() });

                var inv1 = new List<StorageItemData> { Saved("shovel", 3), Saved(card, 13, (JObject)cardState.DeepClone()) };
                var gad1 = new List<StorageItemData> { Saved(card, state: new JObject { ["placedOn"] = "mount" }) };
                var lf1 = new List<StorageItemData>();
                var combined = Load(inv1, lf1, new List<StorageItemData>(), gad1);
                var save = new SaveGameData();
                safeguard.Invoke(controller, new object[] { inv1, combined, save, false });
                check(combined.Count(i => i.itemPrefabName == ChecklistBooklet.PrefabName) == 2 && combined.All(i => i.itemPrefabName != card)
                      && inv1.Count == 2 && inv1[1].itemPrefabName == ChecklistBooklet.PrefabName && inv1[1].inventorySlotIndex == 13
                      && gad1.Count == 0 && lf1.Count == 1 && save.GetBool(ChecklistBookletStartingItems.GivenKey) == true,
                    "loading a save with a carried and a mounted card gives two booklets (backpack, lost and found) and no third from the starting kit");

                var inv2 = new List<StorageItemData> { Saved("shovel", 3) };
                var fresh = new SaveGameData();
                fresh.SetBool("RealisticFiring_FiringChecklistsGiven", true);
                safeguard.Invoke(controller, new object[] { inv2, Load(inv2, new List<StorageItemData>(), new List<StorageItemData>(), new List<StorageItemData>()), fresh, true });
                check(inv2.Count == 2 && inv2[1].itemPrefabName == ChecklistBooklet.PrefabName && inv2[1].inventorySlotIndex >= 0
                      && fresh.GetBool(ChecklistBookletStartingItems.GivenKey) == true,
                    "a save whose card was thrown away, or that never had one, gets one booklet in a free backpack slot");
                var again = new List<StorageItemData> { Saved("shovel", 3) };
                var reloaded = SaveGameData.LoadFromString(fresh.GetJsonString());
                safeguard.Invoke(controller, new object[] { again, Load(again, new List<StorageItemData>(), new List<StorageItemData>(), new List<StorageItemData>()), reloaded, false });
                check(again.Count == 1, "and once that booklet is discarded, a reload gives no other");
            }
            catch (Exception e)
            {
                check(false, "the card migration through the game's loading ran: " + (e.InnerException ?? e).Message);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
                bookletKit = null;
                bookletSpec = null;
            }
        }

        private static void CheckMultiplayerSliceSync(Action<bool, string> check)
        {
            var def = new FireboxDefinition
            {
                ID = "firebox",
                maxCoalCapacity = 200f,
                burnTime = 100f,
                efficiencyAtMaxCombustion = 0.6f,
                coalDumpRate = 20f,
                combustionRateSmoothTime = 5f,
                temperatureSmoothTime = 10f,
            };
            var car = (TrainCar)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(TrainCar));
            var cachedPtr = AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr");
            cachedPtr.SetValue(car, new IntPtr(1));
            var host = new RealisticFirebox(def, car);
            var guest1 = new RealisticFirebox(def, car);
            var guest2 = new RealisticFirebox(def, car);

            // Seed clinker needing cleaning on the host and mirror to both guests.
            var zones = new float[GrateLayout.ZoneCount * 4];
            for (var z = 0; z < GrateLayout.ZoneCount; z++)
            {
                zones[z * 4 + 1] = 5f; // burning coal
                zones[z * 4 + 3] = z == (int)GrateZone.Center ? 8f : 2f; // heavy clinker
            }
            host.Model.RestoreZones(true, zones);
            var initial = ClientMirror.Capture(host, null, null, null, null);
            ClientMirror.Apply(initial, guest1, null, null, null, null);
            ClientMirror.Apply(initial, guest2, null, null, null, null);

            var prevClientCheck = Net.ClientCheck;
            var prevSender = Net.Sender;
            FireAction? sentAction = null;
            var sentZone = -1;
            try
            {
                // Guest 1 requests a slice: sent to host, NOT ticked locally before host accepts it.
                Net.ClientCheck = () => true;
                Net.Sender = (_, action, zone, _, _) =>
                {
                    sentAction = action;
                    sentZone = zone;
                };
                guest1.Slice(GrateZone.Center);
                var guest1BeforeHost = ChecklistReader.Snapshot(guest1, null, null, null, null);
                var hostBeforeAccept = ChecklistReader.Snapshot(host, null, null, null, null);

                // Host accepts and executes RealisticFirebox.Slice directly (as Integration.FireActionFrom does),
                // then replicates via ClientMirror to both guests.
                Net.ClientCheck = () => false;
                host.Slice((GrateZone)sentZone);
                var afterSlice = ClientMirror.Capture(host, null, null, null, null);
                ClientMirror.Apply(afterSlice, guest1, null, null, null, null);
                ClientMirror.Apply(afterSlice, guest2, null, null, null, null);

                var hostSnap = ChecklistReader.Snapshot(host, null, null, null, null);
                var guest1Snap = ChecklistReader.Snapshot(guest1, null, null, null, null);
                var guest2Snap = ChecklistReader.Snapshot(guest2, null, null, null, null);
                var hostList = FiringChecklists.Evaluate(hostSnap, ChecklistId.CleaningClinkerAndAsh);
                var guest1List = FiringChecklists.Evaluate(guest1Snap, ChecklistId.CleaningClinkerAndAsh);
                var guest2List = FiringChecklists.Evaluate(guest2Snap, ChecklistId.CleaningClinkerAndAsh);

                check(sentAction == FireAction.Slice && sentZone == (int)GrateZone.Center
                      && !guest1BeforeHost.SlicedRecently && !hostBeforeAccept.SlicedRecently
                      && hostSnap.NeedsCleaning && guest1Snap.NeedsCleaning && guest2Snap.NeedsCleaning
                      && hostSnap.SlicedRecently && guest1Snap.SlicedRecently && guest2Snap.SlicedRecently
                      && hostList.Steps[2].Done && guest1List.Steps[2].Done && guest2List.Steps[2].Done,
                    "host-authoritative slice checklist sync agrees across host, slicing guest, and other crew guests without client-only desync");

                // Once clinker clears on the host, the next mirror clears recent-slice state everywhere.
                host.LayBurningBed(40f);
                var afterClean = ClientMirror.Capture(host, null, null, null, null);
                ClientMirror.Apply(afterClean, guest1, null, null, null, null);
                ClientMirror.Apply(afterClean, guest2, null, null, null, null);
                check(!host.SlicedRecently && !guest1.SlicedRecently && !guest2.SlicedRecently,
                    "clearing clinker on the host clears mirrored recent-slice state for host and guests");
            }
            finally
            {
                Net.ClientCheck = prevClientCheck;
                Net.Sender = prevSender;
                cachedPtr.SetValue(car, IntPtr.Zero);
            }
        }

        private static void CheckDualInjectorReader(Action<bool, string> check)
        {
            Port MakePort(string id, float value)
            {
                var p = new Port(id, new PortDefinition(PortType.EXTERNAL_IN, PortValueType.CONTROL, id)) { Value = value };
                return p;
            }

            var boiler = (Boiler)new BoilerDefinition
            {
                ID = "boiler", diameter = 1.7f, length = 8.8f,
                spawnPressure = 9f, spawnWaterLevel = 12000f,
                safetyValveOpeningPressure = 15f, safetyValveClosingPressure = 14.8f,
                crownSheetNormalizedWaterLevel = 0.6f, steamOutletNormalizedWaterLevel = 0.98f,
            }.InstantiateImplementation();
            boiler.waterLevelReadOut.Value = 0.765f;

            var right = new InjectorCircuit(MakePort("waterR", 0.7f), new Injector(new InjectorSpec(5f, 14f)), "right")
            {
                Added = MakePort("steamR", 0f),
                TenderValve = MakePort("tenderR", 1f),
                Flow = new InjectorFlow { State = InjectorState.Off, Overflow = 0.5f },
            };
            var left = new InjectorCircuit(MakePort("waterL", 0f), new Injector(new InjectorSpec(5f, 14f)), "left")
            {
                Added = MakePort("steamL", 0.5f),
                TenderValve = MakePort("tenderL", 0f),
                Flow = new InjectorFlow { State = InjectorState.NoWater, Overflow = 0f },
            };

            var rig = (InjectorRig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InjectorRig));
            AccessTools.Field(typeof(InjectorRig), "boiler").SetValue(rig, boiler);
            AccessTools.Field(typeof(InjectorRig), "<Circuits>k__BackingField").SetValue(rig, new[] { right, left });

            var snap = ChecklistReader.Snapshot(null, null, rig, null, null);
            snap.BoilerGaugeBar = 8f;
            snap.GlassLevel = 0.5f;
            var list = FiringChecklists.Evaluate(snap, ChecklistId.InjectorsAndWater);
            check(snap.InjectorName == "left" && snap.InjectorWaterValve == 0f && snap.InjectorSteamPrime && !snap.InjectorSteamFeed
                  && !snap.TenderWaterValveOpen && snap.OverflowWaterKgPerS == 0f
                  && list.Name == "Injectors & Water (left)" && !list.Steps[1].Done && !list.Steps[2].Done,
                "dual-injector checklist reader isolates one circuit (water on right + steam on left never ticks water and prime together)");

            // Straight-to-Feed Broken jet on a real circuit never ticks Prime and presents recovery step.
            right.WaterValve.Value = 0.7f;
            right.SteamValve.Value = 1.0f;
            right.Flow = new InjectorFlow { State = InjectorState.Broken, Overflow = 0.05f };
            left.SteamValve.Value = 0f;
            left.Flow = new InjectorFlow { State = InjectorState.Off, Overflow = 0f };
            var brokenSnap = ChecklistReader.Snapshot(null, null, rig, null, null);
            brokenSnap.BoilerGaugeBar = 8f;
            var brokenList = FiringChecklists.Evaluate(brokenSnap, ChecklistId.InjectorsAndWater);
            check(brokenSnap.InjectorName == "right" && !brokenSnap.InjectorSteamPrime && brokenSnap.InjectorSteamFeed
                  && brokenSnap.InjectorState == InjectorState.Broken
                  && brokenList.CurrentIndex == 3 && brokenList.Steps[3].Title.Contains("Jet broken")
                  && !brokenList.Steps[4].Done,
                "broken jet at Feed leaves Prime unticked and surfaces the Off-then-Prime recovery step");

            // Single-lever CrackOpening (e.g. GWI's 0.25f notch) propagates so "Open lever to feed" stays unticked while cracked.
            var singleSpec = new InjectorSpec(5f, 14f) { CrackOpening = 0.25f };
            var single = new InjectorCircuit(MakePort("injector", 0.25f), new Injector(singleSpec), "")
            {
                Flow = new InjectorFlow { State = InjectorState.Working, Overflow = 0.05f },
            };
            var singleRig = (InjectorRig)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(InjectorRig));
            AccessTools.Field(typeof(InjectorRig), "boiler").SetValue(singleRig, boiler);
            AccessTools.Field(typeof(InjectorRig), "<Circuits>k__BackingField").SetValue(singleRig, new[] { single });
            var crackedSnap = ChecklistReader.Snapshot(null, null, singleRig, null, null);
            crackedSnap.BoilerGaugeBar = 8f;
            var crackedList = FiringChecklists.Evaluate(crackedSnap, ChecklistId.InjectorsAndWater);
            single.Control.Value = 0.5f;
            var openedSnap = ChecklistReader.Snapshot(null, null, singleRig, null, null);
            openedSnap.BoilerGaugeBar = 8f;
            var openedList = FiringChecklists.Evaluate(openedSnap, ChecklistId.InjectorsAndWater);
            check(crackedSnap.InjectorCrackOpening == 0.25f && crackedList.Steps[2].Done && !crackedList.Steps[3].Done
                  && openedList.Steps[2].Done && openedList.Steps[3].Done,
                "single-lever checklist reader propagates CrackOpening so 'Open lever to feed' stays unticked while cracked and ticks once opened past crack");
        }

        private static Rect Scaled(Rect r, float scale) => new Rect(r.x * scale, r.y * scale, r.width * scale, r.height * scale);
    }
}
