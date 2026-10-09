using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Cars;
using DV.Simulation.Ports;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The drop grate's lever in the cab: a floor lever like the stoker's
    /// operating lever (the S282's cylinder cock lever) with two notches,
    /// Closed and Open, standing beside the loco's fire-dump lever with a
    /// DROP GRATE plate on the floor. Opened, whatever lies on the drop
    /// grate's zones falls through (see <see cref="RealisticFirebox.DropGrateControl"/>).
    /// </summary>
    internal static class DropGrateCab
    {
        private const string RootName = "[RealisticFiring drop grate]";
        private static readonly string[] Notches = { "Closed", "Open" };

        // Where the lever stands from the fire-dump lever, in the car frame (x
        // right, z forward), in m, before the placement settings: a foot aft
        // of it, unless seated for the loco.
        private static readonly Vector3 DefaultFromDump = new Vector3(0f, 0f, -0.3f);
        private static readonly Dictionary<string, Vector3> Placements = new Dictionary<string, Vector3>();

        // The floor is looked for straight down from this far above the dump
        // lever's pivot, which on the S282 sits just under the deck.
        private const float SearchAbove = 0.6f;
        private const float FallbackRise = 0.07f;

        private static readonly Dictionary<TrainCar, Transform> Interiors = new Dictionary<TrainCar, Transform>();

        /// <summary>Builds the lever in a loaded cab. Returns a sentence for the log, or null when not wanted.</summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !FireboxRegistry.TryGet(car, out var firebox))
                return null;
            var controller = car.SimController;
            var flow = controller != null ? controller.SimulationFlow : null;
            if (flow == null || !flow.TryGetPort(RealisticFirebox.DropGrateControl.PortId, out var port))
                return null;

            // The fire-dump lever: whatever feeds the firebox's dump control.
            var graph = controller.connectionsDefinition;
            var dumpPort = graph != null && graph.portReferenceConnections != null
                ? graph.portReferenceConnections.FirstOrDefault(r => r.portReferenceId == $"{firebox.id}.COAL_DUMP_CONTROL")?.portId
                : null;
            var dump = dumpPort == null ? null : interior.GetComponentsInChildren<InteractablePortFeeder>(false)
                .FirstOrDefault(f => f.portId == dumpPort)?.transform;
            if (dump == null)
                return $"no fire-dump lever ('{dumpPort}') in the cab to put the drop grate lever by";

            if (!Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {StokerCab.SourceLivery} cab to take the drop grate lever from";
            var cabPrefab = livery.interiorPrefab.transform;
            var sourceValve = cabPrefab.Find(StokerCab.SourceValve);
            var sourceControl = sourceValve != null ? sourceValve.Find(StokerCab.SourceControl) : null;
            var sourceLabel = cabPrefab.Find(StokerCab.SourceLabel);

            Interiors[car] = interior;
            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);

            var settings = Main.Settings;
            var offset = (Placements.TryGetValue(car.carLivery.id, out var seated) ? seated : DefaultFromDump)
                         + new Vector3(settings.DropGrateNudgeAcross, 0f, settings.DropGrateNudgeAlong) / 100f;
            var spot = dump.position + car.transform.TransformVector(offset);
            var onFloor = StokerCab.TryFindFloor(car, spot + car.transform.up * SearchAbove, out var floor);
            if (!onFloor)
                floor = spot + car.transform.up * FallbackRise;

            var holder = StokerCab.BuildFloorLever(cabPrefab, root.transform, car, floor, RealisticFirebox.DropGrateControl.Id,
                RealisticFirebox.DropGrateControl.PortId, port, Notches,
                () => $"{RealisticFirebox.DropGrateControl.Label}: {(port.Value > 0.5f ? "open" : "closed")}");
            if (holder == null)
            {
                Object.Destroy(root);
                return $"no cylinder cock lever in the {StokerCab.SourceLivery} cab to copy for the drop grate";
            }
            if (sourceControl != null)
                StokerCab.PlaceFloorLabel(holder, root.transform, sourceControl, sourceLabel, car, floor, "DROP GRATE");
            root.SetActive(true);

            var local = car.transform.InverseTransformPoint(floor);
            return $"drop grate lever on the floor at ({local.x:F2}, {local.y:F2}, {local.z:F2}) m, " +
                   $"({offset.x:F2}, {offset.z:F2}) m from the fire-dump lever '{dump.name}'" + (onFloor ? "" : " (no floor found; guessed)") +
                   $"; the drop grate covers {string.Join(", ", firebox.Model.DropGrate)}";
        }

        /// <summary>Rebuilds the lever in every loaded cab, as the placement settings move.</summary>
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
                if (old != null)
                    Object.DestroyImmediate(old.gameObject);
                var result = Attach(car, interior);
                if (result != null && Main.Settings.LogFireboxCreation)
                    Main.Mod.Logger.Log($"{car.ID}: {result}");
            }
        }
    }
}
