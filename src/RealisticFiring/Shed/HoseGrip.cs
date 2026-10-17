using DV.Interaction;
using UnityEngine;

namespace RealisticFiring.Shed
{
 internal static class HoseGrip
    {
        private const string GripName = "RealisticFiring hose grip";

        // The engine's colliders are looked for this far out along the hose;
        // the grip reaches this far past the outermost, and at least and at
        // most this far from the coupling's mouth, m.
        private const float Search = 3f;
        private const float Past = 0.15f;
        private const float Least = 0.25f;
        private const float Most = 1.5f;

        // Its radius on the blower line's coupling, m.
        private const float Radius = 0.03f;

        /// <summary>Gives <paramref name="plug"/>, just put into a connection on <paramref name="car"/>, its grip out past the engine.</summary>
        public static void Reach(PluggableObject plug, TrainCar car)
        {
            var handler = plug != null ? plug.GetComponent<AGrabHandler>() : null;
            if (handler == null || car == null)
                return;
            var root = plug.transform;
            var axis = root.up;
            var outermost = 0f;
            foreach (var hit in Physics.RaycastAll(root.position + axis * Search, -axis, Search, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.transform.IsChildOf(root) || TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                outermost = Mathf.Max(outermost, Search - hit.distance);
            }
            var reach = Mathf.Clamp(outermost + Past, Least, Most);
            var scale = (HoseKind.ByTag(plug.connectionTag) ?? HoseKind.BlowerLine).Scale;
            var grip = Grip(root);
            grip.direction = 1;
            grip.radius = Radius * scale;
            grip.height = reach;
            grip.center = new Vector3(0f, 0.5f * reach, 0f);
            grip.enabled = true;
            handler.interactionColliders.Add(grip);
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car.ID}: {plug.connectionTag} plug in, its grip {reach:F2} m out along the hose (the engine reaches {outermost:F2} m out there)");
        }

        /// <summary>Takes the grip away from <paramref name="plug"/>, out of its connection.</summary>
        public static void Let(PluggableObject plug)
        {
            var grip = plug != null ? plug.transform.Find(GripName)?.GetComponent<CapsuleCollider>() : null;
            if (grip == null)
                return;
            grip.enabled = false;
            plug.GetComponent<AGrabHandler>()?.interactionColliders.Remove(grip);
        }

        private static CapsuleCollider Grip(Transform plug)
        {
            var found = plug.Find(GripName);
            if (found != null)
                return found.GetComponent<CapsuleCollider>();
            var colliders = plug.Find("Colliders");
            var go = new GameObject(GripName);
            go.layer = colliders != null ? colliders.gameObject.layer : plug.gameObject.layer;
            go.transform.SetParent(plug, false);
            var grip = go.AddComponent<CapsuleCollider>();
            grip.isTrigger = true;
            return grip;
        }
    }
}
