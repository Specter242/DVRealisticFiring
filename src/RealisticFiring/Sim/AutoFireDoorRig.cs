using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Interaction;
using DV.Interaction.Inputs;
using DV.InventorySystem;
using DV.Utils;
using DV.VRTK_Extensions;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Upgrades;
using RealisticFiring.Visuals;
using UnityEngine;
using GameInput = DV.Interaction.Inputs.InputManager;

namespace RealisticFiring.Sim
{
    /// <summary>Which engines have the automatic fire door. Per-locomotive modes live in <see cref="LocomotiveProfiles"/>.</summary>
    internal static class AutoFireDoorFit
    {
        /// <summary>The car packs' engines whose prototypes had a power-worked door.</summary>
        internal static readonly IReadOnlyCollection<string> Prototype = new HashSet<string>(
            LocomotiveProfiles.All.Where(p => p.AutoFireDoor == AutoFireDoorMode.Prototype).SelectMany(p => p.Liveries));

        /// <summary>Whether <paramref name="car"/> has its automatic fire door now.</summary>
        public static bool Has(TrainCar car)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (LocomotiveProfiles.TryGet(livery, out var profile))
            {
                if (profile.AutoFireDoor == AutoFireDoorMode.Upgradable)
                    return Fittings.HasAutoFireDoor(car);
                // Never fitted hands the jump key back on these too.
                return profile.AutoFireDoor == AutoFireDoorMode.Prototype && Main.Settings?.AutomaticFireDoor != UpgradeRule.NeverFitted;
            }
            return MakerFitRegistry.HasAutoFireDoor(livery) && Main.Settings?.AutomaticFireDoor != UpgradeRule.NeverFitted;
        }
    }

 internal static class AutoFireDoorRig
    {
        // The stock keyboard's reach for a cab control (AKeyboardInput), in m.
        private const float ReachAcross = 4f;
        private const float ReachUp = 2f;

        private static readonly AutoFireDoor Cycle = new AutoFireDoor();
        private static readonly VrFireDoorPedal VrPedal = new VrFireDoorPedal();
        private static RealisticFirebox worked;
        private static bool vrDoorHeld;

        /// <summary>Whether the VR shovel-hand trigger is currently holding an automatic fire door open.</summary>
        internal static bool IsVrDoorHeld => vrDoorHeld;

        // The phase whose air has been heard.
        private static AutoFireDoorPhase heard;

        // Where each engine's door is in the car's frame, kept by livery: the
        // game unloads a cab while the player stands on its tender, and the
        // door is still in reach from there.
        private static readonly Dictionary<string, Vector3> DoorInCar = new Dictionary<string, Vector3>();

        // The cab last searched, so one without a door target is searched once.
        private static Transform searchedInterior;

        /// <summary>Whether the desktop jump action works a fire door now, rather than jumping.</summary>
        internal static bool ClaimsJump
        {
            get
            {
                if (VRManager.IsVREnabled())
                    return false;
                // Asked from inside the game's walking code: a failure here
                // must not stop the player walking.
                try
                {
                    return MayAct && CanWork(out _);
                }
                catch (Exception e)
                {
                    Faults.Report("the automatic fire door", null, e);
                    return false;
                }
            }
        }

        /// <summary>Reads the pedal input (desktop jump or VR shovel trigger) and moves the door, once a frame.</summary>
        internal static void Tick()
        {
            if (VRManager.IsVREnabled())
            {
                TickVr();
                return;
            }
            if (!Cycle.Working && !ShovelInHand.Any)
                return;
            var input = GameInput.NewPlayer;
            if (input == null)
                return;
            var jump = GameInput.Actions.Jump;
            var now = Time.time;
            if (input.GetButtonDown(jump) && ClaimsJump && CanWork(out var reached))
                Press(reached, now);
            Hear();
            if (!Cycle.Working)
                return;
            var port = worked?.Car != null ? worked.DoorControlPort : null;
            if (port == null)
            {
                Stop();
                worked = null;
                return;
            }
            var held = input.GetButton(jump) && ClaimsJump && CanWork(out var still) && ReferenceEquals(still, worked);
            if (Cycle.Step(port.Value, held, now) is float set && set != port.Value)
                port.ExternalValueUpdate(set);
            Hear();
        }

        private static void TickVr()
        {
            var mayAct = MayAct;
            if (!Cycle.Working && !ShovelInHand.Any)
            {
                vrDoorHeld = false;
                if (!mayAct)
                    VrPedal.SuppressUntilRelease();
                else
                    VrPedal.Reset();
                return;
            }
            var now = Time.time;
            VrFireDoorSignal signal;
            if (!mayAct)
            {
                VrPedal.SuppressUntilRelease();
                signal = default;
            }
            else
            {
                ShovelInHand.ReadVrTrigger(out var triggerDown, out var useModified, out var stickDeflected);
                signal = VrPedal.Step(triggerDown, useModified, stickDeflected, InventoryViewVR.IsPointingAtUI, now);
            }
            if (signal.Pressed && CanWork(out var reached))
                Press(reached, now);
            Hear();
            if (!Cycle.Working)
            {
                vrDoorHeld = false;
                return;
            }
            var port = worked?.Car != null ? worked.DoorControlPort : null;
            if (port == null)
            {
                Stop();
                worked = null;
                vrDoorHeld = false;
                return;
            }
            var held = signal.Held && mayAct && CanWork(out var still) && ReferenceEquals(still, worked);
            vrDoorHeld = held;
            if (Cycle.Step(port.Value, held, now) is float set && set != port.Value)
                port.ExternalValueUpdate(set);
            Hear();
        }

        private static void Stop()
        {
            // Cut short (another engine's door, or this one's port gone): its air goes out, so no hiss is left on.
            if ((heard == AutoFireDoorPhase.Opening || heard == AutoFireDoorPhase.Open) && worked?.Car != null && TryGetDoor(worked.Car, out var door))
                AutoFireDoorSounds.Play(worked.Car, door, AutoFireDoorAir.Exhausted);
            Cycle.Stop();
            heard = AutoFireDoorPhase.Idle;
        }

        // The door's air, at the door, as the cycle changes (a press as it shuts reopens it).
        private static void Hear()
        {
            var air = AutoFireDoor.AirFor(heard, Cycle.Phase);
            heard = Cycle.Phase;
            if (air != AutoFireDoorAir.None && worked?.Car != null && TryGetDoor(worked.Car, out var door))
                AutoFireDoorSounds.Play(worked.Car, door, air);
        }

        private static void Press(RealisticFirebox firebox, float now)
        {
            // Another engine's door still in its cycle goes straight back.
            if (Cycle.Working && !ReferenceEquals(firebox, worked))
            {
                if (worked?.Car != null)
                    worked.DoorControlPort?.ExternalValueUpdate(Cycle.Rest);
                Stop();
            }
            worked = firebox;
            Cycle.Press(firebox.DoorControlPort.Value, now);
        }

        // The game is taking the player's movement: not paused, and walking allowed.
        // In VR Teleport Locomotion, PlayerTransform is PlayAreaTransform (which has
        // no LocomotionInputWrapper), so only check walking.inputEnabled when present.
        private static bool MayAct
        {
            get
            {
                var player = PlayerManager.PlayerTransform;
                if (player == null)
                    return false;
                var app = SingletonBehaviour<AppUtil>.Instance;
                if (app != null && (app.IsTimePaused || app.IsPauseMenuOpen))
                    return false;
                var walking = player.GetComponentInChildren<LocomotionInputWrapper>();
                if (VRManager.IsVREnabled())
                    return walking == null || walking.inputEnabled;
                return walking != null && walking.inputEnabled;
            }
        }

        private static bool CanWork(out RealisticFirebox firebox)
        {
            firebox = null;
            if (!ShovelInHand.Any || !FireboxRegistry.TryGetTended(PlayerManager.Car, out var tended)
                || !AutoFireDoorFit.Has(tended.Car) || tended.DoorControlPort == null || !DoorInReach(tended.Car))
                return false;
            firebox = tended;
            return true;
        }

        private static bool DoorInReach(TrainCar car)
        {
            if (!TryGetDoor(car, out var door))
                return false;
            var camera = PlayerManager.PlayerCamera;
            var eye = camera != null ? camera.transform : PlayerManager.PlayerTransform;
            if (eye == null)
                return false;
            var toDoor = car.transform.TransformPoint(door) - eye.position;
            if (Mathf.Abs(toDoor.y) > ReachUp)
                return false;
            toDoor.y = 0f;
            return toDoor.sqrMagnitude <= ReachAcross * ReachAcross;
        }

        // The door is where the shovel's own target for it is, read from the
        // cab the first time it is loaded and asked about.
        internal static bool TryGetDoor(TrainCar car, out Vector3 door)
        {
            var livery = car.carLivery.id;
            if (DoorInCar.TryGetValue(livery, out door))
                return true;
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            if (interior == null || interior == searchedInterior)
                return false;
            searchedInterior = interior;
            var targets = interior.GetComponentsInChildren<ItemUseTarget>(true);
            var target = targets.FirstOrDefault(t => t.GetComponent<NonPhysicsCoalTarget>() != null)
                ?? targets.FirstOrDefault(t => t.GetComponent<Fire>() != null);
            var collider = target != null && target.targetColliders != null
                ? target.targetColliders.FirstOrDefault(c => c != null)
                : null;
            if (collider == null)
                return false;
            door = CarSpace.LocalBounds(car.transform, collider).center;
            DoorInCar[livery] = door;
            return true;
        }

        /// <summary>The jump key's name, for the prompt at the door.</summary>
        internal static string KeyName => GameInput.Actions.Jump.LocalizeInput();
    }

    /// <summary>Tells the rig when one of the player's shovels is in hand (desktop or VR).</summary>
    internal sealed class ShovelInHand : MonoBehaviour
    {
        // Matches TouchpadInputInterpreter's 0.4 deadzone (0.4² = 0.16).
        private const float StickDeadzoneSq = 0.16f;

        private static readonly List<ShovelInHand> Shovels = new List<ShovelInHand>();

        private ItemBase item;
        private VRTK_InteractableObject_DV interactable;

        /// <summary>Whether the player is holding a shovel.</summary>
        internal static bool Any
        {
            get
            {
                foreach (var shovel in Shovels)
                    if (shovel.item != null && shovel.item.IsGrabbed())
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Reads VR trigger and stick state across every hand currently grabbing
        /// a held shovel (supporting both one-handed and two-handed holds).
        /// </summary>
        internal static void ReadVrTrigger(out bool triggerDown, out bool useModified, out bool stickDeflected)
        {
            triggerDown = false;
            useModified = false;
            stickDeflected = false;
            foreach (var shovel in Shovels)
            {
                if (shovel.item == null || !shovel.item.IsGrabbed())
                    continue;
                if (shovel.interactable == null)
                    shovel.interactable = shovel.GetComponent<VRTK_InteractableObject_DV>();
                if (shovel.interactable == null)
                    continue;
                InspectHand(shovel.interactable.GetGrabbingObject(), ref triggerDown, ref useModified, ref stickDeflected);
                var secondary = shovel.interactable.GetSecondaryGrabbingObject();
                if (secondary != null && secondary != shovel.interactable.GetGrabbingObject())
                    InspectHand(secondary, ref triggerDown, ref useModified, ref stickDeflected);
            }
        }

        private static void InspectHand(GameObject hand, ref bool triggerDown, ref bool useModified, ref bool stickDeflected)
        {
            if (hand == null)
                return;
            var use = hand.GetComponent<VRTK_InteractUse_DV>();
            if (use == null)
                return;
            if (use.UseModified)
                useModified = true;
            if (!use.UsePressed)
                return;
            triggerDown = true;
            var touchpad = use.GetComponent<TouchpadInputInterpreter>();
            if (touchpad != null && touchpad.AxisValue.sqrMagnitude > StickDeadzoneSq)
                stickDeflected = true;
        }

        private void Awake()
        {
            item = GetComponent<ItemBase>();
            interactable = GetComponent<VRTK_InteractableObject_DV>();
        }

        private void OnEnable() => Shovels.Add(this);

        private void OnDisable() => Shovels.Remove(this);
    }

    /// <summary>Each shovel reports being held (desktop and VR) when the realistic fireboxes are on.</summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "Start")]
    internal static class ShovelInHandPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            if (!Main.ReplacementEnabled || __instance == null
                || __instance.GetComponent<ItemBase>() == null || __instance.GetComponent<ShovelInHand>() != null)
                return;
            __instance.gameObject.AddComponent<ShovelInHand>();
        }
    }

    /// <summary>At the door, shovel in hand, the jump key is the door's and the player stays down.</summary>
    [HarmonyPatch(typeof(LocomotionInputNonVr), nameof(LocomotionInputNonVr.JumpRequested), MethodType.Getter)]
    internal static class AutoFireDoorJumpPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (__result && AutoFireDoorRig.ClaimsJump)
                __result = false;
        }
    }

    /// <summary>A loaded shovel pointed at the shut door names the key that opens it.</summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleHover))]
    internal static class AutoFireDoorPromptPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target)
        {
            if (!__instance.IsLoaded || !ShovelTargets.TryGetFirebox(target, out _, out var firebox)
                || firebox.DoorOpening >= AutoFireDoor.ShovelOpening || !AutoFireDoorRig.ClaimsJump)
                return;
            var prompt = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            if (prompt != null)
                prompt.DisplayText($"Hold {AutoFireDoorRig.KeyName}: fire door open");
        }
    }
}
