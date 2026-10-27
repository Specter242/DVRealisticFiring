using System.Collections.Generic;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class CoalLumps : MonoBehaviour
    {
        private static readonly int LumpZonesId = Shader.PropertyToID("_LumpZones");
        private static readonly int BedReliefId = Shader.PropertyToID("_BedRelief");
        private static readonly int BedMinId = Shader.PropertyToID("_BedMin");
        private static readonly int BedMaxId = Shader.PropertyToID("_BedMax");
        private static readonly int DoorSlopeId = Shader.PropertyToID("_DoorSlope");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int GlowStrengthId = Shader.PropertyToID("_GlowStrength");

        // The lumps' spacing, m: about one or two a cell; their sizes, m:
        // chips, the usual run, and a few big ones (the footage: caps a few
        // percent of the door's width across, chips between).
        private const float Cell = 0.055f;
        private const float SecondLump = 0.85f;
        private static readonly Vector2 Chips = new Vector2(0.02f, 0.04f);
        private static readonly Vector2 Usual = new Vector2(0.05f, 0.08f);
        private static readonly Vector2 Big = new Vector2(0.09f, 0.13f);
        private const float ChipShare = 0.25f;
        private const float BigShare = 0.15f;

        // How deep a lump sits in the pile, a share of its size.
        private static readonly Vector2 Burial = new Vector2(0.25f, 0.55f);

        // The rocks the lumps are cut from.
        private const int Rocks = 6;

        // The lumps' glow, to be set against the footage on the test bed.
        internal static float GlowStrength = 0.6f;

        // How far under the coal's surface the bed draws the deeper coal
        // between the lumps, m: about a lump's size, so they lie on it rather
        // than in it (drawn at the surface, it covered most of them).
        internal static float UnderDrop = 0.05f;

        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private MeshRenderer lumpRenderer;
        private Material material;
        private Texture2D zones;
        private Mesh mesh;
        private readonly Color[] pixels = new Color[GrateLayout.ZoneCount];
        // Metres per the bed's unit up.
        private float upPerUnit = 1f;

        /// <summary>The lumps' renderer, for the fire's light (FireboxLighting).</summary>
        internal MeshRenderer Renderer => lumpRenderer;

        /// <summary>Lays lumps over <paramref name="bed"/>. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed)
        {
            var source = PlumeAssets.CoalLumpsMaterial;
            if (fire == null || source == null || bed == null || !bed.Ready || bed.Effects == null || fire.GetComponent<CoalLumps>() != null)
                return null;
            var lumps = fire.gameObject.AddComponent<CoalLumps>();
            lumps.bed = bed;
            lumps.flames = fire.GetComponent<FireboxFlames>();
            lumps.material = new Material(source) { name = source.name + " (firebox)" };
            lumps.zones = new Texture2D(GrateLayout.Columns, GrateLayout.Rows, TextureFormat.RGBAHalf, false, true)
            {
                name = "coal lump zones",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            lumps.material.SetTexture(LumpZonesId, lumps.zones);
            var count = lumps.Build();
            return $"{count} coal lumps laid over the bed";
        }

        private int Build()
        {
            var box = bed.BedBox;
            var holder = new GameObject("[RealisticFiring coal lumps]").transform;
            holder.SetParent(bed.Effects, false);
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = bed.FullScale;
            material.SetVector(BedMinId, box.min);
            material.SetVector(BedMaxId, box.max);
            material.SetFloat(DoorSlopeId, ZonedCoalBed.DoorSlope);

            // Metres to the bed's units along its axes (across, up, along).
            var origin = bed.BedPoint(0f, 0f, 0f);
            var width = (bed.BedPoint(1f, 0f, 0f) - origin).magnitude;
            var depth = (bed.BedPoint(0f, 0f, 1f) - origin).magnitude;
            var length = (bed.BedPoint(0f, 1f, 0f) - origin).magnitude;
            var perUnit = new Vector3(
                width / Mathf.Max(1e-5f, box.size.x),
                depth / Mathf.Max(1e-5f, box.size.y),
                length / Mathf.Max(1e-5f, box.size.z));
            upPerUnit = perUnit.y;

            var random = new System.Random(588030);
            float Range(Vector2 r) => Mathf.Lerp(r.x, r.y, (float)random.NextDouble());
            var rocks = new List<(Vector3[] Points, Vector3[] Normals, int[] Triangles)>();
            for (var r = 0; r < Rocks; r++)
                rocks.Add(Rock(random));

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var anchors = new List<Vector2>();
            var lumpData = new List<Vector3>();
            var rockPoints = new List<Vector3>();
            var triangles = new List<int>();
            var across = Mathf.Max(1, Mathf.CeilToInt(width / Cell));
            var along = Mathf.Max(1, Mathf.CeilToInt(length / Cell));
            var count = 0;
            for (var j = 0; j < along; j++)
            for (var i = 0; i < across; i++)
            {
                var inCell = random.NextDouble() < SecondLump ? 2 : 1;
                for (var k = 0; k < inCell; k++)
                {
                    var pick = random.NextDouble();
                    var size = pick < ChipShare ? Range(Chips) : pick > 1.0 - BigShare ? Range(Big) : Range(Usual);
                    var anchor = new Vector2((i + (float)random.NextDouble()) / across, (j + (float)random.NextDouble()) / along);
                    var turn = Quaternion.Euler(Range(new Vector2(-25f, 25f)), Range(new Vector2(0f, 360f)), Range(new Vector2(-25f, 25f)));
                    var sink = size * Range(Burial) / perUnit.y;
                    var seed = (float)random.NextDouble();
                    var rock = rocks[random.Next(rocks.Count)];
                    var start = vertices.Count;
                    for (var v = 0; v < rock.Points.Length; v++)
                    {
                        var metres = turn * (rock.Points[v] * (0.5f * size));
                        vertices.Add(new Vector3(metres.x / perUnit.x, metres.y / perUnit.y, metres.z / perUnit.z));
                        // Through the holder's scale the normal comes out right.
                        var normal = turn * rock.Normals[v];
                        normals.Add(new Vector3(normal.x * perUnit.x, normal.y * perUnit.y, normal.z * perUnit.z).normalized);
                        anchors.Add(anchor);
                        lumpData.Add(new Vector3(seed, size, sink));
                        rockPoints.Add(rock.Points[v]);
                    }
                    foreach (var t in rock.Triangles)
                        triangles.Add(start + t);
                    count++;
                }
            }

            mesh = new Mesh { name = "RealisticFiring coal lumps" };
            if (vertices.Count > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(1, anchors);
            mesh.SetUVs(2, lumpData);
            mesh.SetUVs(3, rockPoints);
            mesh.SetTriangles(triangles, 0);
            // The shader lifts the lumps to the coal: drawn wherever the pile can reach.
            mesh.bounds = bed.LumpSurfaceBounds;

            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            lumpRenderer = holder.gameObject.AddComponent<MeshRenderer>();
            lumpRenderer.sharedMaterial = material;
            lumpRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lumpRenderer.receiveShadows = false;
            lumpRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            lumpRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            return count;
        }

        // A broken lump: a sphere squashed a little and cut by a few planes,
        // its faces roughened. Points about the unit sphere, outward normals.
        private static (Vector3[] Points, Vector3[] Normals, int[] Triangles) Rock(System.Random random)
        {
            Icosphere(out var points, out var triangles);
            float Next(float a, float b) => a + (b - a) * (float)random.NextDouble();
            var squash = new Vector3(Next(0.85f, 1.2f), Next(0.6f, 0.85f), Next(0.85f, 1.2f));
            // Broken coal is angular: cut close by many planes, so its sides
            // are flat faces meeting at edges.
            var cuts = new List<(Vector3 Normal, float Distance)>();
            for (var c = 0; c < 8; c++)
                cuts.Add((Random(random), Next(0.45f, 0.75f)));
            for (var v = 0; v < points.Count; v++)
            {
                var p = Vector3.Scale(points[v], squash);
                foreach (var (normal, distance) in cuts)
                {
                    var over = Vector3.Dot(p, normal) - distance;
                    if (over > 0f)
                        p -= normal * over;
                }
                points[v] = p * Next(0.95f, 1.05f);
            }
            var normals = new Vector3[points.Count];
            for (var t = 0; t < triangles.Count; t += 3)
            {
                var a = points[triangles[t]];
                var face = Vector3.Cross(points[triangles[t + 1]] - a, points[triangles[t + 2]] - a);
                normals[triangles[t]] += face;
                normals[triangles[t + 1]] += face;
                normals[triangles[t + 2]] += face;
            }
            for (var v = 0; v < normals.Length; v++)
                normals[v] = normals[v].normalized;
            return (points.ToArray(), normals, triangles.ToArray());
        }

        private static Vector3 Random(System.Random random)
        {
            Vector3 v;
            do
            {
                v = new Vector3((float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f, (float)random.NextDouble() * 2f - 1f);
            }
            while (v.sqrMagnitude < 0.01f || v.sqrMagnitude > 1f);
            return v.normalized;
        }

        // An icosahedron split once: 42 points on the unit sphere, 80 faces
        // wound so their cross product points out, which Unity draws as the
        // front (clockwise seen from outside).
        private static void Icosphere(out List<Vector3> points, out List<int> triangles)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            points = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (var i = 0; i < points.Count; i++)
                points[i] = points[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
                1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
                4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var middles = new Dictionary<long, int>();
            var list = points;
            int Middle(int a, int b)
            {
                var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (middles.TryGetValue(key, out var found))
                    return found;
                list.Add(((list[a] + list[b]) * 0.5f).normalized);
                middles[key] = list.Count - 1;
                return list.Count - 1;
            }
            triangles = new List<int>();
            for (var f = 0; f < faces.Count; f += 3)
            {
                var a = faces[f];
                var b = faces[f + 1];
                var c = faces[f + 2];
                var ab = Middle(a, b);
                var bc = Middle(b, c);
                var ca = Middle(c, a);
                triangles.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
        }

        private void LateUpdate()
        {
            var start = PerfMeter.Start();
            UpdateLumps();
            PerfMeter.Stop(PerfSection.CoalLumps, start);
        }

        private void UpdateLumps()
        {
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready;
            if (lumpRenderer != null && lumpRenderer.enabled != show)
                lumpRenderer.enabled = show;
            // Unseen (ZonedCoalBed.Seen), the zones' upload waits.
            if (!show || !bed.Seen)
                return;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var texel = GrateLayout.Row(zone) * GrateLayout.Columns + GrateLayout.Column(zone);
                pixels[texel] = new Color(bed.ZoneHeight(zone), bed.ZoneHeat(zone), bed.ZoneCover(zone), bed.ZoneAsh(zone));
            }
            zones.SetPixels(pixels);
            zones.Apply(false);
            // The bed's swells and heaps, which the lumps lie on as the bed does.
            if (bed.ReliefTexture != null)
                material.SetTexture(BedReliefId, bed.ReliefTexture);
            material.SetFloat(PulseId, flames != null ? flames.Pulse : 0f);
            material.SetFloat(GlowStrengthId, GlowStrength);
            // The continuous layers follow this same surface and heat,
            // with their own depth below it.
            bed.SetLumpSurface(zones, UnderDrop, upPerUnit);
        }

        private void OnDestroy()
        {
            if (lumpRenderer != null)
                Destroy(lumpRenderer.gameObject);
            if (material != null)
                Destroy(material);
            if (zones != null)
                Destroy(zones);
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
