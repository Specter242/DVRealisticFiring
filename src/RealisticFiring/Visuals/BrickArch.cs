using System.Collections.Generic;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class BrickArch : MonoBehaviour
    {
        private static readonly int ArchId = Shader.PropertyToID("_Arch");
        private static readonly int ArchHeatId = Shader.PropertyToID("_ArchHeat");
        private static readonly int ArchGlowId = Shader.PropertyToID("_ArchGlow");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int CutoffId = Shader.PropertyToID("_Cutoff");
        private static readonly int BumpMapId = Shader.PropertyToID("_BumpMap");

        /// <summary>An engine's arch, in its car's frame (m; y up, z along the car), as its firehole is given.</summary>
        internal readonly struct Shape
        {
            public Shape(float footAlong, float footUp, float lipAlong, float slope, int tubes)
            {
                FootAlong = footAlong;
                FootUp = footUp;
                LipAlong = lipAlong;
                Slope = slope;
                Tubes = tubes;
            }

            /// <summary>Where the bricks' underside meets the throat sheet or the tube plate: its place along the car and its height.</summary>
            public readonly float FootAlong;
            public readonly float FootUp;
            /// <summary>The lip's place along the car: where the bricks end, toward the door.</summary>
            public readonly float LipAlong;
            /// <summary>How steeply the bricks rise toward the door, degrees from the car's level.</summary>
            public readonly float Slope;
            /// <summary>The arch tubes the bricks lie on.</summary>
            public readonly int Tubes;
        }

        // Each engine's arch, set in its own box as its maker drew it: the
        // foot on the throat sheet or the tube plate under the lowest tubes,
        // the lip leaving room under the crown for the gases.
        private static readonly Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>
        {
            // The S282, a light Mikado's box with a short combustion chamber
            // and a Security arch on arch tubes (RME Aug. 1918). The stock
            // box (mesh 's282_firebox'): the door sheet at z 0.21, the grate
            // sloping down 7 degrees to z 2.0 to 2.2, a throat sheet rising
            // forward from there to y 2.10 at z 2.74, the chamber's floor on
            // to the tube plate at z 3.14 (its tubes from y 2.19 up), the
            // crown at y 2.95 over the door and 3.09 from z 2.74 on, 1.03 m
            // between the side sheets. The arch springs from the throat
            // sheet under the chamber and covers the front half of the
            // grate, its lip 0.9 m over the grate and 0.65 m under the
            // crown; two tubes and three rows of bricks span the box, as
            // the footage's.
            ["LocoS282A"] = new Shape(2.59f, 1.9f, 1.25f, 15f, 2),
            // The S060, a small switcher's narrow box with a short arch and
            // no arch tubes (the USATC S100's bricks spring from studs on
            // the side sheets). The stock box (mesh 's060_firebox'): the
            // door sheet at z -1.82, a throat sheet rising forward from y
            // 1.88 at z -0.17 to y 2.10 at the tube plate, z 0.04, the crown
            // level at y 2.78, 0.90 m between the side sheets. The arch
            // springs from the throat and covers the front two fifths of
            // the box, its lip 0.55 m under the crown.
            ["LocoS060"] = new Shape(-0.12f, 1.93f, -0.8f, 15f, 0),
        };

        // The test bed's: the foot raised, the bricks reaching further
        // toward the door, m, and a slope in place of the engine's own (0
        // leaves the engine's).
        internal static float ArchRaise = 0f;
        internal static float ArchReach = 0f;
        internal static float ArchSlope = 0f;

        // The bricks: about 4.5 in thick and a foot long up the slope; the
        // tubes 4 in across, their middles a little under the bricks'
        // underside, so they hang under it as round bars between the rows
        // (the footage; a little over it, they drew as flat dark slots).
        internal static float ArchThickness = 0.11f;
        internal static float ArchBrick = 0.3f;
        internal static float ArchTube = 0.05f;
        private const float TubeRise = -0.012f;
        private const float Joint = 0.016f;
        private const float Chamfer = 0.01f;
        private const int TubeSides = 14;
        // On tubes, the outer rows lie down toward the side sheets (the
        // footage's outer panels): their outer edges this much lower, m.
        private const float OuterDrop = 0.035f;
        // With no tubes under them the bricks are sprung across the box as
        // an arch proper: this much higher at the middle than at the side
        // sheets, m.
        private const float Crown = 0.09f;

        // How much of the fire's light the bricks give back over their own
        // color (pale firebrick, against the walls' sooted steel), and how
        // brightly they glow by their own heat.
        internal static float ArchLight = 0.4f;
        internal static float ArchGlow = 0.5f;
        // Their heat against the coal's under them, and what the flames
        // against them add; how long they take to heat and to cool, s (a
        // real arch takes longer still: it is what keeps a fire's gases
        // burning between rounds); and the test bed's lag, 0 none.
        // (No hotter under a working fire than its flames look: paler than
        // they, a flame crossing the bricks darkened them, like orange
        // glass before a lit panel: the council's sixth round. The footage's
        // working bricks are duller than the flame sheets over them.)
        internal static float ArchHeat = 0.7f;
        internal static float ArchFlameHeat = 0.2f;
        // (By more than the coal's heat itself: over a drifting fire the
        // bricks stood brighter than any coal under them, and no dimmer
        // than over a steady one: the council.)
        internal static float ArchHeatCurve = 1.4f;
        internal static float ArchHeatTime = 120f;
        internal static float ArchCoolTime = 420f;
        internal static float ArchLag = 1f;

        internal static float ArchLayer = 0.85f;
        internal static float ArchBend = 1.6f;
        internal static float ArchDrag = 0.25f;
        internal static float ArchTurn = 0.9f;
        internal static float ArchBlur = 1f;
        // And how far up its flames a climb through that layer counts as,
        // against the same climb in the open: under 1, the flames fill the
        // layer under the bricks, where the gases gather.
        internal static float ArchSqueeze = 0.4f;
        // And how far back along the bricks the gas at them has come at the
        // most, in the layer's depths.
        internal static float ArchCarry = 1.5f;
        // Under that layer: how far back toward the lip the flames' zones
        // trail at the bricks (in the bed's lengths), and how much more the
        // flames lean over the arch above its lip (bed lengths per box
        // height).
        internal static float ArchBack = 0.16f;
        internal static float ArchOver = 0.9f;
        // And how much taller the flames stand just behind the lip, where
        // the front grate's gases come out from under the bricks and turn
        // up round it.
        // (A little: at 0.8 they stood over the lip as a row of tips.)
        internal static float ArchRoll = 0.3f;
        // The test bed's: 1 to draw only the flames of the coal under the
        // arch, to see their way along the bricks and round the lip.
        internal static float ArchAlone = 0f;

        // The test bed's: 0 to see the box over the arch without its shadow,
        // and 0 to take the arch out (its bricks, its hold on the flames,
        // and its light and shadow), for its cost.
        internal static float ArchShadow = 1f;
        internal static float ArchShown = 1f;

        /// <summary>Whether the arch is drawn and holds the flames.</summary>
        internal bool Shown => archRenderer != null && archRenderer.enabled;

        // (Little blue in it: a white-hot fire's light on a paler buff drew
        // the bricks pink.)
        private static readonly Color Brick = new Color(0.85f, 0.74f, 0.52f);
        // What a brick's face gives back on average, under its texture.
        internal const float ArchAlbedo = 0.55f;
        private static readonly Color Scale = new Color(0.2f, 0.18f, 0.16f);

        private ZonedCoalBed bed;
        private FireboxFlames flames;
        private FireboxVolume volume;
        private Shape shape;
        private Transform holder;
        private MeshRenderer archRenderer;
        private Material material;
        private Mesh mesh;
        // The box, m (across, up, along), and which way along the box's own z
        // the tube plate lies; the car, the box's middle across it, which
        // way along it the tube plate lies from the door, and the door
        // sheet's place along it.
        private Vector3 boxSize;
        private float front = 1f;
        private Transform car;
        private float middle;
        private float ahead = 1f;
        private float doorAlong;
        private float heat;
        private bool heated;
        private int builtVersion = -1;

        /// <summary>The arch's renderer, for the fire's light (FireboxLighting).</summary>
        internal MeshRenderer Renderer => archRenderer;

        /// <summary>The bricks' heat, 0 to 1 on the fire's ramp.</summary>
        internal float Heat => heat;

        private float Thickness => Mathf.Clamp(ArchThickness, 0.03f, 0.3f);
        private float Angle => Mathf.Clamp(ArchSlope > 0f ? ArchSlope : shape.Slope, 0f, 60f) * Mathf.Deg2Rad;
        // How far up the slope the bricks run, from the foot to the lip.
        private float Run => Mathf.Max(0.3f, ahead * (shape.FootAlong - shape.LipAlong) + ArchReach) / Mathf.Cos(Angle);

        // A point of the arch in its own frame (the box's, in metres), by
        // its place across the box from the middle, its way up the slope
        // from the foot, and its height off the bricks' underside.
        private Vector3 OnSlope(float across, float slope, float off)
        {
            var sin = Mathf.Sin(Angle);
            var cos = Mathf.Cos(Angle);
            var inCar = new Vector3(middle + across, shape.FootUp + ArchRaise + slope * sin + off * cos, shape.FootAlong - ahead * (slope * cos - off * sin));
            return holder.InverseTransformPoint(car.TransformPoint(inCar));
        }

        // A point's place along the box, m from its end at the tube plate.
        private float Place(Vector3 local) => -front * local.z;

        /// <summary>
        /// The arch for the fire volume, in its box (0 to 1 each way): the
        /// lip's place along it and the foot's, and the underside's height
        /// at each.
        /// </summary>
        internal Vector4 Line
        {
            get
            {
                float Along(Vector3 local) => front > 0f ? 1f - Place(local) / boxSize.z : Place(local) / boxSize.z;
                var lip = OnSlope(0f, Run, 0f);
                var foot = OnSlope(0f, 0f, 0f);
                return new Vector4(Along(lip), Along(foot), lip.y / boxSize.y, foot.y / boxSize.y);
            }
        }

        /// <summary>The lip's place along the bed, 0 the door to 1 the front.</summary>
        internal float LipAlongBed => holder != null ? bed.BoxPoint(holder.TransformPoint(OnSlope(0f, Run, 0f))).y : 1f;

        /// <summary>The room under the bricks over a point on the coal, m; no end to it clear of the arch.</summary>
        internal float Room(Vector3 world)
        {
            if (holder == null || !Shown)
                return float.PositiveInfinity;
            var local = holder.InverseTransformPoint(world);
            var lip = OnSlope(0f, Run, 0f);
            var foot = OnSlope(0f, 0f, 0f);
            if (Place(local) > Place(lip))
                return float.PositiveInfinity;
            var under = Mathf.Lerp(foot.y, lip.y, Mathf.InverseLerp(Place(foot), Place(lip), Place(local)));
            return Mathf.Max(0.05f, under - local.y);
        }

        /// <summary>
        /// The bricks' underside in the world, for its shadow on the walls
        /// over it: its corner at the lip, its edges across the box and down
        /// to the foot, and its face's way up.
        /// </summary>
        internal void Underside(out Vector3 corner, out Vector3 across, out Vector3 along, out Vector3 up)
        {
            var half = 0.5f * boxSize.x;
            corner = holder.TransformPoint(OnSlope(-half, Run, 0f));
            across = holder.TransformPoint(OnSlope(half, Run, 0f)) - corner;
            along = holder.TransformPoint(OnSlope(-half, 0f, 0f)) - corner;
            up = Vector3.Cross(along, across).normalized;
            if (Vector3.Dot(up, holder.up) < 0f)
                up = -up;
        }

        /// <summary>Builds the arch in <paramref name="fire"/>'s box where its engine has one. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed, RealisticFirebox firebox)
        {
            var wall = PlumeAssets.FireboxWallMaterial;
            var flames = fire != null ? fire.GetComponent<FireboxFlames>() : null;
            var car = firebox != null ? firebox.Car : null;
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (wall == null || bed == null || !bed.Ready || flames == null || flames.SmokeSpace == null || livery == null
                || !Shapes.TryGetValue(livery, out var shape) || fire.GetComponent<BrickArch>() != null)
                return null;
            var arch = fire.gameObject.AddComponent<BrickArch>();
            arch.bed = bed;
            arch.flames = flames;
            arch.shape = shape;
            arch.material = new Material(wall) { name = wall.name + " (brick arch)" };
            BrickTextures();
            arch.material.SetTexture(MainTexId, brickTexture);
            arch.material.SetTexture(BumpMapId, brickRelief);
            arch.material.SetColor(ColorId, Color.white);
            arch.material.SetFloat(CutoffId, 0f);
            arch.material.SetFloat(ArchId, 1f);
            arch.Frame(car.transform);
            arch.Build();
            arch.volume = fire.GetComponent<FireboxVolume>();
            if (arch.volume != null)
                arch.volume.Arch = arch;
            var foot = arch.OnSlope(0f, 0f, 0f);
            var lip = arch.OnSlope(0f, arch.Run, 0f);
            return $"brick arch {(shape.Tubes > 0 ? $"on {shape.Tubes} arch tubes" : "sprung from the side sheets")}: its foot {arch.Place(foot):F2} m from the box's end at the tube plate and {foot.y:F2} m over the grate, " +
                   $"its lip {arch.Place(lip):F2} m from it and {lip.y:F2} m over the grate, {arch.boxSize.y - lip.y - arch.Thickness:F2} m under the box's top";
        }

        // The arch's own frame in the volume's box: metres, from the middle
        // of the grate's end at the tube plate; x across, y up from the
        // grate, z along the box.
        private void Frame(Transform ofCar)
        {
            var space = flames.SmokeSpace;
            var bounds = flames.SmokeBox;
            var scale = space.lossyScale;
            var size = new Vector3(Mathf.Max(Mathf.Abs(scale.x), 1e-4f), Mathf.Max(Mathf.Abs(scale.y), 1e-4f), Mathf.Max(Mathf.Abs(scale.z), 1e-4f));
            boxSize = Vector3.Scale(bounds.size, size);
            front = Mathf.Sign(space.InverseTransformPoint(bed.BedPoint(0.5f, 1f, 0f)).z - space.InverseTransformPoint(bed.BedPoint(0.5f, 0f, 0f)).z);
            holder = new GameObject("[RealisticFiring brick arch]").transform;
            holder.SetParent(space, false);
            holder.localPosition = new Vector3(bounds.center.x, bounds.min.y, front > 0f ? bounds.max.z : bounds.min.z);
            holder.localRotation = Quaternion.identity;
            holder.localScale = new Vector3(1f / size.x, 1f / size.y, 1f / size.z);
            car = ofCar;
            var back = car.InverseTransformPoint(bed.BedPoint(0.5f, 0f, 0f));
            ahead = Mathf.Sign(car.InverseTransformPoint(bed.BedPoint(0.5f, 1f, 0f)).z - back.z);
            middle = back.x;
            doorAlong = back.z;
            mesh = new Mesh { name = "RealisticFiring brick arch" };
            holder.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            archRenderer = holder.gameObject.AddComponent<MeshRenderer>();
            archRenderer.sharedMaterial = material;
            archRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            archRenderer.receiveShadows = false;
            archRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            archRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        // The mesh's parts as they are gathered: flat-shaded faces, each its
        // own corners.
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();

        private void Build()
        {
            points.Clear();
            normals.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();
            var random = new System.Random(7741);
            float Random(float from, float to) => from + (to - from) * (float)random.NextDouble();

            var thickness = Thickness;
            var radius = Mathf.Clamp(ArchTube, 0.01f, 0.1f);

            // The rows of bricks: between the side sheets and the tubes,
            // lying on the tubes' shoulders; with no tubes, five rows
            // sprung from side sheet to side sheet, crowned.
            var half = 0.5f * boxSize.x;
            var rows = shape.Tubes > 0 ? shape.Tubes + 1 : 5;
            float Sprung(float across) => shape.Tubes > 0
                ? -OuterDrop * Mathf.Clamp01((Mathf.Abs(across) - (half - boxSize.x / rows)) / (boxSize.x / rows))
                : Crown * (1f - across * across / (half * half));
            var shoulder = shape.Tubes > 0 ? 0.55f * radius : 0f;
            var run = Run;
            var count = Mathf.Max(2, Mathf.RoundToInt(run / Mathf.Max(ArchBrick, 0.1f)));
            var length = run / count;
            for (var row = 0; row < rows; row++)
            {
                var left = row == 0 ? -half + Joint : -half + boxSize.x * row / rows + shoulder;
                var right = row == rows - 1 ? half - Joint : -half + boxSize.x * (row + 1) / rows - shoulder;
                // Every other row begins on a half brick, so the joints do
                // not run straight across the arch; each ends a little
                // short or long, as bricks set by hand do.
                var staggered = (row & 1) == 1;
                // (Its first let into the sheet it springs from.)
                var from = -0.05f;
                // (By several centimetres: within three, the lip drew
                // ruler-straight: the council.)
                var end = run + Random(-0.09f, 0.03f);
                for (var i = 0; from < end - 0.02f; i++)
                {
                    var to = Mathf.Min(end, staggered ? (i + 0.5f) * length : (i + 1f) * length);
                    if (end - to < 0.04f)
                        to = end;
                    // Each brick its own tone, and all of them darker toward
                    // the sheet they spring from, where ash and slag lie on
                    // the cooler brick.
                    var tone = Random(0.88f, 1.08f) * (random.NextDouble() < 0.15 ? 0.82f : 1f)
                               * Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(0.5f * (from + to) / (0.6f * run)));
                    // (A sprung arch's bricks are wedged tight: at the
                    // tubes' joints its rows stood apart as dark slots.)
                    var gap = 0.5f * Joint * Random(0.7f, 1.5f) * (shape.Tubes > 0 ? 1f : 0.5f);
                    BrickAt(random, left + gap, right - gap, from + gap, to - gap, Sprung(left), Sprung(right),
                        Random(-0.003f, 0.004f), thickness + Random(-0.004f, 0.004f), Brick * tone, to >= end,
                        row == 0 || shape.Tubes > 0, row == rows - 1 || shape.Tubes > 0);
                    from = to;
                }
            }

            // The arch tubes, from the throat up past the lip to the door
            // sheet: water tubes, dark however hot the fire.
            var rise = TubeRise;
            var last = ahead * (shape.FootAlong - doorAlong) / Mathf.Cos(Angle);
            var origin = OnSlope(0f, 0f, 0f);
            for (var tube = 0; tube < shape.Tubes; tube++)
            {
                var across = -half + boxSize.x * (tube + 1f) / (shape.Tubes + 1);
                var start = points.Count;
                // (Three rings: under the bricks, where they bear on it and
                // their ash lies on it, its scale is darker than on its
                // bare run to the door sheet.)
                for (var ring = 0; ring < 3; ring++)
                {
                    var slope = ring == 0 ? -0.08f : ring == 1 ? run : last;
                    var scale = ring < 2 ? 0.7f : 1.1f;
                    var axis = OnSlope(across, slope, rise);
                    for (var side = 0; side <= TubeSides; side++)
                    {
                        var angle = 2f * Mathf.PI * side / TubeSides;
                        var out3 = OnSlope(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) - origin;
                        points.Add(axis + radius * out3);
                        normals.Add(out3);
                        colors.Add(new Color(scale * Scale.r, scale * Scale.g, scale * Scale.b, 0f));
                        uvs.Add(new Vector2(0.4f * side / TubeSides + 0.3f * tube, 0.8f * slope));
                    }
                }
                for (var ring = 0; ring < 2; ring++)
                {
                    for (var side = 0; side < TubeSides; side++)
                    {
                        var a = start + ring * (TubeSides + 1) + side;
                        var b = a + 1;
                        var c = a + TubeSides + 1;
                        var d = b + TubeSides + 1;
                        triangles.AddRange(new[] { a, c, b, b, c, d });
                    }
                }
            }

            mesh.Clear();
            mesh.SetVertices(points);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            builtVersion = FireboxTestBed.TuningVersion;
        }

        // How hot a place on the arch runs against its hottest: cooler
        // toward the sheets the bricks spring from and lie against, which
        // the boiler's water keeps cold, and in broad patches, by the place
        // and not by the brick (each brick its own heat, a hard fire showed
        // some as bright rectangles beside their neighbors: the council).
        private float Warmth(float across, float slope)
        {
            var half = 0.5f * boxSize.x;
            var patches = Mathf.PerlinNoise(2.3f * across + 13.7f, 2.3f * slope + 4.1f);
            return Mathf.Lerp(0.86f, 1f, Mathf.Clamp01(slope / Run)) * Mathf.Lerp(0.9f, 1f, Mathf.Clamp01((half - Mathf.Abs(across)) / 0.15f))
                   * (0.92f + 0.08f * patches);
        }

        // How finely a brick's underside is cut: its wear is in its shape,
        // not in its texture alone.
        private const int Cut = 4;

        // One brick: a block whose underside, the face the fire and the eye
        // see, is worn: uneven by a few millimetres, here and there spalled
        // a centimetre or two deep, its edges broken back irregularly and
        // its corners sometimes gone, the lip's edge most of all (flat and
        // sharp-edged, each read as a machined slab: the council). Alpha
        // carries how hot each point runs against the arch's heat: the
        // bevels and sides cooler than the face (the shader darkens them by
        // it too while the arch is cool), and the face cooler along an edge
        // that lies on a water tube or against a side sheet.
        private void BrickAt(System.Random random, float left, float right, float from, float to, float liftLeft, float liftRight,
            float low, float high, Color tint, bool atLip, bool coolLeft, bool coolRight)
        {
            if (right - left < 0.02f || to - from < 0.02f)
                return;
            float Any() => (float)random.NextDouble();
            float Off() => (Any() - 0.5f) * 0.01f;
            var chamfer = Mathf.Min(Chamfer, 0.25f * (right - left), 0.25f * (to - from));
            var width = right - left;
            var length = to - from;
            // One brick in four has a corner broken off; one in three a spall.
            var broken = Any() < 0.25f ? random.Next(4) : -1;
            var breakBy = 0.02f + 0.035f * Any();
            var spalled = Any() < 0.5f;
            var spallAt = new Vector2(0.2f + 0.6f * Any(), 0.2f + 0.6f * Any());
            var spallWide = 0.3f + 0.25f * Any();
            var spallDeep = 0.012f + 0.02f * Any();
            var shift = new Vector2(Any(), Any()) * 3f;
            var sprung = shape.Tubes > 0 ? 0f : 2f * Crown / (0.25f * boxSize.x * boxSize.x);

            // The underside's points: its border broken back from the
            // brick's outline, the lip's edge further and each point its
            // own way.
            var under = new Vector3[Cut + 1, Cut + 1];
            var places = new Vector2[Cut + 1, Cut + 1];
            for (var j = 0; j <= Cut; j++)
            {
                for (var i = 0; i <= Cut; i++)
                {
                    var a = (float)i / Cut;
                    var b = (float)j / Cut;
                    var onBorder = i == 0 || i == Cut || j == 0 || j == Cut;
                    var back = chamfer + (onBorder ? 0.006f * Any() : 0f);
                    var wear = atLip && j == Cut ? 0.008f + 0.035f * Any() : 0f;
                    // Toward a broken corner, further in.
                    if (broken >= 0)
                    {
                        var corner = new Vector2(broken == 1 || broken == 2 ? 1f : 0f, broken >= 2 ? 1f : 0f);
                        back += breakBy * Mathf.Clamp01(1f - Vector2.Distance(new Vector2(a, b), corner) / 0.5f) * (0.6f + 0.8f * Any());
                    }
                    var across = Mathf.Lerp(left + (i == 0 ? back : 0f), right - (i == Cut ? back : 0f), a);
                    var slope = Mathf.Lerp(from + (j == 0 ? back : 0f), to - (j == Cut ? back + wear : 0f), b);
                    var height = low + Mathf.Lerp(liftLeft, liftRight, a) + (onBorder ? 0.4f : 1f) * (Any() - 0.5f) * 0.014f;
                    if (spalled)
                        height += spallDeep * Mathf.Clamp01(1f - Vector2.Distance(new Vector2(a, b), spallAt) / spallWide);
                    under[i, j] = OnSlope(across, slope, height);
                    places[i, j] = new Vector2(across, slope);
                }
            }

            // The brick's outline a bevel's height up, and its top.
            var edge = new Vector3[4];
            var top = new Vector3[4];
            var warm = new float[4];
            for (var corner = 0; corner < 4; corner++)
            {
                var across = corner == 0 || corner == 3 ? left : right;
                var slope = corner < 2 ? from : to;
                var offAcross = Off();
                var offSlope = Off();
                var offHeight = Off() + (corner == 0 || corner == 3 ? liftLeft : liftRight);
                // A sprung arch's bricks are cut to its curve: their sides
                // lean with it, so the joints between the rows are even to
                // the top (upright, they opened into wedges there).
                var cut = sprung * across;
                edge[corner] = OnSlope(across + offAcross + cut * chamfer, slope + offSlope, low + chamfer + offHeight);
                top[corner] = OnSlope(across + offAcross + cut * high, slope + offSlope, high + offHeight);
                warm[corner] = Warmth(across, slope);
            }
            var inside = 0.25f * (edge[0] + edge[1] + edge[2] + edge[3]) + 0.5f * (top[0] - edge[0]);
            // The outline's sides point by point: the lip's end broken back
            // unevenly through the brick's whole thickness, so its end face
            // is ragged and not one flat band.
            var rim = new Vector3[4, Cut + 1];
            var rimTop = new Vector3[4, Cut + 1];
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                for (var k = 0; k <= Cut; k++)
                {
                    var share = (float)k / Cut;
                    var lost = side == 2 && atLip && k > 0 && k < Cut
                        ? (OnSlope(0f, -(0.006f + 0.035f * Any()), 0f) - OnSlope(0f, 0f, 0f)) : Vector3.zero;
                    rim[side, k] = Vector3.Lerp(edge[side], edge[next], share) + lost;
                    // (And its upper edge chipped down: broken back along
                    // the slope alone, the end face still drew flat and
                    // straight-edged from below, where it is seen end on.)
                    var chip = side == 2 && atLip ? (OnSlope(0f, 0f, -(k > 0 && k < Cut ? 0.035f : 0.012f) * Any()) - OnSlope(0f, 0f, 0f)) : Vector3.zero;
                    rimTop[side, k] = Vector3.Lerp(top[side], top[next], share) + lost * (0.5f + Any()) + chip;
                }
            }

            // The underside, smooth over its wear.
            var start = points.Count;
            var facing = Vector3.Cross(under[Cut, 0] - under[0, 0], under[0, Cut] - under[0, 0]).normalized;
            var outward = Vector3.Dot(facing, under[Cut / 2, Cut / 2] - inside) >= 0f;
            for (var j = 0; j <= Cut; j++)
            {
                for (var i = 0; i <= Cut; i++)
                {
                    var across = under[Mathf.Min(i + 1, Cut), j] - under[Mathf.Max(i - 1, 0), j];
                    var along = under[i, Mathf.Min(j + 1, Cut)] - under[i, Mathf.Max(j - 1, 0)];
                    var normal = Vector3.Cross(across, along).normalized;
                    var cool = (i == 0 && coolLeft) || (i == Cut && coolRight) ? 0.8f : 1f;
                    points.Add(under[i, j]);
                    normals.Add(outward ? normal : -normal);
                    colors.Add(new Color(tint.r, tint.g, tint.b, cool * Warmth(places[i, j].x, places[i, j].y)));
                    uvs.Add(shift + TextureScale * (places[i, j] - new Vector2(left, from)));
                }
            }
            for (var j = 0; j < Cut; j++)
            {
                for (var i = 0; i < Cut; i++)
                {
                    var a = start + j * (Cut + 1) + i;
                    var b = a + 1;
                    var c = a + Cut + 1;
                    var d = c + 1;
                    triangles.AddRange(outward ? new[] { a, b, d, a, d, c } : new[] { a, d, b, a, c, d });
                }
            }

            // The bevels from the underside's border up to the outline, the
            // sides, and the top.
            for (var side = 0; side < 4; side++)
            {
                var next = (side + 1) % 4;
                for (var k = 0; k < Cut; k++)
                {
                    Vector3 Border(int n) => side == 0 ? under[n, 0] : side == 1 ? under[Cut, n] : side == 2 ? under[Cut - n, Cut] : under[0, Cut - n];
                    var from0 = (float)k / Cut;
                    var to0 = (k + 1f) / Cut;
                    var warmFrom = Mathf.Lerp(warm[side], warm[next], from0);
                    var warmTo = Mathf.Lerp(warm[side], warm[next], to0);
                    Face(Border(k), Border(k + 1), rim[side, k + 1], rim[side, k], inside, tint, 0.85f, warmFrom, warmTo, warmTo, warmFrom, shift);
                    Face(rim[side, k], rim[side, k + 1], rimTop[side, k + 1], rimTop[side, k], inside, tint, 0.7f, warmFrom, warmTo, warmTo, warmFrom, shift);
                }
            }
            Face(top[0], top[1], top[2], top[3], inside, tint, 0.55f, warm[0], warm[1], warm[2], warm[3], shift);
        }

        // A flat face, facing away from its brick's middle; its texture laid
        // by its own edges, in metres; its corners' heat the face's share
        // of each one's.
        private void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 inside, Color tint, float share,
            float warmA, float warmB, float warmC, float warmD, Vector2 shift)
        {
            var normal = Vector3.Cross(b - a, d - a).normalized;
            var outward = Vector3.Dot(normal, 0.25f * (a + b + c + d) - inside) >= 0f;
            if (!outward)
                normal = -normal;
            var along = (b - a).normalized;
            var up = Vector3.Cross(normal, along);
            var start = points.Count;
            var corners = new[] { a, b, c, d };
            var warm = new[] { warmA, warmB, warmC, warmD };
            for (var i = 0; i < 4; i++)
            {
                points.Add(corners[i]);
                normals.Add(normal);
                colors.Add(new Color(tint.r, tint.g, tint.b, share * warm[i]));
                // (Finer than the underside's: seen square on where the
                // underside is seen along its length, a lip's end face
                // drew a coarser grain than the faces beside it.)
                uvs.Add(shift + 1.8f * TextureScale * new Vector2(Vector3.Dot(corners[i] - a, along), Vector3.Dot(corners[i] - a, up)));
            }
            triangles.AddRange(outward
                ? new[] { start, start + 1, start + 2, start, start + 2, start + 3 }
                : new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        private static Texture2D brickTexture;
        private static Texture2D brickRelief;
        // The firebrick's texture, tiles a metre: 256 texels to 62 cm.
        private const float TextureScale = 1.6f;

        // Firebrick's face (laid over each brick's own tone): mottled and
        // grainy, with pits and runs of slag; in its alpha, how hot each
        // spot glows against its place's heat (the pits brighter as hollows
        // are, the slag duller); and its relief, for the fire's light
        // across it.
        private static void BrickTextures()
        {
            if (brickTexture != null && brickRelief != null)
                return;
            const int size = 256;
            var pixels = new Color[size * size];
            var heights = new float[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var u = 4f * x / size;
                    var v = 4f * y / size;
                    var mottle = 0.5f * Mathf.PerlinNoise(u + 3.1f, v + 7.7f) + 0.3f * Mathf.PerlinNoise(2.1f * u + 17.3f, 2.1f * v + 5.9f)
                                 + 0.2f * Mathf.PerlinNoise(5.3f * u + 1.7f, 5.3f * v + 29.3f);
                    // (No finer than four texels: finer, it drew as salt
                    // on the hottest bricks.)
                    var grain = 0.6f * Mathf.PerlinNoise(8f * u + 11.1f, 8f * v + 3.3f) + 0.4f * Mathf.PerlinNoise(15f * u + 51.7f, 15f * v + 19.9f);
                    var pit = Mathf.SmoothStep(0.62f, 0.78f, Mathf.PerlinNoise(9f * u + 41.3f, 9f * v + 13.1f));
                    // Slag's glaze, in blotches (run in streaks down the
                    // slope, a cold brick read as planed wood).
                    var slag = Mathf.SmoothStep(0.55f, 0.8f, Mathf.PerlinNoise(3.1f * u + 9.9f, 2.3f * v + 23.7f));
                    var value = Mathf.Lerp(0.74f, 1f, Mathf.SmoothStep(0.25f, 0.8f, mottle)) * (1f - 0.3f * pit) * (1f - 0.2f * slag) * (0.85f + 0.3f * grain);
                    // (Its fine detail alone: each brick reads the texture
                    // at its own place, and broad patches in it stopped at
                    // each brick's edges.)
                    var hot = Mathf.Clamp01(0.5f + 0.2f * pit - 0.2f * slag + 0.4f * (grain - 0.5f) + 0.1f * (mottle - 0.5f));
                    pixels[y * size + x] = new Color(Mathf.Clamp01(value), Mathf.Clamp01(0.97f * value), Mathf.Clamp01(0.93f * value), Mathf.Max(hot, 0.02f));
                    heights[y * size + x] = 0.5f * grain - 0.9f * pit + 0.3f * mottle;
                }
            }
            // A few hairline cracks, a little darker than the face cold and
            // hot (dark and deep cold and brighter than the face hot, they
            // read as lines drawn on it).
            var cracks = new System.Random(5309);
            for (var crack = 0; crack < 5; crack++)
            {
                var at = new Vector2(size * (float)cracks.NextDouble(), size * (float)cracks.NextDouble());
                var heading = 2f * Mathf.PI * (float)cracks.NextDouble();
                var steps = 50 + cracks.Next(90);
                for (var step = 0; step < steps; step++)
                {
                    // (Turning little at a step: sharply, a crack drew as a zigzag.)
                    heading += ((float)cracks.NextDouble() - 0.5f) * 0.18f;
                    at += new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));
                    var x = Mathf.Clamp(Mathf.RoundToInt(at.x), 0, size - 1);
                    var y = Mathf.Clamp(Mathf.RoundToInt(at.y), 0, size - 1);
                    var pixel = pixels[y * size + x];
                    pixels[y * size + x] = new Color(0.78f * pixel.r, 0.78f * pixel.g, 0.78f * pixel.b, Mathf.Clamp01(pixel.a - 0.12f));
                    heights[y * size + x] -= 0.5f;
                }
            }
            brickTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "RealisticFiring firebrick",
                wrapMode = TextureWrapMode.Mirror,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            brickTexture.SetPixels(pixels);
            brickTexture.Apply(true, true);

            // The relief as a normal map (x and y in red and green, as the
            // shaders unpack it), from the heights' slopes.
            float Height(int x, int y) => heights[Mathf.Clamp(y, 0, size - 1) * size + Mathf.Clamp(x, 0, size - 1)];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var normal = new Vector3(3.5f * (Height(x - 1, y) - Height(x + 1, y)), 3.5f * (Height(x, y - 1) - Height(x, y + 1)), 1f).normalized;
                    pixels[y * size + x] = new Color(0.5f + 0.5f * normal.x, 0.5f + 0.5f * normal.y, 0.5f + 0.5f * normal.z, 1f);
                }
            }
            brickRelief = new Texture2D(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "RealisticFiring firebrick relief",
                wrapMode = TextureWrapMode.Mirror,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            brickRelief.SetPixels(pixels);
            brickRelief.Apply(true, true);
        }

        private void LateUpdate()
        {
            var show = Main.Settings != null && Main.Settings.ShowZonedCoalBed && bed != null && bed.Ready && ArchShown > 0.5f;
            if (archRenderer != null && archRenderer.enabled != show)
                archRenderer.enabled = show;
            if (!show)
                return;
            if (builtVersion != FireboxTestBed.TuningVersion)
                Build();

            // The bricks follow the fire under them: quickly on the test bed
            // with no lag, else over minutes, slower cooling than heating.
            var target = Target();
            var lag = Mathf.Clamp01(ArchLag);
            if (!heated || lag <= 0f)
                heat = target;
            else
                heat += (target - heat) * (1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(lag * (target > heat ? ArchHeatTime : ArchCoolTime), 0.01f)));
            heated = true;
            material.SetFloat(ArchHeatId, heat);
            material.SetFloat(ArchGlowId, ArchGlow);
        }

        // The heat the bricks come to over this fire: the coal's under them
        // and the flames against them, each zone by its share under the arch.
        private float Target()
        {
            var lip = LipAlongBed;
            var sum = 0f;
            var weights = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var row = GrateLayout.Row(zone);
                var weight = Mathf.Max(0f, (row + 1f) / GrateLayout.Rows - Mathf.Max((float)row / GrateLayout.Rows, lip));
                if (weight <= 0f)
                    continue;
                sum += weight * (ArchHeat * Mathf.Pow(Mathf.Clamp01(bed.ZoneHeat(zone)), Mathf.Max(ArchHeatCurve, 0.1f)) + ArchFlameHeat * (volume != null ? volume.ZoneStrength(zone) : 0f));
                weights += weight;
            }
            return weights > 0f ? Mathf.Clamp01(sum / weights) : 0f;
        }

        private void OnDestroy()
        {
            if (volume != null && volume.Arch == this)
                volume.Arch = null;
            if (holder != null)
                Destroy(holder.gameObject);
            if (mesh != null)
                Destroy(mesh);
            if (material != null)
                Destroy(material);
        }
    }
}
