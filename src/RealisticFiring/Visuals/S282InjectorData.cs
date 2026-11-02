using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RealisticFiring.Visuals
{
    /// <summary>Injector mesh data; it never contains the reference locomotive.</summary>
    internal sealed class S282InjectorData
    {
        public int Version { get; set; }
        public bool Prototype { get; set; }
        public string Credit { get; set; }
        public string SourceSha256 { get; set; }
        public Piece[] Meshes { get; set; }
        public Mask[] Masks { get; set; }
        public float[] WaterPivot { get; set; }
        public float[] SteamPivot { get; set; }
        public float[] WaterAxis { get; set; }
        public float[] SteamAxis { get; set; }
        public float WaterTravel { get; set; }
        public float SteamTravel { get; set; }
        public float[] StemPin { get; set; }
        public float[] StemGuide { get; set; }
        public float[] StemAxis { get; set; }
        public float[] CarrierPivot { get; set; }

        internal sealed class Piece
        {
            public string Name { get; set; }
            public string Source { get; set; }
            public string Role { get; set; }
            public string Material { get; set; }
            public float[] Pivot { get; set; }
            public float[][] Vertices { get; set; }
            public float[][] Normals { get; set; }
            public float[][] Uv { get; set; }
            public float[][] Uv2 { get; set; }
            public int[] Triangles { get; set; }
        }

        internal sealed class Mask
        {
            public string Path { get; set; }
            public string MeshName { get; set; }
            public string GeometryHash { get; set; }
            public int VertexCount { get; set; }
            public int[] TriangleCounts { get; set; }
            public int[][] RemovedTriangles { get; set; }
        }

        internal void Validate()
        {
            if (Version != 3 || !Prototype || Credit != "Injector model: Lilla" || !Hash(SourceSha256))
                throw new InvalidDataException("Not the supported S282 injector model");
            if (Meshes == null || Meshes.Length != 9 || Meshes.Select(m => m?.Name).Distinct().Count() != 9)
                throw new InvalidDataException("Expected nine injector pieces, including the split suspension link");
            Vector(WaterPivot, 3); Vector(SteamPivot, 3); Axis(WaterAxis); Axis(SteamAxis);
            Vector(StemPin, 3); Vector(StemGuide, 3); Vector(CarrierPivot, 3); Axis(StemAxis);
            if (StemPin.Zip(StemGuide, (a, b) => (a - b) * (a - b)).Sum() < .0001f)
                throw new InvalidDataException("Missing steam rod guide span");
            if (!(WaterTravel > 0f && WaterTravel <= 354f && SteamTravel > 0f && SteamTravel <= 90f))
                throw new InvalidDataException("Invalid valve travel");
            var vertices = 0;
            foreach (var p in Meshes)
            {
                if (p == null || string.IsNullOrEmpty(p.Name) || !(p.Role == "body" || p.Role == "water" || p.Role == "steam" || p.Role == "stem" || p.Role == "carrier")
                    || !(p.Material == "LocoS282A_Interior" || p.Material == "LocoS282A_Body"))
                    throw new InvalidDataException("Unknown injector part or material");
                Vector(p.Pivot, 3);
                var n = p.Vertices?.Length ?? 0;
                if (n == 0 || n > 20000 || p.Normals?.Length != n || p.Uv?.Length != n || p.Uv2?.Length != n
                    || p.Triangles == null || p.Triangles.Length == 0 || p.Triangles.Length % 3 != 0
                    || p.Triangles.Length > 60000 || p.Triangles.Any(i => i < 0 || i >= n))
                    throw new InvalidDataException("Invalid injector mesh arrays");
                for (var i = 0; i < n; i++)
                {
                    Vector(p.Vertices[i], 3); Vector(p.Normals[i], 3); Vector(p.Uv[i], 2); Vector(p.Uv2[i], 2);
                }
                vertices += n;
            }
            if (vertices > 30000 || !Meshes.Any(p => p.Role == "water") || !Meshes.Any(p => p.Role == "steam") || !Meshes.Any(p => p.Role == "stem") || !Meshes.Any(p => p.Role == "carrier"))
                throw new InvalidDataException("The injector is incomplete or unexpectedly large");
            if (Masks == null || Masks.Length == 0 || Masks.Length > 2 || Masks.Select(m => m?.Path).Distinct().Count() != Masks.Length)
                throw new InvalidDataException("Missing stock-fitting removal masks");
            foreach (var m in Masks)
            {
                if (m == null || !(m.Path == "Static/Cab" || m.Path == "Static/Things") || !Hash(m.GeometryHash)
                    || string.IsNullOrEmpty(m.MeshName) || m.VertexCount <= 0 || m.TriangleCounts == null
                    || m.TriangleCounts.Length == 0 || m.RemovedTriangles?.Length != m.TriangleCounts.Length)
                    throw new InvalidDataException("Invalid stock-fitting mask");
                var count = 0;
                for (var s = 0; s < m.TriangleCounts.Length; s++)
                {
                    var removed = m.RemovedTriangles[s];
                    if (m.TriangleCounts[s] <= 0 || removed == null || removed.Distinct().Count() != removed.Length
                        || removed.Any(i => i < 0 || i >= m.TriangleCounts[s]))
                        throw new InvalidDataException("Invalid stock-fitting triangle selection");
                    count += removed.Length;
                }
                if (count == 0 || count > .1f * m.TriangleCounts.Sum())
                    throw new InvalidDataException("The mask must remove only the old injector fittings");
            }
        }

        private static bool Hash(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        private static void Vector(float[] value, int count)
        {
            if (value == null || value.Length != count || value.Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                throw new InvalidDataException("Invalid model coordinate");
        }
        private static void Axis(float[] value)
        {
            Vector(value, 3);
            if (Math.Abs(value.Sum(x => x * x) - 1f) > .001f)
                throw new InvalidDataException("Valve axis is not normalized");
        }
    }
}
