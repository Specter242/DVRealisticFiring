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
        private static readonly int BedUpId = Shader.PropertyToID("_BedUp");
        private static readonly int NoiseId = Shader.PropertyToID("_Noise");

        // The lumps' spacing, m: about one or two a cell; their sizes, m:
        // chips, the usual run, and a few big ones (the footage: caps a few
        // percent of the door's width across, chips between).
        private const float Cell = 0.055f;
        private const float SecondLump = 0.85f;
        private static readonly Vector2 Chips = new Vector2(0.02f, 0.04f);
        private static readonly Vector2 Usual = new Vector2(0.05f, 0.08f);
        private static readonly Vector2 Big = new Vector2(0.09f, 0.13f);
        private const float ChipShare = 0.25f;

        // Under them, in each cell, a smaller lump lying down in the gaps,
        // its top this far under the pile's surface, m: without them the
        // gaps between lumps standing proud showed the bed under them as flat
        // glowing panes.
        private static readonly Vector2 Filler = new Vector2(0.03f, 0.055f);
        private static readonly Vector2 FillerDepth = new Vector2(0.012f, 0.04f);
        // And a chip just under the surface, breaking up the widest gaps
        // (the council, round 3: "broad luminous channels").
        private static readonly Vector2 Chink = new Vector2(0.02f, 0.035f);
        private static readonly Vector2 ChinkDepth = new Vector2(0.002f, 0.02f);
        private const float BigShare = 0.15f;

        // How deep a lump sits in the pile: a share of its own height above
        // its middle as it lies. (A share of its size, as the rounded rocks
        // were sunk, would bury flat slabs and short blocks whole.)
        private static readonly Vector2 Burial = new Vector2(0.05f, 0.7f);

        // How far a lump is tipped off level either way, degrees: shovelled,
        // not laid (at 25 the pile's slabs lay as paving, their tops within a
        // centimetre of one height).
        private const float Tumble = 40f;

        private static readonly CoalRock.Kind[] Rocks =
        {
            CoalRock.Kind.Broken, CoalRock.Kind.Broken, CoalRock.Kind.Broken, CoalRock.Kind.Broken,
            CoalRock.Kind.Broken, CoalRock.Kind.Broken, CoalRock.Kind.Broken, CoalRock.Kind.Block,
            CoalRock.Kind.Block, CoalRock.Kind.Block, CoalRock.Kind.Block, CoalRock.Kind.Slab,
        };

        // A point's height on its rock as the lump shader reads it for the
        // glow at its foot (low on the rock) and its darker cap: the rounded
        // rocks stood about this far either way of their middle, and the
        // shader was tuned on them.
        private const float GlowHeight = 0.6f;

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
        private int[] fullTriangles;
        // Where each lump's triangles begin in fullTriangles, and their end.
        private int[] lumpStarts;
        private int drawnStride;
        private float nextUploadAt;
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
            material.SetFloat(BedUpId, upPerUnit);

            var random = new System.Random(588030);
            float Range(Vector2 r) => Mathf.Lerp(r.x, r.y, (float)random.NextDouble());
            var rocks = new List<(Vector3[] Points, Vector3[] Normals, Vector3[] Heights, int[] Triangles)>();
            foreach (var kind in Rocks)
                rocks.Add(Rock(random, kind));

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var anchors = new List<Vector2>();
            var lumpData = new List<Vector3>();
            var rockPoints = new List<Vector3>();
            var triangles = new List<int>();
            var starts = new List<int>();
            var across = Mathf.Max(1, Mathf.CeilToInt(width / Cell));
            var along = Mathf.Max(1, Mathf.CeilToInt(length / Cell));
            var count = 0;
            for (var j = 0; j < along; j++)
            for (var i = 0; i < across; i++)
            {
                var inCell = random.NextDouble() < SecondLump ? 2 : 1;
                // The last two in each cell are its filler, down in the
                // gaps, and its chip, just under the surface.
                for (var k = 0; k <= inCell + 1; k++)
                {
                    var filler = k >= inCell;
                    var chink = k > inCell;
                    var pick = random.NextDouble();
                    var size = chink ? Range(Chink) : filler ? Range(Filler) : pick < ChipShare ? Range(Chips) : pick > 1.0 - BigShare ? Range(Big) : Range(Usual);
                    var anchor = new Vector2((i + (float)random.NextDouble()) / across, (j + (float)random.NextDouble()) / along);
                    var turn = Quaternion.Euler(Range(new Vector2(-Tumble, Tumble)), Range(new Vector2(0f, 360f)), Range(new Vector2(-Tumble, Tumble)));
                    var rock = rocks[random.Next(rocks.Count)];
                    var top = 0f;
                    foreach (var point in rock.Points)
                        top = Mathf.Max(top, (turn * point).y);
                    var sink = (filler ? 0.5f * size * top + Range(chink ? ChinkDepth : FillerDepth) : 0.5f * size * top * Range(Burial)) / perUnit.y;
                    var seed = (float)random.NextDouble();
                    var start = vertices.Count;
                    starts.Add(triangles.Count);
                    for (var v = 0; v < rock.Points.Length; v++)
                    {
                        var metres = turn * (rock.Points[v] * (0.5f * size));
                        vertices.Add(new Vector3(metres.x / perUnit.x, metres.y / perUnit.y, metres.z / perUnit.z));
                        // Through the holder's scale the normal comes out right.
                        var normal = turn * rock.Normals[v];
                        normals.Add(new Vector3(normal.x * perUnit.x, normal.y * perUnit.y, normal.z * perUnit.z).normalized);
                        anchors.Add(anchor);
                        lumpData.Add(new Vector3(seed, size, sink));
                        rockPoints.Add(rock.Heights[v]);
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
            // Keep each lump's complete index block, so switching detail
            // never moves the retained lumps.
            starts.Add(triangles.Count);
            lumpStarts = starts.ToArray();
            fullTriangles = triangles.ToArray();
            ApplyQuality();
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

        private void ApplyQuality()
        {
            var stride = FireboxDetail.For(Main.Settings.FireboxQuality).LumpStride;
            if (drawnStride == stride)
                return;
            drawnStride = stride;
            nextUploadAt = 0f;
            if (stride == 1)
                mesh.SetTriangles(fullTriangles, 0, false);
            else
            {
                var indices = new List<int>(fullTriangles.Length / stride + 64);
                for (var lump = 0; lump + 1 < lumpStarts.Length; lump += stride)
                    for (var i = lumpStarts[lump]; i < lumpStarts[lump + 1]; i++)
                        indices.Add(fullTriangles[i]);
                mesh.SetTriangles(indices, 0, false);
            }
        }

        // A broken lump (CoalRock), its largest half extent 1. Its normals
        // point out from its middle: each face is lit flat by its own normal in
        // the shader, and these round its highlights across the faces. Its
        // heights for the glow are its points with their height on the rock
        // scaled to GlowHeight either way.
        private static (Vector3[] Points, Vector3[] Normals, Vector3[] Heights, int[] Triangles) Rock(System.Random random, CoalRock.Kind kind)
        {
            var shape = CoalRock.Make(random, kind);
            var count = shape.PointCount;
            var points = new Vector3[count];
            var normals = new Vector3[count];
            var heights = new Vector3[count];
            var half = 1e-4f;
            for (var i = 0; i < count; i++)
            {
                points[i] = new Vector3(shape.Points[3 * i], shape.Points[3 * i + 1], shape.Points[3 * i + 2]);
                normals[i] = points[i].sqrMagnitude > 1e-8f ? points[i].normalized : Vector3.up;
                half = Mathf.Max(half, Mathf.Abs(points[i].y));
            }
            for (var i = 0; i < count; i++)
                heights[i] = new Vector3(points[i].x, points[i].y * GlowHeight / half, points[i].z);
            return (points, normals, heights, shape.Triangles);
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
            {
                nextUploadAt = 0f;
                return;
            }
            ApplyQuality();
            if (Time.unscaledTime < nextUploadAt)
                return;
            nextUploadAt = Time.unscaledTime + FireboxDetail.For(Main.Settings.FireboxQuality).UploadInterval;
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
            bed.SetLumpSurface(zones, UnderDrop, upPerUnit, material.GetTexture(NoiseId));
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
