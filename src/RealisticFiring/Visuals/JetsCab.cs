using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Cars;
using DV.Simulation.Ports;
using LocoSim.Definitions;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class JetsCab
    {
        internal const string RootName = "[RealisticFiring over-fire jets]";
        private const string Plate = "JETS";

        /// <summary>
        /// A seat: the wheel's place, m, and turns, degrees: in the car's own
        /// frame and from its axes where <see cref="InCar"/> (so the same with
        /// any cab mod: CabImmersionPack moves and turns the S282's blower
        /// valve, and a seat from it landed elsewhere in a plain cab), else
        /// from the blower valve's wheel in its own frame; and the JETS plate's move from
        /// where the S282's blower plate sits to its wheel, m in the same
        /// frame, its spin in its own face, and its turn about the frame's up,
        /// degrees.
        /// </summary>
        internal readonly struct Seat
        {
            public Seat(Vector3 move, Vector3 turns, Vector3 plateMove = default, float plateSpin = 0f, float plateTurn = 0f, bool plateSquare = false, bool inCar = false)
            {
                InCar = inCar;
                PlateSquare = plateSquare;
                Move = move;
                Turns = turns;
                PlateMove = plateMove;
                PlateSpin = plateSpin;
                PlateTurn = plateTurn;
            }

            public float PlateTurn { get; }

            /// <summary>Whether the place and turns are in the car's frame, not the blower wheel's.</summary>
            public bool InCar { get; }

            /// <summary>The plate set square to the backhead, facing into the cab and upright, before its spin and turn (the S282's own plates under its manifold are turned to it).</summary>
            public bool PlateSquare { get; }

            public Vector3 Move { get; }
            public Vector3 Turns { get; }
            public Vector3 PlateMove { get; }
            public float PlateSpin { get; }
        }

        private static readonly Seat Default = new Seat(new Vector3(0.15f, 0f, 0f), Vector3.zero);

        private static readonly Dictionary<string, Seat> Seats = new Dictionary<string, Seat>
        {
            ["LocoS282A"] = new Seat(new Vector3(-0.11f, 2.41f, 0.11f), Vector3.zero, new Vector3(0.10f, 0.21f, -0.045f), 0f, 180f, plateSquare: true, inCar: true),
            // On the turret's empty valve, left of the blowdown's (the turret's
            // handles 0.197 m apart), its plate under it as the others'.
            ["LocoS060"] = new Seat(new Vector3(-0.204f, 3.205f, -1.973f), Vector3.zero, inCar: true),
        };

        // Seats set this session on the seating bench, by livery: they come first and are never saved.
        internal static readonly Dictionary<string, Seat> DevSeats = new Dictionary<string, Seat>();

        internal static Seat SeatFor(string livery) =>
            DevSeats.TryGetValue(livery, out var dev) ? dev
            : Seats.TryGetValue(livery, out var seat) ? seat
            : Default;

        private static readonly Dictionary<TrainCar, Transform> Interiors = new Dictionary<TrainCar, Transform>();

        private static readonly HashSet<string> OwnHandle = new HashSet<string> { "LocoS060" };

        private static string Local(TrainCar car, Vector3 world)
        {
            var l = car.transform.InverseTransformPoint(world);
            return $"({l.x:F2}, {l.y:F2}, {l.z:F2})";
        }

        /// <summary>
        /// The valve's seat in the car's frame for the seating bench: its
        /// table's or the bench's own where they are, else where it is built
        /// now, facing as the car does.
        /// </summary>
        internal static Seat SeatInCar(TrainCar car, string livery)
        {
            var seat = SeatFor(livery);
            if (seat.InCar)
                return seat;
            var root = car != null && car.loadedInterior != null ? car.loadedInterior.transform.Find(RootName) : null;
            var wheel = root != null ? root.GetComponentInChildren<DV.Simulation.Ports.InteractablePortFeeder>(true) : null;
            var at = wheel != null ? car.transform.InverseTransformPoint(wheel.transform.position) : Vector3.zero;
            return new Seat(at, Vector3.zero, seat.PlateMove, seat.PlateSpin, seat.PlateTurn, seat.PlateSquare, inCar: true);
        }

        // A transform's path under a root, for finding its like in the cab's prefab.
        private static string PathFrom(Transform root, Transform t)
        {
            var names = new List<string>();
            for (var at = t; at != null && at != root; at = at.parent)
                names.Add(at.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // The cab's label plate nearest a control: its own plate, to copy as the JETS plate.
        private static Transform NearestLabel(Transform cab, Transform control)
        {
            if (control == null)
                return null;
            return cab.GetComponentsInChildren<TMPro.TextMeshPro>(true)
                .Select(t => t.transform)
                .Select(t => t.parent != null && t.parent.name.StartsWith("Label") ? t.parent : t)
                .OrderBy(t => (t.position - control.position).sqrMagnitude)
                .FirstOrDefault();
        }

        /// <summary>Rebuilds the valve in every loaded cab: as the seating bench moves it, and as the jets are fitted or taken off.</summary>
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
                Rebuild(car);
            }
        }

        /// <summary>Rebuilds the valve in <paramref name="car"/>'s cab, if loaded.</summary>
        public static void Rebuild(TrainCar car)
        {
            var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
            if (interior == null)
                return;
            var old = interior.Find(RootName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var result = Attach(car, interior);
            if (result != null && Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car.LogName()}: {result}");
        }

        /// <summary>Builds the valve in a loaded cab while the jets are fitted. Returns a sentence for the log, or null.</summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !FireboxRegistry.TryGet(car, out var firebox))
                return null;
            Interiors[car] = interior;
            if (!firebox.JetsFitted)
                return null;
            var controller = car.SimController;
            var flow = controller != null ? controller.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(RealisticFirebox.JetsControl.PortId, out var port))
                return null;

            // The cab's own blower valve: whatever feeds the exhaust's blower control.
            var graph = controller.connectionsDefinition;
            var exhaust = graph != null && graph.executionOrder != null ? graph.executionOrder.OfType<SteamExhaustDefinition>().FirstOrDefault() : null;
            var blowerPort = exhaust != null && graph.portReferenceConnections != null
                ? graph.portReferenceConnections.FirstOrDefault(r => r.portReferenceId == $"{exhaust.ID}.BLOWER_CONTROL")?.portId
                : null;
            var blower = blowerPort == null ? null : interior.GetComponentsInChildren<InteractablePortFeeder>(false)
                .FirstOrDefault(f => f.portId == blowerPort)?.transform;
            if (blower == null)
                return $"no blower valve ('{blowerPort}') in the cab to put the over-fire jets' valve by";

            // A copy of the S282's blower handwheel; or, where the cab's own
            // blower handle is to be copied, that, from its own cab.
            var own = OwnHandle.Contains(car.carLivery.id);
            var sourceLivery = own ? car.carLivery.id : StokerCab.SourceLivery;
            if (!Globals.G.Types.TryGetLivery(sourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {sourceLivery} cab to take the jets' valve from";
            var cabPrefab = livery.interiorPrefab.transform;
            Transform sourceValve, sourceControl, sourceLabel;
            if (own)
            {
                sourceControl = cabPrefab.Find(PathFrom(interior, blower));
                sourceValve = sourceControl != null ? sourceControl.parent : null;
                sourceLabel = NearestLabel(cabPrefab, sourceControl);
            }
            else
            {
                sourceValve = cabPrefab.Find(StokerCab.SourceValve);
                sourceControl = sourceValve != null ? sourceValve.Find(StokerCab.SourceControl) : null;
                sourceLabel = cabPrefab.Find(StokerCab.SourceLabel);
            }
            if (sourceControl == null || sourceControl.name != StokerCab.SourceControl)
                return $"no blower valve '{StokerCab.SourceControl}' in the {sourceLivery} cab to copy for the jets";

            var seat = SeatFor(car.carLivery.id);
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            // A frame on the blower's wheel, turned by the seat.
            var frame = root.transform;
            if (seat.InCar)
                frame.SetPositionAndRotation(car.transform.TransformPoint(seat.Move), car.transform.rotation * Quaternion.Euler(seat.Turns));
            else
                frame.SetPositionAndRotation(blower.position, blower.rotation * Quaternion.Euler(seat.Turns));
            var control = StokerCab.PlaceWheel(sourceValve, frame, seat.InCar ? Vector3.zero : seat.Move, RealisticFirebox.JetsControl, port);

            // The plate where the S282's blower plate sits to its wheel.
            if (sourceLabel != null)
            {
                var plateFromWheel = Quaternion.Inverse(sourceControl.rotation) * (sourceLabel.position - sourceControl.position);
                var plateAt = frame.InverseTransformPoint(control.position + control.rotation * plateFromWheel) + seat.PlateMove;
                StokerCab.PlaceLabel(sourceControl, sourceLabel, control, frame, plateAt, Plate, 1f);
                var plate = frame.Find("label " + Plate);
                if (plate != null && seat.PlateSquare)
                    plate.rotation = Quaternion.LookRotation(car.transform.forward, car.transform.up);
                if (plate != null && seat.PlateSpin != 0f)
                    plate.rotation *= Quaternion.Euler(0f, 0f, seat.PlateSpin);
                if (plate != null && seat.PlateTurn != 0f)
                    plate.rotation = Quaternion.AngleAxis(seat.PlateTurn, frame.up) * plate.rotation;
            }
            root.AddComponent<JetsHiss>().Init(firebox, interior);
            root.SetActive(true);

            var local = car.transform.InverseTransformPoint(control.position);
            return $"over-fire jets' valve at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car, " +
                   (seat.InCar ? "seated in the car's frame" : $"({seat.Move.x:F2}, {seat.Move.y:F2}, {seat.Move.z:F2}) m from the blower valve '{blower.name}'")
                   + $"; the blower valve '{blower.name}' at {Local(car, blower.position)}";
        }
    }

 internal sealed class JetsHiss : MonoBehaviour
    {
        private const string ClipName = "SteamRelease_01";
        private const float Loudest = 0.35f;

        private RealisticFirebox firebox;
        private AudioSource source;
        private float full;

        internal void Init(RealisticFirebox firebox, Transform interior)
        {
            this.firebox = firebox;
            var jets = firebox.Model.Jets;
            full = jets != null ? jets.Count * Core.OverFireJets.PerJet(jets.Tube, 200f / 14.5038f).Steam : 0f;
            var clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == ClipName);
            if (clip == null || !(full > 0f))
                return;
            source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = 25f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = interior.GetComponentsInChildren<AudioSource>(true).Select(a => a.outputAudioMixerGroup).FirstOrDefault(g => g != null);
        }

        private void Update()
        {
            if (source == null || firebox == null)
                return;
            var share = Mathf.Clamp01(firebox.Model.JetSteamFlow / full);
            var volume = Loudest * Mathf.Sqrt(share) * Mathf.Max(0f, Main.Settings.SteamVentVolume);
            source.volume = volume;
            source.pitch = 1.1f + 0.2f * share;
            if (volume > 0.005f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.005f && source.isPlaying)
                source.Stop();
        }
    }
}
