using System.Linq;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Rays against a loco's own colliders from points on its cab. A moving
    /// car's transform is interpolated behind its physics body, where its
    /// colliders are, so rays run in the body's pose: from the point's place
    /// in the car's frame, carried to where the body is.
    /// </summary>
    internal static class CabRay
    {
        /// <summary>
        /// The distance to the first surface of <paramref name="car"/> along a
        /// ray, skipping triggers and interactable controls.
        /// </summary>
        public static bool TryHit(TrainCar car, Vector3 from, Vector3 direction, float reach, out float distance, out Collider collider)
        {
            distance = 0f;
            collider = null;
            var body = car.rb;
            var bodyPosition = body != null ? body.position : car.transform.position;
            var bodyRotation = body != null ? body.rotation : car.transform.rotation;
            var start = bodyPosition + bodyRotation * car.transform.InverseTransformPoint(from);
            var way = bodyRotation * car.transform.InverseTransformDirection(direction.normalized);
            var interactable = LayerMask.NameToLayer("Interactable");
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(start, way, reach, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (hit.collider.gameObject.layer == interactable || TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                distance = hit.distance;
                collider = hit.collider;
                return true;
            }
            return false;
        }
    }
}
