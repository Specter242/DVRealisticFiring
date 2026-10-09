using System.Collections.Generic;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// The fire iron's shape, in the held item's frame (the stock shovel's:
    /// +z ahead, the hands at z ≈ 0.09): a forged round bar of an inch, a
    /// flattened, slightly turned-down chisel blade at the working end, and a
    /// ring handle at the other. Texture U runs from the ring's far side (0)
    /// to the blade's tip (1), V around the bar.
    /// </summary>
    internal static class FireIronMesh
    {
        public const float BarRadius = 0.0125f;
        public const float BarStart = -0.495f;
        public const float BladeStart = 0.95f;
        public const float Tip = 1.12f;
        public const float RingCenter = -0.555f;
        public const float RingRadius = 0.06f;
        public const float RingThickness = 0.011f;

        private const int Sides = 12;
        private const float BladeHalfWidth = 0.034f;
        private const float BladeHalfThickness = 0.003f;
        private const float BladeDroop = 0.025f;

        private static float U(float z) => Mathf.InverseLerp(RingCenter - RingRadius, Tip, z);

        public static Mesh Create()
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // The bar and blade: one loft of rings along z, round along the bar,
            // flattening and widening into the blade, which droops at its tip.
            var stations = new List<float> { BarStart, BladeStart - 0.04f };
            for (var i = 0; i <= 6; i++)
                stations.Add(Mathf.Lerp(BladeStart, Tip, i / 6f));
            foreach (var z in stations)
            {
                var t = Mathf.Clamp01(Mathf.InverseLerp(BladeStart - 0.04f, Tip, z));
                var halfWidth = Mathf.Lerp(BarRadius, BladeHalfWidth, Mathf.SmoothStep(0f, 1f, t * 1.4f));
                var halfThickness = Mathf.Lerp(BarRadius, BladeHalfThickness, Mathf.SmoothStep(0f, 1f, t));
                var droop = -BladeDroop * t * t;
                Ring(vertices, uvs, new Vector3(0f, droop, z), halfWidth, halfThickness, U(z));
            }
            for (var s = 0; s < stations.Count - 1; s++)
                Stitch(triangles, s * (Sides + 1), (s + 1) * (Sides + 1));
            Cap(vertices, uvs, triangles, (stations.Count - 1) * (Sides + 1), new Vector3(0f, -BladeDroop, Tip + 0.002f), true);
            Cap(vertices, uvs, triangles, 0, new Vector3(0f, 0f, BarStart), false);

            // The ring handle, upright in the plane of the bar, joining it at its front.
            var ringStart = vertices.Count;
            const int Around = 24;
            for (var a = 0; a <= Around; a++)
            {
                var angle = a * 2f * Mathf.PI / Around;
                var center = new Vector3(0f, RingRadius * Mathf.Sin(angle), RingCenter + RingRadius * Mathf.Cos(angle));
                var outward = new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle));
                for (var s = 0; s <= Sides; s++)
                {
                    var phi = s * 2f * Mathf.PI / Sides;
                    var normal = outward * Mathf.Cos(phi) + Vector3.right * Mathf.Sin(phi);
                    vertices.Add(center + normal * RingThickness);
                    uvs.Add(new Vector2(0.02f + 0.08f * a / Around, (float)s / Sides));
                }
            }
            for (var a = 0; a < Around; a++)
                Stitch(triangles, ringStart + a * (Sides + 1), ringStart + (a + 1) * (Sides + 1));

            var mesh = new Mesh { name = "fire iron" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A ring of vertices around z: an ellipse, its seam vertex repeated
        // for the texture.
        private static void Ring(List<Vector3> vertices, List<Vector2> uvs, Vector3 center, float halfWidth, float halfThickness, float u)
        {
            for (var s = 0; s <= Sides; s++)
            {
                var phi = s * 2f * Mathf.PI / Sides;
                vertices.Add(center + new Vector3(Mathf.Cos(phi) * halfWidth, Mathf.Sin(phi) * halfThickness, 0f));
                uvs.Add(new Vector2(u, (float)s / Sides));
            }
        }

        // Two rings of Sides + 1 vertices joined by quads, facing outward
        // (Unity's front face is cross(b − a, c − a)).
        private static void Stitch(List<int> triangles, int a, int b)
        {
            for (var s = 0; s < Sides; s++)
            {
                triangles.Add(a + s);
                triangles.Add(a + s + 1);
                triangles.Add(b + s);
                triangles.Add(a + s + 1);
                triangles.Add(b + s + 1);
                triangles.Add(b + s);
            }
        }

        // Closes a ring with a fan to a center point.
        private static void Cap(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles, int ring, Vector3 center, bool facingForward)
        {
            var middle = vertices.Count;
            vertices.Add(center);
            uvs.Add(new Vector2(uvs[ring].x, 0.5f));
            for (var s = 0; s < Sides; s++)
            {
                triangles.Add(middle);
                triangles.Add(facingForward ? ring + s : ring + s + 1);
                triangles.Add(facingForward ? ring + s + 1 : ring + s);
            }
        }
    }
}
