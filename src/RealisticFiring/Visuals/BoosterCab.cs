using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class BoosterCab
    {
        private const string RootName = "[RealisticFiring booster controls]";
        private const string ReverserPort = "reverser.CONTROL_EXT_IN";
        private const string SourceLatch = "Right/Sand";
        private const string SourceLatchControl = "C_Sand";
        private static readonly string[] LatchNotches = { "Down", "Up" };

        private static readonly Dictionary<string, Vector3> Placements = new Dictionary<string, Vector3>
        {
            ["S464-730"] = new Vector3(-26f, 15f, 0f),
        };

        private static readonly Dictionary<string, Vector3> CarSeats = new Dictionary<string, Vector3>
        {
            ["LocoS282A"] = new Vector3(1.45f, 1.60f, -0.24f),
        };

        /// <summary>Car-frame seats set on the seating bench this session, by livery; ahead of both tables.</summary>
#if !PUBLIC_RELEASE
        internal static readonly Dictionary<string, Vector3> DevCarSeats = new Dictionary<string, Vector3>();
#endif

        /// <summary>Where the cluster is pinned in the car's frame for <paramref name="livery"/>, if it is.</summary>
        internal static Vector3? CarSeatFor(string livery) =>
#if PUBLIC_RELEASE
            CarSeats.TryGetValue(livery, out var seated) ? seated
#else
            DevCarSeats.TryGetValue(livery, out var dev) ? dev
            : CarSeats.TryGetValue(livery, out var seated) ? seated
#endif
            : (Vector3?)null;
        private static readonly Vector3 DefaultPlacement = new Vector3(0f, 115f, 0f);

        // Where the controls go by the reverser for a livery not pinned in the car's frame.
        private static Vector3 PlacementFor(string livery) =>
            Placements.TryGetValue(livery, out var seated) ? seated : DefaultPlacement;

        /// <summary>The cluster's root in a cab, for the seating bench.</summary>
        internal const string Root = RootName;

        // How far in from the reverser to start looking for the wall, and how far to look, m.
        private const float WallSearchStart = 0.4f;
        private const float WallSearch = 1.2f;
        private const float FallbackWallOutboard = 0.12f;

        // The cluster in its own frame: x along the wall, y up, z into the
        // wall, which is at Wall; m.
        private const float Wall = 0.035f;
        private static readonly Vector3 GaugeAt = new Vector3(0f, 0.11f, 0.015f);
        private static readonly Vector3 LatchAt = new Vector3(-0.07f, -0.04f, Wall);

        // The S282's sander lever is 2 cm thick, its pivot on its mounting
        // face (its −x side) and its body toward +x. A quarter turn about the
        // vertical puts that face on the wall and the lever flush along it.
        private static readonly Quaternion LatchOnWall = Quaternion.Euler(0f, 90f, 0f);
        private static readonly Vector3 IdleAt = new Vector3(0.07f, -0.04f, -0.012f);
        private const float LabelDrop = 0.06f;
        private const float BodyFront = -0.004f;
        private const float BodyRadius = 0.014f;
        private const float PlateOnWall = 0.002f;

        /// <summary>Builds the controls in a newly loaded cab. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null
                || !RealisticBooster.TryGet(car, out var booster))
                return null;
            Interiors[car] = interior;
            // The S282's booster not yet bought: no controls, until it is (EnsureFitted).
            if (!booster.Fitted)
                return "the booster is not fitted (an upgrade at a career manager): no controls in the cab";
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(BoosterFit.Latch.PortId, out var latchPort) || !flow.TryGetPort(BoosterFit.Idle.PortId, out var idlePort))
                return null;

            // The engineer's reverser: the one furthest to the right.
            var anchor = interior.GetComponentsInChildren<InteractablePortFeeder>(false)
                .Where(f => f.portId == ReverserPort)
                .Select(f => f.transform)
                .OrderByDescending(t => car.transform.InverseTransformPoint(t.position).x)
                .FirstOrDefault();
            var pinned = CarSeatFor(car.carLivery.id);
            if (anchor == null && !pinned.HasValue)
                return "no reverser in the cab to place the booster controls by";
            if (!Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {StokerCab.SourceLivery} cab to take controls from";
            var cabPrefab = livery.interiorPrefab.transform;
            var sourceValve = cabPrefab.Find(StokerCab.SourceValve);
            var sourceControl = sourceValve != null ? sourceValve.Find(StokerCab.SourceControl) : null;
            var sourceLabel = cabPrefab.Find(StokerCab.SourceLabel);
            var sourceLatch = cabPrefab.Find(SourceLatch);
            if (sourceControl == null || sourceLatch == null || sourceLatch.Find(SourceLatchControl) == null)
                return $"no valve or sander lever in the {StokerCab.SourceLivery} cab to copy";

            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);

            // Upright on the side wall by the reverser, facing into the cab.
            var frame = root.transform;
            float side;
            string where;
            if (pinned is Vector3 at)
            {
                side = at.x < 0f ? -1f : 1f;
                var cab = CabFrame.Of(car);
                frame.rotation = Quaternion.LookRotation(cab.right * side, cab.up);
                frame.position = cab.TransformPoint(at);
                where = "pinned in the car frame";
            }
            else
            {
                var mount = anchor.parent != null ? anchor.parent : anchor;
                var seat = PlacementFor(car.carLivery.id);
                var reverser = car.transform.InverseTransformPoint(mount.position);
                side = reverser.x < 0f ? -1f : 1f;
                var floor = InjectorOverflow.TryFindCabFloor(car, out var deck) ? deck : reverser.y;
                var into = car.transform.right * side;
                var looking = car.transform.TransformPoint(new Vector3(reverser.x - side * WallSearchStart, floor + seat.y / 100f, reverser.z + seat.x / 100f));
                var onWall = CabRay.TryHit(car, looking, into, WallSearch, out var toWall, out var wall);
                if (!onWall)
                    toWall = WallSearchStart + FallbackWallOutboard;
                frame.rotation = Quaternion.LookRotation(into, car.transform.up);
                frame.position = looking + into * (toWall + seat.z / 100f - Wall);
                where = $"by '{anchor.name}' ({seat.x:F0} cm along from the reverser, {seat.y:F0} cm above the floor, {seat.z:F1} cm out, " +
                        $"wall {(onWall ? $"'{wall.name}'" : "not found; guessed")})";
            }

            var latch = PlaceLatch(sourceLatch, frame, latchPort, booster);
            var idle = StokerCab.PlaceWheel(sourceValve, frame, IdleAt, BoosterFit.Idle, idlePort);
            var bodyFront = StokerCab.BodyFrontTo(idle, frame, BodyFront);
            StokerCab.Pipe(frame, "body idling valve", new Vector3(IdleAt.x, IdleAt.y, Wall), new Vector3(IdleAt.x, IdleAt.y, bodyFront), BodyRadius);
            var idleHover = idle.GetComponent<HoverName>();
            idleHover.Source = () => $"{BoosterFit.Idle.Label}: " + (idlePort.Value > 0.5f
                ? $"Idling (latch locked out; warm {booster.Model.Warmth:P0})"
                : $"Running (warm {booster.Model.Warmth:P0})");
            var labels = 0;
            // The plates lie on the wall, their faces standing proud of it.
            if (latch != null && StokerCab.PlaceLabel(sourceControl, sourceLabel, idle, frame, new Vector3(LatchAt.x, LatchAt.y - LabelDrop, Wall - PlateOnWall), "BOOSTER LATCH"))
                labels++;
            if (StokerCab.PlaceLabel(sourceControl, sourceLabel, idle, frame, new Vector3(IdleAt.x, IdleAt.y - LabelDrop, Wall - PlateOnWall), "IDLING VALVE"))
                labels++;
            var gauge = StokerGauges.BuildOne(frame, cabPrefab, GaugeAt, "BOOSTER\nbar", 25f, 5f, () => booster.Flow.InletPressure);

            root.SetActive(true);
            gauge.WriteScales();
            BoosterSounds.Attach(car, booster, frame);
            var local = car.transform.InverseTransformPoint(frame.position);
            return $"booster controls on the {(side < 0f ? "left" : "right")} wall at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car frame, {where}, " +
                   $"latch {(latch != null ? "placed" : "missing")}, idling valve's body {(Wall - bodyFront) * 100f:F1} cm, {labels} labels, {gauge.Count} gauge";
        }

        private static readonly Dictionary<TrainCar, Transform> Interiors = new Dictionary<TrainCar, Transform>();
        private static float nextCheck;
        private static int fittedVersion = -1;

        /// <summary>
        /// Builds an upgrade's booster controls in a loaded cab once it is
        /// bought, and takes them out if the menu's rule unfits it, checking
        /// once a second; call every frame.
        /// </summary>
        public static void EnsureFitted()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            var refit = fittedVersion != Upgrades.Fittings.Version;
            fittedVersion = Upgrades.Fittings.Version;
            foreach (var car in Interiors.Keys.ToList())
            {
                var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
                if (car == null || interior == null || interior != Interiors[car] || !RealisticBooster.TryGet(car, out var booster))
                {
                    Interiors.Remove(car);
                    continue;
                }
                var root = interior.Find(RootName);
                if (root != null && !booster.Fitted)
                {
                    Object.DestroyImmediate(root.gameObject);
                    Main.Mod.Logger.Log($"{car.ID}: the booster is no longer fitted; its controls are taken out of the cab");
                    continue;
                }
                if (root != null || !booster.Fitted)
                    continue;
                var result = Attach(car, interior);
                if (result != null)
                    Main.Mod.Logger.Log(refit ? $"{car.ID}: the booster fitted; {result}" : $"{car.ID}: the cab had lost its booster controls; {result}");
            }
        }

        /// <summary>Rebuilds the controls in every loaded cab, as the seating bench moves them.</summary>
        public static void RebuildLoaded()
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
                if (old == null)
                    continue;
                Object.DestroyImmediate(old.gameObject);
                var result = Attach(car, interior);
                if (result != null)
                    Main.Mod.Logger.Log($"{car.LogName()}: {result}");
            }
        }

        // The latch: the S282's sander lever with two notches, Down and Up.
        private static Transform PlaceLatch(Transform source, Transform frame, Port port, RealisticBooster booster)
        {
            var holder = Object.Instantiate(source.gameObject, frame, false).transform;
            holder.name = BoosterFit.Latch.Id;
            var control = holder.Find(SourceLatchControl);
            holder.rotation = frame.rotation * LatchOnWall * Quaternion.Inverse(control.localRotation);
            holder.position += frame.TransformPoint(LatchAt) - control.position;

            foreach (var input in control.GetComponents<MonoBehaviour>()
                         .Where(c => StokerCab.InputComponents.Contains(c.GetType().Name) || c.GetType().Name == "PercentageControlNames")
                         .ToList())
                Object.DestroyImmediate(input);
            var spec = control.GetComponent<DV.CabControls.Spec.Lever>();
            if (spec != null)
            {
                spec.notches = LatchNotches.Length;
                spec.useSteppedJoint = true;
                spec.steppedValueUpdate = true;
            }
            control.gameObject.AddComponent<DV.HUD.NotchedControlCustomNames>().customNames = LatchNotches;
            var feeder = control.GetComponent<InteractablePortFeeder>();
            if (feeder == null)
            {
                Object.DestroyImmediate(holder.gameObject);
                return null;
            }
            feeder.portId = BoosterFit.Latch.PortId;
            control.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            control.gameObject.AddComponent<HoverName>().Source = () => $"{BoosterFit.Latch.Label}: {Describe(booster.Flow, port.Value > 0.5f)}";
            return control;
        }

        /// <summary>The booster's state in a few words, for the latch and the overlay.</summary>
        internal static string Describe(BoosterFlow flow, bool latched)
        {
            switch (flow.State)
            {
                case BoosterState.Working:
                    return "Up, working" + Warnings(flow);
                case BoosterState.CocksOpen:
                    return "Up, in gear, cylinder cocks open" + Warnings(flow);
                case BoosterState.Idling:
                    return "Down, idling out of gear";
                default:
                    return latched ? "Up" : "Down";
            }
        }

        private static string Warnings(BoosterFlow flow) =>
            (flow.Overspeed ? " (OVERSPEED: cut it out)" : "") +
            (flow.Slipping ? " (trailer wheels slipping: knock it out)" : "") +
            (flow.Priming ? " (priming: water in the cylinders)" : "") +
            (flow.Backing ? " (backing in gear!)" : "");
    }

    /// <summary>
    /// What the crew hears of the booster: its rapid staccato exhaust, four
    /// beats a crank turn; the cylinder cocks blowing while they are open; and
    /// the gears clashing. Lives under the cab on the car body.
    /// </summary>
    internal sealed class BoosterSounds : MonoBehaviour
    {
        private static readonly string[] ChuffClips = { "Steam_ChuffS_01", "Steam_ChuffS_02", "Steam_ChuffS_03", "Steam_ChuffS_04" };
        private const string CocksClip = "Steam_CylinderCock_02";
        private const string ClashClip = "GearGrind_02";

        private RealisticBooster booster;
        private AudioSource beats;
        private AudioSource cocks;
        private AudioClip[] chuffs;
        private AudioClip clash;
        private float beatPhase;
        private int clashes;

        public static void Attach(TrainCar car, RealisticBooster booster, Transform near)
        {
            var sounds = car.GetComponentInChildren<BoosterSounds>(true);
            if (sounds == null)
            {
                var go = new GameObject("[RealisticFiring booster sounds]");
                go.transform.SetParent(car.transform, false);
                sounds = go.AddComponent<BoosterSounds>();
                sounds.Build(car);
            }
            sounds.booster = booster;
            sounds.clashes = booster.Clashes;
            // Under the cab, on the centerline, about where the trailer axle is.
            var local = car.transform.InverseTransformPoint(near.position);
            sounds.transform.localPosition = new Vector3(0f, Mathf.Max(0.5f, local.y - 1.6f), local.z);
        }

        private void Build(TrainCar car)
        {
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            chuffs = ChuffClips.Select(n => clips.FirstOrDefault(c => c.name == n)).Where(c => c != null).ToArray();
            clash = clips.FirstOrDefault(c => c.name == ClashClip);
            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            beats = Source(null, mixer, loop: false);
            cocks = Source(clips.FirstOrDefault(c => c.name == CocksClip), mixer, loop: true);
        }

        private AudioSource Source(AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer, bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = 60f;
            source.dopplerLevel = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private void Update()
        {
            if (booster == null)
                return;
            var flow = booster.Flow;
            var working = flow.State == BoosterState.Working || flow.State == BoosterState.CocksOpen || flow.State == BoosterState.Idling;

            // Its exhaust: a small chuff a beat, louder the harder it works.
            if (working && chuffs.Length > 0 && flow.BeatRate > 0f)
            {
                beatPhase += flow.BeatRate * Time.deltaTime;
                var loudness = flow.State == BoosterState.Idling ? 0.15f : Mathf.Lerp(0.25f, 0.8f, Mathf.Clamp01(flow.InletPressure / 17f));
                for (var n = 0; beatPhase >= 1f && n < 4; n++, beatPhase -= 1f)
                {
                    beats.pitch = Random.Range(1.25f, 1.45f);
                    beats.PlayOneShot(chuffs[Random.Range(0, chuffs.Length)], loudness);
                }
                beatPhase = Mathf.Min(beatPhase, 1f);
            }
            else
            {
                beatPhase = 0f;
            }

            SetLoop(cocks, flow.CocksOpen ? 0.15f : 0f);

            if (booster.Clashes != clashes)
            {
                clashes = booster.Clashes;
                if (clash != null)
                    beats.PlayOneShot(clash, 1f);
            }
        }

        private static void SetLoop(AudioSource source, float volume)
        {
            if (source == null || source.clip == null)
                return;
            source.volume = volume;
            if (volume > 0.01f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.01f && source.isPlaying)
                source.Stop();
        }
    }
}
