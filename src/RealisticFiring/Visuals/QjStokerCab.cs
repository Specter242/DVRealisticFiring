using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DV;
using DV.CabControls;
using DV.Simulation.Ports;
using LocoSim.Implementations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RealisticFiring.Visuals
{
    /// <summary>The QJ's jet bank, auxiliary overhead valves and fireman's stoker/pump controls.</summary>
    internal static class QjStokerCab
    {
        internal const string Livery = "S2102-870";
        internal const string NativeBank = "Controls/fireman/";

        internal enum ControlSeat { Overhead, Stoker, FeedPump }

        internal readonly struct Wheel
        {
            internal Wheel(string holder, string label, string port, int jet, Vector2 overhead, ControlSeat seat = ControlSeat.Overhead)
            { Holder = holder; Label = label; Port = port; Jet = jet; Overhead = overhead; Seat = seat; }
            internal string Holder { get; }
            internal string Label { get; }
            internal string Port { get; }
            internal int Jet { get; }
            internal Vector2 Overhead { get; }
            internal ControlSeat Seat { get; }
        }

        // Read from LittleNiko36's 1.0.0 cab. From the footplate, front jets
        // are the upper outer pair, back jets the middle pair, fines below.
        internal static readonly IReadOnlyList<Wheel> Wheels = new[]
        {
            new Wheel("Blowdown", "blowdown", "blowdown.EXT_IN", 0, new Vector2(0.3f, 0.075f)),
            new Wheel("Dynamo", "dynamo", "dynamoControl.EXT_IN", 1, new Vector2(-0.3f, -0.075f)),
            new Wheel("Injector", "injector (1)", "injector.EXT_IN", 2, Vector2.zero, ControlSeat.FeedPump),
            new Wheel("Compressor", "compressor", "compressorControl.EXT_IN", 3, new Vector2(-0.3f, 0.075f)),
            new Wheel("Stoker", "stoker (1)", "stokerControl.EXT_IN", 4, Vector2.zero, ControlSeat.Stoker),
            new Wheel("Blower", "blower", "blower.EXT_IN", 5, new Vector2(0.3f, -0.075f)),
        };

        internal readonly struct Seat
        {
            internal Seat(Vector3 at, Vector3 turns) { At = at; Turns = turns; }
            internal Vector3 At { get; }
            internal Vector3 Turns { get; }
        }

        // Measured from 1.0.2's Cube.036: its front is centered at
        // (0, 4.259, -0.225), pitched down 15 degrees. The native holders
        // already carry 10 degrees of pitch; stand their stems off the face.
        internal static readonly Seat StartingSeat = new Seat(new Vector3(0f, 4.25f, -0.255f), new Vector3(-25f, 0f, 0f));
        // Across the top of the native triangle, below the speedometer.
        // The usual vertical column would cover that instrument.
        private static readonly Vector3[] GaugeSeats =
        {
            new Vector3(-0.17f, 0f, 0.035f),
            new Vector3(0f, 0f, 0.035f),
            new Vector3(0.17f, 0f, 0.035f),
        };
        private static readonly Dictionary<string, Seat> Seats = new Dictionary<string, Seat>();
        internal static readonly Dictionary<string, Seat> DevSeats = new Dictionary<string, Seat>();
        private static readonly Dictionary<string, Vector3> StokerSeats = new Dictionary<string, Vector3>
        {
            [Livery] = new Vector3(0.12f, 0.17f, 0.06f),
        };
        internal static readonly Dictionary<string, Vector3> DevStokerSeats = new Dictionary<string, Vector3>();
        internal static Vector3 StokerSeatFor(string id) => DevStokerSeats.TryGetValue(id, out var dev) ? dev
            : StokerSeats.TryGetValue(id, out var seat) ? seat : new Vector3(-0.27f, -0.28f, 0f);
        internal static Seat SeatFor(string id) => DevSeats.TryGetValue(id, out var seat) ? seat
            : Seats.TryGetValue(id, out seat) ? seat : StartingSeat;

        internal static Vector3 LeverFloor(TrainCar car, Vector3 move)
        {
            // Start just above foot level, below the casing and control
            // hover areas. The native teleport point is 9 mm below the
            // walkable deck's top; using it as the surface buried the plate.
            var deck = car.cabTeleportDestination != null
                ? car.transform.InverseTransformPoint(car.cabTeleportDestination.transform.position).y : 1.985f;
            var at = car.transform.TransformPoint(new Vector3(1.15f, deck, -0.7f) + move);
            Physics.SyncTransforms();
            foreach (var hit in Physics.RaycastAll(at + car.transform.up * 0.15f, -car.transform.up, 0.3f,
                         LayerMask.GetMask("Train_Walkable"), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                if (TrainCar.Resolve(hit.collider.gameObject) == car && Vector3.Dot(hit.normal, car.transform.up) > 0.9f)
                    return hit.point;
            return at + car.transform.up * 0.009f;
        }

        internal static bool HiddenOriginal(Component component) => component.GetComponentInParent<QjHiddenControl>() != null;

        // CCL's LabelProcessor wraps an authored label in <name>_text and
        // puts both its plate and localized text under that wrapper. The
        // authored transform is then empty; moving or hiding it does nothing.
        private static Transform Label(Transform cab, Wheel wheel) =>
            cab.Find("labels/controls/" + wheel.Label + "_text");

        internal static string Attach(TrainCar car, Transform interior, SimulationFlow flow)
        {
            var prefab = car.carLivery.interiorPrefab != null ? car.carLivery.interiorPrefab.transform : null;
            if (prefab == null || !Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var s282) || s282.interiorPrefab == null)
                return "QJ cab layout unavailable: a cab template is missing; native controls retained";
            var stock = s282.interiorPrefab.transform;
            var labelSource = stock.Find(StokerCab.SourceLabel);
            var valveSource = stock.Find(StokerCab.SourceValve);
            var controlSource = valveSource != null ? valveSource.Find(StokerCab.SourceControl) : null;
            if (controlSource == null || labelSource == null)
                return "QJ cab layout unavailable: stock fittings are missing; native controls retained";
            var pumpPresent = Sim.InjectorRig.TryGet(car, out var pumpRig) && pumpRig.Pump != null;
            Port pumpPort = null;
            if (pumpPresent && !flow.TryGetPort(Sim.FeedwaterFit.Valve.PortId, out pumpPort))
                return "QJ cab layout unavailable: feed-pump control is missing; native controls retained";

            // Validate the entire bank before changing a single native control.
            foreach (var wheel in Wheels)
            {
                var source = prefab.Find(NativeBank + wheel.Holder);
                var live = interior.Find(NativeBank + wheel.Holder);
                if (source == null || live == null || Label(prefab, wheel) == null || Label(interior, wheel) == null
                    || source.GetComponentsInChildren<InteractablePortFeeder>(true).Count(f => f.portId == wheel.Port) != 1
                    || !flow.TryGetPort(wheel.Port, out _) || !flow.TryGetPort(Sim.StokerJetControls.Valves[wheel.Jet].PortId, out _))
                    return $"QJ cab layout unavailable at '{wheel.Holder}'; native controls retained";
            }

            var root = new GameObject(StokerCab.RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            var assembly = root.AddComponent<QjCabAssembly>();
            try
            {
                var overhead = new GameObject("overhead controls").transform;
                overhead.SetParent(root.transform, false);
                assembly.Overhead = overhead;
                assembly.ApplySeat(SeatFor(Livery));
                var fittings = new GameObject("stoker instruments").transform;
                fittings.SetParent(root.transform, false);
                var anchor = interior.Find(NativeBank + "Blowdown");
                var placement = StokerCab.PlacementFor(Livery);
                fittings.SetPositionAndRotation(anchor.TransformPoint(placement.OffsetCm / 100f),
                    anchor.rotation * Quaternion.Euler(placement.Tilt, placement.Turn, 0f));
                var jetLabels = new List<(Transform Label, string Text)>();
                foreach (var wheel in Wheels)
                {
                    var live = interior.Find(NativeBank + wheel.Holder);
                    var source = prefab.Find(NativeBank + wheel.Holder);
                    flow.TryGetPort(wheel.Port, out var nativePort);
                    var label = Label(interior, wheel);
                    var nativeLabelSource = Label(prefab, wheel);
                    // The cab's two injector wheels shared one port. Keep the
                    // engineer's station; the fireman's spare wheel runs the
                    // separate feed pump instead of duplicating that injector.
                    if (wheel.Seat != ControlSeat.FeedPump || pumpPresent)
                    {
                        var above = wheel.Seat == ControlSeat.Overhead;
                        var moved = Object.Instantiate(source.gameObject, above ? overhead : fittings, false).transform;
                        if (above) moved.localPosition = new Vector3(wheel.Overhead.x, wheel.Overhead.y, 0f);
                        else
                        {
                            var pumpAt = FeedPumpCab.SpotFor(Livery).Valve;
                            moved.localPosition = wheel.Seat == ControlSeat.Stoker ? StokerSeatFor(Livery) : new Vector3(pumpAt.x, pumpAt.y, 0f);
                            moved.localRotation = Quaternion.identity;
                        }
                        var pump = wheel.Seat == ControlSeat.FeedPump;
                        var control = Bind(moved, wheel.Port, pump ? Sim.FeedwaterFit.Valve.PortId : wheel.Port, pump ? pumpPort : nativePort, pump, pump ? "Feed pump" : null);
                        if (pump) control.GetComponent<HoverName>().Source = () => FeedPumpCab.Hover(pumpRig);
                        var movedLabel = Object.Instantiate(nativeLabelSource.gameObject, moved, false).transform;
                        movedLabel.localPosition = live.InverseTransformPoint(label.position);
                        movedLabel.localRotation = Quaternion.Inverse(live.rotation) * label.rotation;
                        if (pump)
                        {
                            RemoveLocalization(movedLabel);
                            jetLabels.Add((movedLabel, "FEED PUMP"));
                        }
                    }

                    var jet = Sim.StokerJetControls.Valves[wheel.Jet];
                    flow.TryGetPort(jet.PortId, out var jetPort);
                    var holder = Object.Instantiate(source.gameObject, root.transform, false).transform;
                    holder.name = jet.Id;
                    holder.SetPositionAndRotation(live.position, live.rotation);
                    Bind(holder, wheel.Port, jet.PortId, jetPort, true, jet.Label);
                    // Reuse the QJ's own plate, at its exact native seat.
                    // A stock S282 plate uses a different facing convention.
                    var jetLabel = Object.Instantiate(nativeLabelSource.gameObject, root.transform, false).transform;
                    jetLabel.name = "label " + jet.Id;
                    jetLabel.SetPositionAndRotation(label.position, label.rotation);
                    RemoveLocalization(jetLabel);
                    jetLabels.Add((jetLabel, JetPlate(wheel.Jet)));
                    assembly.Hide(live);
                    assembly.Hide(label, false);
                }

                var gauges = StokerGauges.Build(fittings, stock, car, GaugeSeats);
                for (var i = 0; i < GaugeSeats.Length; i++)
                {
                    var at = GaugeSeats[i];
                    StokerCab.PipeToBackhead(fittings, car, new Vector2(at.x, at.y), at.z - 0.005f,
                        0.006f, 0.013f, "stoker gauge line " + i, 0.15f);
                }
                // The native wheel above already controls the pump. Build its
                // gauge and sound, without a second valve on a long S282 stem.
                var pumpGauge = FeedPumpCab.BuildInManifold(car, flow, fittings, stock, null, controlSource, labelSource, null);
                if (flow.TryGetPort(Sim.StokerJetControls.Reverse.PortId, out var reverse))
                    StokerCab.PlaceLever(stock, fittings, reverse, car, controlSource, labelSource);
                root.SetActive(true);
                foreach (var plate in jetLabels)
                    foreach (var text in plate.Label.GetComponentsInChildren<TMPro.TextMeshPro>(true))
                        text.text = plate.Text;
                gauges.WriteScales();
                pumpGauge.Gauge?.WriteScales();
                return "QJ six native handwheels work the stoker jets; stoker engine and feed pump beside the fireman; four auxiliary valves overhead; injector on the engineer's side (placement test)";
            }
            catch
            {
                Object.DestroyImmediate(root);
                throw;
            }
        }

        private static void RemoveLocalization(Transform label)
        {
            foreach (var localize in label.GetComponentsInChildren<MonoBehaviour>(true).Where(c => c.GetType().Name == "Localize").ToList())
                Object.DestroyImmediate(localize);
        }

        private static Transform Bind(Transform holder, string oldPort, string newPort, Port port, bool removeKeys, string name)
        {
            var feeder = holder.GetComponentsInChildren<InteractablePortFeeder>(true).Single(f => f.portId == oldPort);
            if (removeKeys)
                foreach (var input in holder.GetComponentsInChildren<MonoBehaviour>(true).Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)).ToList())
                    Object.DestroyImmediate(input);
            feeder.portId = newPort;
            feeder.gameObject.AddComponent<SimControlBinder>().Bind(feeder, port);
            if (name != null) feeder.gameObject.AddComponent<HoverName>().Text = name;
            return feeder.transform;
        }

        private static string JetPlate(int jet) => new[] { "MASTER", "L BACK", "R BACK", "L FRONT", "R FRONT", "FINES" }[jet];

        internal static void RefreshAttachments(TrainCar car, Transform interior)
        {
            if (car.carLivery.id != Livery) return;
            // The steam lever follows the overhead water wheel, including
            // when a settings change restores the unmodified native bank.
            InjectorSteamCab.RebuildLoaded();
            if (Sim.InjectorRig.TryGet(car, out var rig))
                InjectorOverflow.Attach(car, rig, new[] { interior });
        }
    }

    // Keep the original feeders alive for the game's serialized controller
    // list, but remove their rendering, hover, collision and keyboard input.
    // Fresh prefab copies get joints at their final positions, on PC and VR.
    // Destroying/rebuilding the RF assembly restores every original flag.
    internal sealed class QjHiddenControl : MonoBehaviour
    {
        private readonly List<Action> restore = new List<Action>();
        private readonly HashSet<Object> captured = new HashSet<Object>();
        private bool controls;

        internal void Hide(bool controls)
        {
            this.controls = controls;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                if (captured.Add(renderer))
                {
                    var enabled = renderer.enabled;
                    restore.Add(() => { if (renderer != null) renderer.enabled = enabled; });
                }
                renderer.enabled = false;
            }
            if (!controls) return;
            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                if (captured.Add(collider))
                {
                    var enabled = collider.enabled;
                    restore.Add(() => { if (collider != null) collider.enabled = enabled; });
                }
                collider.enabled = false;
            }
            foreach (var input in GetComponentsInChildren<MonoBehaviour>(true).Where(c => StokerCab.InputComponents.Contains(c.GetType().Name)))
            {
                if (captured.Add(input))
                {
                    var enabled = input.enabled;
                    restore.Add(() => { if (input != null) input.enabled = enabled; });
                }
                input.enabled = false;
            }
        }

        private IEnumerator Start()
        {
            // Stock static interaction areas create their hover collider at
            // end of frame. Hide that too after the stock controls settle.
            for (var i = 0; i < 5; i++) yield return null;
            Hide(controls);
        }

        private void OnDestroy() { foreach (var action in restore) action(); }
    }

    internal sealed class QjCabAssembly : MonoBehaviour
    {
        internal Transform Overhead;
        private readonly List<QjHiddenControl> hidden = new List<QjHiddenControl>();
        internal void Hide(Transform source, bool controls = true)
        {
            var guard = source.gameObject.AddComponent<QjHiddenControl>();
            hidden.Add(guard);
            guard.Hide(controls);
        }

        internal void ApplySeat(QjStokerCab.Seat seat)
        {
            Overhead.localPosition = seat.At;
            Overhead.localRotation = Quaternion.Euler(seat.Turns);
            // Called while inactive. The bench rebuilds after a move, so
            // each PC/VR joint starts at its final seat and orientation.
        }

        private void OnDestroy()
        {
            foreach (var guard in hidden)
                if (guard != null) Object.DestroyImmediate(guard);
        }
    }
}
