using DV.CabControls.Spec;
using DV.Interaction;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>Retains the stock grip data and initializes runtime-created controls.</summary>
    internal static class FiringTabletSetup
    {
        internal static void RemoveStockModel(Transform item)
        {
            // Inventory and distance-grab align through these stock hand
            // anchors, even with precision grabbing enabled. Removing them
            // made those paths fall back to the item's root orientation.
            for (var i = item.childCount - 1; i >= 0; i--)
            {
                var child = item.GetChild(i);
                if (child.name != "[anchor]" && child.name != "[left anchor]" && child.name != "[right anchor]")
                    Object.DestroyImmediate(child.gameObject);
            }
        }

        internal static void ConfigurePageButton(Button spec, GameObject collider)
        {
            // Stock VR controls read this during Awake before completing
            // their touch setup; runtime-added specs do not deserialize it.
            spec.handPosesOverride = new InteractionHandPoses();
            spec.colliderGameObjects = new[] { collider };
            spec.createRigidbody = false;
            spec.useJoints = false;
        }
    }
}
