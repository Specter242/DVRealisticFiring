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

 internal sealed class NotchScroll : MonoBehaviour
    {
        // The wheel still this long, in s, ends a click.
        private const float Still = 0.15f;

        // Turned at this rate on average, in degrees a second.
        private const float Rate = 360f;

        private static readonly AccessTools.FieldRef<RotaryBase, Quaternion> InitialRotation =
            AccessTools.FieldRefAccess<RotaryBase, Quaternion>("initialLocalRotation");

        private float last = float.MinValue;
        private RotaryBase control;
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
        {
            control = rotary;
            if (joint == null)
                joint = GetComponent<HingeJoint>();
            if (body == null)
                body = GetComponent<Rigidbody>();
            var step = 1f / (spec.notches - 1);
            var from = turning ? goalValue : rotary.Value;
            var value = Mathf.Clamp01((Mathf.Round(from / step) + direction) * step);
            if (joint == null || body == null)
            {
                rotary.SetValue(value);
                return;
            }
            invert = spec.invertDirection;
            fromAngle = turning ? angle : AngleOf(rotary.Value);
            goalValue = value;
            goalAngle = AngleOf(value);
            started = Time.time;
            duration = Mathf.Abs(goalAngle - fromAngle) / Rate;
            if (!turning)
            {
                wasKinematic = body.isKinematic;
                body.isKinematic = true;
            }
            turning = true;
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
            transform.localRotation = InitialRotation(control) * Quaternion.AngleAxis(angle, joint.axis);
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
