using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Interaction;
using DV.Simulation.Ports;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class OilBurnerCab
    {
        private const string RootName = "[RealisticFiring oil burner valves]";

        // From the firing valve, in a frame facing the backhead (x the car's
        // right, y up, z into the boiler), m, inboard: the atomizer, then the
        // heater beyond it.
        private const float FirstValve = 0.16f;
        private const float ValveSpacing = 0.14f;
        private const float LabelDrop = 0.05f;

        // Seated per livery once placed in game: the first valve's offset from
        // the firing valve, in the frame (across inboard, up, out), m; others
        // take the default.
        private static readonly Dictionary<string, Vector3> Seats = new Dictionary<string, Vector3>
        {
            ["A.T.&S.F. 2901-A"] = new Vector3(-0.36f, 0.03f, 0f),
        };

        private static readonly Dictionary<string, Dictionary<string, string>> LooseWheels = new Dictionary<string, Dictionary<string, string>>
        {
            ["A.T.&S.F. 2901-A"] = new Dictionary<string, string>
            {
                [OilBurnerFit.Atomizer.Id] = "HW.014",
                [OilBurnerFit.Heater.Id] = "HW.017",
                [OilBurnerFit.ShopSteam.Id] = "HW.015",
            },
        };

        private const int AtomizerNotches = 41;
        private const int FiringValveNotches = 41;

        // The peep hole's sand target stands this far toward the cab from the
        // cover, m, and is this big.
        private const float PeepTargetOut = 0.1f;
        private static readonly Vector3 PeepTargetSize = new Vector3(0.25f, 0.25f, 0.15f);

        // The sand box, seated per livery once placed: across (toward the car's
        // right) and along (toward the front) from the fire door, m.
        private static readonly Dictionary<string, Vector2> SandSeats = new Dictionary<string, Vector2>
        {
            ["A.T.&S.F. 2901-A"] = new Vector2(-0.64f, -0.85f),
        };

        // The sand box: this far to the fireman's side of the fire door and
        // this far back from it, m.
        private const float SandBoxAside = 0.55f;
        private const float SandBoxBack = 0.45f;

        private static float nextCheck;

        /// <summary>Builds any loaded oil burner cab's valves that are missing.</summary>
        public static void EnsureBuilt()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            foreach (var firebox in RealisticOilFirebox.All)
            {
                var car = firebox.Car;
                var interior = car != null && car.loadedInterior != null ? car.loadedInterior.transform : null;
                if (interior == null || interior.Find(RootName) != null)
                    continue;
                var result = Attach(car, firebox, interior);
                if (result != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {result}");
            }
        }

        private static string Attach(TrainCar car, RealisticOilFirebox firebox, Transform interior)
        {
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(OilBurnerFit.Atomizer.PortId, out var atomizerPort) || !flow.TryGetPort(OilBurnerFit.Heater.PortId, out var heaterPort))
                return "oil burner: no atomizer or heater control in the sim graph";
            flow.TryGetPort(OilBurnerFit.ShopSteam.PortId, out var shopSteamPort, canBeNullOrEmpty: true);
            if (!Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"oil burner: no {StokerCab.SourceLivery} cab to take the valves from";
            var cabPrefab = livery.interiorPrefab.transform;
            var sourceValve = cabPrefab.Find(StokerCab.SourceValve);
            var sourceControl = sourceValve != null ? sourceValve.Find(StokerCab.SourceControl) : null;
            var sourceLabel = cabPrefab.Find(StokerCab.SourceLabel);
            if (sourceValve == null || sourceControl == null)
                return "oil burner: no S282 blower valve to copy";

            // The firing valve: the cab control feeding the stoker's port.
            var controlPort = firebox.Feed != null ? firebox.Feed.ControlPortId : null;
            var firing = controlPort != null
                ? interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == controlPort)
                : null;
            if (firing != null && firing.GetComponent<HoverName>() == null)
                firing.gameObject.AddComponent<HoverName>().Source = () => FiringHover(firebox);
            // "A quadrant lever with fine notches" (ICS pp. 22-26): 2.5 % a
            // notch, where the oil-fired mods' stoker levers step by 10 %.
            var restepped = firing != null && ControlSteps.Restep(firing.transform, FiringValveNotches);

            var frameCar = car.transform;
            var anchor = firing != null ? firing.transform.position : interior.position + frameCar.up * 1.8f;
            var anchorLocal = frameCar.InverseTransformPoint(anchor);
            // Inboard is toward the middle of the cab, from whichever side the valve is on.
            var inboard = anchorLocal.x > 0f ? -1f : 1f;
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            var frame = new GameObject("frame").transform;
            frame.SetParent(root.transform, false);
            // Facing the backhead: x the car's right, y up, z into the boiler;
            // offsets across are signed toward the middle of the cab.
            frame.SetPositionAndRotation(anchor, Quaternion.LookRotation(frameCar.forward, frameCar.up));

            var first = Seats.TryGetValue(car.carLivery.id, out var seat)
                ? new Vector3(inboard * seat.x, seat.y, seat.z)
                : new Vector3(inboard * FirstValve, 0f, 0f);
            // The valves wear the cab's own handwheels where it has them.
            var look = HandwheelLook.Find(interior, frameCar, anchor);
            var notes = new List<string>();
            var valves = new List<(StokerJetControls.Valve Valve, Port Port, string Plate, System.Func<string> Hover)>
            {
                (OilBurnerFit.Atomizer, atomizerPort, "ATOMIZER", () => AtomizerHover(firebox)),
                (OilBurnerFit.Heater, heaterPort, "OIL HEATER", () => HeaterHover(firebox)),
            };
            if (shopSteamPort != null)
                valves.Add((OilBurnerFit.ShopSteam, shopSteamPort, "SHOP STEAM", () => ShopSteamHover(firebox)));
            var loose = LooseWheels.TryGetValue(car.carLivery.id, out var assigned) ? assigned : null;
            var inRow = 0;
            var placed = new List<Transform>();
            foreach (var (valve, port, plate, hover) in valves)
            {
                Transform control = null;
                if (loose != null && loose.TryGetValue(valve.Id, out var wheelName))
                {
                    control = PlaceOnLoose(car, interior, root.transform, sourceValve, sourceControl, sourceLabel, valve, port, plate, wheelName);
                    if (control != null)
                        notes.Add($"{plate.ToLowerInvariant()} in place of the cab's '{wheelName}'");
                }
                if (control == null)
                {
                    var at = first + new Vector3(inboard * ValveSpacing * inRow++, 0f, 0f);
                    control = StokerCab.PlaceWheel(sourceValve, frame, at, valve, port);
                    look?.Dress(control, frameCar);
                    StokerCab.PlaceLabel(sourceControl, sourceLabel, control, frame, at - new Vector3(0f, LabelDrop, 0f), plate);
                    notes.Add(plate.ToLowerInvariant());
                }
                control.GetComponent<HoverName>().Source = hover;
                placed.Add(control);
                // One notch a click of the mouse wheel: the S282 valve they are
                // copied from spins under a torque per click and coasts across
                // notches, so it turns to the next notch instead. The atomizer
                // is set finely, at 2.5 % notches (41 across its travel).
                if (control.GetComponent<DV.CabControls.Spec.Rotary>() is DV.CabControls.Spec.Rotary rotary)
                {
                    rotary.scrollWheelUseSpringRotation = true;
                    rotary.scrollWheelHoverScroll = rotary.scrollWheelHoverScroll < 0f ? -1f : 1f;
                    if (valve == OilBurnerFit.Atomizer)
                        rotary.notches = AtomizerNotches;
                }
            }
            if (firing != null)
                FiringValvePassThrough.Attach(root, firing, placed);
            notes.Add(PlaceSandBox(car, root.transform, interior));
            notes.Add(PlaceSandTargets(car, root.transform, interior));
            root.SetActive(true);
            var local = frameCar.InverseTransformPoint(frame.TransformPoint(first));
            return $"oil burner valves ({string.Join(", ", notes)}) by the firing valve " +
                   (firing != null ? $"'{firing.name}'{(restepped ? $" (now {FiringValveNotches} notches)" : "")}" : "(no firing valve found in the cab; placed at the backhead's middle)") +
                   (inRow > 0
                       ? $", the row's first at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car frame" +
                         (Seats.ContainsKey(car.carLivery.id) ? " (seated for the livery)" : "") +
                         (look != null ? $", wheels like the cab's '{look.Name}'" : ", wheels like the S282's")
                       : "");
        }

        // A valve in place of the cab's loose handwheel of that name, drawn as
        // that wheel at its size, with a plate under it; null without the wheel.
        private static Transform PlaceOnLoose(TrainCar car, Transform interior, Transform root, Transform sourceValve, Transform sourceControl,
            Transform sourceLabel, StokerJetControls.Valve valve, Port port, string plate, string wheelName)
        {
            var loose = interior.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.transform).FirstOrDefault(t => t.name == wheelName);
            var wheelLook = loose != null ? HandwheelLook.OfLoose(loose, car.transform) : null;
            if (wheelLook == null || !HandwheelLook.TryPlaceOf(loose, car.transform, out var center, out var intoBackhead))
                return null;
            var spot = new GameObject(valve.Id + " frame").transform;
            spot.SetParent(root, false);
            spot.SetPositionAndRotation(center, Quaternion.LookRotation(intoBackhead, car.transform.up));
            var control = StokerCab.PlaceWheel(sourceValve, spot, Vector3.zero, valve, port);
            wheelLook.Dress(control, car.transform, lookSize: true);
            // Its hover and grip spheres are the S282 valve's, 8 and 12 cm
            // across: as wide as the wheel drawn, so its rim takes a click.
            foreach (var sphere in control.parent.GetComponentsInChildren<SphereCollider>(true))
            {
                var scale = sphere.transform.lossyScale;
                var world = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                if (world > 0f && sphere.radius * world < wheelLook.Diameter / 2f)
                    sphere.radius = wheelLook.Diameter / 2f / world;
            }
            loose.GetComponent<MeshRenderer>().enabled = false;
            StokerCab.PlaceLabel(sourceControl, sourceLabel, control, spot, new Vector3(0f, -LabelDrop - 0.03f, 0f), plate);
            return control;
        }

        private static string ShopSteamHover(RealisticOilFirebox firebox)
        {
            if (firebox.HouseLine > 0f)
                return $"Shop steam: the roundhouse's blower line in at the smoke arch, {(RealisticOilFirebox.SteamLinePressure - 1f) * firebox.SteamLineOpening:F1} bar" +
                       (firebox.Model.OnSteamLine ? " to the blower, atomizer, and heater" : " (the boiler's own steam is stronger)");
            return firebox.StandReaches
                ? "Shop steam: plug the stand's blower line into the fitting on the smokebox and open the stand's valve"
                : "Shop steam: it comes by a roundhouse or engine shed stand's blower line on the smokebox; tow a dead engine to one";
        }

        // Shovel targets into the firebox where the cab's own coal targets are
        // (or before the fire door without one), on the layers the shovel's
        // click sees. Returns a note for the log.
        private static string PlaceSandTargets(TrainCar car, Transform root, Transform interior)
        {
            // The peep hole: a target just in front of it, so a click there
            // reaches it with the door shut.
            var forward = car.transform.forward;
            var peepHole = interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == OilBurnerFit.PeepHolePortId);
            var peepNote = "";
            if (peepHole != null)
            {
                FlueSandBox.BuildFireboxTarget(root, peepHole.transform.position - forward * PeepTargetOut,
                    Quaternion.LookRotation(forward, car.transform.up), PeepTargetSize);
                peepNote = ", one at the peep hole";
            }
            var built = 0;
            foreach (var coal in interior.GetComponentsInChildren<NonPhysicsCoalTarget>(true))
            {
                var box = coal.GetComponent<BoxCollider>();
                if (box == null || coal.name.Contains("VR"))
                    continue;
                var t = coal.transform;
                FlueSandBox.BuildFireboxTarget(root, t.TransformPoint(box.center), t.rotation, Vector3.Scale(box.size, t.lossyScale));
                built++;
            }
            if (built > 0)
                return $"{built} flue sand target{(built > 1 ? "s" : "")} at the cab's coal target{peepNote}";
            if (peepHole != null)
                return "a flue sand target at the peep hole";
            var doorPort = car.SimController != null && car.SimController.firebox != null ? car.SimController.firebox.fireboxDoorPortId : null;
            var door = doorPort != null
                ? interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == doorPort)
                : null;
            if (door == null)
                return "no flue sand target (no coal target or fire door in the cab)";
            FlueSandBox.BuildFireboxTarget(root, door.transform.position + car.transform.forward * 0.25f,
                Quaternion.LookRotation(car.transform.forward, car.transform.up), new Vector3(0.5f, 0.4f, 0.3f));
            return "a flue sand target before the fire door";
        }

        // The flue sand box: on the floor beside the fire door, on the fireman's
        // side (left), unless moved. Returns a note for the log.
        private static string PlaceSandBox(TrainCar car, Transform parent, Transform interior)
        {
            var doorPort = car.SimController != null && car.SimController.firebox != null ? car.SimController.firebox.fireboxDoorPortId : null;
            var door = doorPort != null
                ? interior.GetComponentsInChildren<InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == doorPort)
                : null;
            var frame = car.transform;
            var near = door != null ? door.transform.position : interior.position + frame.up * 1.5f;
            var offset = SandSeats.TryGetValue(car.carLivery.id, out var seat) ? seat : new Vector2(-SandBoxAside, -SandBoxBack);
            var above = near + frame.right * offset.x + frame.forward * offset.y;
            if (!StokerCab.TryFindFloor(car, above, out var floor))
                floor = above - frame.up * 1.2f;
            var box = FlueSandBox.Build(car, parent, floor);
            if (box == null)
                return "no sand box (no sand on the loco)";
            var local = frame.InverseTransformPoint(floor);
            return $"a flue sand box on the floor at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m";
        }

        private static string FiringHover(RealisticOilFirebox firebox)
        {
            var fire = firebox.Model;
            return $"Firing valve: {(firebox.Feed != null ? firebox.Feed.FiringValve : 0f):P0}, oil {fire.FeedRate:F2} kg/s" +
                   (fire.FireOn ? "" : " (fire out)");
        }

        private static string AtomizerHover(RealisticOilFirebox firebox)
        {
            var fire = firebox.Model;
            var state = fire.FeedRate <= 0f ? "" : fire.Atomizing < 0.8f ? ", too little: it smokes" : fire.Drumming > 0f ? ", too much: it drums" : "";
            return $"Atomizer: {firebox.AtomizerValve * 100f:0.#} %{state}";
        }

        private static string HeaterHover(RealisticOilFirebox firebox)
        {
            var oil = firebox.Model.OilTemperature;
            var state = oil < OilFire.WarmOil - 5f ? "cold" : oil > OilFire.HotOil ? "too hot" : "warm";
            return $"Oil heater: {firebox.HeaterValve:P0}, oil {oil:F0} °C ({oil * 9f / 5f + 32f:F0} °F, {state})";
        }
    }

    /// <summary>
    /// Lets the cursor reach the oil burner's valves past the firing valve
    /// (a player, 2026-10-15: on the 2901 the atomizer took no click from the
    /// engineer's seat). The 2901's firing valve has a 15 cm hover sphere and
    /// a lever collider made solid from the floor to its handle, standing
    /// between the engineer and the valves, and the cursor's ray takes the
    /// first control it meets. The game asks a control whether to let the ray
    /// on at each hit on it; the firing valve's answer is wrapped to let it on
    /// when the ray would go on to one of these valves.
    /// </summary>
    internal sealed class FiringValvePassThrough : MonoBehaviour
    {
        // The game's hover ray is this long, m (GrabberRaycasterDV).
        private const float Reach = 4f;

        internal const string PassThroughField = "interactionPassThrough";

        private static readonly AccessTools.FieldRef<AGrabHandler, AGrabHandler.InteractionPassThroughDelegate> PassThrough =
            AccessTools.FieldRefAccess<AGrabHandler, AGrabHandler.InteractionPassThroughDelegate>(PassThroughField);

        private Component firing;
        private Collider[] valves;
        private AGrabHandler handler;
        private AGrabHandler.InteractionPassThroughDelegate wrapped;

        public static void Attach(GameObject root, Component firing, IEnumerable<Transform> valves)
        {
            var pass = root.AddComponent<FiringValvePassThrough>();
            pass.firing = firing;
            pass.valves = valves.Where(v => v != null).SelectMany(v => (v.parent != null ? v.parent : v).GetComponentsInChildren<Collider>(true)).ToArray();
        }

        // The game gives the lever its hover handler and answer a frame or
        // more after the cab loads: wrapped once they are there.
        private void Update()
        {
            if (firing == null || valves == null || valves.Length == 0)
                return;
            if (handler == null)
                handler = firing.GetComponentInChildren<AGrabHandler>(true);
            if (handler == null)
                return;
            var game = PassThrough(handler);
            if (game == wrapped && wrapped != null)
                return;
            wrapped = point => game != null && game(point) || ValveBeyond(point);
            PassThrough(handler) = wrapped;
        }

        private bool ValveBeyond(Vector3 point)
        {
            var camera = PlayerManager.ActiveCamera;
            if (camera == null)
                return false;
            var origin = camera.transform.position;
            var ray = new Ray(origin, point - origin);
            foreach (var valve in valves)
            {
                if (valve != null && valve.enabled && valve.gameObject.activeInHierarchy && valve.Raycast(ray, out _, Reach))
                    return true;
            }
            return false;
        }
    }
}
