using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Shows the realistic fire's zones inside the firebox. The stock coal
    /// pile (one mesh the game stretches a little with the coal level) is
    /// hidden; nine copies of it, one per grate zone, stand as deep as that
    /// zone's coal and glow with its burn rate. An empty zone shows as a gap,
    /// and the zone a loaded shovel is aimed at pulses blue.
    /// </summary>
    internal sealed class ZonedCoalBed : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly Color AimColor = new Color(0.25f, 0.6f, 1f);

        // Height of a zone's pile over the stock pile's, per even share of capacity.
        private const float MinHeightScale = 0.08f;
        private const float MaxHeightScale = 1.6f;

        // Below this a zone shows as a gap in the bed, in kg.
        private const float EmptyZoneMass = 0.2f;

        // Each pile covers this much more than its zone, so neighbors blend
        // instead of leaving a trough where the stock pile's edges slope down.
        private const float Overlap = 1.3f;

        // Lit coal: dull red when burning slowly, orange when working, yellow-white when forced.
        private static readonly Color DullRed = new Color(1f, 0.35f, 0.2f);
        private static readonly Color Orange = new Color(1f, 0.6f, 0.3f);
        private static readonly Color WhiteHot = new Color(1f, 0.88f, 0.7f);

        // Rows run along the pile's forward axis and columns along its right
        // axis, so those must point roughly along and across the car (cos 30°).
        // A sloping grate tilts the pile, which is fine.
        private const float MinAxisAlignment = 0.866f;

        private readonly Transform[] tiles = new Transform[GrateLayout.ZoneCount];
        private readonly Renderer[] tileRenderers = new Renderer[GrateLayout.ZoneCount];
        private readonly Material[] tileMaterials = new Material[GrateLayout.ZoneCount];

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
            bed.Build(firebox, scaler, coal, filter.sharedMesh);
            // The pile lies on the grate, so its tilt is the grate's slope; a
            // moving loco shakes coal down it.
            var slope = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            firebox.SetGrateSlope(slope);
            return $"zoned coal bed built from '{coal.name}', {bed.bed.size.x * bed.fullScale.x:F2} m wide " +
                   $"and {bed.bed.size.z * bed.fullScale.z:F2} m long, grate sloping {slope:F1}° " +
                   $"(up toward the front when positive), turned {Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg:F1}°";
        }

        private void Build(RealisticFirebox owner, IndicatorScaler scaler, Renderer coal, Mesh mesh)
        {
            firebox = owner;
            stockCoal = coal;
            pivot = scaler.indicatorToScale;
            fullScale = scaler.endScale;
            zoneShare = owner.Model.Parameters.MaxCoalCapacity / GrateLayout.ZoneCount;

            // The pile's extent in the pivot's own units, before the pivot's scale.
            var local = mesh.bounds;
            bed = new Bounds(pivot.InverseTransformPoint(coal.transform.TransformPoint(local.center)), Vector3.zero);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                bed.Encapsulate(pivot.InverseTransformPoint(coal.transform.TransformPoint(corner)));
            }

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
            if (stockCoal == null || pivot == null)
                return;

            if (!Main.Settings.ShowZonedCoalBed)
            {
                stockCoal.enabled = true;
                foreach (var tile in tileRenderers)
                    tile.enabled = false;
                return;
            }

            stockCoal.enabled = false;
            var fire = firebox.Model;
            var aim = firebox.HoverAim;
            var aimed = aim.HasValue && aim.Value.IsLiveAt(Time.time) ? aim.Value.Zone : (GrateZone?)null;
            var pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 8f);
            var zoneRate = fire.Parameters.MaxCombustionRate / GrateLayout.ZoneCount;

            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = fire.Zones[i];
                var isAimed = aimed == (GrateZone)i;
                var visible = zone.Coal >= EmptyZoneMass || isAimed;
                tileRenderers[i].enabled = visible;
                if (!visible)
                    continue;

                Place(i, Mathf.Clamp(zone.Coal / zoneShare, MinHeightScale, MaxHeightScale));

                // Lit coal glows even when burning slowly and brighter as it is
                // forced; fresh coal stays dark until it catches.
                var glow = fire.FireOn && zone.Coal > 0f
                    ? zone.Burning / zone.Coal * (0.2f + 0.8f * Mathf.Clamp01(zone.CombustionRate / zoneRate))
                    : 0f;
                var flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 1.7f, i * 3.1f);
                var emission = FireColor(glow) * (Mathf.Lerp(0.4f, 3.2f, glow) * glow * flicker);
                if (isAimed)
                    emission += AimColor * (1.5f * pulse);
                tileMaterials[i].SetColor(EmissionColorId, emission);
            }
        }

        private static Color FireColor(float glow) =>
            glow < 0.5f ? Color.Lerp(DullRed, Orange, glow * 2f) : Color.Lerp(Orange, WhiteHot, glow * 2f - 1f);

        // Scales a copy of the stock pile to an overlapping share of its full
        // footprint and to the given height, standing on the stock pile's
        // base, and centers it on its zone, pulling the outer piles in so they
        // stay inside the bed. Rows run from the door (the car's back, -Z) to
        // the front, columns from left (-X) to right; all in the pivot's units.
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
            if (stockCoal != null)
                stockCoal.enabled = true;
        }
    }
}
