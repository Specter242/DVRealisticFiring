using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Simulation.Ports;
using DV.ThingTypes;
using DV.Utils;
using LocoSim.Implementations;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Puts the stoker's jet manifold in the cab beside the loco's own stoker
    /// valve on the fireman's side (the left, car −X, as on the stock locos;
    /// the Hudson has a stoker valve on each side): a header pipe with six
    /// valve bodies, each carrying a working handwheel cloned from the stock
    /// S282's blower valve, laid out like an arrow pointing down (master jet
    /// on top; back jets, front jets, fine-coal jet below), with label plates
    /// cloned from the S282's. Each wheel works its jet's control port, which
    /// the game saves. The manifold belongs to the cab interior, so it is
    /// rebuilt every time the interior loads.
    /// </summary>
    internal static class StokerCab
    {
        internal const string SourceLivery = "LocoS282A";
        internal const string SourceValve = "Left/Valves/Blower";
        internal const string SourceControl = "C_Blower";
        internal const string SourceLabel = "LocoS282A_Interior_Labels/Blower/Label_RM_1";

        private const string SourceLever = "Right/CylinderCock";
        private const string SourceLeverControl = "C_CylinderCock";

        // Looked for straight down from this point, in the manifold's frame;
        // the pivot sits this far above the floor, as on the S282.
        private static readonly Vector3 LeverAbove = new Vector3(0f, -0.3f, -0.3f);
        private const float FloorSearch = 2.5f;
        private const float FallbackDrop = 0.9f;
        private const float PivotAboveFloor = 0.05f;

        private static readonly Dictionary<string, Vector3> LeverMoves = new Dictionary<string, Vector3>
        {
            ["S464-730"] = new Vector3(0.45f, 0f, -0.15f), // a foot and a half inboard, six inches aft
            ["LocoS282A"] = new Vector3(0f, 0f, -0.3f), // a foot aft
        };

        // The label is a plate lying flat on the floor, face up, in line with
        // the lever, aft of its foot, between it and the fireman standing
        // behind it, so the handle never hides it, in m. (The plate's face
        // stands about 1.7 cm proud of its origin.)
        private const float LabelAft = 0.22f;
        private const float LabelAboveFloor = 0.003f;
        private const float FloorLabelScale = 1.4f;
        private static readonly string[] LeverNotches = { "Reverse", "Neutral", "Forward" };

        // What the manifold is placed from: the loco's own stoker valve on the
        // fireman's side, or, on a loco fitted with the mod's stoker, the
        // fireman's blower valve.
        private const string StokerPort = "stokerControl.EXT_IN";

        private static readonly Dictionary<string, string> AnchorPorts = new Dictionary<string, string>
        {
            ["LocoS282A"] = "blower.EXT_IN",
        };

        // The stoker engine's own wheel, above the master jet, on locos fitted
        // with the mod's stoker.
        private static readonly Vector2 EngineAt = new Vector2(0f, 0.135f);
        private const float EngineHeaderTop = 0.165f;
        private const string RootName = "[RealisticFiring stoker manifold]";

        // The blower's own keys and joystick axis; the clones have none.
        internal static readonly string[] InputComponents =
        {
            "AnalogSetValueJoystickInput",
            "ToggleValueKeyboardInput",
            "MouseScrollKeyboardInput",
        };

        private static readonly (Vector2 At, string Plate)[] Layout =
        {
            (new Vector2(0f, 0.05f), "MASTER"),
            (new Vector2(-0.07f, -0.12f), "L BACK"),
            (new Vector2(0.07f, -0.12f), "R BACK"),
            (new Vector2(-0.07f, -0.035f), "L FRONT"),
            (new Vector2(0.07f, -0.035f), "R FRONT"),
            (new Vector2(0f, -0.205f), "FINES"),
        };

        private const float HeaderDepth = 0.035f;
        private const float HeaderRadius = 0.017f;
        private const float BranchRadius = 0.011f;
        private const float BodyRadius = 0.014f;
        private const float BodyFront = -0.004f;
        private const float WheelDepth = -0.012f;
        private const float HeaderBottom = -0.235f;
        private const float HeaderTop = 0.08f;
        private const float LabelDrop = 0.044f;
        private const float LabelScale = 0.75f;

        /// <summary>
        /// Where a loco's manifold sits, from its fireman's stoker valve mount:
        /// across (toward the middle of the cab), up, and out (into the panel),
        /// in cm, in the mount's frame; then turned and tilted from the mount's
        /// facing, in degrees. It always stands upright.
        /// </summary>
        private readonly struct Placement
        {
            public Placement(Vector3 offsetCm, float turn, float tilt)
            {
                OffsetCm = offsetCm;
                Turn = turn;
                Tilt = tilt;
            }

            public Vector3 OffsetCm { get; }
            public float Turn { get; }
            public float Tilt { get; }
        }

        private static readonly Dictionary<string, Placement> Placements = new Dictionary<string, Placement>
        {
            ["S464-730"] = new Placement(new Vector3(19.2f, -8.1f, -11.5f), 8f, -1f),
            ["LocoS282A"] = new Placement(new Vector3(88f, 25f, 9f), -9f, 0f),
            // The S484-840 has one stoker valve, on the right beside the injector.
            ["S484-840A"] = new Placement(new Vector3(-1f, -20f, 0f), -6f, 0f),
            // The 844's stoker valve sits low beside its blower valve; the
            // manifold stands 80 cm above it.
            ["FEF-3"] = new Placement(new Vector3(-34f, 80f, 8f), 0f, 0f),
        };

        private static readonly Dictionary<string, string> StokerValveCopies = new Dictionary<string, string>
        {
            ["FEF-3"] = "Controllers/Left/automaticStoker",
        };

        // Where the copy's wheel sits on the manifold's face, below the
        // fine-coal jet and its plate, in m.
        private static readonly Vector2 CopiedValveAt = new Vector2(0f, -0.34f);

        private static readonly Placement DefaultPlacement = new Placement(new Vector3(18f, 0f, 0f), 0f, 0f);

        private static readonly Dictionary<string, float> SupplyPipeAt = new Dictionary<string, float>
        {
            ["S464-730"] = -0.08f,
        };

        private const float SupplyRadius = 0.014f;
        private const float SupplySearch = 0.6f;
        private const float SupplyInto = 0.02f;
        private const float FlangeRadius = 0.03f;
        private const float FlangeThickness = 0.012f;

        private static readonly Dictionary<TrainCar, Transform> Interiors = new Dictionary<TrainCar, Transform>();
        private static Mesh cylinder;
        private static Material steel;

        /// <summary>
        /// Builds the manifold in a newly loaded cab interior. Returns a line for
        /// the log, or null when the loco has no stoker jet controls.
        /// </summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !Main.Settings.ShowStokerJetValves)
                return null;
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            // Looked for by its component first: the port lookup logs an error for every loco without it.
            if (flow == null || !flow.OrderedSimComps.Any(c => c != null && c.id == StokerJetControls.Master.Id)
                || !flow.TryGetPort(StokerJetControls.Master.PortId, out _))
                return null;

            // The anchor valve on the fireman's side: the one furthest to the left.
            var anchorPort = AnchorPorts.TryGetValue(car.carLivery.id, out var listed) ? listed : StokerPort;
            var anchor = interior.GetComponentsInChildren<InteractablePortFeeder>(false)
                .Where(f => f.portId == anchorPort)
                .Select(f => f.transform)
                .OrderBy(t => car.transform.InverseTransformPoint(t.position).x)
                .FirstOrDefault();
            if (anchor == null)
                return $"no valve working '{anchorPort}' in the cab to place the manifold by; the jets stay at their settings";
            if (!Globals.G.Types.TryGetLivery(SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {SourceLivery} cab to take valves from; the jets stay at their settings";
            var sourceValve = livery.interiorPrefab.transform.Find(SourceValve);
            var sourceControl = sourceValve != null ? sourceValve.Find(SourceControl) : null;
            if (sourceControl == null)
                return $"no '{SourceValve}/{SourceControl}' in the {SourceLivery} cab; the jets stay at their settings";
            var sourceLabel = livery.interiorPrefab.transform.Find(SourceLabel);

            Interiors[car] = interior;

            // Built inactive, so nothing wakes before it is posed and rewired.
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            root.AddComponent<ManifoldTracer>().Car = car;
            // The wheel spins as it turns; its mount doesn't.
            var mount = anchor.parent != null ? anchor.parent : anchor;
            var settings = Main.Settings;
            var placement = Placements.TryGetValue(car.carLivery.id, out var known) ? known : DefaultPlacement;
            var offset = placement.OffsetCm + new Vector3(settings.ManifoldNudgeAcross, settings.ManifoldNudgeUp, settings.ManifoldNudgeOut);
            var facing = mount.rotation * Quaternion.Euler(placement.Tilt + settings.ManifoldNudgeTilt, placement.Turn + settings.ManifoldNudgeTurn, 0f);
            var into = facing * Vector3.forward;
            var up = Vector3.ProjectOnPlane(car.transform.up, into);
            var frame = root.transform;
            frame.rotation = Quaternion.LookRotation(into, up.sqrMagnitude > 1e-6f ? up : facing * Vector3.up)
                             * Quaternion.Euler(0f, 0f, settings.ManifoldNudgeRoll);
            frame.position = mount.TransformPoint(offset / 100f);

            var engine = flow.TryGetPort(StokerJetControls.Engine.PortId, out var enginePort);
            BuildPipes(frame, engine);
            var supply = SupplyPipeAt.TryGetValue(car.carLivery.id, out var supplyAt)
                ? BuildSupplyPipe(frame, car, supplyAt)
                : null;
            var labels = 0;
            Transform plateFacing = null;
            if (engine)
            {
                var control = PlaceWheel(sourceValve, frame, new Vector3(EngineAt.x, EngineAt.y, WheelDepth), StokerJetControls.Engine, enginePort);
                if (PlaceLabel(sourceControl, sourceLabel, control, frame, new Vector3(EngineAt.x, EngineAt.y - LabelDrop, BodyFront - 0.002f), "STOKER"))
                    labels++;
            }
            for (var i = 0; i < Layout.Length; i++)
            {
                var valve = StokerJetControls.Valves[i];
                if (!flow.TryGetPort(valve.PortId, out var port))
                    continue;
                var at = Layout[i].At;
                var control = PlaceWheel(sourceValve, frame, new Vector3(at.x, at.y, WheelDepth), valve, port);
                plateFacing = control;
                if (PlaceLabel(sourceControl, sourceLabel, control, frame, new Vector3(at.x, at.y - LabelDrop, BodyFront - 0.002f), Layout[i].Plate))
                    labels++;
            }

            var lever = flow.TryGetPort(StokerJetControls.Reverse.PortId, out var leverPort)
                ? PlaceLever(livery.interiorPrefab.transform, frame, leverPort, car, sourceControl, sourceLabel)
                : null;

            var copied = !engine && StokerValveCopies.TryGetValue(car.carLivery.id, out var copyPath)
                ? PlaceStokerValveCopy(car, flow, frame, mount, copyPath, anchorPort, sourceControl, sourceLabel, plateFacing)
                : null;

            var gauges = StokerGauges.Build(frame, livery.interiorPrefab.transform, car);
            var feedPump = FeedPumpCab.BuildInManifold(car, flow, frame, livery.interiorPrefab.transform, sourceValve, sourceControl, sourceLabel, plateFacing);

            root.SetActive(true);
            gauges.WriteScales();
            feedPump.Gauge?.WriteScales();
            var local = car.transform.InverseTransformPoint(frame.position);
            return $"stoker jet manifold placed by the fireman's '{anchor.name}' at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car frame " +
                   $"(across {offset.x:F1} cm, up {offset.y:F1} cm, out {offset.z:F1} cm from its mount, " +
                   $"turn {placement.Turn + settings.ManifoldNudgeTurn:F0}°, tilt {placement.Tilt + settings.ManifoldNudgeTilt:F0}°, " +
                   $"roll {settings.ManifoldNudgeRoll:F0}°), {Layout.Length + (engine ? 1 : 0)} valves, {labels} labels, {gauges.Count} gauges" +
                   (lever != null ? $", operating lever {lever}" : "") +
                   (supply != null ? $", {supply}" : "") +
                   (copied != null ? $", {copied}" : "") +
                   (feedPump.Note != null ? $"; {feedPump.Note}" : "");
        }

        private static float nextCheck;

        /// <summary>
        /// Puts the manifold back in any loaded cab that has lost it, checking
        /// once a second; call every frame.
        /// </summary>
        public static void EnsureBuilt()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            foreach (var car in Interiors.Keys.ToList())
            {
                if (car == null)
                {
                    Interiors.Remove(car);
                    continue;
                }
                var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
                if (interior == null || interior.Find(RootName) != null)
                    continue;
                var result = Attach(car, interior);
                if (result != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: the cab had lost its stoker manifold; {result}");
            }
        }

        /// <summary>True while the manifold is being rebuilt on purpose.</summary>
        internal static bool Rebuilding { get; private set; }

        /// <summary>Rebuilds the manifold in every loaded cab, as the placement settings move.</summary>
        public static void RebuildLoaded()
        {
            Rebuilding = true;
            try
            {
                RebuildEach();
            }
            finally
            {
                Rebuilding = false;
            }
        }

        private static void RebuildEach()
        {
            foreach (var pair in Interiors.ToList())
            {
                var car = pair.Key;
                var interior = pair.Value;
                if (car == null || interior == null || car.loadedInterior == null || car.loadedInterior.transform != interior)
                {
                    Interiors.Remove(car);
                    continue;
                }
                var old = interior.Find(RootName);
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
                var result = Attach(car, interior);
                if (result != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {result}");
            }
        }

        internal static Transform PlaceWheel(Transform sourceValve, Transform frame, Vector3 at, StokerJetControls.Valve valve, Port port)
        {
            var holder = Object.Instantiate(sourceValve.gameObject, frame, false).transform;
            holder.name = valve.Id;
            var control = holder.Find(SourceControl);
            // The wheel turns about the manifold's z, like the stoker valve's.
            holder.rotation = frame.rotation * Quaternion.Inverse(control.localRotation);
            holder.position += frame.TransformPoint(at) - control.position;

            foreach (var input in control.GetComponents<MonoBehaviour>().Where(c => InputComponents.Contains(c.GetType().Name)))
                Object.DestroyImmediate(input);
            var feeder = control.GetComponent<InteractablePortFeeder>();
            feeder.portId = valve.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Text = valve.Label;
            return control;
        }

        // The operating lever on the floor: the S282's cylinder cock lever with
        // three named notches, working the lever's port. Returns where it
        // went, for the log, or null.
        private static string PlaceLever(Transform cabPrefab, Transform frame, Port port, TrainCar car, Transform sourceControl, Transform sourceLabel)
        {
            var move = LeverMoves.TryGetValue(car.carLivery.id, out var seated) ? seated : Vector3.zero;
            var above = frame.TransformPoint(LeverAbove) + car.transform.TransformVector(move);
            var onFloor = TryFindFloor(car, above, out var floor);
            if (!onFloor)
                floor = above - car.transform.up * FallbackDrop;

            var holder = BuildFloorLever(cabPrefab, frame, car, floor, StokerJetControls.Reverse.Id, StokerJetControls.Reverse.PortId, port, LeverNotches, () =>
            {
                var direction = StokerJetControls.Direction(port.Value);
                var jammed = FireboxRegistry.TryGet(car, out var firebox) && firebox.LastStoker is Core.StokerFlow flow && flow.Jammed;
                return $"{StokerJetControls.Reverse.Label}: {direction}" + (jammed ? " (jammed: reverse to clear)" : "");
            });
            if (holder == null)
                return null;
            PlaceFloorLabel(holder, frame, sourceControl, sourceLabel, car, floor, "STOKER FWD-REV");

            var local = car.transform.InverseTransformPoint(floor);
            return $"on the floor at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m" + (onFloor ? "" : " (no floor found; guessed)");
        }

        /// <summary>
        /// A floor lever: a copy of the S282's cylinder cock lever standing
        /// upright on <paramref name="floor"/>, swinging fore and aft along the
        /// loco, with the given named notches, working <paramref name="port"/>.
        /// Returns its holder, or null when there is no lever to copy.
        /// </summary>
        internal static Transform BuildFloorLever(Transform cabPrefab, Transform parent, TrainCar car, Vector3 floor,
            string name, string portId, Port port, string[] notches, System.Func<string> hover)
        {
            var source = cabPrefab.Find(SourceLever);
            if (source == null || source.Find(SourceLeverControl) == null)
                return null;

            // Upright on the floor, swinging straight fore and aft along the loco.
            var up = car.transform.up;
            var forward = car.transform.forward;
            var facing = Quaternion.LookRotation(forward, up);
            var holder = Object.Instantiate(source.gameObject, parent, false).transform;
            holder.name = name;
            var control = holder.Find(SourceLeverControl);
            holder.rotation = facing * Quaternion.Inverse(control.localRotation);
            holder.position += floor + up * PivotAboveFloor - control.position;

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => InputComponents.Contains(c.GetType().Name) || c.GetType().Name == "PercentageControlNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var spec = control.GetComponent<DV.CabControls.Spec.Lever>();
            if (spec != null)
            {
                spec.notches = notches.Length;
                spec.useSteppedJoint = true;
                spec.steppedValueUpdate = true;
                // A wheel tick turns a stepped lever by this many notches; the
                // cylinder cock lever's own setting skipped Neutral.
                spec.scrollWheelHoverScroll = 1f;
            }
            control.gameObject.AddComponent<DV.HUD.NotchedControlCustomNames>().customNames = notches;
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (feeder == null)
            {
                Object.Destroy(holder.gameObject);
                return null;
            }
            feeder.portId = portId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Source = hover;
            return holder;
        }

        /// <summary>
        /// A floor lever's label: a plate flat on the floor aft of its foot,
        /// face up, its text reading from behind, so the handle never hides it.
        /// </summary>
        internal static void PlaceFloorLabel(Transform holder, Transform frame, Transform sourceControl, Transform sourceLabel, TrainCar car, Vector3 floor, string text)
        {
            // A label sits like the S282's blower label to a valve whose wheel
            // faces back along its z, so give it a frame whose z points down,
            // away from someone looking at it, and whose up is forward.
            var up = car.transform.up;
            var forward = car.transform.forward;
            var plate = new GameObject("label frame").transform;
            plate.SetParent(holder, false);
            plate.SetPositionAndRotation(floor + up * LabelAboveFloor - forward * LabelAft,
                Quaternion.LookRotation(-up, forward));
            PlaceLabel(sourceControl, sourceLabel, plate, frame, frame.InverseTransformPoint(plate.position), text, FloorLabelScale);
        }

        // The cab floor under a point: the first upward-facing surface of this
        // car straight below it.
        internal static bool TryFindFloor(TrainCar car, Vector3 from, out Vector3 floor)
        {
            floor = from;
            var down = -car.transform.up;
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(from, down, FloorSearch, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (Vector3.Dot(hit.normal, car.transform.up) < 0.7f || TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                floor = hit.point;
                return true;
            }
            return false;
        }

        // A copy of the S282's blower label plate under a wheel, turned as the
        // S282's sits to its valve, smaller, with new text.
        internal static bool PlaceLabel(Transform sourceControl, Transform sourceLabel, Transform control, Transform frame, Vector3 at, string text, float scale = LabelScale)
        {
            if (sourceLabel == null)
                return false;
            var turn = Quaternion.Inverse(sourceControl.rotation) * sourceLabel.rotation;
            var label = Object.Instantiate(sourceLabel.gameObject, frame, false).transform;
            label.name = "label " + text;
            label.SetPositionAndRotation(frame.TransformPoint(at), control.rotation * turn);
            label.localScale = sourceLabel.lossyScale * scale;

            // The stock text is a localization key; ours is plain text.
            foreach (var localize in label.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c.GetType().Name == "Localize").ToList())
                Object.DestroyImmediate(localize);
            var tmp = label.GetComponentInChildren<TextMeshPro>(true);
            if (tmp != null)
                tmp.text = text;
            return tmp != null;
        }

        // The header up the middle, a branch out to each side valve, and a
        // body under every wheel. Plain dark steel; no colliders.
        private static void BuildPipes(Transform frame, bool engine)
        {
            Pipe(frame, "header", new Vector3(0f, HeaderBottom, HeaderDepth), new Vector3(0f, engine ? EngineHeaderTop : HeaderTop, HeaderDepth), HeaderRadius);
            if (engine)
                Pipe(frame, "body STOKER", new Vector3(EngineAt.x, EngineAt.y, HeaderDepth), new Vector3(EngineAt.x, EngineAt.y, BodyFront), BodyRadius);
            foreach (var (at, plate) in Layout)
            {
                if (at.x != 0f)
                    Pipe(frame, "branch " + plate, new Vector3(0f, at.y, HeaderDepth), new Vector3(at.x, at.y, HeaderDepth), BranchRadius);
                Pipe(frame, "body " + plate, new Vector3(at.x, at.y, HeaderDepth), new Vector3(at.x, at.y, BodyFront), BodyRadius);
            }
        }

        // A copy of the loco's own stoker engine valve from its cab prefab, on
        // its own under the manifold, facing as the original does, working the
        // same port; its keys stay with the original. Returns a line for the
        // log.
        private static string PlaceStokerValveCopy(TrainCar car, SimulationFlow flow, Transform frame, Transform liveValve, string sourcePath,
            string portId, Transform sourceControl, Transform sourceLabel, Transform plateFacing)
        {
            var source = car.carLivery.interiorPrefab != null ? car.carLivery.interiorPrefab.transform.Find(sourcePath) : null;
            if (source == null || !flow.TryGetPort(portId, out var port))
                return $"no '{sourcePath}' working '{portId}' in its cab to copy";
            var holder = Object.Instantiate(source.gameObject, frame, false).transform;
            holder.name = "stoker engine valve";
            var feeder = holder.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == portId);
            if (feeder == null)
            {
                Object.DestroyImmediate(holder.gameObject);
                return $"'{sourcePath}' does not work '{portId}'; no copy";
            }
            var control = feeder.transform;
            var at = new Vector3(CopiedValveAt.x, CopiedValveAt.y, WheelDepth);
            holder.rotation = liveValve.rotation;
            holder.position += frame.TransformPoint(at) - control.position;

            foreach (var input in control.GetComponents<MonoBehaviour>().Where(c => InputComponents.Contains(c.GetType().Name)))
                Object.DestroyImmediate(input);
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Text = "Stoker engine";

            Pipe(frame, "body stoker engine", new Vector3(at.x, at.y, HeaderDepth), new Vector3(at.x, at.y, BodyFront), BodyRadius);
            var supply = BuildSupplyPipe(frame, car, at.y, "stoker engine steam");
            var label = plateFacing != null
                        && PlaceLabel(sourceControl, sourceLabel, plateFacing, frame, new Vector3(at.x, at.y - LabelDrop, BodyFront - 0.002f), "STOKER");
            return $"a copy of its stoker valve under the manifold{(label ? " with a STOKER plate" : "")} ({supply})";
        }

        // The supply pipe: from the header straight into the panel to the first
        // surface of the loco behind it, with a flange where it enters. The
        // ray runs in the car body's physics pose, where its colliders are; a
        // moving car's transform is interpolated behind it.
        private static string BuildSupplyPipe(Transform frame, TrainCar car, float y, string name = "steam supply") =>
            PipeToBackhead(frame, car, new Vector2(0f, y), HeaderDepth + HeaderRadius * 0.5f, SupplyRadius, FlangeRadius, name, null);

        /// <summary>
        /// A pipe from a point on the manifold's face straight back into the
        /// backhead, with a flange where it enters: to the first surface of the
        /// loco behind it, or, with none, to <paramref name="panelDepth"/> in
        /// the frame. Returns a line for the log.
        /// </summary>
        internal static string PipeToBackhead(Transform frame, TrainCar car, Vector2 at, float fromDepth, float radius, float flangeRadius,
            string name, float? panelDepth)
        {
            var from = new Vector3(at.x, at.y, fromDepth);
            float end;
            string reached;
            if (CabRay.TryHit(car, frame.TransformPoint(from), frame.forward, SupplySearch, out var distance, out var surface))
            {
                end = fromDepth + distance;
                reached = $"'{surface.name}'";
            }
            else if (panelDepth is float panel && panel > fromDepth)
            {
                end = panel;
                reached = "the panel (no surface found)";
            }
            else
            {
                return $"no backhead within {SupplySearch * 100f:F0} cm behind the manifold for its {name} pipe";
            }
            Pipe(frame, name, from, new Vector3(at.x, at.y, end + SupplyInto), radius);
            Pipe(frame, name + " flange", new Vector3(at.x, at.y, end - FlangeThickness), new Vector3(at.x, at.y, end + SupplyInto), flangeRadius);
            return $"{name} pipe {(end - fromDepth) * 100f:F0} cm back to {reached}";
        }

        internal static void Pipe(Transform frame, string name, Vector3 from, Vector3 to, float radius)
        {
            var pipe = new GameObject(name).transform;
            pipe.SetParent(frame, false);
            var along = to - from;
            pipe.localPosition = from;
            pipe.localRotation = Quaternion.LookRotation(along.normalized, Mathf.Abs(along.normalized.y) > 0.9f ? Vector3.forward : Vector3.up);
            pipe.localScale = new Vector3(radius, radius, along.magnitude);
            pipe.gameObject.AddComponent<MeshFilter>().sharedMesh = Cylinder();
            pipe.gameObject.AddComponent<MeshRenderer>().sharedMaterial = Steel();
        }

        // A unit cylinder: radius 1, from z = 0 to z = 1, with end caps.
        private static Mesh Cylinder()
        {
            if (cylinder != null)
                return cylinder;
            const int sides = 16;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var i = 0; i <= sides; i++)
            {
                var angle = 2f * Mathf.PI * i / sides;
                var radial = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                vertices.Add(radial);
                vertices.Add(radial + Vector3.forward);
                normals.Add(radial);
                normals.Add(radial);
            }
            for (var i = 0; i < sides; i++)
            {
                var a = 2 * i;
                triangles.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 });
            }
            foreach (var z in new[] { 0f, 1f })
            {
                var center = vertices.Count;
                var normal = z == 0f ? Vector3.back : Vector3.forward;
                vertices.Add(new Vector3(0f, 0f, z));
                normals.Add(normal);
                for (var i = 0; i <= sides; i++)
                {
                    var angle = 2f * Mathf.PI * i / sides;
                    vertices.Add(new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), z));
                    normals.Add(normal);
                }
                for (var i = 0; i < sides; i++)
                {
                    if (z == 0f)
                        triangles.AddRange(new[] { center, center + i + 2, center + i + 1 });
                    else
                        triangles.AddRange(new[] { center, center + i + 1, center + i + 2 });
                }
            }
            cylinder = new Mesh { name = "RealisticFiring cylinder" };
            cylinder.SetVertices(vertices);
            cylinder.SetNormals(normals);
            cylinder.SetTriangles(triangles, 0);
            cylinder.RecalculateBounds();
            return cylinder;
        }

        private static Material Steel()
        {
            if (steel != null)
                return steel;
            steel = new Material(Shader.Find("Standard")) { name = "RealisticFiring steel" };
            steel.color = new Color(0.16f, 0.15f, 0.14f);
            steel.SetFloat("_Metallic", 0.6f);
            steel.SetFloat("_Glossiness", 0.35f);
            return steel;
        }
    }

    /// <summary>
    /// Logs who removes a manifold while its cab is still loaded, to find
    /// what takes it away after a cab reload.
    /// </summary>
    internal sealed class ManifoldTracer : MonoBehaviour
    {
        public TrainCar Car { get; set; }

        private void OnDestroy()
        {
            if (StokerCab.Rebuilding || Car == null || Car.loadedInterior == null || !Main.Settings.LogFireboxCreation)
                return;
            var ownCab = transform.parent != null && transform.parent.gameObject == Car.loadedInterior;
            Main.Mod.Logger.Log($"{Car.ID}: stoker manifold removed while {(ownCab ? "its" : "another")} cab is loaded; " +
                                StackTraceUtility.ExtractStackTrace().Replace("\n", " | "));
        }
    }

    /// <summary>
    /// Connects a cloned cab control to its sim port once the control's
    /// implementation has spawned, as InteractablePortFeedersController does
    /// for the stock controls it lists.
    /// </summary>
    internal sealed class SimControlBinder : MonoBehaviour
    {
        private InteractablePortFeeder feeder;
        private Port port;
        private bool bound;

        public void Bind(InteractablePortFeeder portFeeder, Port simPort)
        {
            feeder = portFeeder;
            port = simPort;
        }

        private IEnumerator Start()
        {
            // The stock controller waits two frames for the control to settle.
            yield return null;
            yield return null;
            if (feeder == null || port == null || GetComponent<ControlImplBase>() == null)
            {
                Main.Mod.Logger.Warning($"Cab control '{name}' never spawned; it does not work its port");
                yield break;
            }
            feeder.Init(port, null);
            bound = true;
            yield return null;
            feeder.SetupControlChangedListeners();
        }

        private void OnDestroy()
        {
            if (bound)
                feeder.Deinit();
        }
    }

    /// <summary>Names a cab control under the cursor while it is hovered.</summary>
    internal sealed class HoverName : MonoBehaviour, IHoverReaction
    {
        private bool hovered;

        public string Text { get; set; }

        /// <summary>Text worked out each frame, over <see cref="Text"/>.</summary>
        public System.Func<string> Source { get; set; }

        public void OnHovered() => hovered = true;

        public void OnUnhovered() => hovered = false;

        private void Update()
        {
            if (!hovered)
                return;
            var text = Source != null ? Source() : Text;
            if (string.IsNullOrEmpty(text))
                return;
            var cursorText = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            if (cursorText != null)
                cursorText.DisplayText(text);
        }
    }
}
