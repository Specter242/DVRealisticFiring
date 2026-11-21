using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    internal sealed partial class ZonedCoalBed
    {
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

        // The smallest grate a bed is taken to have, in m², should a stock
        // pile measure flat. How hard each zone burns for its grate (its
        // forcing) comes from the fire model, which the bed tells the area.
        private const float MinGrateArea = 0.45f;

        // A pile mesh turned more than this under its pivot, in degrees, is
        // measured by its corners: its box's box is too big.
        private const float MeshTurned = 1f;

        // Rows run along the car and columns across it, on whichever of the
        // pile's axes point that way (the 8F's pile is turned: its forward up
        // the car, its right along it), each within 30° (cos 30°). A sloping
        // grate tilts the pile, which is fine.
        private const float MinAxisAlignment = 0.866f;

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
            // In the firing frame: on an engine fired from the front, the
            // car's turned half about, so the door is at the bed's back.
            var firing = FiringFrame.Of(car);
            var forward = firing.InverseTransformDirection(frameRotation * Vector3.forward);
            var right = firing.InverseTransformDirection(frameRotation * Vector3.right);
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
        // and along it (z), each signed to point right, up, and forward in
        // the firing frame (the fireman's right, and from the door toward
        // the tubes), and the turn from the bed's frame to the pivot's.
        private static (int[] AxisOf, Quaternion ToPivot) BedAxes(TrainCar car, Transform pivot)
        {
            var firing = FiringFrame.Of(car);
            Vector3 Local(Vector3 carDirection) => pivot.InverseTransformDirection(firing.TransformDirection(carDirection));
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
            bare = new List<Vector2>(new Vector2[count]);
            lumps = new float[count];
            glowJitter = new float[count];
            var uvs = new Vector2[count];
            var triangles = new List<int>();
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
            surfaceReady = false;
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
    }
}
