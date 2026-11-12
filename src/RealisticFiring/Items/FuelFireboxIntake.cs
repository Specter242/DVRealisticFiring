using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Interaction;
using DV.Utils;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using RealisticFiring.Visuals;
using UnityEngine;

namespace RealisticFiring.Items
{
    /// <summary>
    /// A firebox's mouth for loose wood and rags: what goes through the open
    /// door joins the fire once, at the zone it lands in; what the grate has
    /// no room for, or a shut door stops, comes back out into the cab. All
    /// tests are in the car's own frame, so a moving engine does not sweep
    /// its door over things in the cab. Also draws the wood and rags on the
    /// grate, lit by the fire.
    /// </summary>
    internal sealed class FuelFireboxIntake : MonoBehaviour
    {
        private static readonly List<FuelFireboxIntake> active = new List<FuelFireboxIntake>();
        internal const int PiecesPerZone = 8;
        // How far an item may be from an engine for its door to be checked, m.
        private const float Reach = 12f;
        // A door this little open still lets a thrown piece through.
        private const float PhysicalOpening = 0.05f;

        internal TrainCar Car;
        private ItemUseTarget target;
        private Transform interior;
        private Bounds hole;
        private Bounds volume;
        private FireholeGate gate;
        private bool ready;
        private Matrix4x4 previousCarFrame;
        private bool haveCarFrame;
        private FireboxLighting lighting;

        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);
        private void OnDestroy() => ClearVisuals();

        private void LateUpdate()
        {
            if (Car == null) return;
            previousCarFrame = Car.transform.worldToLocalMatrix;
            haveCarFrame = true;
        }

        /// <summary>The intake whose firehole <paramref name="useTarget"/> is.</summary>
        internal static bool ForTarget(ItemUseTarget useTarget, out FuelFireboxIntake intake)
        {
            intake = null;
            if (useTarget == null) return false;
            foreach (var candidate in active)
                if (candidate != null && candidate.ready && candidate.target == useTarget) { intake = candidate; return true; }
            return false;
        }

        // The firehole: found again only when the cab interior loads or changes.
        private void Refresh()
        {
            var loaded = Car.loadedInterior != null ? Car.loadedInterior.transform : null;
            if (loaded == interior && (target != null || loaded == null)) return;
            interior = loaded;
            ClearVisuals();
            target = null;
            ready = false;
            lighting = null;
            if (loaded == null) return;
            var targets = loaded.GetComponentsInChildren<ItemUseTarget>(true);
            target = targets.FirstOrDefault(t => t.GetComponent<NonPhysicsCoalTarget>() != null)
                ?? targets.FirstOrDefault(t => t.GetComponent<Fire>() != null);
            if (target == null || target.targetColliders == null || !target.targetColliders.Any(c => c != null)) return;
            var first = true;
            foreach (var c in target.targetColliders.Where(c => c != null))
            {
                var b = CarSpace.LocalBounds(Car.transform, c);
                if (first) { hole = b; first = false; } else hole.Encapsulate(b);
            }
            var opening = FireholeOpening.For(Car, hole);
            var fitted = FireholeOpening.Backhead(Car, out _, out _);
            var survey = fitted ? new Doorway { Bounds = opening.Bounds } : SurveyDoorway(hole);
            hole = survey.Bounds;
            var plane = hole.center.z;
            // The box behind the door plane: the grate's extent where there is
            // one, up to the crown; a door's width either side where not.
            // It is an outline only: entry is judged by the doorway (FireholeGate).
            volume = new Bounds();
            volume.SetMinMax(new Vector3(hole.min.x - 0.35f, hole.min.y - 0.6f, plane), new Vector3(hole.max.x + 0.35f, hole.max.y + 0.5f, plane + 1.6f));
            if (FireboxRegistry.TryGet(Car, out var fire) && fire.GrateBox is Bounds grate)
            {
                var min = Vector3.Min(volume.min, new Vector3(grate.min.x, grate.min.y - 0.05f, plane));
                var max = Vector3.Max(new Vector3(volume.max.x, volume.max.y, plane), new Vector3(grate.max.x, hole.max.y + 0.6f, grate.max.z + 0.1f));
                volume.SetMinMax(min, max);
            }
            gate = fitted
                ? FireholeGate.Fitted(hole.center.x, hole.min.y, hole.size.x, hole.size.y, plane, FittedFrameDepth,
                    volume.min.x, volume.max.x, volume.min.y, volume.max.y, volume.max.z)
                : new FireholeGate(hole.min.x, hole.max.x, hole.min.y, hole.max.y, plane,
                    volume.min.x, volume.max.x, volume.min.y, volume.max.y, volume.max.z);
            if (survey.Open != null) gate.SetAperture(survey.Open, SurveyColumns, SurveyRows);
            DoorwaySource = fitted ? "fitted firehole" : survey.Open != null ? "surveyed backhead" : "door target's outline";
            if (fitted) BuildFrame(loaded, hole, plane);
            Main.Mod?.Logger.Log($"{Car.ID} ({Car.carLivery?.id}): wood and rags go in through the {DoorwaySource}: " +
                                 $"x {hole.min.x:F2} to {hole.max.x:F2}, y {hole.min.y:F2} to {hole.max.y:F2}, plane z {plane:F2} m in the car frame" +
                                 (fitted ? $", its frame {FittedFrameDepth:F2} m deep" : ""));
            lighting = target.GetComponent<FireboxLighting>();
            ready = true;
        }

        private const int SurveyColumns = 12, SurveyRows = 8;

        private const float FrameSide = 0.45f, FrameBelow = 0.5f, FrameAbove = 0.4f, FrameFrom = 0.005f, FrameThickness = 0.06f;
        private GameObject frame;

        private void BuildFrame(Transform loaded, Bounds doorway, float plane)
        {
            if (frame != null) Destroy(frame);
            frame = null;
            // Beside the cab's own item colliders, on their layer.
            var items = loaded.Find("[items]");
            var own = items != null ? items.GetComponentsInChildren<Collider>(true).FirstOrDefault(c => c != null && !c.isTrigger) : null;
            var layer = own != null ? own.gameObject.layer : LayerMask.NameToLayer("Train_Interior");
            if (layer < 0) return;
            frame = new GameObject("RealisticFiring firehole frame") { layer = layer };
            frame.transform.SetPositionAndRotation(Car.transform.position, Car.transform.rotation);
            frame.transform.SetParent(items != null ? items : loaded, true);
            float x0 = doorway.min.x, x1 = doorway.max.x, y0 = doorway.min.y, y1 = doorway.max.y;
            void Strip(float left, float right, float bottom, float top)
            {
                var box = frame.AddComponent<BoxCollider>();
                box.center = new Vector3((left + right) / 2f, (bottom + top) / 2f, plane + FrameFrom + FrameThickness / 2f);
                box.size = new Vector3(right - left, top - bottom, FrameThickness);
            }
            Strip(x0 - FrameSide, x0, y0 - FrameBelow, y1 + FrameAbove);
            Strip(x1, x1 + FrameSide, y0 - FrameBelow, y1 + FrameAbove);
            Strip(x0, x1, y0 - FrameBelow, y0);
            Strip(x0, x1, y1, y1 + FrameAbove);
        }

        // How far behind a fitted firehole's plane the backhead's face may
        // stand, m: the S282's surveyed face is 4.6 cm behind its fitted hole.
        // A piece that strikes the plate around the door that deep is neither
        // taken nor sent back; one past it came through a wall.
        private const float FittedFrameDepth = 0.15f;

        /// <summary>Where this cab's doorway came from: its fitted firehole, the surveyed backhead, or the door target's outline.</summary>
        internal string DoorwaySource { get; private set; }

        private struct Doorway
        {
            public Bounds Bounds;
            public bool[] Open;
        }

        // Rays straight in along the car, on a grid over the door target's
        // outline: where the backhead stops them the wall is; where they go
        // on (or meet only something sloping inside, a ramp or the grate) is
        // the doorway. Its plane is the backhead's face. Surveyed twice: the
        // outline, then the open cells' extent, finer. Interactable (the
        // door's handles, which move) and items are left out.
        private Doorway SurveyDoorway(Bounds outline)
        {
            var itemLayer = LayerMask.NameToLayer("World_Item");
            var mask = 0;
            for (var layer = 0; layer < 32; layer++)
                if (itemLayer < 0 || !Physics.GetIgnoreLayerCollision(itemLayer, layer)) mask |= 1 << layer;
            mask &= ~LayerMask.GetMask("Interactable", "World_Item", "Grabbed_Item", "Inventory", "Player");
            var first = Survey(outline, outline.min.z - 0.05f, outline.size.z + 0.7f, mask, null);
            if (first.Open == null) return new Doorway { Bounds = outline };
            var cellX = outline.size.x / SurveyColumns;
            var cellY = outline.size.y / SurveyRows;
            float x0 = float.MaxValue, x1 = float.MinValue, y0 = float.MaxValue, y1 = float.MinValue;
            for (var r = 0; r < SurveyRows; r++)
                for (var c = 0; c < SurveyColumns; c++)
                {
                    if (!first.Open[r * SurveyColumns + c]) continue;
                    x0 = Mathf.Min(x0, outline.min.x + c * cellX); x1 = Mathf.Max(x1, outline.min.x + (c + 1) * cellX);
                    y0 = Mathf.Min(y0, outline.min.y + r * cellY); y1 = Mathf.Max(y1, outline.min.y + (r + 1) * cellY);
                }
            var tight = new Bounds();
            tight.SetMinMax(new Vector3(x0, y0, first.Bounds.center.z), new Vector3(x1, y1, first.Bounds.center.z));
            var second = Survey(tight, outline.min.z - 0.05f, outline.size.z + 0.7f, mask, first.Bounds.center.z);
            return second.Open != null ? second : first;
        }

        private Doorway Survey(Bounds area, float fromZ, float length, int mask, float? knownPlane)
        {
            var car = Car.transform;
            var along = car.forward;
            var hits = new float?[SurveyColumns * SurveyRows];
            var walls = new bool[hits.Length];
            var faces = new List<float>();
            for (var r = 0; r < SurveyRows; r++)
                for (var c = 0; c < SurveyColumns; c++)
                {
                    var x = Mathf.Lerp(area.min.x, area.max.x, (c + 0.5f) / SurveyColumns);
                    var y = Mathf.Lerp(area.min.y, area.max.y, (r + 0.5f) / SurveyRows);
                    if (!Physics.Raycast(car.TransformPoint(new Vector3(x, y, fromZ)), along, out var hit, length, mask, QueryTriggerInteraction.Ignore)) continue;
                    var i = r * SurveyColumns + c;
                    hits[i] = car.InverseTransformPoint(hit.point).z;
                    walls[i] = Vector3.Dot(hit.normal, -along) > 0.7f;
                    if (walls[i]) faces.Add(hits[i].Value);
                }
            if (knownPlane == null && faces.Count == 0) return default;
            faces.Sort();
            var plane = knownPlane ?? faces[faces.Count / 2];
            var open = new bool[hits.Length];
            var any = false;
            for (var i = 0; i < hits.Length; i++)
            {
                // Blocked: the backhead's face here, or anything standing in
                // front of it; open: nothing, or only what lies further in.
                var blocked = hits[i] is float z && (z < plane - 0.03f || walls[i] && z < plane + 0.08f);
                open[i] = !blocked;
                any |= open[i];
            }
            if (!any) return default;
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(area.min.x, area.min.y, plane), new Vector3(area.max.x, area.max.y, plane));
            return new Doorway { Bounds = bounds, Open = open };
        }

        private float DoorOpening =>
            FireboxRegistry.TryGet(Car, out var coal) ? coal.DoorOpening
            : RealisticOilFirebox.TryGet(Car, out var oil) ? oil.DoorOpening : 0f;

        /// <summary>Open as the stock shovel needs it to lay coal (60 %, a quarter in VR).</summary>
        internal bool DoorOpenFor(bool vr) => DoorOpening >= (vr ? 0.25f : ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR);

        internal bool CanTake(StartingFuelItem item) => RefusalFor(item) == null;

        /// <summary>Why this firebox cannot take the item now, or null.</summary>
        internal string RefusalFor(StartingFuelItem item)
        {
            if (FireboxRegistry.TryGet(Car, out var coal))
            {
                var model = coal.Model;
                if (item.Kind == StartingFuelKind.Wood)
                {
                    if (model.Parameters.MaxCoalCapacity - model.CoalMass - model.StarterMass < StarterFuel.PieceMass)
                        return "The firebox is full";
                    for (var z = 0; z < GrateLayout.ZoneCount; z++)
                        if (StarterFuel.ZoneCapacity - model.Starters[z].Mass >= StarterFuel.PieceMass) return null;
                    return "The grate has no room for more wood";
                }
                return null;
            }
            if (RealisticOilFirebox.TryGet(Car, out _))
                return item.Kind == StartingFuelKind.Wood ? "An oil burner is lit with a burning rag, not wood" : null;
            return "No fire here";
        }

        /// <summary>The zone the player aims at through the door, as the shovel aims; the door's zone otherwise.</summary>
        internal GrateZone AimedZone(ItemUseTarget useTarget, GrabHandlerItem handle)
        {
            var grabber = handle != null ? handle.GetGrabber() : null;
            if (grabber != null && grabber.Cursor != null && FireboxRegistry.TryGet(Car, out var firebox)
                && FireboxAim.TryAim(grabber.Cursor.GetRay(), useTarget, Car, firebox, out var across, out var depth))
                return DoorAim.ZoneFor(across, depth);
            return GrateZone.UnderDoor;
        }

        /// <summary>Lays the held item in (the place use). Host: at once; a client lets it go just inside the door, for the host to take.</summary>
        internal bool Place(StartingFuelItem item, GrateZone zone)
        {
            if (item == null || item.Consumed || !ready) return false;
            if (Net.IsClient)
            {
                // Let go just inside the hole, moving toward the zone aimed
                // at; the host takes it as released through the door.
                var inside = Car.transform.TransformPoint(InsidePoint(zone));
                var handler = PlayerManager.PlayerTransform != null ? PlayerManager.PlayerTransform.GetComponentInChildren<GrabberInteractionHandlerDV>() : null;
                handler?.RequestDrop();
                item.transform.position = inside;
                var laid = item.Body;
                if (laid != null)
                    laid.velocity = (Car.rb != null ? Car.rb.GetPointVelocity(inside) : Vector3.zero) + Car.transform.TransformVector(TowardZone(zone));
                return true;
            }
            if (!Accept(item, zone, true)) return false;
            // Out of the hand first, so nothing goes on holding a destroyed item.
            if (item.Item != null && item.Item.IsGrabbed())
                PlayerManager.PlayerTransform?.GetComponentInChildren<GrabberInteractionHandlerDV>()?.RequestDrop();
            Consume(item);
            return true;
        }

        // Just behind the hole, low in it, across it by the zone's column.
        private Vector3 InsidePoint(GrateZone zone)
        {
            var across = (GrateLayout.Column(zone) + 0.5f) / GrateLayout.Columns;
            return new Vector3(Mathf.Lerp(hole.min.x + 0.04f, hole.max.x - 0.04f, across), Mathf.Lerp(hole.min.y, hole.max.y, 0.3f), gate.Plane + 0.1f);
        }

        // A velocity in the car's frame that carries a piece from the hole to the zone's middle (see FireholeGate.LandingZone).
        private Vector3 TowardZone(GrateZone zone)
        {
            if (!FireboxRegistry.TryGet(Car, out var fire) || !(fire.GrateBox is Bounds grate)) return Vector3.zero;
            var x = Mathf.Lerp(grate.min.x, grate.max.x, (GrateLayout.Column(zone) + 0.5f) / GrateLayout.Columns);
            var z = Mathf.Lerp(grate.min.z, grate.max.z, (GrateLayout.Row(zone) + 0.5f) / GrateLayout.Rows);
            var from = InsidePoint(zone);
            // Level, it falls to the grate's top in t; that time carries it there.
            var t = Mathf.Sqrt(2f * Mathf.Max(0.01f, from.y - grate.max.y) / Mathf.Max(0.1f, -Physics.gravity.y));
            return new Vector3((x - from.x) / t, 0f, Mathf.Max(0f, z - from.z) / t);
        }

        /// <summary>
        /// Checks a loose item against every engine near it (host only): one
        /// let go inside a firebox, or one that has flown in through the door,
        /// goes into the fire or back out.
        /// </summary>
        internal static void Track(StartingFuelItem item)
        {
            var now = item.transform.position;
            if (item.Consumed || Net.IsClient || !item.isActiveAndEnabled)
            {
                item.Previous = now; item.HavePrevious = true;
                return;
            }
            var held = item.Held;
            if (held)
            {
                item.WasHeld = true;
                item.WasHeldLocally = item.HeldLocally;
            }
            else if (item.WasHeld || item.HavePrevious && (now - item.Previous).sqrMagnitude < 25f)
            {
                var released = item.WasHeld;
                var local = item.WasHeldLocally;
                item.WasHeld = false;
                foreach (var intake in active)
                {
                    if (intake == null || !intake.ready || intake.Car == null) continue;
                    if ((intake.Car.transform.position - now).sqrMagnitude > Reach * Reach) continue;
                    if (released ? intake.CheckRelease(item, now, local) : intake.Check(item, item.Previous, now)) break;
                }
            }
            item.Previous = now; item.HavePrevious = true;
        }

 internal static void Strike(StartingFuelItem item)
        {
            if (item == null || item.Consumed || Net.IsClient || !item.isActiveAndEnabled) return;
            var body = item.Body;
            var centre = body != null ? body.worldCenterOfMass : item.transform.position;
            foreach (var intake in active)
            {
                if (intake == null || !intake.ready || intake.Car == null) continue;
                if ((intake.Car.transform.position - centre).sqrMagnitude > Reach * Reach) continue;
                if (intake.CheckStrike(item, centre)) break;
            }
        }

        /// <summary>
        /// Where the local player's throw from a view at <paramref name="eye"/>
        /// toward <paramref name="target"/> is aimed: just inside an engine's
        /// open doorway, when the view passes through it
        /// (<see cref="FireholeGate.AimsThrough"/>); otherwise the target.
        /// </summary>
        internal static Vector3 AimThrough(Vector3 eye, Vector3 target)
        {
            foreach (var intake in active)
            {
                if (intake == null || !intake.ready || intake.Car == null) continue;
                if ((intake.Car.transform.position - eye).sqrMagnitude > Reach * Reach) continue;
                var car = intake.Car.transform;
                intake.gate.Opening = intake.DoorOpening < PhysicalOpening ? 0f : intake.DoorOpening;
                if (intake.gate.AimsThrough(Point(car.InverseTransformPoint(eye)), Point(car.InverseTransformPoint(target)), out var aim))
                    return car.TransformPoint(new Vector3(aim.X, aim.Y, aim.Z));
            }
            return target;
        }

        private bool CheckStrike(StartingFuelItem item, Vector3 centre)
        {
            var local = Car.transform.InverseTransformPoint(centre);
            var carVelocity = Car.rb != null ? Car.rb.GetPointVelocity(centre) : Vector3.zero;
            var flight = Car.transform.InverseTransformVector(item.LastVelocity - carVelocity);
            var gravity = Car.transform.InverseTransformVector(Physics.gravity).y;
            gate.Opening = DoorOpening < PhysicalOpening ? 0f : DoorOpening;
            if (!gate.StrikesDoorway(Point(local), Point(flight), gravity)) return false;
            Resolve(item, FireholeEntry.Through, local, flight);
            return true;
        }

        private static CarPoint Point(Vector3 v) => new CarPoint(v.x, v.y, v.z);

        // A piece just let go: in the box only if the hand reached in
        // through the hole. The local player's own eye shows the reach; for
        // a remote player's release the host has only a standing player's
        // eye before the door to judge by.
        private bool CheckRelease(StartingFuelItem item, Vector3 at, bool local)
        {
            var b = Car.transform.InverseTransformPoint(at);
            gate.Opening = DoorOpening < PhysicalOpening ? 0f : DoorOpening;
            var camera = PlayerManager.ActiveCamera;
            var eye = local && camera != null
                ? Car.transform.InverseTransformPoint(camera.transform.position)
                : new Vector3(hole.center.x, hole.center.y + 1.0f, gate.Plane - 0.85f);
            return Resolve(item, gate.ClassifyRelease(Point(eye), Point(b)), b);
        }

        private bool Check(StartingFuelItem item, Vector3 from, Vector3 to)
        {
            // Each endpoint in the car's frame of its own moment.
            var a = haveCarFrame ? previousCarFrame.MultiplyPoint3x4(from) : Car.transform.InverseTransformPoint(from);
            var b = Car.transform.InverseTransformPoint(to);
            gate.Opening = DoorOpening < PhysicalOpening ? 0f : DoorOpening;
            return Resolve(item, gate.Classify(Point(a), Point(b)), b);
        }

        private bool Resolve(StartingFuelItem item, FireholeEntry entry, Vector3 local, Vector3? flight = null)
        {
            switch (entry)
            {
                case FireholeEntry.None:
                    return false;
                case FireholeEntry.Stray:
                    // Through a wall or a shut door: it never reached the fire. Back into the cab.
                    Eject(item, DoorOpening < PhysicalOpening ? "The fire door is shut" : null);
                    return true;
            }
            var zone = LandingZone(item, local, flight);
            if (Accept(item, zone, false)) Consume(item);
            else Eject(item, RefusalFor(item) ?? "The firebox cannot take that");
            return true;
        }

        // Where a piece in the box will come down: where its flight relative
        // to the engine meets the grate.
        private GrateZone LandingZone(StartingFuelItem item, Vector3 local, Vector3? flight = null)
        {
            if (!FireboxRegistry.TryGet(Car, out var fire) || !(fire.GrateBox is Bounds grate)) return GrateZone.UnderDoor;
            var body = item.Body;
            var velocity = flight ?? (body != null ? Car.transform.InverseTransformVector(body.velocity) : Vector3.zero);
            // A strike's flight is already the engine's own frame's.
            var carVelocity = flight.HasValue || Car.rb == null ? Vector3.zero : Car.transform.InverseTransformVector(Car.rb.GetPointVelocity(item.transform.position));
            return FireholeGate.LandingZone(Point(local), Point(velocity), Point(carVelocity), grate.min.x, grate.max.x, grate.min.z, grate.max.z, grate.max.y, Physics.gravity.y);
        }

        private bool Accept(StartingFuelItem item, GrateZone zone, bool placed)
        {
            // Throws bypass the placement compatibility check. Require room
            // for the whole piece before AddWood can accept a partial mass;
            // consuming the world item must not discard the remainder.
            if (!CanTake(item)) return false;
            if (FireboxRegistry.TryGet(Car, out var coal))
            {
                var model = coal.Model;
                // The aimed zone, or the nearest with room.
                foreach (var z in ByDistance(zone))
                {
                    if (item.Kind == StartingFuelKind.Wood)
                    {
                        if (StarterFuel.ZoneCapacity - model.Starters[(int)z].Mass < StarterFuel.PieceMass) continue;
                        if (model.AddWood(StarterFuel.PieceMass, z) > 0f) { Arrived(z, StarterFuel.PieceMass); return true; }
                        return false;
                    }
                    if (model.AddRag(item.Rag.Oil, item.Rag.Lit, z))
                    {
                        if (item.Rag.Oil <= 0f) FireController.Announce("A dry rag only burns away; oil it and light it first.");
                        return true;
                    }
                }
                return false;
            }
            if (item.Kind == StartingFuelKind.Rag && RealisticOilFirebox.TryGet(Car, out var oil))
            {
                if (item.Rag.Lit) oil.Model.OfferRag(item.Rag.RemainingSeconds);
                else FireController.Announce("An unlit rag does nothing for the burner; light it first.");
                return true;
            }
            return false;
        }

        private static IEnumerable<GrateZone> ByDistance(GrateZone zone) =>
            Enumerable.Range(0, GrateLayout.ZoneCount).Select(i => (GrateZone)i)
                .OrderBy(z => Mathf.Abs(GrateLayout.Row(z) - GrateLayout.Row(zone)) + Mathf.Abs(GrateLayout.Column(z) - GrateLayout.Column(zone)))
                .ThenBy(z => (int)z);

        private static void Consume(StartingFuelItem item)
        {
            item.Consumed = true;
            Destroy(item.gameObject);
        }

        // Back out of the door into the cab, settling there.
        private void Eject(StartingFuelItem item, string why)
        {
            var back = Car.transform.TransformPoint(new Vector3(hole.center.x, hole.center.y, hole.center.z - 0.35f));
            item.transform.position = back;
            var body = item.Body;
            if (body != null)
            {
                body.velocity = (Car.rb != null ? Car.rb.velocity : Vector3.zero) - Car.transform.forward * 0.6f;
                body.angularVelocity = Vector3.zero;
            }
            item.Previous = back;
            if (why != null) FireController.Announce(why + ".");
        }

        // --- The wood and rags on the grate ---

        private Transform[] logs;
        private Renderer[][] logRenderers;
        private readonly Transform[] rags = new Transform[GrateLayout.ZoneCount];
        private readonly Renderer[][] ragRenderers = new Renderer[GrateLayout.ZoneCount][];
        // A burning rag's own small flame, until its wood catches and the
        // firebox's flames take over (Visuals.WoodFlame).
        private readonly ParticleSystem[] ragFlames = new ParticleSystem[GrateLayout.ZoneCount];
        private readonly float[] landed = new float[GrateLayout.ZoneCount];
        private readonly MaterialPropertyBlock appearance = new MaterialPropertyBlock();
        private Transform oilRag;
        private Light oilRagLight;

        // A piece just arrived shows at once, before the next frame's draw.
        private void Arrived(GrateZone zone, float mass) => landed[(int)zone] = Time.time;

        private void ClearVisuals()
        {
            if (logs != null) foreach (var log in logs) if (log != null) Destroy(log.gameObject);
            logs = null;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                if (rags[i] != null) Destroy(rags[i].gameObject);
                if (ragFlames[i] != null) Destroy(ragFlames[i].gameObject);
                rags[i] = null; ragFlames[i] = null;
            }
            if (oilRag != null) Destroy(oilRag.gameObject);
            oilRag = null;
            if (frame != null) Destroy(frame);
            frame = null;
            if (lighting != null) lighting.Extra.Clear();
        }

        private void Update()
        {
            if (Car == null) return;
            Refresh();
            if (!ready) return;
            if (RealisticOilFirebox.TryGet(Car, out var burner)) DrawOilRag(burner);
            if (!FireboxRegistry.TryGet(Car, out var fire) || !ZonedCoalBed.TryGet(fire, out var bed)) return;
            var model = fire.Model;
            if (model.StarterMass <= 0f && logs == null) return;
            if (logs == null)
            {
                logs = new Transform[GrateLayout.ZoneCount * PiecesPerZone];
                logRenderers = new Renderer[logs.Length][];
            }
            // On the car itself, unscaled (the firebox's effects are scaled
            // to its bed), placed in the world each frame.
            var parent = transform;
            var layer = bed.BedRenderer != null ? bed.BedRenderer.gameObject.layer : gameObject.layer;
            var up = (bed.BedPoint(0.5f, 0.5f, 1f) - bed.BedPoint(0.5f, 0.5f, 0f)).normalized;
            var across = (bed.BedPoint(1f, 0f, 0f) - bed.BedPoint(0f, 0f, 0f)).normalized;
            var along = (bed.BedPoint(0f, 1f, 0f) - bed.BedPoint(0f, 0f, 0f)).normalized;
            for (var zone = 0; zone < GrateLayout.ZoneCount; zone++)
            {
                var starter = model.Starters[zone];
                var x = (GrateLayout.Column((GrateZone)zone) + 0.5f) / GrateLayout.Columns;
                var z = (GrateLayout.Row((GrateZone)zone) + 0.5f) / GrateLayout.Rows;
                // On the bars, under any coal fired on top of it later.
                var foot = bed.BedPoint(x, z, 0f);
                var pieces = (starter.Wood + starter.Char * 2.2f) / StarterFuel.PieceMass;
                var flaming = starter.Flaming;
                for (var piece = 0; piece < PiecesPerZone; piece++)
                {
                    var i = zone * PiecesPerZone + piece;
                    var share = Mathf.Clamp01(pieces - piece);
                    if (share <= 0.02f)
                    {
                        if (logs[i] != null) logs[i].gameObject.SetActive(false);
                        continue;
                    }
                    if (logs[i] == null)
                    {
                        logs[i] = StartingFuelBody.Build(parent, StartingFuelKind.Wood, (zone * 3 + piece) % StartingFuelBody.WoodVariants, lighting != null);
                        logRenderers[i] = logs[i].GetComponentsInChildren<Renderer>(true);
                        foreach (var r in logRenderers[i]) { r.gameObject.layer = layer; lighting?.Extra.Add(r); }
                    }
                    var log = logs[i];
                    log.gameObject.SetActive(true);
                    // Laid crosswise in pairs, a small crib, the first pair on the bars.
                    var level = piece / 2;
                    log.localScale = Vector3.one;
                    var side = piece % 2 == 0 ? -1f : 1f;
                    var crosswise = level % 2 == 1;
                    var offset = (crosswise ? along : across) * (side * 0.055f) + up * (0.042f + level * 0.062f);
                    log.position = foot + offset;
                    log.rotation = Quaternion.LookRotation(crosswise ? across : along, up) * Quaternion.Euler(0f, 0f, piece % 3 == 0 ? 180f : piece * 23f % 50f - 25f);
                    // It burns away as it chars through.
                    log.localScale = Vector3.one * Mathf.Lerp(0.45f, 1f, Mathf.Sqrt(share) * (1f - 0.35f * starter.CharFraction)) / Mathf.Max(0.01f, transform.lossyScale.x);
                    // Its own surface char about the zone's: the lower pieces, nearer the rag, first.
                    var own = Mathf.Clamp01(starter.SurfaceChar * (1.25f - 0.12f * level) + 0.6f * starter.CharFraction);
                    // Embers glow from the char; flaming wood only a little at its cracks.
                    var glow = Mathf.Clamp01(starter.EmberGlow * 1.6f + 0.15f * flaming) * (starter.Burning ? 1f : 0f);
                    var scorch = Mathf.Clamp01((starter.FrontTemperature - 120f) / 250f);
                    StartingFuelBody.SetState(logRenderers[i], appearance, own, glow, scorch, 0f, (i * 0.618f) % 1f);
                }
                // Their flames are the firebox's own (Visuals.WoodFlame).
                DrawRag(zone, starter, foot, up, parent, layer);
            }
        }

        private void DrawRag(int zone, StarterFuel starter, Vector3 foot, Vector3 up, Transform parent, int layer)
        {
            var show = starter.RagOil > 0f || starter.RagCount > 0;
            if (!show)
            {
                if (rags[zone] != null) rags[zone].gameObject.SetActive(false);
                if (ragFlames[zone] != null) { var e = ragFlames[zone].emission; e.rateOverTime = 0f; }
                return;
            }
            if (rags[zone] == null)
            {
                rags[zone] = StartingFuelBody.Build(parent, StartingFuelKind.Rag, zone % StartingFuelBody.RagPoses, lighting != null);
                ragRenderers[zone] = rags[zone].GetComponentsInChildren<Renderer>(true);
                foreach (var r in ragRenderers[zone]) { r.gameObject.layer = layer; lighting?.Extra.Add(r); }
                ragFlames[zone] = StartingFuelBody.FlameSystem(parent, "rag flame");
            }
            rags[zone].gameObject.SetActive(true);
            // On top of the wood, where it was thrown.
            var top = Mathf.Min(3f, Mathf.Ceil(starter.Wood / StarterFuel.PieceMass / 2f)) * 0.062f;
            rags[zone].position = foot + up * (0.03f + top);
            var burnt = 1f - starter.RagOil / Mathf.Max(1e-4f, starter.RagCount * RagFuel.OilCapacity);
            StartingFuelBody.SetState(ragRenderers[zone], appearance, Mathf.Clamp01(burnt * 1.2f), starter.RagLit ? 0.5f : 0f, starter.RagLit ? 1f : 0f, starter.RagOil > 0f ? 1f : 0f, zone * 0.37f % 1f);
            if (ragFlames[zone] != null)
            {
                ragFlames[zone].transform.position = rags[zone].position + up * 0.04f;
                ragFlames[zone].transform.rotation = Quaternion.LookRotation(up, rags[zone].forward);
                var emission = ragFlames[zone].emission;
                emission.rateOverTime = starter.RagLit && !(starter.Involved > 0f) ? 22f : 0f;
                var main = ragFlames[zone].main;
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
                main.startSizeZ = main.startSizeX;
                var shape = ragFlames[zone].shape;
                shape.scale = new Vector3(0.12f, 0.12f, 0.02f);
            }
        }

        // An oil burner's rag, lying in the firebox while it burns.
        private void DrawOilRag(RealisticOilFirebox burner)
        {
            var burning = burner.Model.Torch > 0f;
            if (oilRag == null && burning)
            {
                oilRag = StartingFuelBody.Build(transform, StartingFuelKind.Rag, 1, false);
                oilRag.localPosition = new Vector3(hole.center.x, hole.min.y - 0.25f, hole.center.z + 0.5f);
                StartingFuelBody.AddFlame(oilRag, 0.07f, 0.2f);
                oilRagLight = oilRag.gameObject.AddComponent<Light>();
                oilRagLight.color = new Color(1f, 0.5f, 0.15f); oilRagLight.range = 1.6f; oilRagLight.shadows = LightShadows.None;
            }
            if (oilRag != null) oilRag.gameObject.SetActive(burning);
        }
    }
}
