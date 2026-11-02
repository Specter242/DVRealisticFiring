using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Patches
{
 [HarmonyPatch(typeof(RotaryBase), nameof(RotaryBase.Scroll))]
    internal static class NotchScrollPatch
    {
        private static bool Prefix(RotaryBase __instance, ScrollAction action)
        {
            var gate = __instance.GetComponent<NotchScroll>();
            if (gate == null || action == ScrollAction.Release)
                return true;
            var spec = __instance.GetComponent<Rotary>();
            if (!gate.Take() || spec == null || spec.notches < 2 || __instance.IsGrabbed() || !__instance.InteractionAllowed)
                return false;
            gate.Turn(__instance, spec, action.IsPositive() ? 1 : -1);
            return false;
        }
    }

    // Opt-in, like the rotary patch: only a lever carrying NotchScroll uses
    // this. The injector's three positions must not skip Prime on overshoot.
    [HarmonyPatch(typeof(LeverBase), nameof(LeverBase.Scroll))]
    internal static class LeverNotchScrollPatch
    {
        private static bool Prefix(LeverBase __instance, ScrollAction action)
        {
            var gate = __instance.GetComponent<NotchScroll>();
            if (gate == null) return true;
            // An unstepped native lever returns to zero on release. A
            // continuous valve with wheel presets must retain its setting.
            if (action == ScrollAction.Release) return !gate.ContinuousValue;
            var spec = __instance.GetComponent<Lever>();
            if (!gate.Take() || spec == null || spec.notches < 2 || __instance.IsGrabbed() || !__instance.InteractionAllowed) return false;
            gate.Turn(__instance, spec, action.IsPositive() ? 1 : -1);
            return false;
        }
    }

 internal sealed class NotchScroll : MonoBehaviour
    {
        // The wheel still this long, in s, ends a click.
        private const float Still = 0.15f;

        // Turned at this rate on average, in degrees a second.
        internal float DegreesPerSecond = 360f;
        internal bool ContinuousValue;
        internal bool DirectionalPresets;

        private static readonly AccessTools.FieldRef<RotaryBase, Quaternion> InitialRotation =
            AccessTools.FieldRefAccess<RotaryBase, Quaternion>("initialLocalRotation");
        private static readonly AccessTools.FieldRef<LeverBase, Quaternion> LeverInitialRotation =
            AccessTools.FieldRefAccess<LeverBase, Quaternion>("initialLocalRotation");

        private float last = float.MinValue;
        private ControlImplBase control;
        private Quaternion initialRotation;
        private HingeJoint joint;
        private Rigidbody body;
        private bool wasKinematic;
        private bool turning;
        private float goalValue;
        private float fromAngle;
        private float goalAngle;
        private float angle;
        private float started;
        private float duration;
        private bool invert;

        public bool Take()
        {
            var now = Time.unscaledTime;
            var take = now - last >= Still;
            last = now;
            return take;
        }

        public void Turn(RotaryBase rotary, Rotary spec, int direction)
            => Turn(rotary, spec.notches, spec.invertDirection, InitialRotation(rotary), direction);

        public void Turn(LeverBase lever, Lever spec, int direction)
            => Turn(lever, spec.notches, spec.invertDirection, LeverInitialRotation(lever), direction);

        private void Turn(ControlImplBase native, int notches, bool inverted, Quaternion rest, int direction)
        {
            control = native; initialRotation = rest;
            if (joint == null)
                joint = GetComponent<HingeJoint>();
            if (body == null)
                body = GetComponent<Rigidbody>();
            var from = turning ? goalValue : native.Value;
            var value = DirectionalPresets ? NextPreset(from, notches, direction) : NextValue(from, notches, direction);
            if (joint == null || body == null)
            {
                native.SetValue(value);
                return;
            }
            invert = inverted;
            fromAngle = turning ? angle : AngleOf(native.Value);
            goalValue = value;
            goalAngle = AngleOf(value);
            started = Time.time;
            duration = Mathf.Abs(goalAngle - fromAngle) / DegreesPerSecond;
            if (!turning)
            {
                wasKinematic = body.isKinematic;
                body.isKinematic = true;
            }
            turning = true;
        }

        internal static float NextValue(float from, int notches, int direction)
        {
            var step = 1f / (notches - 1);
            return Mathf.Clamp01((Mathf.Round(from / step) + direction) * step);
        }

        internal static float NextPreset(float from, int notches, int direction)
        {
            var scaled = from * (notches - 1);
            var next = direction > 0 ? Mathf.Floor(scaled + .00001f) + 1f : Mathf.Ceil(scaled - .00001f) - 1f;
            return Mathf.Clamp01(next / (notches - 1));
        }

        private float AngleOf(float value) => Mathf.Lerp(joint.limits.min, joint.limits.max, invert ? 1f - value : value);

        private void Update()
        {
            if (!turning)
                return;
            if (control == null || joint == null || body == null || control.IsGrabbed())
            {
                Finish(false);
                return;
            }
            var done = duration <= 0f ? 1f : Mathf.Clamp01((Time.time - started) / duration);
            angle = Mathf.Lerp(fromAngle, goalAngle, Mathf.SmoothStep(0f, 1f, done));
            transform.localRotation = initialRotation * Quaternion.AngleAxis(angle, joint.axis);
            if (ContinuousValue)
            {
                var value = Mathf.InverseLerp(joint.limits.min, joint.limits.max, angle);
                control.SetValue(invert ? 1f - value : value);
            }
            if (done >= 1f)
                Finish(true);
        }

        private void Finish(bool arrived)
        {
            turning = false;
            if (body != null)
                body.isKinematic = wasKinematic;
            if (arrived && control != null)
                control.SetValue(goalValue);
        }

        private void OnDisable()
        {
            if (turning)
                Finish(false);
        }
    }
}
