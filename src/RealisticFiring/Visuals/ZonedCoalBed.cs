using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
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
    internal sealed class ZonedCoalBed : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int AimRectId = Shader.PropertyToID("_AimRect");
        private static readonly int AimColorId = Shader.PropertyToID("_AimColor");
        private static readonly int FlickerSpeedId = Shader.PropertyToID("_FlickerSpeed");
        private static readonly int TilingId = Shader.PropertyToID("_Tiling");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly Color AimColor = new Color(0.25f, 0.6f, 1f);

        // Height of a zone's coal over the stock pile's, per even share of capacity.
        private const float MinHeightScale = 0.08f;
        private const float MaxHeightScale = 1.6f;

        // Below this a zone shows as a gap in the bed, in kg.
        private const float EmptyZoneMass = 0.2f;

        // Each pile covers this much more than its zone, so neighbors blend
        // instead of leaving a trough where the stock pile's edges slope down.
        private const float Overlap = 1.3f;

        // A stock pile shallower than this, in m, is flat (a quad, as on the
        // 844, or the S282's 2.5 cm pile): it gets the nominal depth at an
        // even share of coal, and a sliding one hides well under the grate.
        private const float FlatPile = 0.05f;

        // A stock pile with no depth (a flat quad, as on the 844) gets this
        // much at an even share of coal, in m.
        private const float NominalDepth = 0.25f;

        // A floor over the empty pile's base, up to this far over its top and
        // reaching this near its sides, is the grate the bed rests on (the
        // S060's lies 8 mm over its empty pile), sunk this far into it so an
        // empty zone hides under it, all in m.
        private const float FloorReach = 0.03f;
        private const float FloorFit = 0.05f;
        private const float FloorSink = 0.005f;

        // The continuous bed's grid, and its coal lumps' relief as a share of
        // the coal's depth there.
        private const int GridAcross = 16;
        private const int GridAlong = 32;
        private const float Relief = 0.3f;

        // The glowing embers lie this far under the dark lumps on top, in m.
        private const float EmberDepth = 0.08f;

        // The embers stay at least this far under the coal layer, in m, and
        // show only where some zone has more heat than this.
        private const float EmberUnder = 0.01f;
        private const float ColdHeat = 0.001f;

        // The coal texture's repeats across the bed's width: about 5 cm lumps
        // on a metre-wide grate, with smaller pieces between.
        private const float LumpsAcross = 2f;

        // The bed slopes down to the grate over this share of its length at
        // the door end, where the door looks down on its edge.
        private const float DoorSlope = 0.07f;

        // Under this share of an even share of coal a burning zone is thin:
        // the draft rushes through and it burns white-hot.
        private const float ThinShare = 0.3f;

        // How far ash holds a zone's glow below white: by this much at a
        // zone that is all ash.
        private const float DirtyFireDulling = 0.55f;

        // The smallest grate a bed is taken to have, in m², should a stock
        // pile measure flat. How hard each zone burns for its grate (its
        // forcing) comes from the fire model, which the bed tells the area.
        private const float MinGrateArea = 0.45f;

        // A zone with this share of its coal burning glows all through; the
        // green coal in it lies as dark lumps on the fire. Stoked coal keeps
        // about a third of a working bed green, and a fresh round more.
        private const float LitShare = 0.5f;

        // Over a forced fire green coal catches from below almost at once:
        // at full forcing only this share of it still shows dark.
        private const float ForcedGreenShows = 0.5f;

        // Lit coal (tiles): dull red when burning slowly, orange when working, yellow-white when forced.
        private static readonly Color DullRed = new Color(1f, 0.35f, 0.2f);
        private static readonly Color Orange = new Color(1f, 0.6f, 0.3f);
        private static readonly Color WhiteHot = new Color(1f, 0.88f, 0.7f);

        // A pile mesh turned more than this under its pivot, in degrees, is
        // measured by its corners: its box's box is too big.
        private const float MeshTurned = 1f;

        // Rows run along the car and columns across it, on whichever of the
        // pile's axes point that way (the 8F's pile is turned: its forward up
        // the car, its right along it), each within 30° (cos 30°). A sloping
        // grate tilts the pile, which is fine.
        private const float MinAxisAlignment = 0.866f;

        private readonly float[] heights = new float[GrateLayout.ZoneCount];
        private readonly float[] heat = new float[GrateLayout.ZoneCount];
        private readonly float[] green = new float[GrateLayout.ZoneCount];
        private readonly float[] cover = new float[GrateLayout.ZoneCount];
        private readonly float[] forcing = new float[GrateLayout.ZoneCount];
        private float fireForcing;
        private readonly float[] ash = new float[GrateLayout.ZoneCount];
        private readonly float[] clinker = new float[GrateLayout.ZoneCount];

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

        private readonly float[] empty = new float[GrateLayout.ZoneCount];
        private System.Collections.Generic.List<Vector2> bare;
        private float[] lumps;
        private float[] glowJitter;
        private MaterialPropertyBlock properties;
        private MaterialPropertyBlock emberProperties;

        // The beds by their firebox, for the shovel's and the fire iron's aim.
        private static readonly System.Collections.Generic.Dictionary<RealisticFirebox, ZonedCoalBed> ByFirebox =
            new System.Collections.Generic.Dictionary<RealisticFirebox, ZonedCoalBed>();

        // A look into the firebox is followed this far a step, and no further, in m.
        private const float AimStep = 0.02f;
        private const float AimReach = 6f;

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

        /// <summary>
        /// Builds the bed on <paramref name="fire"/>'s coal pile. Returns a
        /// sentence for the log: what was built, or why the stock pile stays.
        /// </summary>
        public static string Attach(Fire fire, TrainCar car, RealisticFirebox firebox)
        {
            var scaler = fire.GetComponentsInChildren<IndicatorScaler>(true)
                .FirstOrDefault(s => s.indicatorToScale != null && s.indicatorToScale.GetComponentInChildren<MeshRenderer>(true) != null);
            // The JK3 raises a fixed-size pile with a slider alone. Scaled
            // piles may also slide; either layout supplies an empty place
            // and a full-size mesh for the bed.
            var sliders = fire.GetComponentsInChildren<IndicatorSlider>(true);
            var slider = scaler != null
                ? sliders.FirstOrDefault(s => s.pointer == scaler.indicatorToScale)
                : sliders.FirstOrDefault(s => s.pointer != null && s.pointer.GetComponentInChildren<MeshRenderer>(true) != null);
            var pivot = scaler != null ? scaler.indicatorToScale : slider?.pointer;
            if (pivot == null)
                return "no scaled or sliding coal pile under the fire; keeping the stock coal";
            var coal = pivot.GetComponentInChildren<MeshRenderer>(true);
            var filter = coal.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return $"coal pile '{coal.name}' has no mesh; keeping the stock coal";
            var (axisOf, toPivot) = BedAxes(car, pivot);
            var frameRotation = pivot.rotation * toPivot;
            var forward = car.transform.InverseTransformDirection(frameRotation * Vector3.forward);
            var right = car.transform.InverseTransformDirection(frameRotation * Vector3.right);
            if (forward.z < MinAxisAlignment || right.x < MinAxisAlignment)
            {
                var grate = StockPileBox(car, pivot, coal, filter.sharedMesh, slider);
                if (grate.HasValue)
                    firebox.SetGrateBox(grate.Value);
                return $"coal pile axes do not run along the car (forward {forward}, right {right}); keeping the stock coal" +
                       (grate.HasValue ? $", the grate taken from the pile: {grate.Value.min:F2} to {grate.Value.max:F2} m" : "");
            }

            // On some locos (the S060, the S282) the stock pile also slides up
            // as coal goes in; the bed shows the coal itself, so it stands
            // where the pile is when the box is empty.
            var bed = fire.gameObject.AddComponent<ZonedCoalBed>();
            var scale = scaler != null ? scaler.endScale : pivot.localScale;
            bed.axisOf = axisOf;
            bed.toPivot = toPivot;
            var kind = bed.Build(firebox, pivot, scale, coal, filter.sharedMesh, slider);
            // The pile lies on the grate, so its tilt is the grate's slope; a
            // moving loco shakes coal down it.
            var slope = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            firebox.SetGrateSlope(slope);
            if (car != null)
            {
                var box = new Bounds(car.transform.InverseTransformPoint(bed.BedPoint(0f, 0f, 0f)), Vector3.zero);
                for (var c = 1; c < 8; c++)
                    box.Encapsulate(car.transform.InverseTransformPoint(bed.BedPoint(c & 1, (c >> 1) & 1, (c >> 2) & 1)));
                firebox.SetGrateBox(box);
            }
            return $"zoned coal bed ({kind}) built from '{coal.name}', {bed.bed.size.x * bed.fullScale.x:F2} m wide " +
                   $"and {bed.bed.size.z * bed.fullScale.z:F2} m long, grate sloping {slope:F1}° " +
                   $"(up toward the front when positive), turned {Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg:F1}°" +
                   (slider != null ? ", at the sliding pile's empty place" : "") +
                   (bed.floorLift > 0f ? $", raised {bed.floorLift * 100f:F0} cm {bed.liftReason}" : "");
        }

        // Which of the pivot's axes runs across the car (the bed's x), up (y),
        // and along it (z), each signed to point right, up, and forward, and
        // the turn from the bed's frame to the pivot's.
        private static (int[] AxisOf, Quaternion ToPivot) BedAxes(TrainCar car, Transform pivot)
        {
            Vector3 Local(Vector3 carDirection) => pivot.InverseTransformDirection(car.transform.TransformDirection(carDirection));
            int Largest(Vector3 v, int skip) => Enumerable.Range(0, 3).Where(i => i != skip)
                .OrderByDescending(i => Mathf.Abs(v[i])).First();
            Vector3 Axis(int index, float sign)
            {
                var axis = Vector3.zero;
                axis[index] = sign < 0f ? -1f : 1f;
                return axis;
            }
            var rightLocal = Local(Vector3.right);
            var forwardLocal = Local(Vector3.forward);
            var across = Largest(rightLocal, -1);
            var along = Largest(forwardLocal, across);
            var up = 3 - across - along;
            var x = Axis(across, rightLocal[across]);
            var z = Axis(along, forwardLocal[along]);
            return (new[] { across, up, along }, Quaternion.LookRotation(z, Vector3.Cross(z, x)));
        }

        // A vector along the pivot's axes, as along the bed's (scales), and back.
        private Vector3 FromPivot(Vector3 v) => new Vector3(v[axisOf[0]], v[axisOf[1]], v[axisOf[2]]);

        private Vector3 ToPivot(Vector3 v)
        {
            var result = Vector3.zero;
            for (var i = 0; i < 3; i++)
                result[axisOf[i]] = v[i];
            return result;
        }

        // The stock pile where it lies with the box empty, at its slider's
        // start, in the car's frame.
        private static Bounds? StockPileBox(TrainCar car, Transform pivot, Renderer coal, Mesh mesh, IndicatorSlider slider)
        {
            if (car == null)
                return null;
            var lift = slider != null && pivot.parent != null ? pivot.parent.TransformVector(slider.startPosition - pivot.localPosition) : Vector3.zero;
            var b = mesh.bounds;
            var box = new Bounds();
            for (var c = 0; c < 8; c++)
            {
                var corner = coal.transform.TransformPoint(new Vector3(
                    (c & 1) == 0 ? b.min.x : b.max.x,
                    (c & 2) == 0 ? b.min.y : b.max.y,
                    (c & 4) == 0 ? b.min.z : b.max.z)) + lift;
                var local = car.transform.InverseTransformPoint(corner);
                if (c == 0)
                    box = new Bounds(local, Vector3.zero);
                else
                    box.Encapsulate(local);
            }
            return box;
        }

        private string Build(RealisticFirebox owner, Transform coalPivot, Vector3 scale, Renderer coal, Mesh stockMesh, IndicatorSlider slider)
        {
            firebox = owner;
            ByFirebox[owner] = this;
            stockCoal = coal;
            pivot = coalPivot;
            frame = pivot.localRotation * toPivot;
            fullScale = FromPivot(scale);
            rest = slider != null ? slider.startPosition : pivot.localPosition;
            zoneShare = owner.Model.Parameters.MaxCoalCapacity / GrateLayout.ZoneCount;

            var local = stockMesh.bounds;
            var fromPivot = Quaternion.Inverse(toPivot);
            Vector3 InBed(Vector3 meshPoint) => fromPivot * pivot.InverseTransformPoint(coal.transform.TransformPoint(meshPoint));
            bed = new Bounds(InBed(local.center), Vector3.zero);
            var turned = Quaternion.Angle(pivot.rotation, coal.transform.rotation) > MeshTurned;
            var corners = turned && stockMesh.isReadable ? stockMesh.vertices : null;
            if (corners != null && corners.Length > 0)
            {
                bed = new Bounds(InBed(corners[0]), Vector3.zero);
                foreach (var corner in corners)
                    bed.Encapsulate(InBed(corner));
            }
            else
            {
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z);
                    bed.Encapsulate(InBed(corner));
                }
            }
            var depth = bed.size.y * Mathf.Abs(fullScale.y);
            var flat = depth < FlatPile;
            // The bed stands on a floor laid over the empty pile where there
            // is one (the S060). With none, a sliding pile of real depth lies
            // empty under the grate with its top at it (the JK3's, 14 cm
            // deep), so the bed stands on that top; a flat one (the S282's,
            // 2.5 cm) hides well under the grate when empty, 0.3 m there, so
            // the bed stands where the full pile stands, as the cab shows a
            // full firebox.
            var onFloor = FloorLift();
            var up = frame * Vector3.up;
            if (onFloor > 0f)
            {
                floorLift = onFloor;
                liftReason = "onto the floor over it";
                rest += up * onFloor;
            }
            else if (slider != null && flat)
            {
                floorLift = Vector3.Dot(slider.endPosition - slider.startPosition, up);
                liftReason = "to where the full flat pile stands";
                rest = slider.endPosition;
            }
            else if (slider != null)
            {
                floorLift = Mathf.Max(0f, depth - FloorSink);
                liftReason = "to the empty pile's top";
                rest += up * floorLift;
            }
            if (flat && Mathf.Abs(fullScale.y) > 1e-4f)
                bed.max = new Vector3(bed.max.x, bed.min.y + NominalDepth / Mathf.Abs(fullScale.y), bed.max.z);
            owner.SetGrateArea(Mathf.Max(MinGrateArea, Mathf.Abs(bed.size.x * fullScale.x * bed.size.z * fullScale.z)));

            var prefab = PlumeAssets.FireboxPrefab;
            if (prefab != null && BuildMesh(prefab))
                return "continuous";
            BuildTiles(coal);
            return "nine tiles";
        }

        // How far to raise the bed onto a floor laid over the empty pile, in m
        // along the pile's up: a mesh beside the pile (the firebox's own) that
        // spans the bed and whose underside lies over the pile's base, no
        // higher than just over the stock pile's top. Measured before a flat
        // pile is given its nominal depth.
        private float FloorLift()
        {
            var inverse = Quaternion.Inverse(frame);
            Vector3 AtRest(Vector3 world) => inverse * (pivot.parent.InverseTransformPoint(world) - rest);
            var scale = new Vector3(Mathf.Abs(fullScale.x), Mathf.Abs(fullScale.y), Mathf.Abs(fullScale.z));
            var pile = new Bounds(Vector3.Scale(scale, bed.center), Vector3.Scale(scale, bed.size));
            var lift = 0f;
            foreach (var filter in pivot.parent.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || filter.transform.IsChildOf(pivot))
                    continue;
                var local = mesh.bounds;
                var box = new Bounds(AtRest(filter.transform.TransformPoint(local.center)), Vector3.zero);
                for (var i = 0; i < 8; i++)
                {
                    box.Encapsulate(AtRest(filter.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z))));
                }
                var spans = box.min.x <= pile.min.x + FloorFit && box.max.x >= pile.max.x - FloorFit
                            && box.min.z <= pile.min.z + FloorFit && box.max.z >= pile.max.z - FloorFit;
                if (spans && box.min.y > pile.min.y && box.min.y <= pile.max.y + FloorReach)
                    lift = Mathf.Max(lift, box.min.y - FloorSink - pile.min.y);
            }
            return lift;
        }

        // The continuous bed: a grid over the bed's box under the effects
        // prefab, which sits at the pivot unscaled (its flames and smoke stay
        // round); the bed's mesh is scaled into the pivot's units.
        private bool BuildMesh(GameObject prefab)
        {
            var instance = Instantiate(prefab, pivot.parent, false);
            instance.name = "[RealisticFiring firebox]";
            effects = instance.transform;
            effects.localPosition = rest;
            effects.localRotation = frame;
            effects.localScale = Vector3.one;
            bedMesh = effects.Find("Bed");
            bedRenderer = bedMesh != null ? bedMesh.GetComponent<MeshRenderer>() : null;
            var filter = bedMesh != null ? bedMesh.GetComponent<MeshFilter>() : null;
            if (bedRenderer == null || filter == null)
            {
                Destroy(instance);
                effects = null;
                return false;
            }
            bedMesh.localPosition = Vector3.zero;
            bedMesh.localRotation = Quaternion.identity;
            bedMesh.localScale = fullScale;

            var count = (GridAcross + 1) * (GridAlong + 1);
            vertices = new Vector3[count];
            colors = new Color[count];
            bare = new System.Collections.Generic.List<Vector2>(new Vector2[count]);
            lumps = new float[count];
            glowJitter = new float[count];
            var uvs = new Vector2[count];
            var triangles = new System.Collections.Generic.List<int>();
            var random = new System.Random(588030);
            for (var j = 0; j <= GridAlong; j++)
            for (var i = 0; i <= GridAcross; i++)
            {
                var index = j * (GridAcross + 1) + i;
                uvs[index] = new Vector2((float)i / GridAcross, (float)j / GridAlong);
                lumps[index] = (float)(random.NextDouble() * 2.0 - 1.0);
                glowJitter[index] = (float)random.NextDouble();
                if (i < GridAcross && j < GridAlong)
                {
                    var a = index;
                    var b = index + 1;
                    var c = index + GridAcross + 1;
                    var d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            }

            mesh = new Mesh { name = "RealisticFiring fire bed" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.SetTriangles(triangles, 0);
            filter.sharedMesh = mesh;

            // The embers under the lumps: the same grid, a little lower.
            var embers = effects.Find("Embers");
            emberRenderer = embers != null ? embers.GetComponent<MeshRenderer>() : null;
            var emberFilter = embers != null ? embers.GetComponent<MeshFilter>() : null;
            if (emberRenderer != null && emberFilter != null)
            {
                embers.localPosition = Vector3.zero;
                embers.localRotation = Quaternion.identity;
                embers.localScale = fullScale;
                emberVertices = new Vector3[count];
                emberMesh = new Mesh { name = "RealisticFiring embers" };
                emberMesh.MarkDynamic();
                emberMesh.vertices = emberVertices;
                emberMesh.uv = uvs;
                emberMesh.colors = colors;
                emberMesh.SetTriangles(triangles, 0);
                emberFilter.sharedMesh = emberMesh;
            }
            properties = new MaterialPropertyBlock();
            emberProperties = new MaterialPropertyBlock();
            return true;
        }

        private void BuildTiles(Renderer coal)
        {
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var holder = new GameObject($"[RealisticFiring coal {(GrateZone)i}]").transform;
                holder.SetParent(pivot.parent, false);
                holder.localRotation = pivot.localRotation;

                // Same local transform under the holder as the stock pile has under the pivot.
                var copy = Instantiate(coal.gameObject, holder, false);
                foreach (Transform child in copy.transform)
                    Destroy(child.gameObject);
                foreach (var collider in copy.GetComponents<Collider>())
                    Destroy(collider);

                tiles[i] = holder;
                tileRenderers[i] = copy.GetComponent<Renderer>();
                tileRenderers[i].enabled = true;
                tileMaterials[i] = tileRenderers[i].material;
                tileMaterials[i].EnableKeyword("_EMISSION");
            }
        }

        private void LateUpdate()
        {
            var start = PerfMeter.Start();
            UpdateBed();
            PerfMeter.Stop(PerfSection.CoalBed, start);
        }

        private void UpdateBed()
        {
            if (stockCoal == null || pivot == null)
                return;

            var shown = Main.Settings.ShowZonedCoalBed;
            stockCoal.enabled = !shown;
            if (bedRenderer != null)
                bedRenderer.enabled = shown;
            if (emberRenderer != null)
                emberRenderer.enabled = shown;
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
                heat[i] = Mathf.Min(Mathf.Clamp01(Mathf.Lerp(burn, 1f, 0.7f * thin * (1f - dirt))), 1f - DirtyFireDulling * dirt);
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
                emberRenderer.enabled = hottest > ColdHeat;
            }

            if (mesh != null)
                UpdateMesh(fire, aimed, pulse);
            else
                UpdateTiles(fire, aimed, pulse);
        }

        private void UpdateMesh(FireModel fire, GrateZone? aimed, float pulse)
        {
            for (var j = 0; j <= GridAlong; j++)
            {
                var along = (float)j / GridAlong;
                for (var i = 0; i <= GridAcross; i++)
                {
                    var across = (float)i / GridAcross;
                    var index = j * (GridAcross + 1) + i;
                    var height = SurfaceHeight(across, along);
                    var lump = lumps[index] * Relief * Mathf.Min(height, 0.6f);
                    var up = Mathf.Max(0f, height + lump);
                    vertices[index] = new Vector3(
                        Mathf.Lerp(bed.min.x, bed.max.x, across),
                        Mathf.Lerp(bed.min.y, bed.max.y, up),
                        Mathf.Lerp(bed.min.z, bed.max.z, along));
                    var glow = Sample(heat, across, along) * (0.85f + 0.3f * glowJitter[index]);
                    colors[index] = new Color(Mathf.Clamp01(glow), Sample(cover, across, along), Sample(ash, across, along), Sample(clinker, across, along));
                    bare[index] = new Vector2(Sample(empty, across, along), 0f);
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.SetUVs(3, bare);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (emberMesh != null)
            {
                // Always under the coal, even on an empty grate, so the cold coal's
                // surface covers them where both lie flat.
                var drop = EmberDepth / Mathf.Max(1e-4f, Mathf.Abs(fullScale.y));
                var floor = bed.min.y - EmberUnder / Mathf.Max(1e-4f, Mathf.Abs(fullScale.y));
                for (var v = 0; v < vertices.Length; v++)
                    emberVertices[v] = new Vector3(vertices[v].x, Mathf.Max(floor, vertices[v].y - drop), vertices[v].z);
                emberMesh.vertices = emberVertices;
                emberMesh.colors = colors;
                emberMesh.RecalculateBounds();
            }

            bedRenderer.GetPropertyBlock(properties);
            properties.SetFloat(TilingId, LumpsAcross);
            properties.SetFloat(FlickerSpeedId, 0.3f + 1.4f * fireForcing);
            var flames = GetComponent<FireboxFlames>();
            properties.SetFloat(PulseId, flames != null ? flames.Pulse : 0f);
            if (emberRenderer != null)
            {
                emberRenderer.GetPropertyBlock(emberProperties);
                emberProperties.SetFloat(TilingId, LumpsAcross);
                emberProperties.SetFloat(FlickerSpeedId, properties.GetFloat(FlickerSpeedId));
                emberProperties.SetFloat(PulseId, flames != null ? flames.Pulse : 0f);
                emberRenderer.SetPropertyBlock(emberProperties);
            }
            if (aimed is GrateZone zone)
            {
                var c = GrateLayout.Column(zone);
                var r = GrateLayout.Row(zone);
                properties.SetVector(AimRectId, new Vector4(
                    (float)c / GrateLayout.Columns, (float)r / GrateLayout.Rows,
                    (float)(c + 1) / GrateLayout.Columns, (float)(r + 1) / GrateLayout.Rows));
                properties.SetColor(AimColorId, new Color(AimColor.r, AimColor.g, AimColor.b, 1.5f * pulse));
            }
            else
            {
                properties.SetColor(AimColorId, new Color(0f, 0f, 0f, 0f));
            }
            bedRenderer.SetPropertyBlock(properties);
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

        private void UpdateTiles(FireModel fire, GrateZone? aimed, float pulse)
        {
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                var isAimed = aimed == (GrateZone)i;
                var visible = zone.Coal >= EmptyZoneMass || isAimed;
                tileRenderers[i].enabled = visible;
                if (!visible)
                    continue;

                Place(i, Mathf.Clamp(zone.Coal / zoneShare, MinHeightScale, MaxHeightScale));

                var glow = heat[i] * (1f - 0.85f * cover[i]);
                var flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 1.7f, i * 3.1f);
                var emission = FireColor(glow) * (Mathf.Lerp(0.4f, 3.2f, glow) * glow * flicker);
                if (isAimed)
                    emission += AimColor * (1.5f * pulse);
                tileMaterials[i].SetColor(EmissionColorId, emission);
            }
        }

        private static Color FireColor(float glow) =>
            glow < 0.5f ? Color.Lerp(DullRed, Orange, glow * 2f) : Color.Lerp(Orange, WhiteHot, glow * 2f - 1f);

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

        /// <summary>Where a zone's coal stands now, as a share of the stock pile's height.</summary>
        public float ZoneHeight(GrateZone zone) => heights[(int)zone];

        /// <summary>
        /// The drawn bed's surface at a point (across, along as in
        /// <see cref="BedPoint"/>), as a share of the stock pile's height:
        /// the zones' heights blended between their centers and tapered at
        /// the door, without the lumps' relief. Whatever stands on the coal
        /// stands on this, not on its zone's flat height, so it does not float
        /// over a thinner neighbor or sink into a thicker one.
        /// </summary>
        public float SurfaceHeight(float across, float along) =>
            Sample(heights, across, along) * Mathf.SmoothStep(0f, 1f, along / DoorSlope);

        /// <summary>Whether the bed has been built and placed.</summary>
        public bool Ready => pivot != null;

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

 public static bool TryGet(RealisticFirebox owner, out ZonedCoalBed bed) =>
            ByFirebox.TryGetValue(owner, out bed) && bed != null && bed.Ready;

        /// <summary>The stock coal pile the bed stands in for (hidden while the bed shows).</summary>
        public Renderer StockCoal => stockCoal;

        /// <summary>The firebox's effects (flames and smoke live under it), or null without the bundle.</summary>
        public Transform Effects => effects;

        /// <summary>Each zone's heat as the bed shows it, 0 black to 1 white-hot.</summary>
        public float ZoneHeat(GrateZone zone) => heat[(int)zone];

        /// <summary>Each zone's share of green coal.</summary>
        public float ZoneGreen(GrateZone zone) => green[(int)zone];

        /// <summary>How hard each zone burns for its grate, 0 idle to 1 forced.</summary>
        public float ZoneForcing(GrateZone zone) => forcing[(int)zone];

        /// <summary>How hard the whole fire burns for its grate, 0 idle to 1 forced.</summary>
        public float FireForcing => fireForcing;

        /// <summary>How much of each zone's green coal shows dark on the fire, 0 to 1.</summary>
        public float ZoneCover(GrateZone zone) => cover[(int)zone];

        private void Place(int index, float height)
        {
            var zone = (GrateZone)index;
            var across = Overlap / GrateLayout.Columns;
            var along = Overlap / GrateLayout.Rows;
            var x = ZoneCenter(bed.min.x, bed.max.x, GrateLayout.Column(zone), GrateLayout.Columns, across);
            var z = ZoneCenter(bed.min.z, bed.max.z, GrateLayout.Row(zone), GrateLayout.Rows, along);
            var offset = new Vector3(
                x - across * bed.center.x,
                bed.min.y * (1f - height),
                z - along * bed.center.z);
            tiles[index].localPosition = rest + frame * Vector3.Scale(fullScale, offset);
            tiles[index].localScale = ToPivot(new Vector3(fullScale.x * across, fullScale.y * height, fullScale.z * along));
        }

        private static float ZoneCenter(float min, float max, int index, int count, float scale)
        {
            var center = min + (index + 0.5f) * (max - min) / count;
            var halfPile = scale * (max - min) / 2f;
            return Mathf.Clamp(center, min + halfPile, max - halfPile);
        }

        private void OnDestroy()
        {
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
