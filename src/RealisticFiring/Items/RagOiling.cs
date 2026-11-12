using DV.CabControls;
using DV.Interaction;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Items
{
    // The stock oiler has no reservoir or depletion; keep that same supply model.
    [HarmonyPatch(typeof(ManualOilerUseNonVR), "Start")]
    internal static class InstallRagOiling
    {
        private static void Postfix(ManualOilerUseNonVR __instance)
        {
            if (!VRManager.IsVREnabled() && __instance.GetComponent<RagOiling>() == null)
                __instance.gameObject.AddComponent<RagOiling>();
        }
    }
    internal sealed class RagOiling : MonoBehaviour, IItemUse
    {
        private static StartingFuelItem Rag(ItemUseTarget target)
        {
            var rag = target != null ? target.GetComponentInParent<StartingFuelItem>() : null;
            return rag != null && rag.Kind == StartingFuelKind.Rag ? rag : null;
        }
        public bool IsHoverCompatible(ItemUseTarget target) => Rag(target) != null;
        public bool IsUseCompatible(ItemUseTarget target)
        {
            var rag = Rag(target);
            return rag != null && !rag.Rag.Lit && rag.Rag.Oil < RagFuel.OilCapacity;
        }
        public bool HandleHover(ItemUseTarget target)
        {
            var rag = Rag(target);
            if (rag == null) return false;
            SingletonBehaviour<InteractionTextControllerNonVr>.Instance?.DisplayText(rag.Rag.Lit
                ? "The rag is already burning" : rag.Rag.Oil >= RagFuel.OilCapacity
                ? "Oiled rag: use the lighter" : "Use oil can to soak the lighting rag");
            return true;
        }
        public bool HandleUse(ItemUseTarget target)
        {
            if (!IsUseCompatible(target)) return false;
            Rag(target).Oil();
            FireController.Announce("Use the lighter once the rag is oiled.");
            return true;
        }
    }
    [HarmonyPatch(typeof(ManualOilerUseVR), "OnTriggerStay")]
    internal static class RagOilingVr
    {
        private static void Postfix(ManualOilerUseVR __instance, Collider other)
        {
            var rag = other.GetComponentInParent<StartingFuelItem>();
            var item = __instance.GetComponentInParent<ItemBase>();
            if (rag != null && rag.Kind == StartingFuelKind.Rag && item != null && item.IsGrabbed())
                if (rag.Rag.Oil < RagFuel.OilCapacity) rag.Oil();
        }
    }
}
