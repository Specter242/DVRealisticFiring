using System.Collections;
using DV.CabControls;
using DV.Interaction;
using HarmonyLib;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// Reaching into a crate or rag tin with an empty hand takes one piece
    /// out into the hand, the crate staying where it stands; taking hold of
    /// its sides or rim carries the crate. The contents have their own
    /// collider over the open top (<see cref="StartingFuelBody.ContentsName"/>),
    /// so the game's own pointer decides which was meant. A client's piece
    /// comes out on top of the crate for it to pick up.
    /// </summary>
    [HarmonyPatch(typeof(GrabberInteractionHandlerDV), nameof(GrabberInteractionHandlerDV.IdleStartInteraction))]
    internal static class StartingFuelTake
    {
        private static void Postfix(GrabberInteractionHandlerDV __instance, ref Grabber.Trigger? __result)
        {
            if (__result != Grabber.Trigger.Hold || !Main.ReplacementEnabled) return;
            var grabber = __instance.GetComponent<Grabber>();
            var hit = grabber != null ? grabber.Raycaster?.CurrentlyHit.collider : null;
            if (hit == null || hit.name != StartingFuelBody.ContentsName) return;
            var container = hit.GetComponentInParent<StartingFuelItem>();
            if (container == null || !FireStartingItems.IsContainer(container.Kind) || container.Held || container.Count <= 0) return;
            // Not the crate: a piece of what is in it.
            __result = null;
            if (Net.IsClient)
            {
                container.ApplyClientTake();
                return;
            }
            var piece = container.TakeOut(hit.bounds.center + Vector3.up * 0.05f);
            if (piece != null) __instance.StartCoroutine(Hold(__instance, piece));
        }

        // The new item wakes over a frame before it can be held.
        private static IEnumerator Hold(GrabberInteractionHandlerDV handler, StartingFuelItem piece)
        {
            yield return null;
            if (piece == null || piece.Consumed) yield break;
            var grab = piece.GetComponent<AGrabHandler>();
            if (grab != null) handler.RequestForceHold(grab);
        }
    }
}
