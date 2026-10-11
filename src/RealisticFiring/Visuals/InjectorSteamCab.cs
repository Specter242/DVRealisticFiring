using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Simulation.Cars;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class InjectorSteamCab
    {
        private const string RootName = "[RealisticFiring injector steam]";
        private const string SourceLivery = "LocoS282A";
        private const string SanderHolder = "Right/Sand";
        private const string SanderControl = "C_Sand";

        // On the backhead, from behind the stock control's handle: toward the
        // cab's middle and up, in m; looked for forward from this far behind
        // the handle, up to this far.
        private const float Across = 0.15f;
        private const float Up = 0.1f;
        private const float LookFrom = 0.3f;
        private const float BackheadSearch = 1.5f;

        // Its plate's middle this far under the lever's housing, and standing
        // this far proud of the surface, in m.
        private const float PlateGap = 0.035f;
        private const float PlateProud = 0.004f;

        private static readonly Dictionary<string, (Vector3 Move, Vector3 Turns)> Seats = new Dictionary<string, (Vector3, Vector3)>
        {
            ["LocoS282A"] = (new Vector3(0.11f, 0.03f, 0.14f), new Vector3(-180f, 0f, 2f)), // 2026-10-11
            ["LocoS060"] = (new Vector3(-0.09f, -0.37f, 0.02f), new Vector3(-180f, 0f, 2f)), // 2026-10-11
            ["Jk3A"] = (new Vector3(-0.06f, -0.13f, 0.02f), new Vector3(-180f, 0f, 2f)), // both levers, 2026-10-11
            ["S484-840A"] = (new Vector3(-0.25f, -0.19f, -0.01f), new Vector3(-180f, 0f, 2f)), // 2026-10-11
            ["S464-730"] = (new Vector3(0f, -0.22f, 0f), new Vector3(-180f, 0f, 2f)), // 2026-10-11
            ["FEF-3"] = (new Vector3(-0.14f, -0.48f, -0.03f), new Vector3(-180f, 0f, 2f)), // 2026-10-11
        };

        /// <summary>Builds the steam levers in a newly loaded cab interior. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !InjectorRig.TryGet(car, out var rig))
                return null;
            if (!rig.Circuits.Any(c => c.Steam != null))
                return null;
            var root = new GameObject(RootName).transform;
            root.SetParent(interior, false);
            var notes = new List<string>();
            for (var i = 0; i < rig.Circuits.Count; i++)
            {
                var circuit = rig.Circuits[i];
                if (circuit.Steam == null)
                    continue;
                var valve = InjectorValves.For(i);
                var feeders = interior.GetComponentsInChildren<InteractablePortFeeder>(true)
                    .Where(f => f.portId == circuit.Control.id && !f.transform.IsChildOf(root))
                    .ToList();
                if (feeders.Count == 0)
                    notes.Add($"no cab control works '{circuit.Control.id}'");
                foreach (var feeder in feeders)
                    notes.Add(DummyWheels.TryGetValue(car.carLivery.id, out var dummy)
                        ? BuildWheel(car, interior, root, feeder, circuit, valve, dummy.Name, dummy.PlateAft)
                        : Build(car, interior, root, feeder, circuit, valve));
            }
            return "injector steam levers: " + string.Join("; ", notes);
        }

        /// <summary>Rebuilds every loaded cab's steam levers, as the placement sliders move.</summary>
        public static void RebuildLoaded()
        {
            foreach (var rig in InjectorRig.All)
            {
                var car = rig.Car;
                var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
                var old = interior != null ? interior.Find(RootName) : null;
                if (old == null)
                    continue;
                Object.DestroyImmediate(old.gameObject);
                Attach(car, interior);
            }
        }

        private static readonly Dictionary<string, (string Name, float PlateAft)> DummyWheels = new Dictionary<string, (string, float)>
        {
            ["A.T.&S.F. 2901-A"] = ("Injector 2", 0.1f),
        };
        private const float DummyReach = 0.3f;

        private static string BuildWheel(TrainCar car, Transform interior, Transform root, InteractablePortFeeder feeder, InjectorCircuit circuit,
            StokerJetControls.Valve valve, string dummyName, float plateAft)
        {
            var stock = feeder.transform;
            var stockMesh = stock.GetComponentsInChildren<MeshRenderer>(true)
                .OrderBy(r => Vector3.Distance(Drawn(r), stock.position)).FirstOrDefault();
            var stockAt = stockMesh != null ? Drawn(stockMesh) : stock.position;
            var dummyMeshes = interior.GetComponentsInChildren<MeshRenderer>(true)
                .Where(r => r.name.StartsWith(dummyName) && Vector3.Distance(Drawn(r), stockAt) < DummyReach)
                .ToList();
            var source = PrefabHolder(car, interior, stock);
            if (dummyMeshes.Count == 0 || stockMesh == null || source == null || source.Find(stock.name) == null)
                return $"no second wheel '{dummyName}' to work beside '{feeder.name}'; " + Build(car, interior, root, feeder, circuit, valve);
            var dummy = dummyMeshes.OrderBy(r => Vector3.Distance(Drawn(r), stockAt)).First();
            var dummyAt = Drawn(dummy);
            var dummies = dummyMeshes.Where(r => Vector3.Distance(Drawn(r), dummyAt) < DummyReach).ToList();

            var frame = new GameObject(valve.Id).transform;
            frame.gameObject.SetActive(false);
            frame.SetParent(root, false);
            var holder = Object.Instantiate(source.gameObject, frame, false).transform;
            var control = holder.Find(stock.name);
            // Turned as the working wheel, and set where the dummy is drawn.
            holder.rotation = stock.parent.rotation;
            var cloneMesh = control.GetComponentsInChildren<MeshRenderer>(true)
                .OrderBy(r => Vector3.Distance(Drawn(r), control.position)).FirstOrDefault();
            if (cloneMesh != null)
                holder.position += dummyAt - Drawn(cloneMesh);
            // The copy carries the other side's wheel too: that goes.
            var cloneAt = cloneMesh != null ? Drawn(cloneMesh) : control.position;
            foreach (var far in holder.GetComponentsInChildren<MeshRenderer>(true).Where(r => Vector3.Distance(Drawn(r), cloneAt) > DummyReach).ToList())
            {
                far.enabled = false;
                foreach (var collider in far.GetComponents<Collider>())
                    collider.enabled = false;
            }
            foreach (var mesh in dummies)
                mesh.enabled = false;

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => StokerCab.InputComponents.Contains(c.GetType().Name) || c.GetType().Name == "PercentageControlNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var cloneFeeder = control.GetComponent<InteractablePortFeeder>();
            if (cloneFeeder == null)
            {
                Object.Destroy(frame.gameObject);
                foreach (var mesh in dummies)
                    mesh.enabled = true;
                return $"the copied wheel beside '{feeder.name}' has no feeder";
            }
            cloneFeeder.portId = valve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(cloneFeeder, circuit.Steam);
            control.gameObject.AddComponent<HoverName>().Source = () => SteamHover(circuit, valve);
            if (feeder.GetComponent<HoverName>() == null)
                feeder.gameObject.AddComponent<HoverName>().Source = () => WaterHover(circuit);
            frame.gameObject.SetActive(true);
            var move = dummyAt - stockAt;
            var plates = Plates(interior, stock, root, FeedPumpCab.InjectorPlate(interior, stock) is TextMeshPro water
                ? PlateWithBacking(water).position + Vector3.ProjectOnPlane(move, water.transform.forward) - car.transform.forward * plateAft
                : dummyAt, Quaternion.identity);
            var at = car.transform.InverseTransformPoint(dummyAt);
            var stockLocal = car.transform.InverseTransformPoint(stockAt);
            return $"'{valve.Id}' is the second wheel '{dummy.name}' beside '{feeder.name}', the dummy drawn at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m " +
                   $"and the working wheel at ({stockLocal.x:F2}, {stockLocal.y:F2}, {stockLocal.z:F2}) m; a copy of the working wheel: {Where(car, holder)}; " +
                   $"{dummies.Count} dummy meshes hidden" + plates;
        }

        // Each of a copy's meshes, drawn or hidden, and where, for the log.
        private static string Where(TrainCar car, Transform holder) =>
            string.Join(", ", holder.GetComponentsInChildren<MeshRenderer>(true).Select(r =>
            {
                var p = car.transform.InverseTransformPoint(Drawn(r));
                return $"'{r.name}' {(r.enabled && r.gameObject.activeInHierarchy ? "drawn" : "hidden")} at ({p.x:F2}, {p.y:F2}, {p.z:F2})";
            }));

        // Where a mesh is drawn: its bounds' middle, wherever its anchor is.
        private static Vector3 Drawn(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null && filter.sharedMesh != null
                ? renderer.transform.TransformPoint(filter.sharedMesh.bounds.center)
                : renderer.transform.position;
        }

        // The stock control's holder in the interior's prefab: of holders of
        // the same name and place (the 2901 has two "Right/Injector"), the one
        // set where the live one is.
        private static Transform PrefabHolder(TrainCar car, Transform interior, Transform stock)
        {
            var prefab = car.carLivery.interiorPrefab != null ? car.carLivery.interiorPrefab.transform : null;
            var live = stock.parent;
            if (prefab == null || live == null || live.parent == null)
                return null;
            var parentPath = PathFrom(interior, live.parent);
            var parent = parentPath == null ? null : parentPath.Length == 0 ? prefab : prefab.Find(parentPath);
            if (parent == null)
                return null;
            return parent.Cast<Transform>()
                .Where(t => t.name == live.name)
                .OrderBy(t => Vector3.Distance(t.localPosition, live.localPosition))
                .FirstOrDefault();
        }

        private static string PathFrom(Transform root, Transform t)
        {
            var names = new List<string>();
            for (; t != null && t != root; t = t.parent)
                names.Add(t.name);
            if (t != root)
                return null;
            names.Reverse();
            return string.Join("/", names);
        }

        private static string Build(TrainCar car, Transform interior, Transform root, InteractablePortFeeder feeder, InjectorCircuit circuit, StokerJetControls.Valve valve)
        {
            var source = Source(interior, out var facing);
            if (source.Holder == null)
                return $"no lever to copy beside '{feeder.name}'";

            var holder = Object.Instantiate(source.Holder.gameObject, root, false).transform;
            holder.name = valve.Id;
            var control = holder.Find(source.Control.name);
            holder.rotation = facing;

            var stock = feeder.transform;
            var local = car.transform.InverseTransformPoint(stock.position);
            var middle = local.x > 0f ? -car.transform.right : car.transform.right;
            var up = car.transform.up;
            var forward = car.transform.forward;
            var settings = Main.Settings;
            var seatedHere = Seats.TryGetValue(car.carLivery.id, out var seat);
            var nudge = seatedHere ? Vector3.zero : new Vector3(settings.InjectorSteamNudgeAcross, settings.InjectorSteamNudgeUp, settings.InjectorSteamNudgeForward) / 100f;
            var aim = stock.position + middle * (Across + seat.Move.x + nudge.x) + up * (Up + seat.Move.y + nudge.y);
            var proud = seat.Move.z + nudge.z;
            var onBoiler = TryFindBackhead(car, aim - forward * LookFrom, forward, out var surface, out var normal);
            if (!onBoiler)
            {
                surface = aim;
                normal = -forward;
            }

            var shape = Shape(holder, control);
            var upOnFace = Vector3.ProjectOnPlane(up, normal).normalized;
            var across = Vector3.Cross(upOnFace, normal);
            var turns = seatedHere ? seat.Turns : new Vector3(settings.InjectorSteamTurn, settings.InjectorSteamTilt, settings.InjectorSteamTwist);
            var extra = Quaternion.AngleAxis(turns.x, upOnFace)
                        * Quaternion.AngleAxis(turns.y, across)
                        * Quaternion.AngleAxis(turns.z, normal);
            holder.rotation = extra * Quaternion.LookRotation(normal, upOnFace) * Quaternion.Inverse(Quaternion.LookRotation(shape.Face, shape.Handle));
            holder.position = surface + normal * (Back(holder, -normal) - proud);
            var move = holder.position - stock.position;

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)
                                     || c.GetType().Name == "PercentageControlNames" || c.GetType().Name == "NotchedControlCustomNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var spec = control.GetComponent<DV.CabControls.Spec.Lever>();
            if (spec != null)
            {
                spec.notches = InjectorValves.Notches.Length;
                spec.useSteppedJoint = true;
                spec.steppedValueUpdate = true;
                spec.scrollWheelHoverScroll = 1f;
            }
            control.gameObject.AddComponent<DV.HUD.NotchedControlCustomNames>().customNames = InjectorValves.Notches;
            var cloneFeeder = control.GetComponent<InteractablePortFeeder>();
            if (cloneFeeder == null)
            {
                Object.Destroy(holder.gameObject);
                return $"the copied lever beside '{feeder.name}' has no feeder";
            }
            cloneFeeder.portId = valve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(cloneFeeder, circuit.Steam);
            control.gameObject.AddComponent<HoverName>().Source = () => SteamHover(circuit, valve);
            if (feeder.GetComponent<HoverName>() == null)
                feeder.gameObject.AddComponent<HoverName>().Source = () => WaterHover(circuit);

            var bottom = Reach(holder, -upOnFace);
            var plateAt = holder.position - upOnFace * (bottom + PlateGap) - normal * (Back(holder, -normal) - PlateProud);
            var plates = Plates(interior, stock, root, plateAt, Quaternion.FromToRotation(-forward, normal));
            var at = car.transform.InverseTransformPoint(control.position);
            var handle = car.transform.InverseTransformPoint(stock.position);
            return $"'{valve.Id}' {(onBoiler ? "on the backhead" : "beside (no backhead found)")} by '{feeder.name}' (its handle at ({handle.x:F2}, {handle.y:F2}, {handle.z:F2}) m) " +
                   $"at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m, " +
                   $"a copy of the S282's sander lever" + plates;
        }

        // The lever to copy: the S282's sander lever, facing as it does on the
        // S282's backhead, square to the cab.
        private static (Transform Holder, Transform Control) Source(Transform interior, out Quaternion facing)
        {
            facing = interior.rotation;
            if (!Globals.G.Types.TryGetLivery(SourceLivery, out var s282) || s282.interiorPrefab == null)
                return default;
            var s282Root = s282.interiorPrefab.transform;
            var holder = s282Root.Find(SanderHolder);
            var control = holder != null ? holder.Find(SanderControl) : null;
            if (holder == null || control == null || control.GetComponent<InteractablePortFeeder>() == null)
                return default;
            facing = interior.rotation * (Quaternion.Inverse(s282Root.rotation) * holder.rotation);
            return (holder, control);
        }

        // The boiler's backhead in front of a point: the first surface of this
        // car facing back at it, passing over the cab's controls.
        private static bool TryFindBackhead(TrainCar car, Vector3 from, Vector3 forward, out Vector3 point, out Vector3 normal)
        {
            point = from;
            normal = -forward;
            foreach (var hit in Physics.RaycastAll(from, forward, BackheadSearch, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (TrainCar.Resolve(hit.collider.gameObject) != car || Vector3.Dot(hit.normal, -forward) < 0.7f
                    || hit.collider.GetComponentInParent<ControlImplBase>() != null || hit.collider.GetComponentInParent<InteractablePortFeeder>() != null
                    || hit.collider.transform.root.name == RootName || hit.collider.GetComponentInParent<SimControlBinder>() != null)
                    continue;
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            return false;
        }

        // The lever's shape in its holder's frame, before it is turned: the
        // quadrant housing is thinnest through its face, which faces away
        // from the handle's pivot side; the handle is the longest reach, from
        // the pivot toward the meshes' middle.
        private static (Vector3 Face, Vector3 Handle) Shape(Transform holder, Transform control)
        {
            var bounds = new Bounds();
            var any = false;
            foreach (var filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var b = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = holder.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? b.min.x : b.max.x,
                        (i & 2) == 0 ? b.min.y : b.max.y,
                        (i & 4) == 0 ? b.min.z : b.max.z)));
                    if (!any)
                        bounds = new Bounds(corner, Vector3.zero);
                    else
                        bounds.Encapsulate(corner);
                    any = true;
                }
            }
            if (!any)
                return (Vector3.back, Vector3.up);
            var size = bounds.size;
            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var order = Enumerable.Range(0, 3).OrderBy(i => size[i]).ToArray();
            var thin = axes[order[0]];
            var longest = axes[order[2]];
            var pivot = holder.InverseTransformPoint(control.position);
            var toMiddle = bounds.center - pivot;
            var handle = Vector3.Dot(toMiddle, longest) >= 0f ? longest : -longest;
            // The handle stands in front of the housing: the face is the side of
            // the thin axis the pivot sits on, away from the housing's back.
            var face = Vector3.Dot(pivot - bounds.center, thin) >= 0f ? thin : -thin;
            return (face, handle);
        }

        private static float Reach(Transform holder, Vector3 along) => Back(holder, along);

        // How far the lever's meshes reach from its origin along a direction (into the backhead), in m.
        private static float Back(Transform holder, Vector3 into)
        {
            var back = 0f;
            foreach (var filter in holder.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var bounds = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = filter.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z));
                    back = Mathf.Max(back, Vector3.Dot(corner - holder.position, into));
                }
            }
            return back;
        }

        // The stock control's plate reads INJ WATER, and a copy beside the
        // steam lever INJ STEAM. Returns a note for the log.
        private static string Plates(Transform interior, Transform stock, Transform root, Vector3 at, Quaternion turn)
        {
            var plate = FeedPumpCab.InjectorPlate(interior, stock);
            if (plate == null)
                return ", no plate found";
            FeedPumpCab.Retext(plate, "INJ WATER");
            var source = PlateWithBacking(plate);
            var copy = Object.Instantiate(source.gameObject, root, true);
            copy.name = "label INJ STEAM";
            copy.transform.SetPositionAndRotation(at, turn * source.rotation);
            FeedPumpCab.Retext(copy.GetComponentInChildren<TextMeshPro>(true), "INJ STEAM");
            return ", plates INJ WATER and INJ STEAM" + (source == plate.transform ? " (its backing not found)" : "");
        }

        private static Transform PlateWithBacking(TextMeshPro text)
        {
            var t = text.transform.parent;
            for (var i = 0; i < 2 && t != null; i++, t = t.parent)
            {
                var backing = t.GetComponent<MeshRenderer>();
                if (backing != null && t.GetComponent<TextMeshPro>() == null)
                    return t;
            }
            t = text.transform.parent;
            for (var i = 0; i < 2 && t != null; i++, t = t.parent)
            {
                var beside = t.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.GetComponent<TextMeshPro>() == null && r.GetComponent<TMP_SubMesh>() == null).ToList();
                if (beside.Count == 1 && Vector3.Distance(Drawn(beside[0]), text.transform.position) < BackingReach)
                    return t;
            }
            return text.transform;
        }

        private const float BackingReach = 0.1f;

        private static string Side(InjectorCircuit circuit) => string.IsNullOrEmpty(circuit.Name) ? "" : circuit.Name + " ";

        internal static string SteamHover(InjectorCircuit circuit, StokerJetControls.Valve valve)
        {
            var position = InjectorValves.Position(circuit.Steam.Value);
            var flow = circuit.Flow;
            var hint = flow.State switch
            {
                InjectorState.Off when flow.Overflow > 0f => "water running out of the overflow: steam to Prime, or shut the water",
                InjectorState.Off => "shut",
                InjectorState.Priming => "priming: it picks up as the overflow quiets",
                InjectorState.Working when circuit.Steam.Value < InjectorSpec.SteamFeedFrom => "picked up: steam to Feed",
                InjectorState.Working => $"feeding {flow.WaterIn:F1} kg/s; the water valve sets it",
                InjectorState.Hot => "hot: hold it at Prime to cool it",
                InjectorState.LowPressure => "waiting for pressure",
                InjectorState.NoWater when circuit.Control.Value <= InjectorSpec.OpenThreshold => "no water: open the water valve first",
                InjectorState.NoWater => "the tender is dry",
                _ => "the jet broke: steam Off, then prime it again",
            };
            return $"{valve.Label}: {position} ({hint})";
        }

        internal static string WaterHover(InjectorCircuit circuit)
        {
            var flow = circuit.Flow;
            return $"{Side(circuit)}injector water: {circuit.Control.Value:P0} open" +
                   (flow.State == InjectorState.Working ? $", feeding {flow.WaterIn:F1} kg/s" : flow.Overflow > 0f ? ", running at the overflow" : "");
        }
    }
}
