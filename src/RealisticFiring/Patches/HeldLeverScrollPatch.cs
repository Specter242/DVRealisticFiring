using DV.CabControls;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// A lever without notches that stays where it is left (the S282 oil
    /// burner's firing valve and peephole cover, <see cref="Visuals.S282OilCab.HoldsWhereLeft"/>):
    /// when a scroll of the mouse wheel or its keys ends, the game sets such a
    /// lever's spring back to its middle; this keeps it where the scroll put it.
    /// </summary>
    [HarmonyPatch(typeof(LeverBase), nameof(LeverBase.Scroll))]
    internal static class HeldLeverScrollPatch
    {
        private static void Prefix(LeverBase __instance, ScrollAction action, out float? __state)
        {
            __state = null;
            if (action != ScrollAction.Release || __instance == null || __instance.GetComponent<Visuals.S282OilCab.HoldsWhereLeft>() == null)
                return;
            var joint = __instance.GetComponent<HingeJoint>();
            if (joint != null)
                __state = joint.useLimits ? Mathf.Clamp(joint.spring.targetPosition, joint.limits.min, joint.limits.max) : joint.spring.targetPosition;
        }

        private static void Postfix(LeverBase __instance, float? __state)
        {
            if (__state == null || __instance == null)
                return;
            var joint = __instance.GetComponent<HingeJoint>();
            if (joint == null)
                return;
            var spring = joint.spring;
            spring.targetPosition = __state.Value;
            joint.spring = spring;
        }
    }
}
