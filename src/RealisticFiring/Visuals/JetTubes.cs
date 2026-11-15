using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class JetTubes : MonoBehaviour
    {
        /// <summary>
        /// A box's side sheets as drawn, in its mesh's own frame: their
        /// distance from the middle, m, at heights, m, that run down from the
        /// door sheet to the throat as the grate does (the knee where the
        /// sheet turns in to the mud ring follows the grate). The sheet on the
        /// mesh's +x side is <see cref="RightWider"/> further out than the other.
        /// </summary>
        private sealed class Sheets
        {
            public float ZDoor, ZFront;
            public float[] YDoor, YFront, X;
            public float RightWider;

            /// <summary>The sheet's distance from the middle on <paramref name="side"/> (-1 left, +1 right) at a height and place along the car.</summary>
            public float At(float side, float y, float z)
            {
                var t = Mathf.Clamp01((z - ZDoor) / (ZFront - ZDoor));
                var x = X[X.Length - 1];
                if (y <= Mathf.Lerp(YDoor[0], YFront[0], t))
                    x = X[0];
                else
                {
                    for (var i = 1; i < X.Length; i++)
                    {
                        var below = Mathf.Lerp(YDoor[i - 1], YFront[i - 1], t);
                        var above = Mathf.Lerp(YDoor[i], YFront[i], t);
                        if (y <= above)
                        {
                            x = Mathf.Lerp(X[i - 1], X[i], (y - below) / Mathf.Max(1e-4f, above - below));
                            break;
                        }
                    }
                }
                return side > 0f ? x + RightWider : x;
            }
        }

        /// <summary>A box's jets: its drawn mesh and side sheets; the door sheet's place along the car, m; the tubes' bore radius, m; and each jet.</summary>
        private sealed class Layout
        {
            public string Mesh;
            public Sheets Sheets;
            public float DoorSheet;
            public float Radius;
            // Side (-1 left, +1 right), m from the door sheet, m over the grate.
            public (float Side, float FromDoor, float Over)[] Jets;
        }

        // The drawn boxes, as BrickArch's notes measure them (mesh
        // 's282_firebox', 's060_firebox'), in the car's frame; their side
        // sheets measured from the meshes in the meshes' own frames
        // (2026-11-14; the S282's is 1.37 m further back than the car's,
        // the S060's the car's): the S282's flare
        // in below a knee to the mud ring, stand nearly upright, leaning out
        // 11 mm, to 2.10 m, then lean in to the crown; the S060's stand
        // upright over a flare.
        private static readonly Dictionary<string, Layout> Layouts = new Dictionary<string, Layout>
        {
            // Six 3-1/4-in tubes: left 16, 40, 64 in from the door sheet,
            // right 28, 52, 76; the first each side at the sill's height
            // (raised clear of the heel), the rest 18 in up.
            ["LocoS282A"] = new Layout
            {
                Mesh = "s282_firebox",
                Sheets = new Sheets
                {
                    ZDoor = 1.576f,
                    ZFront = 3.56f,
                    YDoor = new[] { 1.328f, 1.616f, 2.097f, 2.953f },
                    YFront = new[] { 1.112f, 1.385f, 2.097f, 3.094f },
                    X = new[] { 0.412f, 0.502f, 0.513f, 0.431f },
                    RightWider = 0.002f,
                },
                DoorSheet = 0.21f,
                Radius = 0.036f,
                Jets = new[] { (-1f, 0.41f, 0.51f), (1f, 0.71f, 0.51f), (-1f, 1.02f, 0.46f), (1f, 1.32f, 0.46f), (-1f, 1.63f, 0.46f), (1f, 1.93f, 0.41f) },
            },
            // Four 2-1/4-in tubes: left 16 and 40 in, right 28 and 52.
            ["LocoS060"] = new Layout
            {
                Mesh = "s060_firebox",
                Sheets = new Sheets
                {
                    ZDoor = -1.82f,
                    ZFront = -0.17f,
                    YDoor = new[] { 1.328f, 1.616f, 2.775f },
                    YFront = new[] { 1.328f, 1.616f, 2.775f },
                    X = new[] { 0.232f, 0.448f, 0.448f },
                },
                DoorSheet = -1.82f,
                Radius = 0.025f,
                Jets = new[] { (-1f, 0.41f, 0.48f), (1f, 0.71f, 0.48f), (-1f, 1.02f, 0.43f), (1f, 1.32f, 0.43f) },
            },
        };

        private const float Proud = 0.008f;
        private const float Rim = 0.007f;

        // The mouth's dark, just off the sheet (the sheet is solid, so a bore
        // behind it would never show): far enough off not to fight it for depth.
        private const float Mouth = 0.004f;

        // The wall's staybolt rows, m (FireboxWall.shader's STAY_PITCH): a
        // tube goes in place of a staybolt, so on their grid.
        private const float StayPitch = 0.1f;
        private const int Sides = 32;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");
        private static readonly int ArchId = Shader.PropertyToID("_Arch");

        private RealisticFirebox firebox;
        private MeshRenderer tubeRenderer;
        private Transform carTransform;
        private readonly List<(Vector3 Mouth, Vector3 Inward)> mouths = new List<(Vector3, Vector3)>();

        /// <summary>The tubes' bore radius, m.</summary>
        internal float Radius { get; private set; }

        /// <summary>The tubes' renderer, for the fire's light (FireboxLighting).</summary>
        internal MeshRenderer Renderer => tubeRenderer;

        /// <summary>Whether they are drawn now.</summary>
        internal bool Shown => tubeRenderer != null && tubeRenderer.enabled;

        /// <summary>Each jet's mouth in the world and the way it blows (level across the box), for the fire volume and the capture's views.</summary>
        internal IEnumerable<(Vector3 Mouth, Vector3 Inward)> Mouths
        {
            get
            {
                if (carTransform == null)
                    yield break;
                foreach (var (mouth, inward) in mouths)
                    yield return (carTransform.TransformPoint(mouth), carTransform.TransformDirection(inward));
            }
        }

        /// <summary>Builds the tubes in <paramref name="fire"/>'s box where its engine can have jets. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed, RealisticFirebox firebox)
        {
            var wall = PlumeAssets.FireboxWallMaterial;
            var car = firebox != null ? firebox.Car : null;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (wall == null || bed == null || !bed.Ready || livery == null || firebox.Character.Jets == null
                || !Layouts.TryGetValue(livery, out var layout) || fire.GetComponent<JetTubes>() != null)
                return null;
            // The drawn box's own frame, for its side sheets.
            var box = (fire.emissiveRenderersAffectedByFire ?? new Renderer[0]).Concat(fire.GetComponentsInChildren<MeshRenderer>(true))
                .Select(r => r != null ? r.GetComponent<MeshFilter>() : null)
                .FirstOrDefault(f => f != null && f.sharedMesh != null && f.sharedMesh.name.StartsWith(layout.Mesh))?.transform;
            if (box == null)
                return $"no drawn box '{layout.Mesh}' to put the over-fire jets' tubes in";
            // A point moved onto the side sheet nearest it, square to the
            // sheet, and the way the sheet faces into the box there.
            Vector3 OnSheet(Vector3 world, out Vector3 inward)
            {
                var local = box.InverseTransformPoint(world);
                var s = local.x >= 0f ? 1f : -1f;
                var lean = (layout.Sheets.At(s, local.y + 0.01f, local.z) - layout.Sheets.At(s, local.y - 0.01f, local.z)) / 0.02f;
                local.x = s * layout.Sheets.At(s, local.y, local.z);
                inward = box.TransformDirection(new Vector3(-s, lean, 0f)).normalized;
                return box.TransformPoint(local);
            }
            var tubes = fire.gameObject.AddComponent<JetTubes>();
            tubes.firebox = firebox;
            tubes.carTransform = car.transform;
            tubes.Radius = layout.Radius;
            var holder = new GameObject("[RealisticFiring over-fire jet tubes]").transform;
            holder.SetParent(car.transform, false);

            // The bed's middle and its grate along the car: its door end and front end.
            var carT = car.transform;
            var back = carT.InverseTransformPoint(bed.BedPoint(0.5f, 0f, 0f));
            var ahead = carT.InverseTransformPoint(bed.BedPoint(0.5f, 1f, 0f));
            var forward = Mathf.Sign(ahead.z - back.z);
            float GrateAt(float z)
            {
                var t = Mathf.Abs(ahead.z - back.z) > 1e-3f ? (z - back.z) / (ahead.z - back.z) : 0f;
                return Mathf.LerpUnclamped(back.y, ahead.y, t);
            }

            // The wall shader's grid on the side sheets: the bed's frame.
            var gridOrigin = bed.BedPoint(0f, 0f, 0f);
            var gridAlong = (bed.BedPoint(0f, 1f, 0f) - gridOrigin).normalized;
            var gridUp = (bed.BedPoint(0.5f, 0.5f, 1f) - bed.BedPoint(0.5f, 0.5f, 0f)).normalized;

            var build = new Build();
            foreach (var (side, fromDoor, over) in layout.Jets)
            {
                var z = layout.DoorSheet + forward * fromDoor;
                var y = GrateAt(z) + over;
                var near = OnSheet(carT.TransformPoint(new Vector3(side * 0.4f, y, z)), out _);
                // On the sheet where the staybolt is, square to it: it leans.
                var sheet = carT.InverseTransformPoint(OnSheet(OnStaybolt(near, gridOrigin, gridAlong, gridUp), out var facing));
                Tube(build, sheet, carT.InverseTransformDirection(facing), layout.Radius);
                // It blows level across the box.
                tubes.mouths.Add((sheet, new Vector3(-side, 0f, 0f)));
            }
            var mesh = new Mesh { name = "RealisticFiring over-fire jet tubes" };
            mesh.SetVertices(build.Points);
            mesh.SetNormals(build.Normals);
            mesh.SetUVs(0, build.Uvs);
            // (Read only by the bores' middle, drawn as the arch's tubes are:
            // a white tint, no heat.)
            mesh.SetColors(Enumerable.Repeat(new Color(1f, 1f, 1f, 0f), build.Points.Count).ToList());
            mesh.subMeshCount = build.Parts.Length;
            for (var i = 0; i < build.Parts.Length; i++)
                mesh.SetTriangles(build.Parts[i], i);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = holder.gameObject.AddComponent<MeshRenderer>();
            // The sheet's own surface, its texture and tint, so the tube ends
            // are sooted as it is; darker into the bore.
            var sheetMaterial = FireboxLighting.WallFor(wall, box.GetComponent<MeshRenderer>()?.sharedMaterial);
            var tint = sheetMaterial.HasProperty(ColorId) ? sheetMaterial.GetColor(ColorId) : Color.white;
            Material Shade(string part, float shade)
            {
                var material = new Material(sheetMaterial) { name = $"{wall.name} (jet {part})" };
                material.SetColor(ColorId, new Color(tint.r * shade, tint.g * shade, tint.b * shade, tint.a));
                return Smooth(material);
            }
            // The bore's middle black, unlit.
            Material Plain(string part, float shade)
            {
                var material = new Material(wall) { name = $"{wall.name} (jet {part})" };
                material.SetColor(ColorId, new Color(shade, shade, shade, 1f));
                return Smooth(material);
            }
            // The middle drawn as the arch's own tubes are, so it takes no
            // light: the sheets' floor for the arch's glow beside it (a tenth
            // of the glow on the blackest steel) lit it a dull red, lighter
            // than the bore's wall round it (the council, round 2).
            var middle = Plain("bores", 0f);
            middle.SetFloat(ArchId, 1f);
            renderer.sharedMaterials = new[] { Shade("beads", BeadShade), Shade("bore walls", BoreShade), Shade("deep bores", DeepShade), middle };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            tubes.tubeRenderer = renderer;
            renderer.enabled = firebox.JetsFitted;
            return $"{layout.Jets.Length} over-fire jet tubes, {2f * layout.Radius * 1000f:F0} mm bore, at " +
                   string.Join(", ", tubes.mouths.Select(m => $"({m.Mouth.x:F3}, {m.Mouth.y:F2}, {m.Mouth.z:F2})")) + " m in the car";
        }

        // Without the sheet's relief: laid on the tubes' small rounds, its
        // dents turned their light about (the bores lit from above, the
        // beads in stripes: the council, round 3).
        private static Material Smooth(Material material)
        {
            if (material.HasProperty(BumpMapId))
                material.SetTexture(BumpMapId, null);
            return material;
        }

        // The nearest staybolt's place to a point on a side sheet, as the wall
        // shader draws them: heads at the middles of the cells StayPitch
        // square of the point's distances along the bed and up from it.
        private static Vector3 OnStaybolt(Vector3 point, Vector3 origin, Vector3 along, Vector3 up)
        {
            var rel = point - origin;
            float Snap(float d) => (Mathf.Floor(d / StayPitch) + 0.5f) * StayPitch;
            var dz = Snap(Vector3.Dot(rel, along)) - Vector3.Dot(rel, along);
            var dy = Snap(Vector3.Dot(rel, up)) - Vector3.Dot(rel, up);
            // The move within the sheet (along and up) that shifts the two
            // distances so: the frame's axes need not be square to each other.
            var c = Vector3.Dot(along, up);
            var det = 1f - c * c;
            if (det < 1e-4f)
                return point;
            var a = (dz - c * dy) / det;
            var b = (dy - c * dz) / det;
            return point + a * along + b * up;
        }

        // The tubes' mesh as it is built: each part drawn with its own
        // material (the beads, the bore's wall, deeper in it, and its dark).
        private sealed class Build
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Vector2> Uvs = new List<Vector2>();
            public readonly List<int>[] Parts = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };

            // A band between two rings, each point with its normal. Once: the
            // wall shader draws both sides of a face (drawn twice, each side
            // its own winding, the two fought for the same pixels along the
            // beads: the council, round 3).
            public void Band(int part, Vector3[] a, Vector3[] na, Vector3[] b, Vector3[] nb)
            {
                var start = Points.Count;
                for (var i = 0; i < a.Length; i++)
                {
                    Add(a[i], na[i]);
                    Add(b[i], nb[i]);
                }
                for (var i = 0; i < a.Length; i++)
                {
                    var j = (i + 1) % a.Length;
                    int ai = start + 2 * i, bi = ai + 1, aj = start + 2 * j, bj = aj + 1;
                    Parts[part].AddRange(new[] { ai, aj, bj, ai, bj, bi });
                }
            }

            private void Add(Vector3 point, Vector3 normal)
            {
                Points.Add(point);
                Normals.Add(normal);
                Uvs.Add(new Vector2(point.z, point.y) * UvScale);
            }
        }

        private const int Beads = 0, BoreWall = 1, DeepBore = 2, Bore = 3;

        // The beads, the bore's wall, and deeper in it, as shares of the
        // sheet's own tone at the tube (no brighter than the sheet beside
        // them, sooted as it is; plain steel inside the S060's near-black
        // sheets drew a pale ring: the council, round 3).
        private const float BeadShade = 0.7f;
        private const float BoreShade = 0.45f;
        private const float DeepShade = 0.18f;
        // The sheet's texture laid on so fine that every pixel spans much of
        // it, and the texture's smallest mipmaps, its mean tone, are read:
        // laid on at the sheet's own scale, its streaks drew as bands round
        // the bead, and at one spot of it a whole S060 tube end went black
        // (the council, round 3).
        private const float UvScale = 2000f;

        // Steps round the bead's half round.
        private const int BeadSteps = 6;
        private const float Sunk = 0.0015f;

        // The wall shader draws both sides of a face, each lit with its
        // normal turned toward the camera: a normal facing along the sheet,
        // or back into it, flipped where it turned from the camera, and the
        // bead drew in hard light and dark sectors and the bore's wall facing
        // up (the council, round 2). Every normal here leans out of the sheet
        // into the box: the bead's round by this much of its slope, with this
        // much more out of the sheet; the bore's wall turned this much
        // toward the tube's middle.
        private const float BeadRound = 0.4f;
        private const float FacingOut = 1f;
        private const float BoreTilt = 0.6f;

        // One tube's end at a point on the sheet, square to it: a bead, half
        // round, expanded over on the sheet (its foot the outer edge, toward
        // the fire below lit from under); inside it the bore, its wall taking
        // the fire on its upper side (facing down toward the bed) and growing
        // darker inward, and its dark middle, with the nozzle far back in it.
        private static void Tube(Build build, Vector3 sheet, Vector3 inward, float radius)
        {
            var across = Vector3.Cross(inward, Vector3.up).normalized;
            var up = Vector3.Cross(across, inward).normalized;
            var outs = new Vector3[Sides];
            for (var i = 0; i < Sides; i++)
            {
                var a = 2f * Mathf.PI * i / Sides;
                outs[i] = Mathf.Cos(a) * across + Mathf.Sin(a) * up;
            }
            Vector3[] Ring(float r, float off) => outs.Select(o => sheet + inward * off + o * r).ToArray();
            Vector3[] Facing(float radial, float axial) => outs.Select(o => (o * radial + inward * axial).normalized).ToArray();

            // The bead: from its foot on the sheet round over its crown to
            // its inner edge at the mouth.
            Vector3[] lastRing = null, lastNormals = null;
            for (var k = 0; k <= BeadSteps; k++)
            {
                var theta = Mathf.PI * k / BeadSteps;
                var r = radius + 0.5f * Rim * (1f + Mathf.Cos(theta));
                // (Its foot a little into the sheet, not on it, where the two
                // would fight for the same pixels.)
                var height = theta <= 0.5f * Mathf.PI ? Mathf.Lerp(-Sunk, Proud, Mathf.Sin(theta)) : Mouth + (Proud - Mouth) * Mathf.Sin(theta);
                var ring = Ring(r, height);
                var normals = Facing(BeadRound * Mathf.Cos(theta), Mathf.Sin(theta) + FacingOut);
                if (lastRing != null)
                    build.Band(Beads, lastRing, lastNormals, ring, normals);
                lastRing = ring;
                lastNormals = normals;
            }
            // The bore's wall, seen down the tube, then deeper, then dark:
            // its upper side faces down toward the bed.
            var wallFacing = Facing(-BoreTilt, 1f);
            build.Band(BoreWall, Ring(radius, Mouth), wallFacing, Ring(0.78f * radius, Mouth), wallFacing);
            build.Band(DeepBore, Ring(0.78f * radius, Mouth), wallFacing, Ring(0.6f * radius, Mouth), wallFacing);
            var middle = Enumerable.Repeat(sheet + inward * Mouth, Sides).ToArray();
            var flat = Enumerable.Repeat(inward, Sides).ToArray();
            build.Band(Bore, Ring(0.6f * radius, Mouth), flat, middle, flat);
        }

        private void LateUpdate()
        {
            if (tubeRenderer == null || firebox == null)
                return;
            var show = firebox.JetsFitted && Main.Settings != null && Main.Settings.ShowZonedCoalBed;
            if (tubeRenderer.enabled != show)
                tubeRenderer.enabled = show;
        }

        private void OnDestroy()
        {
            if (tubeRenderer != null)
                Destroy(tubeRenderer.gameObject);
        }
    }
}
