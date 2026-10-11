using DV.CabControls;
using HarmonyLib;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Lets a stepped rotary that the mouse wheel turns by its spring (a
    /// notch a click, as the mod sets its oil valves and the 2901's firing
    /// valve) reach the next notch. Its stepped joint pulls the spring back
    /// to the notch nearest the wheel whenever the wheel moves, so a click's
    /// target was undone before the wheel got halfway and the wheel hardly
    /// moved. A lever lets go of that pull while it is scrolled and takes it
    /// up again when the scrolling stops (0.3 s after the last click, or
    /// when the pointer leaves it); a rotary now does the same.
    /// </summary>
    [HarmonyPatch(typeof(RotaryBase))]
    internal static class RotaryScrollPatch
    {
        private static readonly AccessTools.FieldRef<RotaryBase, SteppedJoint> Joint =
            AccessTools.FieldRefAccess<RotaryBase, SteppedJoint>("steppedJoint");

        [HarmonyPatch("ScrollWheelRotate")]
        [HarmonyPrefix]
        private static void BeforeScrollWheelRotate(RotaryBase __instance)
        {
            var joint = Joint(__instance);
            if (joint != null)
                joint.isSpringActive = false;
        }

        [HarmonyPatch(nameof(RotaryBase.Scroll))]
        [HarmonyPostfix]
        private static void AfterScroll(RotaryBase __instance, ScrollAction action)
        {
            var joint = Joint(__instance);
            if (action == ScrollAction.Release && joint != null)
                joint.isSpringActive = true;
        }
    }
}
