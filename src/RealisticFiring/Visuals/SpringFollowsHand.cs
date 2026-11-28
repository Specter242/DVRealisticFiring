using DV.CabControls;
using DV.CabControls.Spec;
using DV.VR;
using DV.VRTK_Extensions;
using UnityEngine;
using VRTK;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Keeps an unnotched lever where a VR hand leaves it (JayBee420th,
    /// Nexus, 2026-11-09: the injector's steam lever would not stay at Feed
    /// in VR and had to be held). The game holds a lever by its hinge's
    /// spring. A notched lever's stepped joint moves the spring's target to
    /// the notch it is at, and the mouse's grab moves the target as it drags.
    /// A VR hand moves the lever by force and never moves the target, so an
    /// unnotched lever let go was pulled back to where the target last was:
    /// shut, on a lever nobody had scrolled. While a VR hand holds the lever
    /// the target follows it. The S282's injector steam lever and water
    /// wheel and the 8F's water regulators are the mod's unnotched controls;
    /// a wheel's hinge is held the same way.
    ///
    /// The injector steam valves the mod gives three notches (Off, Prime,
    /// Feed) are left unnotched in VR too (tracker #48, Nicco88 and
    /// Jiitensha, 1.26.0): with notches a third of their travel apart, the
    /// stepped joint's spring pulled a VR hand back to Off until it was past
    /// halfway to Prime, then flung the handle on to Feed. Unnotched, the
    /// hand turns it freely, a pulse in the hand marks each notch it crosses,
    /// and let go it settles at the notch it reads as (<see cref="Settle"/>).
    /// </summary>
    internal sealed class SpringFollowsHand : MonoBehaviour
    {
        /// <summary>Where a value let go settles, or null to stay where the hand left it.</summary>
        internal System.Func<float, float> Settle;

        private ControlImplBase control;
        private HingeJoint joint;
        private HingeJointAngleFix angle;
        private bool inVr;
        private bool held;
        private float notch;

        private void LateUpdate()
        {
            if (control == null)
            {
                // The game adds the control once the cab has loaded.
                control = GetComponent<ControlImplBase>();
                if (control == null)
                    return;
                inVr = IsVrControl(control.GetType());
            }
            if (!inVr)
                return;
            if (!control.IsGrabbed())
            {
                // Let go: to its notch, as a mouse leaves it.
                if (held && Settle != null)
                    control.SetValue(Settle(control.Value));
                held = false;
                return;
            }
            if (Settle != null)
            {
                var at = Settle(control.Value);
                if (held && at != notch)
                    Pulse();
                notch = at;
            }
            held = true;
            if (joint == null)
                joint = GetComponent<HingeJoint>();
            if (angle == null)
                angle = GetComponent<HingeJointAngleFix>();
            if (joint == null)
                return;
            var spring = joint.spring;
            spring.targetPosition = Held(angle != null ? angle.Angle : joint.angle, joint.useLimits, joint.limits.min, joint.limits.max);
            joint.spring = spring;
        }

        // The game's stepped joint's pulse as a notch passes under the hand (SteppedJoint.Update).
        private void Pulse()
        {
            var interactable = GetComponent<VRTK_InteractableObject>();
            var hand = interactable != null ? interactable.GetGrabbingObject() : null;
            if (hand == null)
                return;
            var ctrlRef = VRTK_ControllerReference.GetControllerReference(hand);
            if ((ctrlRef == null || ctrlRef.scriptAlias == null) && hand.TryGetComponent<TelegrabInteractionHandler.FakeController>(out var fake))
                ctrlRef = fake.realController;
            if (ctrlRef != null && ctrlRef.IsValid())
                HapticUtils.DoHapticPulse(ctrlRef, HapticIntensityType.Weak);
        }

        /// <summary>Whether a control of <paramref name="type"/> is the game's VR one (in DV.CabControls.VRTK).</summary>
        internal static bool IsVrControl(System.Type type) =>
            type != null && type.Namespace != null && type.Namespace.EndsWith(".VRTK", System.StringComparison.Ordinal);

        /// <summary>The spring's target for a lever held at <paramref name="at"/> degrees: there, within its limits.</summary>
        internal static float Held(float at, bool limited, float min, float max) =>
            limited ? Mathf.Clamp(at, Mathf.Min(min, max), Mathf.Max(min, max)) : at;

        /// <summary>Whether this session's hands turn the cab's valves by force, as a VR hand does, so a notched valve is left unnotched.</summary>
        internal static bool HandsTurnByForce => VRManager.IsVREnabled();

        /// <summary>
        /// A notched valve's spec, before its control wakes, left unnotched
        /// for a VR hand: its notches stay, for its names and its wheel.
        /// </summary>
        internal static void Unnotch(Rotary spec) => spec.useSteppedJoint = false;

        internal static void Unnotch(Lever spec)
        {
            spec.useSteppedJoint = false;
            spec.steppedValueUpdate = false;
        }

        /// <summary>
        /// A valve <see cref="Unnotch(Rotary)">left unnotched</see> for a VR
        /// hand, as it wakes: held where the hand turns it, settled at a
        /// notch by <paramref name="settle"/> when let go, and turned notch
        /// to notch by its wheel or a telegrab twist, keeping its setting
        /// when the turning stops.
        /// </summary>
        internal static void HoldNotched(GameObject control, System.Func<float, float> settle)
        {
            var hold = control.GetComponent<SpringFollowsHand>();
            if (hold == null)
                hold = control.AddComponent<SpringFollowsHand>();
            hold.Settle = settle;
            var scroll = control.GetComponent<Patches.NotchScroll>();
            if (scroll == null)
                scroll = control.AddComponent<Patches.NotchScroll>();
            scroll.ContinuousValue = true;
            scroll.DirectionalPresets = true;
        }
    }
}
