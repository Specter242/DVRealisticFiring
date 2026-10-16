using DV.CabControls;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Patches
{
 [HarmonyPatch(typeof(Fire), "OnTriggerEnter")]
    internal static class ThrownCoalPatch
    {
        private static void Prefix(Fire __instance, Collider other)
        {
            if (other == null || other.GetComponent<Coal>() == null)
                return;
            var item = other.GetComponent<ItemBase>();
            if (item != null && item.IsGrabbed())
                return;
            var car = TrainCar.Resolve(__instance.gameObject);
            if (!FireboxRegistry.TryGet(car, out var firebox))
                return;
            if (!ThrownCoal.TryLanding(firebox, car, other.attachedRigidbody, out var across, out var along))
                return;
            var zone = DoorAim.ZoneFor(across, along);
            firebox.NoteAim(new DoorAimSample(zone, across, along, false, Time.time));
            // For trying it in a headset: where each throw was taken to land.
            if (Time.time - logged >= LogEvery)
            {
                logged = Time.time;
                Main.Mod?.Logger.Log($"{car.ID}: thrown coal lands on the {GrateLayout.Describe(zone)} (across {across:F2}, along {along:F2})");
            }
        }

        private const float LogEvery = 0.5f;
        private static float logged = float.NegativeInfinity;
    }

    /// <summary>Where a lump in flight lands on a firebox's drawn bed.</summary>
    internal static class ThrownCoal
    {
        // The flight is followed in steps this long, s, for at most this long.
        private const float Step = 0.01f;
        private const float LongestFlight = 1.5f;

        /// <summary>
        /// Where <paramref name="lump"/>, flying as it is now against the cab,
        /// lands on <paramref name="firebox"/>'s drawn coal: across (0 left, 1
        /// right) and along (0 the door, 1 the front), a wall or the tubeplate
        /// it would hit taking its edge. False without a drawn bed or a lump.
        /// </summary>
        public static bool TryLanding(RealisticFirebox firebox, TrainCar car, Rigidbody lump, out float across, out float along)
        {
            across = along = 0f;
            if (lump == null || car == null || !ZonedCoalBed.TryGet(firebox, out var bed))
                return false;
            var carBody = car.GetComponent<Rigidbody>();
            var position = lump.position;
            var velocity = lump.velocity - (carBody != null ? carBody.GetPointVelocity(position) : Vector3.zero);
            var gravity = Physics.gravity;
            var box = bed.BoxPoint(position);
            for (var t = 0f; t < LongestFlight; t += Step)
            {
                across = Mathf.Clamp01(box.x);
                along = Mathf.Clamp01(box.y);
                // Past the door, down on the coal, or at the tubeplate.
                if (box.y >= 1f || box.y > 0f && box.z <= bed.SurfaceHeight(across, along))
                    return true;
                position += velocity * Step;
                velocity += gravity * Step;
                box = bed.BoxPoint(position);
            }
            across = Mathf.Clamp01(box.x);
            along = Mathf.Clamp01(box.y);
            return true;
        }
    }
}
