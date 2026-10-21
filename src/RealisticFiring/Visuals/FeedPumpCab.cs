using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.Simulation.Ports;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The feed pump's cab controls, on the fireman's side as on the
    /// prototypes: its operating valve and gauge beside the stoker manifold
    /// (a Worthington's cold-pump gauge, an Elesco's delivery gauge); or, in a
    /// cab with an injector lever each side, the fireman's lever becomes the
    /// pump's valve and the engineer's stays the injector, as on the NYC.
    /// </summary>
    internal static class FeedPumpCab
    {
        internal readonly struct Spot
        {
            public Spot(Vector2 valve, Vector2 gauge, Vector3? valveInCar = null)
            {
                Valve = valve;
                Gauge = gauge;
                ValveInCar = valveInCar;
            }

            public Vector2 Valve { get; }

            /// <summary>The gauge, in the frame; from the valve when the valve is given in the car's frame.</summary>
            public Vector2 Gauge { get; }

            public Vector3? ValveInCar { get; }
        }

        private static readonly Dictionary<string, Spot> Spots = new Dictionary<string, Spot>
        {
            // Below the manifold, a little inboard, beside the stoker valve.
            ["FEF-3"] = new Spot(new Vector2(-0.11f, -0.40f), new Vector2(-0.11f, -0.28f)),
            // Above and outboard of the dynamo valve, by the sight glass.
            ["S484-840A"] = new Spot(Vector2.zero, new Vector2(0f, 0.12f), new Vector3(0.75f, 3.47f, -8.36f)),
            // The Hudson's valve is its fireman's injector lever; the gauge
            // six inches above the manifold and three inboard, below the lever.
            ["S464-730"] = new Spot(Vector2.zero, new Vector2(0.076f, 0.24f)),
        };

        private static readonly Spot DefaultSpot = new Spot(new Vector2(-0.165f, -0.12f), new Vector2(-0.165f, 0.03f));

        // Seats set this session on the seating bench (SeatingBench, a
        // development tool), by livery, as Spots and LeverGaugeSeats: they
        // come first and are never saved.
        internal static readonly Dictionary<string, Spot> DevSpots = new Dictionary<string, Spot>();
        internal static readonly Dictionary<string, Vector3> DevLeverGaugeSeats = new Dictionary<string, Vector3>();

        /// <summary>Where the pump's valve and gauge go beside the manifold for <paramref name="livery"/>: the bench's seat, the table's, or the default.</summary>
        internal static Spot SpotFor(string livery) =>
            DevSpots.TryGetValue(livery, out var dev) ? dev
            : Spots.TryGetValue(livery, out var seated) ? seated
            : DefaultSpot;

        /// <summary>Where an oil burner's wall gauge goes from its handwheel for <paramref name="livery"/>: the bench's seat, the table's, or the default.</summary>
        internal static Vector3 LeverGaugeSeatFor(string livery) =>
            DevLeverGaugeSeats.TryGetValue(livery, out var dev) ? dev
            : LeverGaugeSeats.TryGetValue(livery, out var seated) ? seated
            : LeverGaugeDefault;

        // Whether a livery's wall gauge is seated (by the bench or the table).
        private static bool LeverGaugeSeated(string livery) => DevLeverGaugeSeats.ContainsKey(livery) || LeverGaugeSeats.ContainsKey(livery);

        /// <summary>Moves every loaded lever-mounted pump gauge, as the seating bench moves it.</summary>
        public static void RebuildLeverGauges()
        {
            foreach (var rig in InjectorRig.All)
            {
                var car = rig.Car;
                var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
                var old = interior != null ? interior.Find(LeverGaugeRoot) : null;
                if (old == null || rig.Pump == null || !FeedwaterFit.TryGet(car.carLivery.id, out var fit))
                    continue;
                Object.DestroyImmediate(old.gameObject);
                var lever = interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == FeedwaterFit.Valve.PortId);
                if (lever == null)
                    continue;
                var result = BuildLeverGauge(car, interior, rig, fit, lever.transform);
                if (result != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.LogName()}: feed pump {result}");
            }
        }

        private const float GaugeDepth = 0.035f;
        private const float WheelDepth = -0.012f;
        private const float BodyBack = 0.035f;
        private const float BodyFront = -0.004f;
        private const float BodyRadius = 0.014f;
        private const float FlangeRadius = 0.03f;
        private const float GaugeLineRadius = 0.006f;
        private const float GaugeLineFlange = 0.013f;

        // Where a pipe ends with no surface found behind it, in the frame, m.
        private const float FallbackPanelDepth = 0.15f;

        // The cab label beside a lever is within this of it, m.
        private const float LabelReach = 0.45f;

        // A loco with no stoker manifold (an oil burner) carries the pump's
        // gauge on the cab's side wall by the fireman's handwheel that works
        // the pump: along the wall (toward the front), up from the handwheel,
        // and its back out from the wall's collider, m, unless seated. The
        // collider can lie outside the drawn wall (the 2901's does, by a few
        // cm), so the back stands off it.
        internal const string LeverGaugeRoot = "[RealisticFiring feed pump gauge]";
        private static readonly Vector3 LeverGaugeDefault = new Vector3(0f, 0.25f, 0.06f);
        private const float WallSearch = 1.5f;
        private static readonly Dictionary<string, Vector3> LeverGaugeSeats = new Dictionary<string, Vector3>();

        /// <summary>
        /// Builds the pump's valve (unless the fireman's injector lever works
        /// it) and gauge in the manifold's frame, which must still be
        /// inactive. Returns a line for the log and the gauge to write once
        /// the frame is active, or nulls.
        /// </summary>
        public static (string Note, StokerGauges.Built Gauge) BuildInManifold(TrainCar car, SimulationFlow flow, Transform frame, Transform cabPrefab,
            Transform sourceValve, Transform sourceControl, Transform sourceLabel, Transform plateFacing)
        {
            if (car == null || flow == null || !InjectorRig.TryGet(car, out var rig) || rig.Pump == null
                || !FeedwaterFit.TryGet(car.carLivery.id, out var fit) || !flow.TryGetPort(FeedwaterFit.Valve.PortId, out var port))
                return (null, null);

            var spot = SpotFor(car.carLivery.id);
            var valveAt = spot.Valve;
            var gaugeAt = spot.Gauge;
            if (spot.ValveInCar is Vector3 inCar)
            {
                var local = frame.InverseTransformPoint(car.transform.TransformPoint(inCar));
                valveAt = new Vector2(local.x, local.y);
                gaugeAt = valveAt + spot.Gauge;
            }
            var valvePoint = new Vector3(valveAt.x, valveAt.y, WheelDepth);
            var gaugePoint = new Vector3(gaugeAt.x, gaugeAt.y, GaugeDepth);

            var notes = new List<string>();
            if (!fit.OnInjectorLever && sourceValve != null)
            {
                // The valve's body runs back into the boiler.
                StokerCab.Pipe(frame, "body feed pump", new Vector3(valvePoint.x, valvePoint.y, BodyBack), new Vector3(valvePoint.x, valvePoint.y, BodyFront), BodyRadius);
                notes.Add(StokerCab.PipeToBackhead(frame, car, new Vector2(valvePoint.x, valvePoint.y), BodyBack, BodyRadius, FlangeRadius,
                    "feed pump valve", FallbackPanelDepth));
                var control = StokerCab.PlaceWheel(sourceValve, frame, valvePoint, FeedwaterFit.Valve, port);
                control.GetComponent<HoverName>().Source = () => Hover(rig);
                var labelled = plateFacing != null && StokerCab.HangLabel(sourceControl, sourceLabel, plateFacing, frame,
                    new Vector2(valvePoint.x, valvePoint.y), "FEED PUMP");
                notes.Add("its operating valve" + (labelled ? " and plate" : ""));
            }
            var open = fit.Type == FeedHeaterType.Open;
            var gauge = StokerGauges.BuildOne(frame, cabPrefab, gaugePoint, open ? "FEED PUMP\nbar" : "FEEDWATER\nbar",
                open ? 5f : 30f, open ? 1f : 5f, () => rig.PumpFlow.Gauge);
            notes.Add(open ? "a cold-pump gauge" : "a delivery gauge");
            // Its gauge line, from behind the gauge into the boiler.
            notes.Add(StokerCab.PipeToBackhead(frame, car, new Vector2(gaugePoint.x, gaugePoint.y), GaugeDepth - 0.005f, GaugeLineRadius,
                GaugeLineFlange, "gauge line", FallbackPanelDepth));
            FeedPumpSounds.Attach(car, rig, fit.Type, frame);
            return ($"{rig.PumpName} feed pump: " + string.Join(", ", notes) + " beside the manifold", gauge);
        }

        /// <summary>
        /// In a cab with an injector lever each side, makes the fireman's (the
        /// left) the pump's operating valve, keys removed and plate relabeled.
        /// Returns a line for the log, or null.
        /// </summary>
        public static string RebindInjectorLever(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || !InjectorRig.TryGet(car, out var rig) || rig.Pump == null
                || !FeedwaterFit.TryGet(car.carLivery.id, out var fit) || !fit.OnInjectorLever)
                return null;
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(FeedwaterFit.Valve.PortId, out var port))
                return null;
            float Across(Component c) => car.transform.InverseTransformPoint(c.transform.position).x;
            var levers = interior.GetComponentsInChildren<InteractablePortFeeder>(true)
                .Where(f => f.portId == rig.LeverPortId)
                .OrderBy(Across)
                .ToList();
            if (levers.Count < 2 || Across(levers[0]) >= 0f)
                return "no fireman's injector lever to work the feed pump";

            var feeder = levers[0];
            feeder.portId = FeedwaterFit.Valve.PortId;
            foreach (var input in feeder.GetComponents<MonoBehaviour>().Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)).ToList())
                Object.DestroyImmediate(input);
            feeder.gameObject.AddComponent<FeederRebinder>().Bind(feeder, port);
            feeder.gameObject.AddComponent<HoverName>().Source = () => Hover(rig);
            var relabeled = Relabel(interior, feeder.transform, "FEED PUMP");
            var gauge = OilBurnerFit.IsOilBurner(car) ? BuildLeverGauge(car, interior, rig, fit, feeder.transform) : null;
            return $"the fireman's injector lever '{feeder.name}' works the {rig.PumpName} feed pump" + (relabeled ? ", its plate relabeled" : "") +
                   (gauge != null ? $", {gauge}" : "");
        }

        // The pump's gauge, its line to the backhead, and the pump's sounds,
        // above the handwheel that works it. Returns a note for the log, or null.
        private static string BuildLeverGauge(TrainCar car, Transform interior, InjectorRig rig, FeedwaterFit.Fit fit, Transform lever)
        {
            if (!DV.Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return null;
            var seated = LeverGaugeSeated(car.carLivery.id);
            var spot = LeverGaugeSeatFor(car.carLivery.id);
            var up = car.transform.up;
            var at = lever.position + car.transform.forward * spot.x + up * spot.y;
            // Out to the wall on the handwheel's side of the cab.
            var outward = car.transform.InverseTransformPoint(lever.position).x < 0f ? -car.transform.right : car.transform.right;
            var onWall = TryFindWall(car, at, outward, out var wall, out var normal);

            var root = new GameObject(LeverGaugeRoot);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            var frame = new GameObject("frame").transform;
            frame.SetParent(root.transform, false);
            // z into the wall (or the backhead without one), y up; the dial faces -z.
            frame.SetPositionAndRotation(onWall ? wall : at, Quaternion.LookRotation(onWall ? -normal : car.transform.forward, up));
            var open = fit.Type == FeedHeaterType.Open;
            var gauge = StokerGauges.BuildOne(frame, livery.interiorPrefab.transform, Vector3.zero,
                open ? "FEED PUMP\nbar" : "FEEDWATER\nbar", open ? 5f : 30f, open ? 1f : 5f, () => rig.PumpFlow.Gauge);
            var dial = frame.childCount > 0 ? frame.GetChild(frame.childCount - 1) : null;
            if (dial != null && onWall)
            {
                // Its back the standoff out from the wall's collider.
                var back = Back(frame, dial);
                dial.localPosition += new Vector3(0f, 0f, -spot.z - back);
            }
            FeedPumpSounds.Attach(car, rig, fit.Type, frame);
            root.SetActive(true);
            gauge.WriteScales();
            var local = car.transform.InverseTransformPoint(frame.position);
            return $"its {(open ? "cold-pump" : "delivery")} gauge " + (onWall ? "on the cab wall" : "facing the backhead (no wall found)") +
                   $" at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car frame" + (seated ? " (seated for the livery)" : "");
        }

        // The cab's side wall out from a point: the first surface of the loco
        // facing back in.
        private static bool TryFindWall(TrainCar car, Vector3 from, Vector3 outward, out Vector3 point, out Vector3 normal)
        {
            point = from;
            normal = -outward;
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(from, outward, WallSearch, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (TrainCar.Resolve(hit.collider.gameObject) != car || Vector3.Dot(hit.normal, -outward) < 0.7f)
                    continue;
                point = hit.point;
                normal = hit.normal;
                return true;
            }
            return false;
        }

        // How far the gauge's meshes reach along the frame's z (into the wall).
        private static float Back(Transform frame, Transform gauge)
        {
            var back = float.MinValue;
            foreach (var filter in gauge.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null)
                    continue;
                var bounds = filter.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                    back = Mathf.Max(back, frame.InverseTransformPoint(filter.transform.TransformPoint(corner)).z);
                }
            }
            return back > float.MinValue ? back : 0f;
        }

        // The cab plate nearest a lever, named for the injector, gets new text.
        private static bool Relabel(Transform interior, Transform lever, string text)
        {
            var plate = InjectorPlate(interior, lever);
            if (plate == null)
                return false;
            Retext(plate, text);
            return true;
        }

        /// <summary>The cab's injector plate nearest <paramref name="lever"/>, or null.</summary>
        internal static TextMeshPro InjectorPlate(Transform interior, Transform lever) =>
            interior.GetComponentsInChildren<TextMeshPro>(true)
                .Where(t => Path(t.transform).ToLowerInvariant().Contains("injector"))
                .Select(t => (Text: t, Distance: Vector3.Distance(t.transform.position, lever.position)))
                .Where(t => t.Distance < LabelReach)
                .OrderBy(t => t.Distance)
                .Select(t => t.Text)
                .FirstOrDefault();

        /// <summary>Gives a plate plain text in place of its localization key.</summary>
        internal static void Retext(TextMeshPro plate, string text)
        {
            if (plate == null)
                return;
            foreach (var localize in plate.GetComponentsInParent<MonoBehaviour>(true)
                         .Concat(plate.GetComponentsInChildren<MonoBehaviour>(true))
                         .Where(c => c.GetType().Name == "Localize" || c.GetType().Name == "LabelLocalizer")
                         .ToList())
                Object.DestroyImmediate(localize);
            // Shrunk to fit its plate if longer than the stock word ("INJ STEA").
            if (!plate.enableAutoSizing)
            {
                plate.fontSizeMax = plate.fontSize;
                plate.fontSizeMin = plate.fontSize * 0.5f;
                plate.enableAutoSizing = true;
            }
            plate.textWrappingMode = TextWrappingModes.NoWrap;
            plate.text = text;
        }

        private static string Path(Transform t)
        {
            var names = new List<string>();
            for (; t != null; t = t.parent)
                names.Add(t.name);
            return string.Join("/", names);
        }

        internal static string Hover(InjectorRig rig) => $"{FeedwaterFit.Valve.Label} ({rig.PumpName}): {rig.PumpValve:P0} open, {Describe(rig.PumpFlow)}";

        /// <summary>The pump and heater in a few words.</summary>
        internal static string Describe(FeedPumpFlow flow)
        {
            switch (flow.State)
            {
                case FeedPumpState.Off:
                    return $"shut; heater {flow.HeaterTemperature:F0} °C";
                case FeedPumpState.Feeding:
                    return $"feeding {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} °C";
                case FeedPumpState.Choked:
                    return $"drifting: choked to half, {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} °C";
                case FeedPumpState.Racing:
                    return "RACING: the heater's water is flashing (ease it before hooking up or shutting off)";
                case FeedPumpState.LostSuction:
                    return "LOST SUCTION: shut it, get water, and open it again to prime it";
                case FeedPumpState.Tripped:
                    return "TRIPPED on overspeed: shut it a few seconds to reset it";
                default:
                    return flow.State.ToString();
            }
        }
    }

    /// <summary>
    /// Points a lever's feeder at a new port once the game has bound the
    /// cab's controls, two frames after the cab loads; if the game bound it
    /// to the old port first, it is unbound and bound again.
    /// </summary>
    internal sealed class FeederRebinder : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<InteractablePortFeeder, Port> BoundPort =
            AccessTools.FieldRefAccess<InteractablePortFeeder, Port>("port");

        private InteractablePortFeeder feeder;
        private Port port;

        public void Bind(InteractablePortFeeder portFeeder, Port simPort)
        {
            feeder = portFeeder;
            port = simPort;
        }

        private IEnumerator Start()
        {
            for (var i = 0; i < 5; i++)
                yield return null;
            if (feeder == null || port == null || GetComponent<ControlImplBase>() == null)
                yield break;
            var bound = BoundPort(feeder);
            if (bound == port)
                yield break;
            if (bound != null)
                feeder.Deinit();
            feeder.Init(port, null);
            yield return null;
            feeder.SetupControlChangedListeners();
        }
    }

    /// <summary>
    /// What the fireman hears of the pump: a Worthington's hot pump clanking
    /// with its strokes and its cold pump's turbine; an Elesco's turbine.
    /// Losing the suction, the turbine runs away to a shriek. Under the cab
    /// on the fireman's side.
    /// </summary>
    internal sealed class FeedPumpSounds : MonoBehaviour
    {
        private const string PumpClip = "Steam_AirPump_01";
        private const string TurbineClip = "Steam_Dynamo_01";

        private InjectorRig rig;
        private FeedHeaterType type;
        private AudioSource pump;
        private AudioSource turbine;

        public static void Attach(TrainCar car, InjectorRig rig, FeedHeaterType type, Transform near)
        {
            var sounds = car.GetComponentInChildren<FeedPumpSounds>(true);
            if (sounds == null)
            {
                var go = new GameObject("[RealisticFiring feed pump sounds]");
                go.transform.SetParent(car.transform, false);
                sounds = go.AddComponent<FeedPumpSounds>();
                sounds.Build(car);
            }
            sounds.rig = rig;
            sounds.type = type;
            var local = car.transform.InverseTransformPoint(near.position);
            sounds.transform.localPosition = new Vector3(Mathf.Sign(local.x) * Mathf.Max(1f, Mathf.Abs(local.x)), Mathf.Max(0.5f, local.y - 1.3f), local.z);
        }

        private void Build(TrainCar car)
        {
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            pump = Source(clips.FirstOrDefault(c => c.name == PumpClip), mixer);
            turbine = Source(clips.FirstOrDefault(c => c.name == TurbineClip), mixer);
        }

        private AudioSource Source(AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private void Update()
        {
            if (rig == null)
                return;
            var flow = rig.PumpFlow;
            var speed = flow.Speed;
            var shrieking = flow.State == FeedPumpState.LostSuction;
            var scale = Mathf.Max(0f, Main.Settings.FeedPumpVolume);
            if (type == FeedHeaterType.Open)
                Set(pump, speed > 0.02f ? scale * Mathf.Lerp(0.04f, 0.2f, Mathf.Clamp01(speed)) : 0f, Mathf.Lerp(0.6f, 1.2f, Mathf.Clamp01(speed / 1.8f)));
            Set(turbine, speed > 0.02f ? scale * (shrieking ? 0.25f * Mathf.Clamp01(speed / 1.5f) : Mathf.Lerp(0.015f, 0.08f, Mathf.Clamp01(speed))) : 0f,
                shrieking ? Mathf.Lerp(1.2f, 1.9f, Mathf.Clamp01(speed / 2.5f)) : Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(speed)));
        }

        private static void Set(AudioSource source, float volume, float pitch)
        {
            if (source == null || source.clip == null)
                return;
            source.volume = volume;
            source.pitch = pitch;
            if (volume > 0.01f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.01f && source.isPlaying)
                source.Stop();
        }
    }
}
