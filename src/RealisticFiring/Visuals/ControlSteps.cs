using DV.CabControls;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Gives a cab's own stepped lever or valve, already set up in a loaded
    /// cab, a different number of notches: its spec's (which turns the notch
    /// into a value), its stepped joint's and the joint's notch angle (worked
    /// out once when it started), and one notch a click of the mouse wheel.
    /// </summary>
    internal static class ControlSteps
    {
        /// <summary>Gives the control on <paramref name="control"/> <paramref name="notches"/> notches; false when it has no stepped joint.</summary>
        public static bool Restep(Transform control, int notches)
        {
            var impl = control != null ? control.GetComponent<ControlImplBase>() : null;
            if (impl == null)
                return false;
            var traverse = Traverse.Create(impl);
            var spec = traverse.Field("spec").GetValue() ?? traverse.Property("Spec").GetValue();
            var joint = traverse.Field("steppedJoint").GetValue<SteppedJoint>();
            if (spec == null || joint == null)
                return false;

            var specTraverse = Traverse.Create(spec);
            specTraverse.Field("notches").SetValue(notches);
            var scroll = specTraverse.Field("scrollWheelHoverScroll");
            var oneClick = scroll.GetValue<float>() < 0f ? -1f : 1f;
            scroll.SetValue(oneClick);
            // A rotary keeps its own copy of the scroll, and may spin under a
            // torque per click rather than turn a notch.
            var ownScroll = traverse.Field("scrollWheelHoverScroll");
            if (ownScroll.FieldExists())
                ownScroll.SetValue(oneClick);
            var springRotation = specTraverse.Field("scrollWheelUseSpringRotation");
            if (springRotation.FieldExists())
                springRotation.SetValue(true);

            joint.notches = notches;
            var hinge = joint.GetComponent<HingeJoint>();
            if (hinge != null)
            {
                var range = hinge.useLimits ? hinge.limits.max - hinge.limits.min : 360f;
                Traverse.Create(joint).Property("SingleNotchAngle").SetValue(range / (notches + (hinge.useLimits ? -1 : 0)));
            }
            return true;
        }
    }
}
