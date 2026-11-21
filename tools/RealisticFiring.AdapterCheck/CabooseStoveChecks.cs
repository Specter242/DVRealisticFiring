using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Serialization;
using System.Xml.Serialization;
using DV.CabControls;
using DV.Interaction;
using DV.Simulation.Ports;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Adapter checks for the caboose stove: patch target resolution against
    /// the installed game assemblies, redirect initialization order, fallback
    /// door cab-control wiring, interior scan caching, stock door discovery
    /// and fallback guard, CarStateSave persistence safety and version checks,
    /// ClientMirror section round-trip and cold-stove suppression, multiplayer
    /// shape/GuestActions/ItemsRemote checks, supply stocking with fireboxes
    /// off, and tightened loose-item / local-space aim geometry.
    /// </summary>
    internal static class CabooseStoveChecks
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private sealed class FakeButtonControl : ControlImplBase
        {
            protected override InteractionHandPoses GenericHandPoses => new InteractionHandPoses();
            protected override void AcceptSetValue(float newValue) { }
            public override bool IsGrabbed() => false;
            public override void ForceEndInteraction() { }
        }

        internal static void Run(Action<bool, string> check)
        {
            // 1. Game methods patched by CabooseStovePatches exist with expected signatures,
            //    and the broken IdleStartInteraction Hold-only fallback patch is gone.
            var awake = AccessTools.Method(typeof(CabooseController), "Awake");
            var getSave = AccessTools.Method(typeof(CarStateSave), nameof(CarStateSave.GetCarStateSaveData));
            var setSave = AccessTools.Method(typeof(CarStateSave), nameof(CarStateSave.SetCarStateSaveData), new[] { typeof(JObject) });
            var oldFallbackPatch = typeof(Main).Assembly.GetType("RealisticFiring.Patches.CabooseStoveFallbackDoorPatch");
            check(awake != null && getSave != null && setSave != null && oldFallbackPatch == null,
                "caboose stove patch targets exist on CabooseController and CarStateSave without an IdleStartInteraction fallback hack");
            // The stock caboose has a damage controller, so the game saves it through SimCarStateSave, never CarStateSave
            // (TrainCar adds that only to cars with cargo or without one): the stove saved nothing in game (job 3112).
            var getSimSave = AccessTools.Method(typeof(DV.Simulation.Cars.SimCarStateSave), nameof(DV.Simulation.Cars.SimCarStateSave.GetStateSaveData));
            var setSimSave = AccessTools.Method(typeof(DV.Simulation.Cars.SimCarStateSave), nameof(DV.Simulation.Cars.SimCarStateSave.SetStateSaveData), new[] { typeof(JObject) });
            var simPatches = new[] { "CabooseStoveSimSavePatch", "CabooseStoveSimLoadPatch" }
                .Select(n => typeof(Main).Assembly.GetType("RealisticFiring.Patches." + n)).ToArray();
            check(getSimSave != null && setSimSave != null && simPatches.All(t => t != null && t.GetCustomAttributes(typeof(HarmonyLib.HarmonyPatch), false).Length > 0),
                "the caboose stove saves and loads through SimCarStateSave, which the stock caboose saves by");
            // The stock levers read 1 shut and 0 open (the door swung out at 0 in game, job 3112; a new caboose spawns at 1).
            check(RealisticFiring.Sim.CabooseStoveRig.StockDoorOpening(1f) == 0f && RealisticFiring.Sim.CabooseStoveRig.StockDoorOpening(0f) == 1f
                && RealisticFiring.Sim.CabooseStoveRig.StockDoorOpening(0.25f) == 0.75f,
                "a stock stove door lever at 1 reads shut and at 0 open");

            // 2. ItemUseRedirect initialization order: inactive before AddComponent, target assigned before activation, explicit Unity != null check.
            CheckRedirectInitializationOrder(check);

            // 3. Fallback door interaction path: stock DV.CabControls.Spec.Button + ControlImplBase.Used -> Toggle().
            CheckFallbackDoorInteraction(check);

            // 4. Interior scan caching & stock door discovery / fallback guard.
            CheckInteriorCachingAndDoorGuards(check);

            // 5. CabooseStoveSave named JSON format, version validation, CarStateSave compatibility, and disabled-setting preservation.
            CheckSavePersistence(check);

            // 6. Multiplayer consistency: Shape(), ItemsRemote, FallbackDoorOpen sync, cold-stove snapshot suppression, and GuestActions.
            CheckMultiplayerConsistency(check);

            // 7. Supply stocking works with CabooseStove enabled even when ReplaceStockFireboxes is off.
            CheckSupplyStockingWhenFireboxesOff(check);

            // 8. Tightened loose-item intake bounds & local-space AimThrough intersection.
            CheckIntakeAndAimGeometry(check);

            // 9. Settings.CabooseStove defaults on and loads cleanly from older Settings.xml.
            var fresh = new Settings();
            check(fresh.CabooseStove, "Settings.CabooseStove defaults to true");
            var serializer = new XmlSerializer(typeof(Settings));
            Settings fromOld;
            using (var reader = new StringReader("<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings><ReplaceStockFireboxes>true</ReplaceStockFireboxes></Settings>"))
                fromOld = (Settings)serializer.Deserialize(reader);
            check(fromOld.CabooseStove, "an older Settings.xml without CabooseStove keeps CabooseStove enabled");
        }

        private static void CheckRedirectInitializationOrder(Action<bool, string> check)
        {
            var buildIntake = typeof(CabooseStoveCab).GetMethod("BuildIntake", Declared);
            var buildDoorRedirect = typeof(CabooseStoveCab).GetMethod("BuildDoorRedirect", Declared);
            var attachRedirect = typeof(CabooseStoveCab).GetMethod(nameof(CabooseStoveCab.AttachRedirect), Declared);

            var intakeIl = PatchProcessor.GetOriginalInstructions(buildIntake);
            var intakeDeactivate = FindSetActiveCall(intakeIl, active: false);
            var intakeFirstRedirect = FindCallIndex(intakeIl, typeof(CabooseStoveCab), nameof(CabooseStoveCab.AttachRedirect));
            var intakeActivate = FindSetActiveCall(intakeIl, active: true, searchFromEnd: true);
            check(intakeDeactivate >= 0 && intakeFirstRedirect > intakeDeactivate && intakeActivate > intakeFirstRedirect,
                "CabooseStoveCab.BuildIntake creates intakeGo inactive before AttachRedirect and activates it only after wiring");

            var doorIl = PatchProcessor.GetOriginalInstructions(buildDoorRedirect);
            var doorDeactivate = FindSetActiveCall(doorIl, active: false);
            var doorAttach = FindCallIndex(doorIl, typeof(CabooseStoveCab), nameof(CabooseStoveCab.AttachRedirect));
            var doorActivate = FindSetActiveCall(doorIl, active: true, searchFromEnd: true);
            var doorUnityCheck = FindCallIndex(doorIl, typeof(UnityEngine.Object), "op_Inequality") >= 0;
            check(doorDeactivate >= 0 && doorAttach > doorDeactivate && doorActivate > doorAttach && doorUnityCheck,
                "CabooseStoveCab.BuildDoorRedirect uses an explicit Unity != null check and builds door redirects on an inactive child before activating");

            var attachIl = PatchProcessor.GetOriginalInstructions(attachRedirect);
            var attachUnityCheck = FindCallIndex(attachIl, typeof(UnityEngine.Object), "op_Equality") >= 0;
            var attachAdd = attachIl.FindIndex(i => i.operand is MethodInfo mi && mi.Name == nameof(GameObject.AddComponent)
                && mi.IsGenericMethod && mi.GetGenericArguments()[0] == typeof(ItemUseRedirect));
            var attachTargetField = attachIl.FindIndex(i => i.operand is FieldInfo fi && fi.Name == "RedirectTarget");
            check(attachUnityCheck && attachAdd >= 0 && attachTargetField > attachAdd,
                "CabooseStoveCab.AttachRedirect uses Unity == null check and assigns RedirectTarget immediately after AddComponent while inactive");
        }

        private static void CheckFallbackDoorInteraction(Action<bool, string> check)
        {
            var spec = (DV.CabControls.Spec.Button)FormatterServices.GetUninitializedObject(typeof(DV.CabControls.Spec.Button));
            CabooseStoveFallbackDoor.ConfigureButton(spec, null);
            var poses = spec.handPosesOverride;
            check(poses != null && poses.nearTouchPose == HandPose.Generic && poses.touchPose == HandPose.Generic
                  && poses.grabPose == HandPose.Generic && spec.colliderGameObjects.Length == 1
                  && !spec.createRigidbody && !spec.useJoints && !spec.isToggle && !spec.isTogglingBack,
                "CabooseStoveFallbackDoor configures a stock DV.CabControls.Spec.Button for both desktop and VR without adding a rigidbody");

            var grabBtn = (GrabHandlerButton)FormatterServices.GetUninitializedObject(typeof(GrabHandlerButton));
            check(!grabBtn.IsItem && grabBtn.IsDraggable,
                "stock ButtonNonVR uses GrabHandlerButton (IsItem == false, IsDraggable == true), routing through world interaction gates");

            var buildIntake = typeof(CabooseStoveCab).GetMethod("BuildIntake", Declared);
            var intakeIl = PatchProcessor.GetOriginalInstructions(buildIntake);
            var addsButtonSpec = intakeIl.Any(i => i.operand is MethodInfo mi && mi.Name == nameof(GameObject.AddComponent)
                && mi.IsGenericMethod && mi.GetGenericArguments()[0] == typeof(DV.CabControls.Spec.Button));
            var configuresButton = FindCallIndex(intakeIl, typeof(CabooseStoveFallbackDoor), nameof(CabooseStoveFallbackDoor.ConfigureButton)) >= 0;
            check(addsButtonSpec && configuresButton,
                "CabooseStoveCab.BuildIntake wires the fallback door through DV.CabControls.Spec.Button and ConfigureButton");

            var savedSettings = Main.Settings;
            var savedClient = Net.ClientCheck;
            CabooseStoveRig rig = null;
            FakeButtonControl ctrl = null;
            try
            {
                Main.Settings = new Settings { CabooseStove = true };
                Net.ClientCheck = () => false;
                rig = CreateRig();
                var door = (CabooseStoveFallbackDoor)FormatterServices.GetUninitializedObject(typeof(CabooseStoveFallbackDoor));
                door.Rig = rig;
                ctrl = (FakeButtonControl)FormatterServices.GetUninitializedObject(typeof(FakeButtonControl));
                SetLive(ctrl, true);
                AccessTools.Field(typeof(ControlImplBase), "_interactionAllowed").SetValue(ctrl, true);
                door.BindControl(ctrl);
                ctrl.Use();
                var opened = rig.FallbackDoorOpen;
                ctrl.Use();
                var shut = !rig.FallbackDoorOpen;
                check(opened && shut,
                    "clicking the fallback door's ControlImplBase fires Used -> Toggle() and toggles CabooseStoveRig.FallbackDoorOpen");
            }
            finally
            {
                SetLive(ctrl, false);
                SetLive(rig, false);
                Main.Settings = savedSettings;
                Net.ClientCheck = savedClient;
            }
        }

        private static void CheckInteriorCachingAndDoorGuards(Action<bool, string> check)
        {
            var refresh = typeof(CabooseStoveCab).GetMethod("RefreshInterior", Declared);
            var refreshIl = PatchProcessor.GetOriginalInstructions(refresh);
            var readsCached = refreshIl.Any(i => i.opcode == OpCodes.Ldfld && i.operand is FieldInfo fi && fi.Name == "interiorScanned");
            var writesCached = refreshIl.Any(i => i.opcode == OpCodes.Stfld && i.operand is FieldInfo fi && fi.Name == "interiorScanned");
            var checksRefEquality = refreshIl.Any(i => i.opcode == OpCodes.Ceq);
            check(readsCached && writesCached && checksRefEquality,
                "CabooseStoveCab.RefreshInterior caches the scanned loadedInterior instance so stove-less cabooses are not rescanned every frame");

            var discover = typeof(CabooseStoveCab).GetMethod("DiscoverDoors", Declared);
            var discoverParams = discover.GetParameters();
            var discoverIl = PatchProcessor.GetOriginalInstructions(discover);
            var scansInteriorControls = discoverParams.Length == 4
                && discoverParams[0].ParameterType == typeof(Transform)
                && discoverParams[1].ParameterType == typeof(Transform)
                && discoverIl.Any(i => i.operand is MethodInfo mi && mi.Name == nameof(Component.GetComponentsInChildren)
                    && mi.IsGenericMethod && mi.GetGenericArguments()[0] == typeof(ControlImplBase))
                && discoverIl.Any(i => i.operand is MethodInfo mi && mi.Name == nameof(Component.GetComponentsInChildren)
                    && mi.IsGenericMethod && mi.GetGenericArguments()[0] == typeof(InteractablePortFeeder))
                && FindCallIndex(discoverIl, typeof(CabooseStoveCab), nameof(CabooseStoveCab.IsDrivenByStockControl)) >= 0;
            check(scansInteriorControls,
                "CabooseStoveCab.DiscoverDoors searches the entire loadedInterior for stock door controls/feeders and excludes stock-driven transforms from fallback doors");

            var updateFallback = typeof(CabooseStoveCab).GetMethod("UpdateFallbackDoors", Declared);
            var fallbackIl = PatchProcessor.GetOriginalInstructions(updateFallback);
            var guardsStock = FindCallIndex(fallbackIl, typeof(CabooseStoveCab), "get_HasStockDoors") >= 0
                && FindCallIndex(fallbackIl, typeof(CabooseStoveRig), "get_HasDoorPorts") >= 0
                && FindCallIndex(fallbackIl, typeof(CabooseStoveCab), nameof(CabooseStoveCab.IsDrivenByStockControl)) >= 0;
            check(guardsStock,
                "CabooseStoveCab.UpdateFallbackDoors guards against rotating transforms driven by stock controls or ports");
        }

        private static void CheckSavePersistence(Action<bool, string> check)
        {
            var model = new CabooseStoveModel();
            model.SetDoorOpening(0.75f);
            model.AddWood(3 * CabooseStoveModel.PieceMass);
            model.AddRag(RagFuel.OilCapacity, lit: true);
            for (var i = 0; i < 600; i++)
                model.Step(0.2f, 4f);

            var written = CabooseStoveSave.Write(model, fallbackDoorOpen: true);
            check(written != null && written.Value<int>("v") == CabooseStoveSave.CurrentVersion
                  && written["state"] == null && written["wood"] != null && written["heat"] != null
                  && written["warmth"] != null && written.Value<bool>("fallbackDoorOpen"),
                "caboose stove writes an explicit versioned named-field JSON object instead of a positional array");

            var carSave = new JObject { ["carDmg"] = 1f, [CabooseStoveSave.SaveKey] = written };
            var stockStateSave = (CarStateSave)FormatterServices.GetUninitializedObject(typeof(CarStateSave));
            var stockLoadedOk = true;
            try { stockStateSave.SetCarStateSaveData(carSave); }
            catch { stockLoadedOk = false; }
            check(stockLoadedOk, "stock CarStateSave loads a save containing rfCabooseStove without error");

            var restored = new CabooseStoveModel();
            var ok = CabooseStoveSave.Read(carSave[CabooseStoveSave.SaveKey] as JObject, restored, 4f, out var fbOpen);
            check(ok && fbOpen && restored.Lit == model.Lit && Math.Abs(restored.WoodKg - model.WoodKg) < 1e-4f
                  && Math.Abs(restored.WarmthC - model.WarmthC) < 1e-3f && Math.Abs(restored.Heat01 - model.Heat01) < 1e-4f,
                "caboose stove restores its lit state, fuel, heat, warmth, and fallback door state from named CarStateSave JSON");

            // Cold empty closed stove writes null (no save clutter); version mismatches and corrupt data reset safely.
            var cold = new CabooseStoveModel();
            check(CabooseStoveSave.Write(cold) == null, "a cold empty closed caboose stove writes no save entry");

            var missingVersion = (JObject)written.DeepClone();
            missingVersion.Remove("v");
            var futureVersion = (JObject)written.DeepClone();
            futureVersion["v"] = CabooseStoveSave.CurrentVersion + 1;
            var stringVersion = (JObject)written.DeepClone();
            stringVersion["v"] = "1";
            var overflowVersion = (JObject)written.DeepClone();
            overflowVersion["v"] = 2147483648L;
            var underflowVersion = (JObject)written.DeepClone();
            underflowVersion["v"] = -2147483649L;
            var hugeVersion = JObject.Parse(written.ToString());
            hugeVersion["v"] = JToken.Parse("99999999999999999999999999999");
            var overflowRagCount = (JObject)written.DeepClone();
            overflowRagCount["ragCount"] = 2147483648L;
            var underflowRagCount = (JObject)written.DeepClone();
            underflowRagCount["ragCount"] = -2147483649L;
            var missingField = (JObject)written.DeepClone();
            missingField.Remove("wood");
            var badType = (JObject)written.DeepClone();
            badType["heat"] = "hot";

            var rejectsAllBad = true;
            foreach (var bad in new[]
            {
                missingVersion, futureVersion, stringVersion, overflowVersion, underflowVersion, hugeVersion,
                overflowRagCount, underflowRagCount, missingField, badType,
            })
            {
                try
                {
                    if (CabooseStoveSave.Read(bad, restored, 10f) || restored.Lit || restored.WoodKg != 0f)
                        rejectsAllBad = false;
                }
                catch
                {
                    rejectsAllBad = false;
                }
            }
            check(rejectsAllBad,
                "CabooseStoveSave.Read validates v == 1, int range, and field types without throwing, resetting safely to cold on bad or overflowing fields");

            // Preserving saved state when Settings.CabooseStove is temporarily toggled off.
            var savedSettings = Main.Settings;
            CabooseStoveRig rigDisabledLoad = null;
            CabooseStoveRig rigDisabledOverflowVersion = null;
            CabooseStoveRig rigDisabledOverflowRag = null;
            CabooseStoveRig rigToggledOff = null;
            try
            {
                Main.Settings = new Settings { CabooseStove = false };
                rigDisabledLoad = CreateRig();
                CabooseStoveSave.LoadFromSave(written, rigDisabledLoad, 4f);
                var reSavedWhileOff = CabooseStoveSave.WriteForSave(rigDisabledLoad);
                check(reSavedWhileOff != null && JToken.DeepEquals(reSavedWhileOff, written),
                    "loading and re-saving while Settings.CabooseStove is off preserves the existing rfCabooseStove state");

                rigDisabledOverflowVersion = CreateRig();
                rigDisabledOverflowRag = CreateRig();
                var overflowPreserved = true;
                try
                {
                    var okOverflowV = CabooseStoveSave.LoadFromSave(overflowVersion, rigDisabledOverflowVersion, 4f);
                    var savedOverflowV = CabooseStoveSave.WriteForSave(rigDisabledOverflowVersion);
                    var okOverflowRag = CabooseStoveSave.LoadFromSave(overflowRagCount, rigDisabledOverflowRag, 4f);
                    var savedOverflowRag = CabooseStoveSave.WriteForSave(rigDisabledOverflowRag);
                    overflowPreserved = !okOverflowV && !okOverflowRag
                        && savedOverflowV != null && JToken.DeepEquals(savedOverflowV, overflowVersion)
                        && savedOverflowRag != null && JToken.DeepEquals(savedOverflowRag, overflowRagCount);
                }
                catch
                {
                    overflowPreserved = false;
                }
                check(overflowPreserved,
                    "disabled load/save round-trip with out-of-range v (2147483648) or ragCount rejects cleanly without throwing and preserves original JSON");

                Main.Settings = new Settings { CabooseStove = true };
                rigToggledOff = CreateRig();
                CabooseStoveSave.LoadFromSave(written, rigToggledOff, 4f);
                Main.Settings.CabooseStove = false;
                var savedAfterToggleOff = CabooseStoveSave.WriteForSave(rigToggledOff);
                var afterToggleModel = new CabooseStoveModel();
                var toggleRoundTrip = CabooseStoveSave.Read(savedAfterToggleOff, afterToggleModel, 4f, out var toggleFb);
                check(toggleRoundTrip && toggleFb && afterToggleModel.Lit == model.Lit
                      && Math.Abs(afterToggleModel.WoodKg - model.WoodKg) < 1e-4f,
                    "toggling Settings.CabooseStove off mid-session and saving preserves the active stove state");
            }
            finally
            {
                SetLive(rigDisabledLoad, false);
                SetLive(rigDisabledOverflowVersion, false);
                SetLive(rigDisabledOverflowRag, false);
                SetLive(rigToggledOff, false);
                Main.Settings = savedSettings;
            }
        }

        internal static void RunWithMultiplayer(Action<bool, string> check)
        {
            var savedSettings = Main.Settings;
            try
            {
                Main.Settings = new Settings { CabooseStove = true };
                var shapeOn = Multiplayer.Integration.Shape();
                Main.Settings.CabooseStove = false;
                var shapeOff = Multiplayer.Integration.Shape();
                check(shapeOn.Contains("caboose stove True") && shapeOff.Contains("caboose stove False") && shapeOn != shapeOff,
                    "Multiplayer Integration.Shape() includes Settings.CabooseStove so host/guest mismatches warn on join");

                var sendSnapshots = typeof(Multiplayer.Integration).GetMethod("SendSnapshots", Declared);
                var sendIl = PatchProcessor.GetOriginalInstructions(sendSnapshots);
                var captureIdx = sendIl.FindIndex(i => i.operand is MethodInfo mi
                    && mi.DeclaringType == typeof(ClientMirror) && mi.Name == nameof(ClientMirror.Capture)
                    && mi.GetParameters().Length == 2);
                var sendCallIdx = sendIl.FindIndex(i => i.operand is MethodInfo mi && mi.Name == "SendSerializablePacketToAll");
                var reliableLocal = captureIdx > 0 && (sendIl[captureIdx - 1].opcode == OpCodes.Ldloca_S || sendIl[captureIdx - 1].opcode == OpCodes.Ldloca)
                    ? sendIl[captureIdx - 1].operand
                    : null;
                var reliableArgIndex = sendCallIdx >= 3 && sendIl[sendCallIdx - 1].opcode == OpCodes.Ldnull
                    ? sendCallIdx - 3
                    : sendCallIdx - 2;
                var loadsReliableLocal = reliableLocal != null && reliableArgIndex >= 0
                    && sendIl[reliableArgIndex].opcode != OpCodes.Ldc_I4_0
                    && Equals(sendIl[reliableArgIndex].operand, reliableLocal);
                check(captureIdx >= 0 && sendCallIdx > captureIdx && loadsReliableLocal,
                    "Multiplayer Integration.SendSnapshots captures the reliable flag from ClientMirror.Capture and passes it to SendSerializablePacketToAll");
            }
            finally
            {
                Main.Settings = savedSettings;
            }
        }

        private static void CheckMultiplayerConsistency(Action<bool, string> check)
        {
            // CabooseStoveTarget.Place handles Net.ItemsRemote like FuelFireboxIntake.Place.
            var place = typeof(CabooseStoveTarget).GetMethod("Place", Declared);
            var placeIl = PatchProcessor.GetOriginalInstructions(place);
            check(FindCallIndex(placeIl, typeof(Net), "get_ItemsRemote") >= 0,
                "CabooseStoveTarget.Place checks Net.ItemsRemote to release shared world items for the host");

            // Cold empty closed stove produces NO ClientMirror packet.
            var cold = new CabooseStoveModel();
            var coldPacket = ClientMirror.Capture(null, null, null, null, null, null, null, stove: cold, fallbackDoorOpen: false);
            check(coldPacket == null,
                "ClientMirror.Capture skips cold, empty, closed cabooses to avoid unnecessary network traffic");

            // FallbackDoorOpen alone triggers capture and mirrors to guest.
            var doorPacket = ClientMirror.Capture(null, null, null, null, null, null, null, stove: cold, fallbackDoorOpen: true);
            var guestCold = new CabooseStoveModel();
            var guestDoor = false;
            ClientMirror.Apply(doorPacket, null, null, null, null, null, "caboose-door-guest", null, null, stove: guestCold, setFallbackDoor: open => guestDoor = open);
            check(doorPacket != null && guestDoor,
                "ClientMirror captures and mirrors FallbackDoorOpen for custom cabooses");

            // Active stove round-trips state and FallbackDoorOpen.
            var model = new CabooseStoveModel();
            model.SetDoorOpening(0.75f);
            model.AddWood(3 * CabooseStoveModel.PieceMass);
            model.AddRag(RagFuel.OilCapacity, lit: true);
            for (var i = 0; i < 400; i++)
                model.Step(0.2f, 4f);

            var guestModel = new CabooseStoveModel();
            var guestFallback = false;
            var packet = ClientMirror.Capture(null, null, null, null, null, null, null, stove: model, fallbackDoorOpen: true);
            check(packet != null && packet.Length > ClientMirror.HeaderSize,
                "ClientMirror captures a caboose stove section for multiplayer guests");
            ClientMirror.Apply(packet, null, null, null, null, null, "caboose-guest", null, null, stove: guestModel, setFallbackDoor: open => guestFallback = open);
            check(guestFallback && guestModel.Lit == model.Lit && guestModel.Flaming == model.Flaming
                  && Math.Abs(guestModel.WoodKg - model.WoodKg) < 1e-4f
                  && Math.Abs(guestModel.Heat01 - model.Heat01) < 1e-4f
                  && Math.Abs(guestModel.Smoke01 - model.Smoke01) < 1e-4f
                  && Math.Abs(guestModel.WarmthC - model.WarmthC) < 1e-3f,
                "ClientMirror applies the host's caboose stove state and fallback door state onto a guest");

            // Rig mirror gating: idle cold caboose emits nothing; active -> cold+closed transition emits once reliably, then idle ticks emit nothing.
            var rig = CreateRig();
            try
            {
                var idleSkip = !rig.ShouldMirror && !rig.NeedsReliableMirror;
                var idleCapture = ClientMirror.Capture(null, null, null, null, null, null, null, rig, out var idleReliable);
                idleSkip = idleSkip && idleCapture == null && !idleReliable;

                rig.FallbackDoorOpen = true;
                var activePacket = ClientMirror.Capture(null, null, null, null, null, null, null, rig, out var activeReliable);
                var transitionGuest = new CabooseStoveModel();
                var transitionGuestDoor = false;
                ClientMirror.Apply(activePacket, null, null, null, null, null, "caboose-transition-guest", null, null, stove: transitionGuest, setFallbackDoor: open => transitionGuestDoor = open);
                var activeOk = activePacket != null && !activeReliable && transitionGuestDoor;

                rig.FallbackDoorOpen = false;
                var needsReliableBefore = rig.ShouldMirror && rig.NeedsReliableMirror;
                var closePacket = ClientMirror.Capture(null, null, null, null, null, null, null, rig, out var closeReliable);
                ClientMirror.Apply(closePacket, null, null, null, null, null, "caboose-transition-guest", null, null, stove: transitionGuest, setFallbackDoor: open => transitionGuestDoor = open);
                var closeOk = needsReliableBefore && closePacket != null && closeReliable && !transitionGuestDoor;

                var afterIdleOk = !rig.ShouldMirror && !rig.NeedsReliableMirror;
                for (var tick = 0; tick < 3; tick++)
                {
                    var next = ClientMirror.Capture(null, null, null, null, null, null, null, rig, out var nextReliable);
                    if (next != null || nextReliable || rig.ShouldMirror)
                        afterIdleOk = false;
                }
                check(idleSkip && activeOk && closeOk && afterIdleOk,
                    "CabooseStoveRig marks the cold+closed transition snapshot reliable, delivers the shut state, and emits nothing on idle ticks");
            }
            finally
            {
                SetLive(rig, false);
            }

            // Multiplayer GuestActions sanitizes & rate-limits all stove actions including StoveDoorToggle.
            var actions = new Multiplayer.GuestActions();
            const uint rate = 24;
            var w1 = actions.Accept(1, FireAction.StoveWood, 0f, 1, 100, rate);
            var wFast = actions.Accept(1, FireAction.StoveWood, 0f, 2, 101, rate);
            var rNan = actions.Accept(1, FireAction.StoveRag, float.NaN, 3, 200, rate);
            var rOk = actions.Accept(1, FireAction.StoveRag, RagFuel.OilCapacity, 4, 200, rate);
            var iOk = actions.Accept(1, FireAction.StoveIgnite, 0f, 5, 200, rate);
            var iFast = actions.Accept(1, FireAction.StoveIgnite, 0f, 6, 201, rate);
            var dOk = actions.Accept(1, FireAction.StoveDoorToggle, 0f, 7, 200, rate);
            var dFast = actions.Accept(1, FireAction.StoveDoorToggle, 0f, 8, 201, rate);
            check(w1 && !wFast && !rNan && rOk && iOk && !iFast && dOk && !dFast,
                "multiplayer GuestActions rate-limits StoveWood, StoveRag, StoveIgnite, and StoveDoorToggle and drops NaN values");
        }

        private static void CheckSupplyStockingWhenFireboxesOff(Action<bool, string> check)
        {
            var savedSettings = Main.Settings;
            var savedReplacement = Main.ReplacementEnabled;
            var setReplacement = AccessTools.PropertySetter(typeof(Main), nameof(Main.ReplacementEnabled));
            try
            {
                var settings = new Settings { ReplaceStockFireboxes = false, CabooseStove = true };
                Main.Settings = settings;
                setReplacement.Invoke(null, new object[] { false });
                var enabledForStoveOnly = !Main.ReplacementEnabled && StartingSupplies.SuppliesEnabled;

                settings.CabooseStove = false;
                var disabledWhenBothOff = !StartingSupplies.SuppliesEnabled;

                var siteUpdate = typeof(StartingSupplySite).GetMethod("Update", Declared);
                var siteStockFor = typeof(StartingSupplySite).GetMethod("StockFor", Declared);
                var siteChecksHelper = FindCallIndex(PatchProcessor.GetOriginalInstructions(siteUpdate), typeof(StartingSupplies), "get_SuppliesEnabled") >= 0
                    && FindCallIndex(PatchProcessor.GetOriginalInstructions(siteStockFor), typeof(StartingSupplies), "get_SuppliesEnabled") >= 0;

                check(enabledForStoveOnly && disabledWhenBothOff && siteChecksHelper,
                    "StartingSupplySite stocks firewood and rag supplies when CabooseStove is on even with ReplaceStockFireboxes off");
            }
            finally
            {
                Main.Settings = savedSettings;
                setReplacement.Invoke(null, new object[] { savedReplacement });
            }
        }

        private static void CheckIntakeAndAimGeometry(Action<bool, string> check)
        {
            var size = new Vector3(0.32f, 0.44f, 0.40f);
            var insideCenter = CabooseStoveTarget.IsInsideChamber(Vector3.zero, size);
            var insideFloor = CabooseStoveTarget.IsInsideChamber(new Vector3(0.02f, -0.15f, 0.05f), size);
            var onStoveTop = CabooseStoveTarget.IsInsideChamber(new Vector3(0f, size.y * 0.5f + 0.02f, 0f), size);
            var nearTopPlate = CabooseStoveTarget.IsInsideChamber(new Vector3(0f, size.y * 0.46f, 0f), size);
            var outsideDoor = CabooseStoveTarget.IsInsideChamber(new Vector3(size.x * 0.55f, 0f, 0f), size);
            check(insideCenter && insideFloor && !onStoveTop && !nearTopPlate && !outsideDoor,
                "CabooseStoveTarget.IsInsideChamber accepts firewood inside the stove firebox and rejects wood on top of the stove or outside the door");

            var hitStraight = CabooseStoveTarget.IntersectsLocalChamber(new Vector3(1.2f, 0f, 0f), new Vector3(-1f, 0f, 0f), 3f, size);
            var missAbove = CabooseStoveTarget.IntersectsLocalChamber(new Vector3(1.2f, 0.35f, 0f), new Vector3(-1f, 0f, 0f), 3f, size);
            var missBehind = CabooseStoveTarget.IntersectsLocalChamber(new Vector3(1.2f, 0f, 0f), new Vector3(1f, 0f, 0f), 3f, size);
            // A ray at z = 0.18 hits an unrotated stove (|z| <= 0.20) but misses a 90°-rotated stove (|x_local| = 0.18 > 0.16).
            var unrotatedHit = CabooseStoveTarget.IntersectsLocalChamber(new Vector3(1.0f, 0f, 0.18f), new Vector3(-1f, 0f, 0f), 3f, size);
            var rotatedMiss = !CabooseStoveTarget.IntersectsLocalChamber(new Vector3(-0.18f, 0f, 1.0f), new Vector3(0f, 0f, -1f), 3f, size);
            var aimThrough = typeof(CabooseStoveTarget).GetMethod(nameof(CabooseStoveTarget.AimThrough), Declared);
            var aimIl = PatchProcessor.GetOriginalInstructions(aimThrough);
            var usesLocalSpace = FindCallIndex(aimIl, typeof(Transform), nameof(Transform.InverseTransformPoint)) >= 0
                && FindCallIndex(aimIl, typeof(Transform), nameof(Transform.InverseTransformDirection)) >= 0
                && FindCallIndex(aimIl, typeof(CabooseStoveTarget), nameof(CabooseStoveTarget.IntersectsLocalChamber)) >= 0;
            check(hitStraight && !missAbove && !missBehind && unrotatedHit && rotatedMiss && usesLocalSpace,
                "CabooseStoveTarget.AimThrough intersects in stove-local space so rotated cabooses do not distort throw redirection");
        }

        private static CabooseStoveRig CreateRig()
        {
            var rig = (CabooseStoveRig)FormatterServices.GetUninitializedObject(typeof(CabooseStoveRig));
            SetLive(rig, true);
            AccessTools.Field(typeof(CabooseStoveRig), nameof(CabooseStoveRig.Model)).SetValue(rig, new CabooseStoveModel());
            AccessTools.Field(typeof(CabooseStoveRig), "doorPorts").SetValue(rig, new List<LocoSim.Implementations.Port>());
            return rig;
        }

        private static void SetLive(UnityEngine.Object obj, bool live)
        {
            if (!ReferenceEquals(obj, null))
                AccessTools.Field(typeof(UnityEngine.Object), "m_CachedPtr").SetValue(obj, live ? new IntPtr(1) : IntPtr.Zero);
        }

        private static int FindCallIndex(List<CodeInstruction> il, Type declaringType, string methodName) =>
            il.FindIndex(i => i.operand is MethodBase m && m.DeclaringType == declaringType && m.Name == methodName);

        private static int FindSetActiveCall(List<CodeInstruction> il, bool active, bool searchFromEnd = false)
        {
            var expected = active ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;
            var start = searchFromEnd ? il.Count - 1 : 1;
            var end = searchFromEnd ? 0 : il.Count;
            var step = searchFromEnd ? -1 : 1;
            for (var i = start; i != end; i += step)
            {
                if (il[i].operand is MethodBase m && m.DeclaringType == typeof(GameObject) && m.Name == nameof(GameObject.SetActive)
                    && i > 0 && il[i - 1].opcode == expected)
                    return i;
            }
            return -1;
        }
    }
}
