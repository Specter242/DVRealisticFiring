using System;
using System.Linq;
using System.Reflection;
using DV;
using DV.InventorySystem;
using DV.Simulation.Controllers;
using DV.VRTK_Extensions;
using HarmonyLib;
using LocoSim.Definitions;
using LocoSim.Implementations;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using UnityEngine;
using VRTK;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Verifies every VR game member, method, event, and Harmony binding used
    /// by VR empty-shovel raking, VR shovelful-size scrolling, and the VR
    /// automatic fire door against the installed game assemblies.
    /// </summary>
    internal static class VrParityChecks
    {
        internal static void Run(SimGameParams gameParams, FireboxDefinition def, Action<bool, string> check)
        {
            CheckVrRakeBindings(gameParams, def, check);
            CheckVrScoopScrollingBindings(check);
            CheckVrAutoFireDoorBindings(check);
        }

        private static void CheckVrRakeBindings(SimGameParams gameParams, FireboxDefinition def, Action<bool, string> check)
        {
            // Stock VR door-open threshold matches Core.VrShovelRake.DoorOpening.
            var vrDoorField = AccessTools.Field(typeof(ShovelNonPhysicalCoal), "REQUIRED_DOOR_OPENED_PERCENTAGE_VR");
            var vrDoor = vrDoorField != null && vrDoorField.IsLiteral ? (float)vrDoorField.GetRawConstantValue() : -1f;
            check(Math.Abs(vrDoor - VrShovelRake.DoorOpening) < 1e-5f,
                $"VR shovel rake door opening matches stock VR shovel ({vrDoor:F2})");

            // Shovel and audio members read by VrShovelRakeController and ShovelCapacityPatch.
            var isLoaded = AccessTools.PropertyGetter(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.IsLoaded));
            var interactionPoint = AccessTools.PropertyGetter(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.InteractionPoint));
            var capacityForPile = AccessTools.DeclaredMethod(typeof(ShovelNonPhysicalCoal), "ShovelCapacityForPile", new[] { typeof(ICoalPile) });
            var loadCoal = AccessTools.DeclaredMethod(typeof(ShovelNonPhysicalCoal), "LoadCoal", new[] { typeof(ICoalPile) });
            var onShovelUsed = AccessTools.DeclaredMethod(typeof(ShovelNonPhysicalCoal), "OnShovelUsed");
            var shovelTip = AccessTools.Field(typeof(Shovel), nameof(Shovel.shovelTip));
            var coalDrop = AccessTools.Field(typeof(ShovelAudio), "coalDrop");
            var playDebounced = typeof(ShovelAudio).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == nameof(DebouncedSound.PlayDebounced) && m.GetParameters().Length == 3);
            check(isLoaded != null && interactionPoint != null && capacityForPile != null && loadCoal != null && onShovelUsed != null
                  && shovelTip != null && shovelTip.FieldType == typeof(Collider)
                  && coalDrop != null && coalDrop.FieldType.Name == "AudioClip"
                  && playDebounced != null && playDebounced.GetParameters()[0].ParameterType.Name == "AudioClip"
                  && playDebounced.GetParameters()[1].ParameterType == typeof(Vector3)
                  && playDebounced.GetParameters()[2].ParameterType == typeof(float),
                "VR shovel blade, coal-loading, and debounced audio bindings resolve on Shovel / ShovelNonPhysicalCoal / ShovelAudio");

            // Verify stock ShovelNonPhysicalCoal.LoadCoal calls ShovelCapacityForPile (so ShovelCapacityPatch applies in VR too).
            var loadCallsCapacity = PatchProcessor.GetOriginalInstructions(loadCoal)
                .Any(i => i.operand is MethodInfo m && m == capacityForPile);
            check(loadCallsCapacity,
                "stock VR ShovelNonPhysicalCoal.LoadCoal calls ShovelCapacityForPile (patched to Main.Settings.Scoop)");

            // Verify VrShovelRakeController attaches via VrShovelRakePatch, desktop CanRake skips VR,
            // VrShovelRakeController respects AimShovelThroughDoor & HudPolicy.Hidden, and RealisticFirebox.Rake works end-to-end.
            var rakePatch = AccessTools.DeclaredMethod(typeof(VrShovelRakePatch), "Postfix");
            var addsRakeController = rakePatch != null && PatchProcessor.GetOriginalInstructions(rakePatch)
                .Any(i => i.operand is MethodInfo m && m.Name == "AddComponent" && m.IsGenericMethod
                          && m.GetGenericArguments()[0] == typeof(VrShovelRakeController));
            check(addsRakeController, "VrShovelRakePatch attaches VrShovelRakeController to VR shovels");

            var canRake = AccessTools.DeclaredMethod(typeof(ShovelTargets), nameof(ShovelTargets.CanRake));
            var vrMethod = AccessTools.Method(typeof(VRManager), nameof(VRManager.IsVREnabled));
            var canRakeSkipsVr = canRake != null && vrMethod != null && PatchProcessor.GetOriginalInstructions(canRake)
                .Any(i => i.operand is MethodInfo m && m == vrMethod);
            check(canRakeSkipsVr, "ShovelTargets.CanRake skips VR so ItemTriggerEnterTarget cannot double-rake");

            var rakeUpdate = AccessTools.DeclaredMethod(typeof(VrShovelRakeController), "Update");
            var aimField = AccessTools.Field(typeof(Settings), nameof(Settings.AimShovelThroughDoor));
            var hudHiddenGetter = AccessTools.PropertyGetter(typeof(HudPolicy), nameof(HudPolicy.Hidden));
            var rakeUpdateInstrs = rakeUpdate != null ? PatchProcessor.GetOriginalInstructions(rakeUpdate) : null;
            var respectsAimAndHud = rakeUpdateInstrs != null && aimField != null && hudHiddenGetter != null
                && rakeUpdateInstrs.Any(i => i.operand is FieldInfo f && f == aimField)
                && rakeUpdateInstrs.Any(i => i.operand is MethodInfo m && m == hudHiddenGetter);
            check(respectsAimAndHud, "VrShovelRakeController gates rake zone highlight on AimShovelThroughDoor and HudPolicy.Hidden");

            var firebox = new RealisticFirebox(def, null);
            firebox.SetGameParams(gameParams);
            firebox.SetSaveStateData(new JObject { ["fireOn"] = true, ["coalMass"] = 0f });
            firebox.Model.AddCoal(20f, GrateZone.Center);
            var beforeCenter = firebox.Model.Zones[(int)GrateZone.Center].Coal;
            firebox.Rake(GrateZone.Center);
            var afterCenter = firebox.Model.Zones[(int)GrateZone.Center].Coal;
            check(afterCenter < beforeCenter && firebox.Model.CoalMass > 19.9f,
                $"RealisticFirebox.Rake levels a piled zone cleanly ({beforeCenter:F1} -> {afterCenter:F1} kg in center, total preserved)");
        }

        private static void CheckVrScoopScrollingBindings(Action<bool, string> check)
        {
            check(typeof(ItemScrolling).IsAssignableFrom(typeof(ShovelScrollingVR)),
                "ShovelScrollingVR derives from the game's ItemScrolling base class");

            // VRTK two-handed interactable, use modifier, touchpad interpreter, tooltips, and haptics.
            var isScrollable = AccessTools.Field(typeof(VRTK_InteractableObject_DV), nameof(VRTK_InteractableObject_DV.isScrollable));
            var grabbedEvent = typeof(VRTK_InteractableObject).GetEvent(nameof(VRTK_InteractableObject.InteractableObjectGrabbed));
            var ungrabbedEvent = typeof(VRTK_InteractableObject).GetEvent(nameof(VRTK_InteractableObject.InteractableObjectUngrabbed));
            var getPrimary = AccessTools.Method(typeof(VRTK_InteractableObject), nameof(VRTK_InteractableObject.GetGrabbingObject));
            var getSecondary = AccessTools.Method(typeof(VRTK_InteractableObject), nameof(VRTK_InteractableObject.GetSecondaryGrabbingObject));
            var useModified = AccessTools.PropertyGetter(typeof(VRTK_InteractUse_DV), nameof(VRTK_InteractUse_DV.UseModified));
            var usePressed = AccessTools.PropertyGetter(typeof(VRTK_InteractUse_DV), nameof(VRTK_InteractUse_DV.UsePressed));
            var delayedDir = typeof(TouchpadInputInterpreter).GetEvent(nameof(TouchpadInputInterpreter.DelayedDirectionalInputGiven));
            var neutralDir = typeof(TouchpadInputInterpreter).GetEvent(nameof(TouchpadInputInterpreter.DirectionalInputNeutral));
            var pressedChanged = typeof(TouchpadInputInterpreter).GetEvent(nameof(TouchpadInputInterpreter.PressedChanged));
            var axisValue = AccessTools.PropertyGetter(typeof(TouchpadInputInterpreter), nameof(TouchpadInputInterpreter.AxisValue));
            var tooltipLeft = AccessTools.PropertyGetter(typeof(VRTK_ControllerUtils_DV), nameof(VRTK_ControllerUtils_DV.ControllerTooltipLeft));
            var tooltipRight = AccessTools.PropertyGetter(typeof(VRTK_ControllerUtils_DV), nameof(VRTK_ControllerUtils_DV.ControllerTooltipRight));
            var showTooltip = AccessTools.Method(typeof(ControllerTooltip), nameof(ControllerTooltip.ShowTooltip), new[] { typeof(string), typeof(bool) });
            var hideTooltip = AccessTools.Method(typeof(ControllerTooltip), nameof(ControllerTooltip.HideTooltip));
            var tooltipTextField = AccessTools.Field(typeof(ControllerTooltip), "tooltipText");
            var haptic = AccessTools.Method(typeof(HapticUtils), nameof(HapticUtils.DoHapticPulse), new[] { typeof(VRTK_ControllerReference), typeof(HapticIntensityType) });

            check(isScrollable != null && isScrollable.FieldType == typeof(bool)
                  && grabbedEvent != null && ungrabbedEvent != null
                  && getPrimary != null && getSecondary != null
                  && useModified != null && usePressed != null
                  && delayedDir != null && neutralDir != null && pressedChanged != null && axisValue != null
                  && tooltipLeft != null && tooltipRight != null
                  && showTooltip != null && hideTooltip != null
                  && tooltipTextField != null && tooltipTextField.FieldType.Name == "TMP_Text"
                  && haptic != null,
                "VR two-handed scrolling, touchpad interpreter, controller tooltip, and haptic bindings resolve");

            // Two-handed scroll ownership: neutral or wand release from the other hand never cancels the active hand.
            var leftHand = new object();
            var rightHand = new object();
            var shouldStopMethod = AccessTools.DeclaredMethod(typeof(ShovelScrollingVR), nameof(ShovelScrollingVR.ShouldStopContinuous));
            var onNeutralMethod = AccessTools.DeclaredMethod(typeof(ShovelScrollingVR), "OnScrollInputNeutral");
            var onPressedMethod = AccessTools.DeclaredMethod(typeof(ShovelScrollingVR), "OnPressedChanged");
            var onScrollMethod = AccessTools.DeclaredMethod(typeof(ShovelScrollingVR), "OnScrollInput");
            var isVrDoorHeldGetter = AccessTools.PropertyGetter(typeof(AutoFireDoorRig), nameof(AutoFireDoorRig.IsVrDoorHeld));
            var neutralChecksOwner = shouldStopMethod != null && onNeutralMethod != null
                && PatchProcessor.GetOriginalInstructions(onNeutralMethod).Any(i => i.operand is MethodInfo m && m == shouldStopMethod);
            var pressedChecksOwner = shouldStopMethod != null && onPressedMethod != null
                && PatchProcessor.GetOriginalInstructions(onPressedMethod).Any(i => i.operand is MethodInfo m && m == shouldStopMethod);
            var scrollDefersToDoor = onScrollMethod != null && isVrDoorHeldGetter != null
                && PatchProcessor.GetOriginalInstructions(onScrollMethod).Any(i => i.operand is MethodInfo m && m == isVrDoorHeldGetter);
            check(neutralChecksOwner && pressedChecksOwner && scrollDefersToDoor
                  && !ShovelScrollingVR.ShouldStopContinuous(leftHand, SDK_BaseController.ControllerHand.Left, rightHand, SDK_BaseController.ControllerHand.Right)
                  && ShovelScrollingVR.ShouldStopContinuous(leftHand, SDK_BaseController.ControllerHand.Left, leftHand, SDK_BaseController.ControllerHand.Left)
                  && !ShovelScrollingVR.ShouldStopContinuous(leftHand, SDK_BaseController.ControllerHand.None, rightHand, SDK_BaseController.ControllerHand.None)
                  && ShovelScrollingVR.ShouldStopContinuous(leftHand, SDK_BaseController.ControllerHand.Left, new object(), SDK_BaseController.ControllerHand.Left)
                  && !ShovelScrollingVR.ShouldStopContinuous(null, SDK_BaseController.ControllerHand.None, leftHand, SDK_BaseController.ControllerHand.Left),
                "ShovelScrollingVR only cancels continuous repeat for the originating hand/controller and defers to an open VR fire door");

            // Tooltip cleanup only hides the shared ControllerTooltip if its text still matches what ShovelfulWheel showed.
            check(ShovelfulWheel.ShouldHideTooltip("Shovelful: 2 / 3 kg", "Shovelful: 2 / 3 kg")
                  && !ShovelfulWheel.ShouldHideTooltip("Drop", "Shovelful: 2 / 3 kg")
                  && !ShovelfulWheel.ShouldHideTooltip("", "Shovelful: 2 / 3 kg")
                  && !ShovelfulWheel.ShouldHideTooltip(null, "Shovelful: 2 / 3 kg"),
                "ShovelfulWheel only hides ControllerTooltip when its current text still matches what ShovelfulWheel showed");

            // ScrollAction direction mapping matches DV's positive/negative scroll convention.
            check(ShovelScrollingVR.ToScrollAction(TouchpadInputDirection.Up) == ScrollAction.ScrollUp
                  && ShovelScrollingVR.ToScrollAction(TouchpadInputDirection.Down) == ScrollAction.ScrollDown
                  && ShovelScrollingVR.ToScrollAction(TouchpadInputDirection.Right) == ScrollAction.ScrollRight
                  && ShovelScrollingVR.ToScrollAction(TouchpadInputDirection.Left) == ScrollAction.ScrollLeft
                  && ShovelScrollingVR.ToScrollAction(TouchpadInputDirection.None) == ScrollAction.Release
                  && ScrollAction.ScrollUp.IsPositive() && ScrollAction.ScrollRight.IsPositive()
                  && !ScrollAction.ScrollDown.IsPositive() && !ScrollAction.ScrollLeft.IsPositive(),
                "VR stick/touchpad directions map to ScrollAction with Up/Right larger and Down/Left smaller");

            // ShovelWheelPatch attaches both VR and non-VR scrolling components.
            var wheelPatch = AccessTools.DeclaredMethod(typeof(ShovelWheelPatch), "Postfix");
            var added = PatchProcessor.GetOriginalInstructions(wheelPatch)
                .Where(i => i.operand is MethodInfo m && m.Name == "AddComponent" && m.IsGenericMethod)
                .Select(i => ((MethodInfo)i.operand).GetGenericArguments()[0])
                .ToList();
            check(added.Contains(typeof(ShovelScrollingVR)) && added.Contains(typeof(ItemScrollingNonVR)) && added.Contains(typeof(ShovelfulWheel)),
                "ShovelWheelPatch wires ShovelScrollingVR in VR and ItemScrollingNonVR on desktop");

            check(ShovelfulWheel.VrNotice(2f, 3f, full: false, atLimit: false) == "Shovelful: 2 / 3 kg"
                  && ShovelfulWheel.VrNotice(1f, 3f, full: false, atLimit: true) == "Shovelful: 1 kg (min)"
                  && ShovelfulWheel.VrNotice(3f, 3f, full: true, atLimit: true) == "Shovelful: 3 kg (full)",
                "VR shovelful tooltip formats partial, minimum, and full scoop sizes cleanly");
        }

        private static void CheckVrAutoFireDoorBindings(Action<bool, string> check)
        {
            // ShovelInHandPatch must no longer exclude VR shovels.
            var inHandPatch = AccessTools.DeclaredMethod(typeof(ShovelInHandPatch), "Postfix");
            var vrMethod = AccessTools.Method(typeof(VRManager), nameof(VRManager.IsVREnabled));
            var skipsVr = PatchProcessor.GetOriginalInstructions(inHandPatch)
                .Any(i => i.operand is MethodInfo m && m == vrMethod);
            check(!skipsVr, "ShovelInHandPatch attaches ShovelInHand in VR as well as on desktop");

            // VR pause/UI guard bindings used by AutoFireDoorRig.
            var isPointingAtUi = AccessTools.PropertyGetter(typeof(InventoryViewVR), nameof(InventoryViewVR.IsPointingAtUI));
            var isPauseMenuOpen = AccessTools.PropertyGetter(typeof(AppUtil), nameof(AppUtil.IsPauseMenuOpen));
            var isTimePaused = AccessTools.PropertyGetter(typeof(AppUtil), nameof(AppUtil.IsTimePaused));
            var inputEnabled = AccessTools.Field(typeof(LocomotionInputWrapper), nameof(LocomotionInputWrapper.inputEnabled));
            check(isPointingAtUi != null && isPauseMenuOpen != null && isTimePaused != null && inputEnabled != null,
                "VR automatic fire door pause, UI-pointer, and locomotion guard bindings resolve");

            // AutoFireDoorRig.Tick routes VR through TickVr, MayAct searches children for LocomotionInputWrapper,
            // and TickVr suppresses the pedal across pauses until trigger release.
            var tickMethod = AccessTools.DeclaredMethod(typeof(AutoFireDoorRig), "Tick");
            var tickVrMethod = AccessTools.DeclaredMethod(typeof(AutoFireDoorRig), "TickVr");
            var mayActGetter = AccessTools.PropertyGetter(typeof(AutoFireDoorRig), "MayAct");
            var suppressMethod = AccessTools.DeclaredMethod(typeof(VrFireDoorPedal), nameof(VrFireDoorPedal.SuppressUntilRelease));
            var callsTickVr = tickMethod != null && tickVrMethod != null && PatchProcessor.GetOriginalInstructions(tickMethod)
                .Any(i => i.operand is MethodInfo m && m == tickVrMethod);
            var hearMethod = AccessTools.DeclaredMethod(typeof(AutoFireDoorRig), "Hear");
            var tickVrInstrs = tickVrMethod != null ? PatchProcessor.GetOriginalInstructions(tickVrMethod) : null;
            var tickVrCallsHear = tickVrInstrs != null && hearMethod != null && tickVrInstrs
                .Any(i => i.operand is MethodInfo m && m == hearMethod);
            var tickVrSuppressesOnPause = tickVrInstrs != null && suppressMethod != null && tickVrInstrs
                .Any(i => i.operand is MethodInfo m && m == suppressMethod);
            var mayActSearchesChildren = mayActGetter != null && PatchProcessor.GetOriginalInstructions(mayActGetter)
                .Any(i => i.operand is MethodInfo m && m.Name == "GetComponentInChildren" && m.IsGenericMethod
                          && m.GetGenericArguments()[0] == typeof(LocomotionInputWrapper));
            check(callsTickVr && tickVrCallsHear && tickVrSuppressesOnPause && mayActSearchesChildren,
                "AutoFireDoorRig.Tick dispatches VR to TickVr with child LocomotionInputWrapper lookup, pause suppression, and shared air audio");

            // Verify VrFireDoorPedal + AutoFireDoor preserves cracked-door restore and suppresses scoop scrolling.
            var pedal = new VrFireDoorPedal();
            var door = new AutoFireDoor();
            var opening = 0.2f;
            var s0 = pedal.Step(true, useModified: true, stickDeflected: true, pointingAtUi: false, 1f);
            check(!s0.Pressed && !s0.Held && !door.Working, "scoop scrolling suppresses the VR automatic fire door");
            pedal.Step(false, false, false, false, 1.2f);
            pedal.Step(true, false, false, false, 1.3f);
            var engaged = pedal.Step(true, false, false, false, 1.3f + VrFireDoorPedal.HoldDelay);
            if (engaged.Pressed)
                door.Press(opening, 1.3f + VrFireDoorPedal.HoldDelay);
            for (var t = 1.44f; t <= 2.0f; t += 0.02f)
                if (door.Step(opening, true, t) is float set)
                    opening = set;
            var opened = opening == 1f;
            for (var t = 2.02f; t <= 2.6f; t += 0.02f)
                if (door.Step(opening, false, t) is float set)
                    opening = set;
            check(opened && Math.Abs(opening - 0.2f) < 1e-4f && !door.Working,
                "VR automatic fire door opens fully on clean hold and restores a cracked door on release");
        }
    }
}
