using DV.CabControls;
using UnityEngine;

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
    /// the target follows it. The S282's injector steam lever and the 8F's
    /// water regulators are the mod's unnotched levers.
    /// </summary>
    internal sealed class SpringFollowsHand : MonoBehaviour
    {
        private ControlImplBase control;
        private HingeJoint joint;
        private HingeJointAngleFix angle;
        private bool inVr;

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
            if (!inVr || !control.IsGrabbed())
                return;
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

        /// <summary>Whether a control of <paramref name="type"/> is the game's VR one (in DV.CabControls.VRTK).</summary>
        internal static bool IsVrControl(System.Type type) =>
            type != null && type.Namespace != null && type.Namespace.EndsWith(".VRTK", System.StringComparison.Ordinal);

        /// <summary>The spring's target for a lever held at <paramref name="at"/> degrees: there, within its limits.</summary>
        internal static float Held(float at, bool limited, float min, float max) =>
            limited ? Mathf.Clamp(at, Mathf.Min(min, max), Mathf.Max(min, max)) : at;
    }
}
