using DV.Interaction;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Marks a cab lever whose drag should be finer than the game makes it:
    /// each movement of the mouse (screen-space or in the world) turns it by
    /// this share of what it otherwise would.
    /// </summary>
    internal sealed class LeverFeel : MonoBehaviour
    {
        public System.Func<float> Sensitivity { get; set; } = () => 1f;
    }

    /// <summary>
    /// Scales the drag of levers marked with <see cref="LeverFeel"/>: the
    /// fire-dump lever, which also rocks the grate, moved full travel on a
    /// quick flick, so a short rock (a wiggle) was hard to give it.
    /// </summary>
    [HarmonyPatch(typeof(GrabHandlerHingeJoint))]
    internal static class LeverFeelPatch
    {
        private static readonly AccessTools.FieldRef<GrabHandlerHingeJoint, float> Target =
            AccessTools.FieldRefAccess<GrabHandlerHingeJoint, float>("newTargetPosition");
        private static readonly AccessTools.FieldRef<GrabHandlerHingeJoint, float> Start =
            AccessTools.FieldRefAccess<GrabHandlerHingeJoint, float>("startTargetPosition");

        // Screen-space dragging adds to the target each frame.
        [HarmonyPatch(nameof(GrabHandlerHingeJoint.FeedValue))]
        [HarmonyPrefix]
        private static void BeforeFeedValue(GrabHandlerHingeJoint __instance, out float __state) => __state = Target(__instance);

        [HarmonyPatch(nameof(GrabHandlerHingeJoint.FeedValue))]
        [HarmonyPostfix]
        private static void AfterFeedValue(GrabHandlerHingeJoint __instance, float __state) => Scale(__instance, __state);

        // Dragging in the world sets the target from where the grab started.
        [HarmonyPatch(nameof(GrabHandlerHingeJoint.FeedPosition))]
        [HarmonyPostfix]
        private static void AfterFeedPosition(GrabHandlerHingeJoint __instance) => Scale(__instance, Start(__instance));

        private static void Scale(GrabHandlerHingeJoint handler, float from)
        {
            var feel = handler.GetComponent<LeverFeel>();
            if (feel == null)
                return;
            var sensitivity = Mathf.Clamp(feel.Sensitivity(), 0.05f, 1f);
            Target(handler) = from + Mathf.DeltaAngle(from, Target(handler)) * sensitivity;
        }
    }
}
