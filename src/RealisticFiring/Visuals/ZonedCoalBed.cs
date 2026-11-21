using System.Collections.Generic;
using RealisticFiring.Core;
#if !PUBLIC_RELEASE
using RealisticFiring.Diagnostics;
#endif
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Shows the realistic fire's zones inside the firebox, in place of the
    /// stock coal pile (one mesh the game stretches a little with the coal
    /// level). With the effects bundle, one continuous bed of coal lumps
    /// covers the grate, as deep at each point as the zones around it hold,
    /// and drawn by its state there: black green coal, coal glowing from dull
    /// red to white-hot as it burns, grey ash, dark glassy clinker, and a
    /// thin, burning spot glowing white where the air rushes through.
    /// Without the bundle, nine copies of the stock pile stand as deep as
    /// their zones and glow with their burn rate. Either way the zone a loaded
    /// shovel is aimed at pulses blue.
    /// </summary>
    internal sealed partial class ZonedCoalBed : MonoBehaviour
    {
        // Height of a zone's coal over the stock pile's, per even share of capacity.
        private const float MinHeightScale = 0.08f;
        private const float MaxHeightScale = 1.6f;

        // Below this a zone shows as a gap in the bed, in kg.
        private const float EmptyZoneMass = 0.2f;

        // The embers show only where some zone has more heat than this.
        private const float ColdHeat = 0.001f;

        // The bed slopes down to the grate over this share of its length at
        // the door end, where the door looks down on its edge.
        internal const float DoorSlope = 0.07f;

        // Under this share of an even share of coal a burning zone is thin:
        // the draft rushes through and it burns white-hot.
        private const float ThinShare = 0.3f;

        // How far ash holds a zone's glow below white: by this much at a
        // zone that is all ash.
        private const float DirtyFireDulling = 0.55f;

        // A zone with this share of its coal burning glows all through; the
        // green coal in it lies as dark lumps on the fire. Stoked coal keeps
        // about a third of a working bed green, and a fresh round more.
        private const float LitShare = 0.5f;

        // Over a forced fire green coal catches from below almost at once:
        // at full forcing only this share of it still shows dark.
        private const float ForcedGreenShows = 0.5f;

        // The drawn work waits while nobody can see the coal: behind a shut
        // door, or farther off than the firebox's volume is drawn (a loaded
        // cab's bed rebuilt its mesh and relief behind a shut door;
        // performance review, 2026-10-25). The heaps land, burn, and slump
        // all the while, and the bed is remade the moment it can be seen.
        private const float SeenDoor = 0.01f;
        private const float SeenWithin = 15f;

        // A look into the firebox is followed this far a step, and no further, in m.
        private const float AimStep = 0.02f;
        private const float AimReach = 6f;

        // How fast the coal's glow follows its burning, s: rising, and falling as it cools.
        internal static float GlowRises = 1.5f;
        internal static float GlowFalls = 8f;

        // The beds by their firebox, for the shovel's and the fire iron's aim.
        private static readonly Dictionary<RealisticFirebox, ZonedCoalBed> ByFirebox =
            new Dictionary<RealisticFirebox, ZonedCoalBed>();

        private readonly float[] heights = new float[GrateLayout.ZoneCount];
        private readonly float[] heat = new float[GrateLayout.ZoneCount];
        private readonly float[] green = new float[GrateLayout.ZoneCount];
        private readonly float[] cover = new float[GrateLayout.ZoneCount];
        private readonly float[] forcing = new float[GrateLayout.ZoneCount];
        private float fireForcing;
        private readonly float[] ash = new float[GrateLayout.ZoneCount];
        private readonly float[] clinker = new float[GrateLayout.ZoneCount];

        private readonly float[] empty = new float[GrateLayout.ZoneCount];

        // The nine tiles, without the effects bundle.
        private readonly Transform[] tiles = new Transform[GrateLayout.ZoneCount];
        private readonly Renderer[] tileRenderers = new Renderer[GrateLayout.ZoneCount];
        private readonly Material[] tileMaterials = new Material[GrateLayout.ZoneCount];

        // The continuous bed, with the effects bundle.
        private Transform effects;
        private Transform bedMesh;
        private MeshRenderer bedRenderer;
        private MeshRenderer emberRenderer;
        private Mesh mesh;
        private Mesh emberMesh;
        private Vector3[] emberVertices;
        private Vector3[] vertices;
        private Color[] colors;
        private List<Vector2> bare;
        private float[] lumps;
        private float[] glowJitter;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock emberProperties;

        private RealisticFirebox firebox;
        private Renderer stockCoal;
        private Transform pivot;
        // Where the bed stands under the pivot's parent: the empty pile's place,
        // raised onto a floor that hides it.
        private Vector3 rest;
        private float floorLift;
        private string liftReason = "";
        private Vector3 fullScale;
        // The bed's frame under the pivot's parent (x across the car, y up,
        // z along it), and which of the pivot's axes each of those is.
        private Quaternion frame;
        private int[] axisOf = { 0, 1, 2 };
        private Quaternion toPivot = Quaternion.identity;
        private Bounds bed;
        private float zoneShare;

        private float nextVisibleUploadAt;
        private bool surfaceReady;

        /// <summary>Whether the camera stands where the box can be seen into (FireboxVisibility.FromCab), this frame.</summary>
        public bool FromCab { get; private set; } = true;

        /// <summary>
        /// Whether the coal can be seen now: its door open (or none) and the
        /// camera near enough, or a firebox capture running.
        /// </summary>
        public bool Seen
        {
            get
            {
#if !PUBLIC_RELEASE
                if (FireboxCapture.KeepsVisible)
                    return true;
#endif
                if (firebox != null && firebox.HasDoorPort && firebox.DoorOpening <= SeenDoor)
                    return false;
                var camera = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
                if (camera == null || pivot == null)
                    return true;
                if ((camera.transform.position - pivot.position).sqrMagnitude >= SeenWithin * SeenWithin)
                    return false;
                return FireboxVisibility.InView(camera, bedRenderer, surfaceReady);
            }
        }

        /// <summary>Whether the bed has been built and placed.</summary>
        public bool Ready => pivot != null;

 public static bool TryGet(RealisticFirebox owner, out ZonedCoalBed bed) =>
            ByFirebox.TryGetValue(owner, out bed) && bed != null && bed.Ready;

        /// <summary>The stock coal pile the bed stands in for (hidden while the bed shows).</summary>
        public Renderer StockCoal => stockCoal;

        /// <summary>The bed's box in its own units (its mesh's frame), across, up, along.</summary>
        internal Bounds BedBox => bed;

        /// <summary>The bed mesh's scale under the effects, its units to the pivot's.</summary>
        internal Vector3 FullScale => fullScale;

        /// <summary>The continuous bed's renderer, or null (the nine stock piles without the bundle).</summary>
        internal MeshRenderer BedRenderer => bedRenderer;

        /// <summary>The firebox's effects (flames and smoke live under it), or null without the bundle.</summary>
        public Transform Effects => effects;

        /// <summary>Where a zone's coal stands now, as a share of the stock pile's height.</summary>
        public float ZoneHeight(GrateZone zone) => heights[(int)zone];

        /// <summary>How much of each zone shows grey ash, 0 to 1.</summary>
        public float ZoneAsh(GrateZone zone) => ash[(int)zone];

        /// <summary>
        /// Each zone's coal heat as the bed shows it, 0 black to 1 white-hot.
        /// Wood draws its own char and flames (FuelFireboxIntake) and lights
        /// the box through FireboxLighting: its heat here glowed bare bars.
        /// </summary>
        public float ZoneHeat(GrateZone zone) => heat[(int)zone];

        /// <summary>Each zone's share of green coal.</summary>
        public float ZoneGreen(GrateZone zone) => green[(int)zone];

        /// <summary>How hard each zone burns for its grate, 0 idle to 1 forced.</summary>
        public float ZoneForcing(GrateZone zone) => forcing[(int)zone];

        /// <summary>How hard the whole fire burns for its grate, 0 idle to 1 forced.</summary>
        public float FireForcing => fireForcing;

        /// <summary>How much of each zone's green coal shows dark on the fire, 0 to 1.</summary>
        public float ZoneCover(GrateZone zone) => cover[(int)zone];

        /// <summary>
        /// A world point in the bed's box: across from left (0) to right (1),
        /// along from the door (0) to the front (1), and up from the grate (0)
        /// to the stock pile's top (1); beyond 0..1 is outside the box.
        /// </summary>
        public Vector3 BedPoint(float across, float along, float up)
        {
            var p = new Vector3(
                Mathf.LerpUnclamped(bed.min.x, bed.max.x, across),
                Mathf.LerpUnclamped(bed.min.y, bed.max.y, up),
                Mathf.LerpUnclamped(bed.min.z, bed.max.z, along));
            return pivot.parent.TransformPoint(rest + frame * Vector3.Scale(fullScale, p));
        }

        /// <summary>
        /// A world point in the bed's box, as <see cref="BedPoint"/> takes it:
        /// across (0 left, 1 right), along (0 the door, 1 the front), and up
        /// (0 the grate, 1 the stock pile's top).
        /// </summary>
        public Vector3 BoxPoint(Vector3 world)
        {
            var local = Quaternion.Inverse(frame) * (pivot.parent.InverseTransformPoint(world) - rest);
            float Share(float value, float scale, float min, float size) =>
                Mathf.Abs(scale) > 1e-6f && Mathf.Abs(size) > 1e-6f ? (value / scale - min) / size : 0.5f;
            return new Vector3(
                Share(local.x, fullScale.x, bed.min.x, bed.size.x),
                Share(local.z, fullScale.z, bed.min.z, bed.size.z),
                Share(local.y, fullScale.y, bed.min.y, bed.size.y));
        }

        /// <summary>
        /// The drawn bed's surface at a point (across, along as in
        /// <see cref="BedPoint"/>), as a share of the stock pile's height:
        /// the zones' heights blended between their centers and tapered at
        /// the door, without the lumps' relief. Whatever stands on the coal
        /// stands on this, not on its zone's flat height, so it does not float
        /// over a thinner neighbor or sink into a thicker one.
        /// </summary>
        public float SurfaceHeight(float across, float along) =>
            Mathf.Max(0f, Sample(heights, across, along) * Mathf.Max(0.2f, 1f + Grid(swells, across, along)) + Grid(heaped, across, along))
            * Mathf.SmoothStep(0f, 1f, along / DoorSlope);

        /// <summary>
        /// The bed's surface without its relief: the zones' heights blended
        /// and tapered at the door. What a shader adds the relief to
        /// (<see cref="ReliefTexture"/>).
        /// </summary>
        public float SmoothHeight(float across, float along) =>
            Sample(heights, across, along) * Mathf.SmoothStep(0f, 1f, along / DoorSlope);

        /// <summary>
        /// Where a look along <paramref name="ray"/> meets the coal as drawn:
        /// across the bed (0 left, 1 right) and along it (0 the door, 1 the
        /// front), as <see cref="BedPoint"/> takes them. The grate aim, a
        /// setting (the only aim from 1.8.1 to 1.10; back on a player's
        /// request, 2026-10-16). A look over the coal to a wall or the
        /// tubeplate takes the last place over the bed it passed. False where
        /// it never passes over the bed.
        /// </summary>
        public bool TryAim(Ray ray, out float across, out float along)
        {
            across = along = 0f;
            if (!Ready || pivot.parent == null)
                return false;
            var over = false;
            for (var t = 0f; t <= AimReach; t += AimStep)
            {
                var p = BoxPoint(ray.GetPoint(t));
                if (p.x < 0f || p.x > 1f || p.y < 0f || p.y > 1f)
                {
                    if (over)
                        break;
                    continue;
                }
                over = true;
                across = p.x;
                along = p.y;
                if (p.z <= SurfaceHeight(across, along))
                    return true;
            }
            return over;
        }

        private void LateUpdate()
        {
#if !PUBLIC_RELEASE
            var start = PerfMeter.Start();
#endif
            UpdateBed();
#if !PUBLIC_RELEASE
            PerfMeter.Stop(PerfSection.CoalBed, start);
#endif
        }

        private void UpdateBed()
        {
            if (stockCoal == null || pivot == null)
                return;

            var shown = Main.Settings.ShowZonedCoalBed;
            stockCoal.enabled = !shown;
            FromCab = FireboxVisibility.FromCab(firebox.Car, PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main);
            if (bedRenderer != null)
                bedRenderer.enabled = shown && FromCab;
            if (emberRenderer != null)
                emberRenderer.enabled = shown && FromCab;
            if (!shown)
            {
                foreach (var tile in tileRenderers)
                {
                    if (tile != null)
                        tile.enabled = false;
                }
                return;
            }

            var fire = firebox.Model;
            var aim = firebox.HoverAim;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(Time.time) ? aim.Value.Zone : (GrateZone?)null;
            var pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 8f);
            fireForcing = fire.FireForcing;

            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                var coal = zone.Coal;
                heights[i] = coal < EmptyZoneMass ? 0f : Mathf.Clamp(coal / zoneShare, MinHeightScale, MaxHeightScale);
                empty[i] = coal < EmptyZoneMass ? 1f : 0f;

                // Lit coal glows even when burning slowly and brighter as it is
                // forced. The back draws hardest, but a forced fire lights the
                // whole bed. Green coal dims a zone only where it smothers it.
                var own = fire.ZoneForcing((GrateZone)i);
                forcing[i] = Mathf.Max(own, 0.5f * (own + fireForcing));
                var lit = fire.FireOn && coal > 0f ? Mathf.Clamp01(zone.Burning / coal / LitShare) : 0f;
                var burn = lit * (0.2f + 0.8f * forcing[i]);
                // Ash lies at the bottom of the fire and chokes it: a dirty fire
                // will not burn white, "a red fire" (Santa Fe 1921 p.34).
                var dirt = Mathf.Clamp01(1.5f * zone.Ash / (zone.Ash + coal + 0.1f));
                // A thin, burning patch over a clean grate is a hole: the draft
                // rushes through it white-hot. Over ash it is only dying.
                var thin = fire.FireOn && zone.Burning > 0f ? 1f - Mathf.Clamp01(coal / (ThinShare * zoneShare)) : 0f;
                // The coal's glow follows its burning, quickly up and slowly
                // down: a starved bed's incandescent coke dims over seconds as
                // it cools, not at once (the council on the over-fire jets,
                // 2026-11-14: with the grate starved it went dull in 5 to 8 s).
                var glowing = Mathf.Min(Mathf.Clamp01(Mathf.Lerp(burn, 1f, 0.7f * thin * (1f - dirt))), 1f - DirtyFireDulling * dirt);
                heat[i] += (glowing - heat[i]) * (1f - Mathf.Exp(-Time.deltaTime / (glowing < heat[i] ? GlowFalls : GlowRises)));
                green[i] = coal > 0f ? zone.Fresh / coal : 0f;
                cover[i] = green[i] * (1f - (1f - ForcedGreenShows) * forcing[i] * forcing[i]);
                // Grey ash shows only where the fire has burned down to it.
                ash[i] = dirt * (1f - Mathf.Clamp01(coal / (ThinShare * zoneShare)));
                clinker[i] = Mathf.Clamp01(1.2f * zone.Clinker / (zone.Clinker + coal + 0.1f));
            }

            if (emberRenderer != null)
            {
                var hottest = 0f;
                for (var i = 0; i < heat.Length; i++)
                    hottest = Mathf.Max(hottest, heat[i]);
                emberRenderer.enabled = hottest > ColdHeat && FromCab;
            }

            var seen = Seen;
            UpdateRelief(fire, seen);
            if (!seen)
            {
                nextVisibleUploadAt = 0f;
                nextUnderLumpsAt = 0f;
                return;
            }
            if (Time.unscaledTime < nextVisibleUploadAt)
                return;
            nextVisibleUploadAt = Time.unscaledTime + FireboxDetail.For(Main.Settings.FireboxQuality).UploadInterval;
            if (mesh != null)
                UpdateMesh(fire, aimed, pulse);
            else
                UpdateTiles(fire, aimed, pulse);
        }

        // A zone value at a point of the bed, blended smoothly between zone centers.
        private static float Sample(float[] values, float across, float along)
        {
            var x = Mathf.Clamp(across * GrateLayout.Columns - 0.5f, 0f, GrateLayout.Columns - 1f);
            var z = Mathf.Clamp(along * GrateLayout.Rows - 0.5f, 0f, GrateLayout.Rows - 1f);
            var c0 = Mathf.Min((int)x, GrateLayout.Columns - 2);
            var r0 = Mathf.Min((int)z, GrateLayout.Rows - 2);
            var fx = Mathf.SmoothStep(0f, 1f, x - c0);
            var fz = Mathf.SmoothStep(0f, 1f, z - r0);
            float V(int r, int c) => values[(int)GrateLayout.At(r, c)];
            var front = Mathf.Lerp(V(r0, c0), V(r0, c0 + 1), fx);
            var back = Mathf.Lerp(V(r0 + 1, c0), V(r0 + 1, c0 + 1), fx);
            return Mathf.Lerp(front, back, fz);
        }

        private void OnDestroy()
        {
            DisposeRelief();
            if (firebox != null && ByFirebox.TryGetValue(firebox, out var listed) && listed == this)
                ByFirebox.Remove(firebox);
            foreach (var material in tileMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
            foreach (var tile in tiles)
            {
                if (tile != null)
                    Destroy(tile.gameObject);
            }
            if (effects != null)
                Destroy(effects.gameObject);
            if (mesh != null)
                Destroy(mesh);
            if (emberMesh != null)
                Destroy(emberMesh);
            if (stockCoal != null)
                stockCoal.enabled = true;
        }
    }
}
