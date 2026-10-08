using System.Collections.Generic;
using UnityEngine;

namespace RealisticFiring
{
    /// <summary>
    /// Measures cab objects in a car's local frame (metres, +Z toward the
    /// car's front), which stays fixed as the car moves and turns.
    /// </summary>
    internal static class CarSpace
    {
        /// <summary>Axis-aligned bounds of <paramref name="collider"/> in <paramref name="car"/>'s frame.</summary>
        public static Bounds LocalBounds(Transform car, Collider collider)
        {
            switch (collider)
            {
                case BoxCollider box:
                    return Enclose(car, box.transform, new Bounds(box.center, box.size));
                case SphereCollider sphere:
                    return Enclose(car, sphere.transform, new Bounds(sphere.center, Vector3.one * (2f * sphere.radius)));
                case MeshCollider mesh when mesh.sharedMesh != null:
                    return Enclose(car, mesh.transform, mesh.sharedMesh.bounds);
                default:
                    return EncloseWorld(car, collider.bounds);
            }
        }

        /// <summary>Axis-aligned bounds of <paramref name="renderer"/> in <paramref name="car"/>'s frame.</summary>
        public static Bounds LocalBounds(Transform car, Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null
                ? Enclose(car, renderer.transform, filter.sharedMesh.bounds)
                : EncloseWorld(car, renderer.bounds);
        }

        /// <summary>The transform path from <paramref name="root"/> down to <paramref name="target"/>.</summary>
        public static string PathFrom(Transform root, Transform target)
        {
            var names = new List<string>();
            for (var t = target; t != null && t != root; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // Corners of local bounds, carried through the object's transform into the car's frame.
        private static Bounds Enclose(Transform car, Transform owner, Bounds local)
        {
            var result = new Bounds(car.InverseTransformPoint(owner.TransformPoint(local.center)), Vector3.zero);
            foreach (var corner in Corners(local))
                result.Encapsulate(car.InverseTransformPoint(owner.TransformPoint(corner)));
            return result;
        }

        private static Bounds EncloseWorld(Transform car, Bounds world)
        {
            var result = new Bounds(car.InverseTransformPoint(world.center), Vector3.zero);
            foreach (var corner in Corners(world))
                result.Encapsulate(car.InverseTransformPoint(corner));
            return result;
        }

        private static IEnumerable<Vector3> Corners(Bounds b)
        {
            for (var i = 0; i < 8; i++)
            {
                yield return new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
            }
        }
    }
}
