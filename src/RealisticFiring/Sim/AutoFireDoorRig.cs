using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Interaction;
using DV.Interaction.Inputs;
using DV.Utils;
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
    /// <summary>Which engines have the automatic fire door.</summary>
    internal static class AutoFireDoorFit
    {
 private static readonly HashSet<string> Upgraded = new HashSet<string> { "LocoS282A", "LocoS060" };

 internal static readonly IReadOnlyCollection<string> Prototype = new HashSet<string>
        {
            // NYC J-3a: "Franklin", one forum member's recollection of the J-1s
            // and J-3s (The Home Machinist, 2009); likely, as RME lists such
            // doors on big American engines of its day, though its article on
            // the class (May 1938) doesn't name the door.
            "S464-730",
            // UP FEF class: "a Franklin firedoor" on the FEF-1 (RME Feb. 1938
            // p.47), and "Fire door, Butterfly" from Franklin on the road's
            // 4-6-6-4s (RME Jan. 1937); the FEF-3 taken to follow them. No.
            // 844 went to oil in 1945; the mod fires it as built, on coal.
            "FEF-3",
            // LittleNiko36's "Black Arrow", an FEF-3 too.
            "S484-840A",
            // China Railways QJ: a retired Chinese engine crewman's account of
            // big five-driver engines gives a floor pedal, an air cylinder above
            // two doors that part sideways, and the doors falling shut on
            // letting go (Zhihu); inferred, as the account names no class.
            "S2102-870",
        };

        /// <summary>Whether <paramref name="car"/> has its automatic fire door now.</summary>
        public static bool Has(TrainCar car)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery == null)
                return false;
            if (Upgraded.Contains(livery))
                return Fittings.AutoFireDoor;
            // Never fitted hands the jump key back on these too.
            return Prototype.Contains(livery) && Main.Settings?.AutomaticFireDoor != UpgradeRule.NeverFitted;
        }
    }

 internal static class AutoFireDoorRig
    {
        // The stock keyboard's reach for a cab control (AKeyboardInput), in m.
        private const float ReachAcross = 4f;
        private const float ReachUp = 2f;

        private static readonly AutoFireDoor Cycle = new AutoFireDoor();
        private static RealisticFirebox worked;

        // The phase whose air has been heard.
        private static AutoFireDoorPhase heard;

        // Where each engine's door is in the car's frame, kept by livery: the
        // game unloads a cab while the player stands on its tender, and the
        // door is still in reach from there.
        private static readonly Dictionary<string, Vector3> DoorInCar = new Dictionary<string, Vector3>();

        // The cab last searched, so one without a door target is searched once.
        private static Transform searchedInterior;

        /// <summary>Whether the jump action works a fire door now, rather than jumping.</summary>
        internal static bool ClaimsJump
        {
            get
            {
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

        /// <summary>Reads the jump action and moves the door, once a frame.</summary>
        internal static void Tick()
        {
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
        private static bool MayAct
        {
            get
            {
                var player = PlayerManager.PlayerTransform;
                if (player == null)
                    return false;
                var app = SingletonBehaviour<AppUtil>.Instance;
                if (app != null && app.IsTimePaused)
                    return false;
                var walking = player.GetComponent<LocomotionInputWrapper>();
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

    /// <summary>Tells the rig when one of the player's shovels is in hand.</summary>
    internal sealed class ShovelInHand : MonoBehaviour
    {
        private static readonly List<ShovelInHand> Shovels = new List<ShovelInHand>();

        private ItemBase item;

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

        private void Awake() => item = GetComponent<ItemBase>();

        private void OnEnable() => Shovels.Add(this);

        private void OnDisable() => Shovels.Remove(this);
    }

    /// <summary>Each shovel reports being held, with the mouse and the realistic fireboxes on.</summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "Start")]
    internal static class ShovelInHandPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            if (!Main.ReplacementEnabled || VRManager.IsVREnabled() || __instance == null
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
