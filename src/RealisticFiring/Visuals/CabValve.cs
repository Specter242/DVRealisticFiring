using System;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Ports;
using LocoSim.Definitions;
using RealisticFiring.Sim;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
 internal sealed class CabValve
    {
        /// <summary>
        /// A seat: the wheel's place, m, and turns, degrees: in the car's own
        /// frame and from its axes where <see cref="InCar"/> (so the same with
        /// any cab mod: CabImmersionPack moves and turns the S282's blower
        /// valve, and a seat from it landed elsewhere in a plain cab), else
        /// from the blower valve's wheel in its own frame; and the plate's
        /// move from where the S282's blower plate sits to its wheel, m in the
        /// same frame, its spin in its own face, and its turn about the
        /// frame's up, degrees.
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

        private sealed class LoadedCab
        {
            public Transform Interior;
            public int CheckedVersion;
            public string CheckedGuid;
        }

        private readonly string what;
        private readonly StokerJetControls.Valve control;
        private readonly Func<string, Seat?> seatLookup;
        private readonly Seat fallback;
        private readonly Func<string, bool> ownHandle;
        private readonly Func<TrainCar, bool> fitted;
        private readonly Action<GameObject, TrainCar, Transform> built;
        private readonly bool byThrottle;
        private readonly Vector3 throttleOffset;
        private readonly Dictionary<TrainCar, LoadedCab> interiors = new Dictionary<TrainCar, LoadedCab>();

        /// <summary>Optional hook used by offline checks to observe sync decisions without Unity scene calls.</summary>
        internal Action<TrainCar, Transform, bool> SyncHook;

 public CabValve(string what, string rootName, string plate, StokerJetControls.Valve control, Func<string, Seat?> seatLookup, Seat fallback,
            Func<string, bool> ownHandle, Func<TrainCar, bool> fitted, Action<GameObject, TrainCar, Transform> built = null,
            bool byThrottle = false, Vector3 throttleOffset = default)
        {
            this.byThrottle = byThrottle;
            this.throttleOffset = throttleOffset;
            this.what = what;
            RootName = rootName;
            Plate = plate;
            this.control = control;
            this.seatLookup = seatLookup ?? (_ => null);
            this.fallback = fallback;
            this.ownHandle = ownHandle ?? (_ => false);
            this.fitted = fitted;
            this.built = built;
        }

        public string RootName { get; }
        public string Plate { get; }

        /// <summary>Seats set this session on the seating bench, by livery: they come first and are never saved.</summary>
#if !PUBLIC_RELEASE
        internal readonly Dictionary<string, Seat> DevSeats = new Dictionary<string, Seat>();
#endif

        internal Seat SeatFor(string livery) =>
#if PUBLIC_RELEASE
            seatLookup(livery) ?? fallback;
#else
            DevSeats.TryGetValue(livery, out var dev) ? dev
            : seatLookup(livery) ?? fallback;
#endif

#if PUBLIC_RELEASE
        private bool Seated(string livery) => seatLookup(livery).HasValue;
#else
        private bool Seated(string livery) => DevSeats.ContainsKey(livery) || seatLookup(livery).HasValue;
#endif

        // The cab's throttle: whatever feeds the steam engine's throttle control.
        private static Transform Throttle(TrainCar car, Transform interior)
        {
            var graph = car.SimController != null ? car.SimController.connectionsDefinition : null;
            var engine = graph != null && graph.executionOrder != null ? graph.executionOrder.OfType<ReciprocatingSteamEngineDefinition>().FirstOrDefault() : null;
            var throttlePort = engine != null && graph.portReferenceConnections != null
                ? graph.portReferenceConnections.FirstOrDefault(r => r.portReferenceId == $"{engine.ID}.{engine.throttleControl.ID}")?.portId
                : null;
            return throttlePort == null ? null : interior.GetComponentsInChildren<InteractablePortFeeder>(false)
                .FirstOrDefault(f => f.portId == throttlePort)?.transform;
        }

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
        internal Seat SeatInCar(TrainCar car, string livery)
        {
            var seat = SeatFor(livery);
            if (seat.InCar)
                return seat;
            var root = car != null && car.loadedInterior != null ? car.loadedInterior.transform.Find(RootName) : null;
            var wheel = root != null ? root.GetComponentInChildren<InteractablePortFeeder>(true) : null;
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

        // The cab's label plate nearest a control: its own plate, to copy as this valve's plate.
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

        /// <summary>Records a loaded cab and the fittings version and car GUID at which it was checked.</summary>
        internal void TrackInterior(TrainCar car, Transform interior)
        {
            interiors[car] = new LoadedCab
            {
                Interior = interior,
                CheckedVersion = Upgrades.Fittings.Version,
                CheckedGuid = Upgrades.Fittings.CarGuidOf(car),
            };
        }

        /// <summary>Stops tracking <paramref name="car"/> (used for teardown in offline checks).</summary>
        internal void Untrack(TrainCar car) => interiors.Remove(car);

        /// <summary>Whether any tracked cab needs rechecking because the fittings version or a car's GUID changed.</summary>
        internal bool NeedsSync()
        {
            var version = Upgrades.Fittings.Version;
            foreach (var pair in interiors)
            {
                var car = pair.Key;
                var cab = pair.Value;
                if (car == null || cab.Interior == null
                    || cab.CheckedVersion != version
                    || !string.Equals(cab.CheckedGuid, Upgrades.Fittings.CarGuidOf(car), StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>Adds or removes the valve on any loaded cab whose fittings version or car GUID changed.</summary>
        public void EnsureFitted()
        {
            if (!NeedsSync())
                return;
            SyncLoaded(onlyChanged: true);
        }

        /// <summary>Rebuilds the valve in every loaded cab: as the seating bench moves it, and as its fitting is fitted or taken off.</summary>
        public void RebuildLoaded()
        {
            foreach (var pair in interiors.ToList())
            {
                var car = pair.Key;
                var interior = pair.Value.Interior;
                if (car == null || interior == null || car.loadedInterior == null || car.loadedInterior.transform != interior)
                {
                    interiors.Remove(car);
                    continue;
                }
                Rebuild(car);
            }
        }

        /// <summary>Adds or removes the valve only on loaded cabs whose fitted state changed, leaving other cabs untouched.</summary>
        public void SyncLoaded(bool onlyChanged = false)
        {
            var version = Upgrades.Fittings.Version;
            var syncHook = SyncHook;
            foreach (var pair in interiors.ToList())
            {
                var car = pair.Key;
                var cab = pair.Value;
                var interior = cab.Interior;
                if (car == null || interior == null || car.loadedInterior == null
                    || (syncHook == null && car.loadedInterior.transform != interior))
                {
                    interiors.Remove(car);
                    continue;
                }
                var guid = Upgrades.Fittings.CarGuidOf(car);
                if (onlyChanged && cab.CheckedVersion == version && string.Equals(cab.CheckedGuid, guid, StringComparison.Ordinal))
                    continue;
                cab.CheckedVersion = version;
                cab.CheckedGuid = guid;
                var shouldHave = fitted(car);
                if (syncHook != null)
                {
                    syncHook(car, interior, shouldHave);
                    continue;
                }
                var existing = interior.Find(RootName);
                if (existing != null && !shouldHave)
                {
                    Object.DestroyImmediate(existing.gameObject);
                    Main.Mod?.Logger.Log($"{car.LogName()}: the {what} is no longer fitted; removed from the cab");
                }
                else if (existing == null && shouldHave)
                {
                    var result = Attach(car, interior);
                    if (result != null)
                        Main.Mod?.Logger.Log($"{car.LogName()}: {result}");
                }
            }
        }

        /// <summary>Rebuilds the valve in <paramref name="car"/>'s cab, if loaded.</summary>
        public void Rebuild(TrainCar car)
        {
            var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
            if (interior == null)
                return;
            var old = interior.Find(RootName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);
            var result = Attach(car, interior);
            if (result != null)
                Main.Mod.Logger.Log($"{car.LogName()}: {result}");
        }

        /// <summary>Builds the valve in a loaded cab while its fitting is fitted. Returns a sentence for the log, or null.</summary>
        public string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !FireboxRegistry.TryGet(car, out _))
                return null;
            TrackInterior(car, interior);
            if (!fitted(car))
                return null;
            var controller = car.SimController;
            var flow = controller != null ? controller.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(control.PortId, out var port))
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
                return $"no blower valve ('{blowerPort}') in the cab to put the {what} by";

            // A copy of the S282's blower handwheel; or, where the cab's own
            // blower handle is to be copied, that, from its own cab.
            var own = ownHandle(car.carLivery.id);
            var sourceLivery = own ? car.carLivery.id : StokerCab.SourceLivery;
            if (!Globals.G.Types.TryGetLivery(sourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {sourceLivery} cab to take the {what} from";
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
                return $"no blower valve '{StokerCab.SourceControl}' in the {sourceLivery} cab to copy for the {what}";

            var seat = SeatFor(car.carLivery.id);
            var cab = CabFrame.Of(car);
            var throttle = byThrottle && !Seated(car.carLivery.id) ? Throttle(car, interior) : null;
            if (throttle != null)
                seat = new Seat(cab.InverseTransformPoint(throttle.position) + throttleOffset, Vector3.zero, seat.PlateMove, seat.PlateSpin, seat.PlateTurn,
                    seat.PlateSquare, inCar: true);
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            // A frame on the blower's wheel, turned by the seat.
            var frame = root.transform;
            if (seat.InCar)
                frame.SetPositionAndRotation(cab.TransformPoint(seat.Move), cab.rotation * Quaternion.Euler(seat.Turns));
            else
                frame.SetPositionAndRotation(blower.position, blower.rotation * Quaternion.Euler(seat.Turns));
            var wheel = StokerCab.PlaceWheel(sourceValve, frame, seat.InCar ? Vector3.zero : seat.Move, control, port);

            // The plate where the S282's blower plate sits to its wheel.
            if (sourceLabel != null)
            {
                var plateFromWheel = Quaternion.Inverse(sourceControl.rotation) * (sourceLabel.position - sourceControl.position);
                var plateAt = frame.InverseTransformPoint(wheel.position + wheel.rotation * plateFromWheel) + seat.PlateMove;
                StokerCab.PlaceLabel(sourceControl, sourceLabel, wheel, frame, plateAt, Plate, 1f);
                var plate = frame.Find("label " + Plate);
                if (plate != null && seat.PlateSquare)
                    plate.rotation = Quaternion.LookRotation(cab.forward, cab.up);
                if (plate != null && seat.PlateSpin != 0f)
                    plate.rotation *= Quaternion.Euler(0f, 0f, seat.PlateSpin);
                if (plate != null && seat.PlateTurn != 0f)
                    plate.rotation = Quaternion.AngleAxis(seat.PlateTurn, frame.up) * plate.rotation;
            }
            built?.Invoke(root, car, interior);
            root.SetActive(true);

            var local = car.transform.InverseTransformPoint(wheel.position);
            return $"{what} at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car, " +
                   (throttle != null ? $"by the throttle '{throttle.name}' at {Local(car, throttle.position)}, not yet seated"
                       : seat.InCar ? "seated in the car's frame" : $"({seat.Move.x:F2}, {seat.Move.y:F2}, {seat.Move.z:F2}) m from the blower valve '{blower.name}'")
                   + $"; the blower valve '{blower.name}' at {Local(car, blower.position)}";
        }
    }
}
