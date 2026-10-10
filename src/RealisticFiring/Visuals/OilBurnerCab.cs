using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Ports;
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
        // the firing valve, in the frame (across, up, out), m.
        private static readonly Dictionary<string, Vector3> Seats = new Dictionary<string, Vector3>();

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

        /// <summary>Moves every loaded cab's valves, as the placement settings change.</summary>
        public static void RebuildLoaded()
        {
            foreach (var firebox in RealisticOilFirebox.All)
            {
                var interior = firebox.Car != null && firebox.Car.loadedInterior != null ? firebox.Car.loadedInterior.transform : null;
                var old = interior != null ? interior.Find(RootName) : null;
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
            }
            nextCheck = 0f;
            EnsureBuilt();
        }

        private static string Attach(TrainCar car, RealisticOilFirebox firebox, Transform interior)
        {
            var flow = car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(OilBurnerFit.Atomizer.PortId, out var atomizerPort) || !flow.TryGetPort(OilBurnerFit.Heater.PortId, out var heaterPort))
                return "oil burner: no atomizer or heater control in the sim graph";
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

            var settings = Main.Settings;
            var seat = Seats.TryGetValue(car.carLivery.id, out var seated) ? seated : new Vector3(FirstValve, 0f, 0f);
            var first = new Vector3(inboard * (seat.x + settings.OilValvesNudgeAcross / 100f), seat.y + settings.OilValvesNudgeUp / 100f,
                seat.z - settings.OilValvesNudgeOut / 100f);
            var notes = new List<string>();
            var valves = new[] { (OilBurnerFit.Atomizer, atomizerPort, "ATOMIZER"), (OilBurnerFit.Heater, heaterPort, "OIL HEATER") };
            for (var i = 0; i < valves.Length; i++)
            {
                var (valve, port, plate) = valves[i];
                var at = first + new Vector3(inboard * ValveSpacing * i, 0f, 0f);
                var control = StokerCab.PlaceWheel(sourceValve, frame, at, valve, port);
                var hover = control.GetComponent<HoverName>();
                hover.Source = valve == OilBurnerFit.Atomizer ? (System.Func<string>)(() => AtomizerHover(firebox)) : () => HeaterHover(firebox);
                StokerCab.PlaceLabel(sourceControl, sourceLabel, control, frame, at - new Vector3(0f, LabelDrop, 0f), plate);
                notes.Add(plate.ToLowerInvariant());
            }
            notes.Add(PlaceSandBox(car, root.transform, interior));
            root.SetActive(true);
            var local = frameCar.InverseTransformPoint(frame.TransformPoint(first));
            return $"oil burner valves ({string.Join(", ", notes)}) beside the firing valve " +
                   (firing != null ? $"'{firing.name}'" : "(no firing valve found in the cab; placed at the backhead's middle)") +
                   $", the first at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m in the car frame";
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
            var settings = Main.Settings;
            var above = near + frame.right * (-SandBoxAside + settings.SandBoxNudgeAcross / 100f)
                             + frame.forward * (-SandBoxBack + settings.SandBoxNudgeAlong / 100f);
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
            return $"Atomizer: {firebox.AtomizerValve:P0}{state}";
        }

        private static string HeaterHover(RealisticOilFirebox firebox)
        {
            var oil = firebox.Model.OilTemperature;
            var state = oil < OilFire.WarmOil - 5f ? "cold" : oil > OilFire.HotOil ? "too hot" : "warm";
            return $"Oil heater: {firebox.HeaterValve:P0}, oil {oil:F0} °C ({oil * 9f / 5f + 32f:F0} °F, {state})";
        }
    }
}
