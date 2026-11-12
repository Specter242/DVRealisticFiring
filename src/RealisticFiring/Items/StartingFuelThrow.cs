using DV.Interaction;
using HarmonyLib;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The local player's non-VR throw (the stock Drop action) of wood or a
    /// rag goes toward the point under the view's centre, on the arc that
    /// reaches it (<see cref="ThrowArc"/>), from the moving engine's frame;
    /// through an open firehole, at the fire just inside it.
    /// Every other item, VR, and a throw replayed for another player
    /// (Multiplayer's NetworkedItem) keep the stock throw.
    /// </summary>
    [HarmonyPatch(typeof(GrabberInputHandler), "Update")]
    internal static class StartingFuelThrowScope
    {
        // Set only while the local, non-VR input handler runs: its Drop is the player's own throw.
        internal static bool Local;
        private static void Prefix() => Local = true;
        private static void Finalizer() => Local = false;
    }

    [HarmonyPatch(typeof(GrabHandlerItem), nameof(GrabHandlerItem.Throw))]
    internal static class StartingFuelThrow
    {
        private static bool Prefix(GrabHandlerItem __instance)
        {
            if (!StartingFuelThrowScope.Local || VRManager.IsVREnabled()) return true;
            var fuel = __instance.GetComponent<StartingFuelItem>();
            if (fuel == null || fuel.Kind != StartingFuelKind.Wood && fuel.Kind != StartingFuelKind.Rag) return true;
            var body = __instance.GetComponent<Rigidbody>();
            var camera = PlayerManager.ActiveCamera;
            if (body == null || camera == null) return true;
            var view = camera.transform;
            var target = Physics.Raycast(view.position, view.forward, out var hit, ThrowArc.Reach, Environment(), QueryTriggerInteraction.Ignore)
                ? hit.point : view.position + view.forward * ThrowArc.Reach;
            // Through an open doorway, at the fire just inside it.
            target = FuelFireboxIntake.AimThrough(view.position, target);
            var from = body.position;
            var launch = ThrowArc.Launch(Point(from), Point(target), Physics.gravity.y);
            // From the ground under the thrower: a moving engine's footplate carries the throw.
            var car = PlayerManager.Car;
            var ground = car != null && car.rb != null ? car.rb.GetPointVelocity(from) : Vector3.zero;
            ThrowLaunch.Start(body, ground + new Vector3(launch.X, launch.Y, launch.Z));
            return false;
        }

        private static CarPoint Point(Vector3 v) => new CarPoint(v.x, v.y, v.z);

        private static int environment;
        // What a loose item collides with, less items and the player.
        private static int Environment()
        {
            if (environment != 0) return environment;
            var item = LayerMask.NameToLayer("World_Item");
            var mask = 0;
            for (var layer = 0; layer < 32; layer++)
                if (item < 0 || !Physics.GetIgnoreLayerCollision(item, layer)) mask |= 1 << layer;
            return environment = mask & ~LayerMask.GetMask("World_Item", "Grabbed_Item", "Inventory", "Player");
        }
    }

    /// <summary>Sets the launch at the first physics step the dropped item is free to move.</summary>
    internal sealed class ThrowLaunch : MonoBehaviour
    {
        private Rigidbody body;
        private Vector3 velocity;
        private int steps;

        internal static void Start(Rigidbody body, Vector3 velocity)
        {
            var launch = body.GetComponent<ThrowLaunch>() ?? body.gameObject.AddComponent<ThrowLaunch>();
            launch.body = body;
            launch.velocity = velocity;
            launch.steps = 0;
        }

        private void FixedUpdate()
        {
            if (body == null || ++steps > 10) { Destroy(this); return; }
            if (body.isKinematic) return;
            body.velocity = velocity;
            body.angularVelocity = Random.insideUnitSphere * 2f;
            Destroy(this);
        }
    }
}
