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

        // A stock pile with no depth (a flat quad, as on the 844) gets this
        // much at an even share of coal, in m.
        private const float NominalDepth = 0.25f;

        // The continuous bed's grid, and its coal lumps' relief as a share of
        // the coal's depth there.
        private const int GridAcross = 16;
        private const int GridAlong = 32;
        private const float Relief = 0.3f;

        // The glowing embers lie this far under the dark lumps on top, in m.
        private const float EmberDepth = 0.08f;

        // The coal texture's repeats across the bed's width: about 5 cm lumps
        // on a metre-wide grate, with smaller pieces between.
        private const float LumpsAcross = 2f;

        // The bed slopes down to the grate over this share of its length at
        // the door end, where the door looks down on its edge.
        private const float DoorSlope = 0.07f;

        // Under this share of an even share of coal a burning zone is thin:
        // the draft rushes through and it burns white-hot.
        private const float ThinShare = 0.3f;

        // A fire is forced, its bed white-hot, at this firing rate in kg per
        // m² of grate an hour (about 120 lb/ft²/h, heavy firing). The stock
        // maximum, a full box burning out in the burn time, is no guide: an
        // S282 working hard burns about a quarter of it.
        private const float ForcedFiringRate = 600f;

        // The smallest grate share a zone is taken to have, in m², should a
        // stock pile measure flat.
        private const float MinZoneGrate = 0.05f;

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

        // Rows run along the pile's forward axis and columns along its right
        // axis, so those must point roughly along and across the car (cos 30°).
        // A sloping grate tilts the pile, which is fine.
        private const float MinAxisAlignment = 0.866f;

        private readonly float[] heights = new float[GrateLayout.ZoneCount];
        private readonly float[] heat = new float[GrateLayout.ZoneCount];
        private readonly float[] green = new float[GrateLayout.ZoneCount];
        private readonly float[] cover = new float[GrateLayout.ZoneCount];
        private readonly float[] forcing = new float[GrateLayout.ZoneCount];
        private float fireForcing;
        private float zoneGrate;
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
        private float[] lumps;
        private float[] glowJitter;
        private MaterialPropertyBlock properties;
        private readonly MaterialPropertyBlock emberProperties = new MaterialPropertyBlock();

        private RealisticFirebox firebox;
        private Renderer stockCoal;
        private Transform pivot;
        private Vector3 fullScale;
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
            if (scaler == null)
                return "no scaled coal pile under the fire; keeping the stock coal";
            var pivot = scaler.indicatorToScale;
            var coal = pivot.GetComponentInChildren<MeshRenderer>(true);
            var filter = coal.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return $"coal pile '{coal.name}' has no mesh; keeping the stock coal";
            var forward = car.transform.InverseTransformDirection(pivot.forward);
            var right = car.transform.InverseTransformDirection(pivot.right);
            if (forward.z < MinAxisAlignment || right.x < MinAxisAlignment)
                return $"coal pile axes do not run along the car (forward {forward}, right {right}); keeping the stock coal";

            var bed = fire.gameObject.AddComponent<ZonedCoalBed>();
            var kind = bed.Build(firebox, scaler, coal, filter.sharedMesh);
            // The pile lies on the grate, so its tilt is the grate's slope; a
            // moving loco shakes coal down it.
            var slope = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            firebox.SetGrateSlope(slope);
            return $"zoned coal bed ({kind}) built from '{coal.name}', {bed.bed.size.x * bed.fullScale.x:F2} m wide " +
                   $"and {bed.bed.size.z * bed.fullScale.z:F2} m long, grate sloping {slope:F1}° " +
                   $"(up toward the front when positive), turned {Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg:F1}°";
        }

        private string Build(RealisticFirebox owner, IndicatorScaler scaler, Renderer coal, Mesh stockMesh)
        {
            firebox = owner;
            stockCoal = coal;
            pivot = scaler.indicatorToScale;
            fullScale = scaler.endScale;
            zoneShare = owner.Model.Parameters.MaxCoalCapacity / GrateLayout.ZoneCount;

            // The pile's extent in the pivot's own units, before the pivot's scale.
            var local = stockMesh.bounds;
            bed = new Bounds(pivot.InverseTransformPoint(coal.transform.TransformPoint(local.center)), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                bed.Encapsulate(pivot.InverseTransformPoint(coal.transform.TransformPoint(corner)));
            }
            var depth = bed.size.y * Mathf.Abs(fullScale.y);
            if (depth < 0.05f && Mathf.Abs(fullScale.y) > 1e-4f)
                bed.max = new Vector3(bed.max.x, bed.min.y + NominalDepth / Mathf.Abs(fullScale.y), bed.max.z);
            zoneGrate = Mathf.Max(MinZoneGrate,
                Mathf.Abs(bed.size.x * fullScale.x * bed.size.z * fullScale.z) / GrateLayout.ZoneCount);

            var prefab = PlumeAssets.FireboxPrefab;
            if (prefab != null && BuildMesh(prefab))
                return "continuous";
            BuildTiles(coal);
            return "nine tiles";
        }

        // The continuous bed: a grid over the bed's box under the effects
        // prefab, which sits at the pivot unscaled (its flames and smoke stay
        // round); the bed's mesh is scaled into the pivot's units.
        private bool BuildMesh(GameObject prefab)
        {
            var instance = Instantiate(prefab, pivot.parent, false);
            instance.name = "[RealisticFiring firebox]";
            effects = instance.transform;
            effects.localPosition = pivot.localPosition;
            effects.localRotation = pivot.localRotation;
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
            // Each zone's forced rate, in kg/s.
            var forcedRate = ForcedFiringRate * zoneGrate / 3600f;
            fireForcing = Mathf.Clamp01(fire.ConsumptionRate / (forcedRate * GrateLayout.ZoneCount));

            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                var coal = zone.Coal;
                heights[i] = coal < EmptyZoneMass ? 0f : Mathf.Clamp(coal / zoneShare, MinHeightScale, MaxHeightScale);

                // Lit coal glows even when burning slowly and brighter as it is
                // forced. The front draws hardest, but a forced fire lights the
                // whole bed. Green coal dims a zone only where it smothers it.
                var own = Mathf.Clamp01(zone.CombustionRate / forcedRate);
                forcing[i] = Mathf.Max(own, 0.5f * (own + fireForcing));
                var lit = fire.FireOn && coal > 0f ? Mathf.Clamp01(zone.Burning / coal / LitShare) : 0f;
                var burn = lit * (0.2f + 0.8f * forcing[i]);
                // A thin, burning patch is a hole: the draft rushes through it white-hot.
                var thin = fire.FireOn && zone.Burning > 0f ? 1f - Mathf.Clamp01(coal / (ThinShare * zoneShare)) : 0f;
                heat[i] = Mathf.Clamp01(Mathf.Lerp(burn, 1f, 0.7f * thin));
                green[i] = coal > 0f ? zone.Fresh / coal : 0f;
                cover[i] = green[i] * (1f - (1f - ForcedGreenShows) * forcing[i] * forcing[i]);
                ash[i] = Mathf.Clamp01(1.5f * zone.Ash / (zone.Ash + coal + 0.1f));
                clinker[i] = Mathf.Clamp01(1.2f * zone.Clinker / (zone.Clinker + coal + 0.1f));
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
                    var height = Sample(heights, across, along) * Mathf.SmoothStep(0f, 1f, along / DoorSlope);
                    var lump = lumps[index] * Relief * Mathf.Min(height, 0.6f);
                    var up = Mathf.Max(0f, height + lump);
                    vertices[index] = new Vector3(
                        Mathf.Lerp(bed.min.x, bed.max.x, across),
                        Mathf.Lerp(bed.min.y, bed.max.y, up),
                        Mathf.Lerp(bed.min.z, bed.max.z, along));
                    var glow = Sample(heat, across, along) * (0.85f + 0.3f * glowJitter[index]);
                    colors[index] = new Color(Mathf.Clamp01(glow), Sample(cover, across, along), Sample(ash, across, along), Sample(clinker, across, along));
                }
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (emberMesh != null)
            {
                var drop = EmberDepth / Mathf.Max(1e-4f, Mathf.Abs(fullScale.y));
                for (var v = 0; v < vertices.Length; v++)
                    emberVertices[v] = new Vector3(vertices[v].x, Mathf.Max(bed.min.y, vertices[v].y - drop), vertices[v].z);
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
            return pivot.parent.TransformPoint(pivot.localPosition + pivot.localRotation * Vector3.Scale(fullScale, p));
        }

        /// <summary>Where a zone's coal stands now, as a share of the stock pile's height.</summary>
        public float ZoneHeight(GrateZone zone) => heights[(int)zone];

        /// <summary>Whether the bed has been built and placed.</summary>
        public bool Ready => pivot != null;

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
            tiles[index].localPosition = pivot.localPosition + pivot.localRotation * Vector3.Scale(fullScale, offset);
            tiles[index].localScale = new Vector3(fullScale.x * across, fullScale.y * height, fullScale.z * along);
        }

        private static float ZoneCenter(float min, float max, int index, int count, float scale)
        {
            var center = min + (index + 0.5f) * (max - min) / count;
            var halfPile = scale * (max - min) / 2f;
            return Mathf.Clamp(center, min + halfPile, max - halfPile);
        }

        private void OnDestroy()
        {
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
