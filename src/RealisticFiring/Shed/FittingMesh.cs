using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RealisticFiring.Shed
{
    /// <summary>The metals a shed fitting is made of.</summary>
    internal enum Metal
    {
        /// <summary>Malleable iron, black with the engine's paint and soot.</summary>
        Iron,

        /// <summary>Brass or bronze, worn bright where handled.</summary>
        Brass,

        /// <summary>Plain steel: clamps, bolts.</summary>
        Steel,

        /// <summary>The hose's own rubber, where a fitting carries a piece of it.</summary>
        Hose,
    }

    /// <summary>
    /// Builds a fitting's meshes from turned parts (a profile revolved about
    /// an axis, round or hexagonal), boxes, and half-round ends, each in its
    /// metal, and puts them on a transform as one mesh a metal. Sizes are in
    /// m, in the fitting's own frame.
    /// </summary>
    internal sealed class FittingMesh
    {
        private const int RoundSides = 20;

        // The grime texture repeats every this many m over a part's surface;
        // each part starts from its own place in it.
        private const float GrimeRepeat = 0.12f;

        private static Vector2 GrimeStart(Vector3 at) =>
            new Vector2(Mathf.Repeat(at.x * 7.31f + at.z * 3.17f, 1f), Mathf.Repeat(at.y * 5.13f + at.z * 2.71f, 1f));

        private sealed class Part
        {
            public readonly List<Vector3> Vertices = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int> Triangles = new List<int>();
        }

        private readonly Dictionary<Metal, Part> parts = new Dictionary<Metal, Part>();

        /// <summary>
        /// The hose's texture along it a metre, as the rope maps it (once
        /// round, this many times along): a piece of hose on a fitting is
        /// mapped so, to match it.
        /// </summary>
        public float HoseTexturePerMetre { get; set; } = 1f;

        private Part For(Metal metal)
        {
            if (!parts.TryGetValue(metal, out var part))
                parts[metal] = part = new Part();
            return part;
        }

        /// <summary>
        /// A part turned about the axis through <paramref name="at"/> along
        /// <paramref name="axis"/>: <paramref name="profile"/> gives (radius,
        /// height along the axis) round the part's section, counterclockwise
        /// with the radius to the right, so the outside runs up the axis and
        /// a bore runs back down it. Round, or with <paramref name="sides"/>
        /// flats (the radius to their corners), turned <paramref name="turn"/>
        /// degrees about the axis; or only from <paramref name="from"/> to
        /// <paramref name="to"/> degrees round it.
        /// </summary>
        public FittingMesh Turned(Metal metal, IList<Vector2> profile, Vector3 at, Vector3 axis, int sides = 0,
            float turn = 0f, float from = 0f, float to = 360f)
        {
            var part = For(metal);
            var faceted = sides > 0;
            var steps = faceted ? sides : Mathf.Max(3, Mathf.CeilToInt(RoundSides * (to - from) / 360f));
            var rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized) * Quaternion.AngleAxis(turn, Vector3.up);
            Vector3 Around(float radius, float height, float degrees)
            {
                var a = degrees * Mathf.Deg2Rad;
                return new Vector3(radius * Mathf.Cos(a), height, radius * Mathf.Sin(a));
            }
            var grime = GrimeStart(at);
            var run = 0f;
            Vector2 Uv(Vector2 p, float degrees, float down) => metal == Metal.Hose
                ? new Vector2(degrees / 360f, down * HoseTexturePerMetre)
                : grime + new Vector2(degrees * Mathf.Deg2Rad * Mathf.Max(p.x, 0.004f), down) / GrimeRepeat;
            for (var k = 0; k + 1 < profile.Count; k++)
            {
                var p0 = profile[k];
                var p1 = profile[k + 1];
                var along = p1 - p0;
                var run0 = run;
                run += along.magnitude;
                if (along.sqrMagnitude < 1e-12f)
                    continue;
                // Outward from the material: the profile's way turned clockwise.
                var normal = new Vector2(along.y, -along.x).normalized;
                for (var j = 0; j < steps; j++)
                {
                    var a0 = from + (to - from) * j / steps;
                    var a1 = from + (to - from) * (j + 1) / steps;
                    var mid = 0.5f * (a0 + a1);
                    var n0 = faceted ? Around(normal.x, normal.y, mid) : Around(normal.x, normal.y, a0);
                    var n1 = faceted ? n0 : Around(normal.x, normal.y, a1);
                    var start = part.Vertices.Count;
                    foreach (var (v, n, uv) in new[]
                             {
                                 (Around(p0.x, p0.y, a0), n0, Uv(p0, a0, run0)), (Around(p0.x, p0.y, a1), n1, Uv(p0, a1, run0)),
                                 (Around(p1.x, p1.y, a0), n0, Uv(p1, a0, run)), (Around(p1.x, p1.y, a1), n1, Uv(p1, a1, run)),
                             })
                    {
                        part.Vertices.Add(at + rotation * v);
                        part.Normals.Add(rotation * n);
                        part.Uvs.Add(uv);
                    }
                    // Unity's front faces wind clockwise seen from outside.
                    part.Triangles.AddRange(new[] { start + 2, start + 3, start + 1, start + 2, start + 1, start });
                }
            }
            return this;
        }

        /// <summary>A plain round bar or tube section from <paramref name="h0"/> to <paramref name="h1"/> along the axis.</summary>
        public FittingMesh Rod(Metal metal, float radius, float h0, float h1, Vector3 at, Vector3 axis) =>
            Turned(metal, new[] { new Vector2(0f, h0), new Vector2(radius, h0), new Vector2(radius, h1), new Vector2(0f, h1) }, at, axis);

        /// <summary>A hexagon <paramref name="acrossFlats"/> wide from <paramref name="h0"/> to <paramref name="h1"/>, its edges chamfered.</summary>
        public FittingMesh Hex(Metal metal, float acrossFlats, float h0, float h1, Vector3 at, Vector3 axis, float turn = 0f)
        {
            var corner = acrossFlats / Mathf.Sqrt(3f);
            var chamfer = Mathf.Min(0.15f * (h1 - h0), 0.12f * corner);
            return Turned(metal, new[]
            {
                new Vector2(0f, h0), new Vector2(corner - chamfer, h0), new Vector2(corner, h0 + chamfer),
                new Vector2(corner, h1 - chamfer), new Vector2(corner - chamfer, h1), new Vector2(0f, h1),
            }, at, axis, 6, turn);
        }

        /// <summary>A box of <paramref name="size"/> about <paramref name="center"/>, turned by <paramref name="rotation"/>.</summary>
        public FittingMesh Box(Metal metal, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var part = For(metal);
            var half = 0.5f * size;
            var grime = GrimeStart(center);
            foreach (var normal in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
            {
                var u = Mathf.Abs(normal.y) > 0.5f ? Vector3.right : Vector3.up;
                var v = Vector3.Cross(normal, u);
                var start = part.Vertices.Count;
                foreach (var (su, sv) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
                {
                    var local = Vector3.Scale(normal + su * u + sv * v, half);
                    part.Vertices.Add(center + rotation * local);
                    part.Normals.Add(rotation * normal);
                    part.Uvs.Add(grime + new Vector2(Vector3.Dot(local, u), Vector3.Dot(local, v)) / GrimeRepeat);
                }
                part.Triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            return this;
        }

        /// <summary>
        /// Puts the meshes on <paramref name="parent"/> under a child named
        /// <paramref name="name"/>, one renderer a metal on
        /// <paramref name="layer"/>. Returns the child.
        /// </summary>
        public Transform Build(Transform parent, string name, int layer, Material hose = null)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.gameObject.layer = layer;
            foreach (var pair in parts.Where(p => p.Value.Vertices.Count > 0))
            {
                var mesh = new Mesh { name = $"{name} ({pair.Key})" };
                mesh.SetVertices(pair.Value.Vertices);
                mesh.SetNormals(pair.Value.Normals);
                mesh.SetUVs(0, pair.Value.Uvs);
                // For a normal-mapped material, as the hose's is.
                mesh.RecalculateTangents();
                mesh.SetTriangles(pair.Value.Triangles, 0);
                mesh.RecalculateBounds();
                var go = new GameObject(pair.Key.ToString());
                go.layer = layer;
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = pair.Key == Metal.Hose && hose != null ? hose : Material(pair.Key);
            }
            return root;
        }

        private static readonly Dictionary<Metal, Material> Materials = new Dictionary<Metal, Material>();

        private static Material Material(Metal metal)
        {
            if (Materials.TryGetValue(metal, out var material) && material != null)
                return material;
            material = FittingGrime.Material(metal);
            Materials[metal] = material;
            return material;
        }
    }
}
