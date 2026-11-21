using System;
using System.Collections.Generic;
using System.Linq;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
    /// <summary>
    /// Reads live engine state into a <see cref="ChecklistSnapshot"/> and
    /// evaluates <see cref="FiringChecklists"/> for the tablet, Comms Tablet
    /// app, and <see cref="Api"/>.
    /// </summary>
    internal static class ChecklistReader
    {
        internal const float RecentActionWindow = 180f;
        private static readonly Dictionary<TrainCar, float> DropGrateAt = new Dictionary<TrainCar, float>();
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, ColdStartLatch> ColdStarts =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, ColdStartLatch>();

        /// <summary>Notes that the drop grate was opened on <paramref name="car"/>.</summary>
        internal static void NoteDropGrate(TrainCar car) => NoteDropGrateAt(car, Time.unscaledTime);

        internal static void NoteDropGrateAt(TrainCar car, float now)
        {
            Prune(DropGrateAt, now);
            if (car != null)
                DropGrateAt[car] = now;
        }

        internal static void ClearRecent(TrainCar car)
        {
            if (car == null)
                return;
            DropGrateAt.Remove(car);
        }

        internal static bool ReadRecentDropGrate(TrainCar car, float now, bool needsCleaning) =>
            ReadRecent(DropGrateAt, car, now, needsCleaning);

        private static bool ReadRecent(Dictionary<TrainCar, float> map, TrainCar car, float now, bool keep)
        {
            Prune(map, now);
            if (car == null)
                return false;
            if (!keep)
            {
                map.Remove(car);
                return false;
            }
            if (!map.TryGetValue(car, out var at))
                return false;
            if (now - at <= RecentActionWindow)
                return true;
            map.Remove(car);
            return false;
        }

        private static void Prune(Dictionary<TrainCar, float> map, float now)
        {
            if (map.Count == 0)
                return;
            List<TrainCar> dead = null;
            foreach (var pair in map)
            {
                if (pair.Key == null || now - pair.Value > RecentActionWindow)
                {
                    if (dead == null)
                        dead = new List<TrainCar>();
                    dead.Add(pair.Key);
                }
            }
            if (dead != null)
            {
                for (var i = 0; i < dead.Count; i++)
                    map.Remove(dead[i]);
            }
        }

        internal static bool TryRead(TrainCar car, ChecklistId? selected, out ChecklistSnapshot snapshot, out Checklist checklist)
        {
            snapshot = default;
            checklist = null;
            if (car == null)
                return false;
            if (FireboxRegistry.TryGetTended(car, out var coal))
            {
                InjectorRig.TryGetTended(coal.Car, out var rig);
                BoilerWaterRig.TryGet(coal.Car, out var water);
                snapshot = Snapshot(coal, null, rig, water, coal.Car);
                checklist = FiringChecklists.Evaluate(snapshot, selected);
                return true;
            }
            if (RealisticOilFirebox.TryGetTended(car, out var oil))
            {
                InjectorRig.TryGetTended(oil.Car, out var rig);
                BoilerWaterRig.TryGet(oil.Car, out var water);
                snapshot = Snapshot(null, oil, rig, water, oil.Car);
                checklist = FiringChecklists.Evaluate(snapshot, selected);
                return true;
            }
            return false;
        }

        internal static Checklist EvaluateParts(RealisticFirebox coal, RealisticOilFirebox oil, InjectorRig rig, BoilerWaterRig water, TrainCar car, ChecklistId? selected = null)
        {
            var snapshot = Snapshot(coal, oil, rig, water, car);
            return FiringChecklists.Evaluate(snapshot, selected);
        }

        internal static ChecklistSnapshot Snapshot(RealisticFirebox coal, RealisticOilFirebox oil, InjectorRig rig, BoilerWaterRig water, TrainCar car)
        {
            var s = new ChecklistSnapshot
            {
                ThinZone = -1,
                TenderWaterValveOpen = true,
                StokerForward = true,
                BackCornersCovered = true,
            };

            s.GlassLevel = rig != null || water != null ? FireOverlay.GlassShown(rig, water) : 0f;
            s.BoilerGaugeBar = rig != null || water != null ? FireOverlay.GaugePressure(rig, water) : 0f;

            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            var comps = flow?.OrderedSimComps;
            var exhaust = comps?.OfType<SteamExhaust>().FirstOrDefault();
            if (exhaust != null)
            {
                s.Damper = Mathf.Clamp01(exhaust.damperControl.Value);
                s.Blower = Mathf.Clamp01(exhaust.blowerControl.Value);
            }

            var engine = EngineDrift.EngineOf(car) ?? comps?.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            if (engine != null)
            {
                s.Throttle = Mathf.Clamp01(engine.throttleControl.Value);
                s.Cutoff = EngineDrift.Cutoff(engine);
                s.ForwardSpeedMs = SafeForwardSpeed(car);
                var fittings = EngineDrift.Fittings(car, engine);
                s.HasDriftingValve = DriftingFittings.Valve(car);
                s.DriftingValveOpen = fittings.Valve > 0.05f;
                s.HasByPassValves = fittings.ByPass;
                s.HasSniftingValves = fittings.Snifting;
            }
            else if (car != null)
            {
                s.ForwardSpeedMs = SafeForwardSpeed(car);
            }

            if (water != null)
                s.DriftingHookedUp = water.DriftingWearing;

            if (rig != null && rig.Circuits != null && rig.Circuits.Count > 0)
            {
                var circuits = new InjectorCircuitSnapshot[rig.Circuits.Count];
                for (var i = 0; i < rig.Circuits.Count; i++)
                {
                    var c = rig.Circuits[i];
                    var hasSeparate = c.SteamValve != null;
                    circuits[i] = new InjectorCircuitSnapshot
                    {
                        Name = c.Name ?? "",
                        HasSeparateSteamValve = hasSeparate,
                        HasTenderWaterValve = c.TenderValve != null,
                        TenderWaterValveOpen = !c.TenderValveShut,
                        WaterValve = hasSeparate ? c.WaterValve.Value : c.Control.Value,
                        SteamValve = hasSeparate ? c.SteamValve.Value : 0f,
                        CrackOpening = c.Injector != null ? c.Injector.Spec.CrackOpening : InjectorSpec.DefaultCrackOpening,
                        State = c.Flow.State,
                        OverflowWaterKgPerS = c.Flow.Overflow,
                    };
                }
                FiringChecklists.ApplyInjectorCircuit(ref s, circuits);
            }

            if (coal != null)
            {
                var fire = coal.Model;
                s.IsOil = false;
                s.FireOn = fire.FireOn;
                s.HasDoor = coal.HasDoorPort;
                s.DoorOpening = coal.HasDoorPort ? coal.DoorOpening : 0f;
                s.BurningCoalMass = fire.BurningMass;
                s.TotalCoalMass = fire.CoalMass;
                s.LighterLightsCoal = LighterRule.LightsCoal;
                s.ClinkerMass = fire.ClinkerMass;
                s.NeedsCleaning = FiringAdvice.WantsCleaning(fire);
                s.WantsShake = FiringAdvice.ShakeDue(fire, coal.StartupMultiplier > 1.01f);
                s.AshpanFill = fire.AshpanFill;
                s.AshpanFull = fire.AshpanFill >= FiringAdvice.FullAshpan;
                s.ThinZone = FiringAdvice.ThinZone(fire.Zones);

                var wood = 0f;
                var ragLit = false;
                var woodCaught = false;
                for (var z = 0; z < GrateLayout.ZoneCount; z++)
                {
                    var starter = fire.Starters[z];
                    wood += starter.Wood;
                    ragLit |= starter.RagLit;
                    woodCaught |= starter.Involved > 0f || fire.WoodIgnitionSupport((GrateZone)z) > 0.3f || starter.Glowing;
                }
                s.WoodMass = wood;
                s.RagLit = ragLit;
                s.WoodCaught = woodCaught;

                var frontCoal = 0f;
                var backCoal = 0f;
                var frontClinker = 0f;
                for (var col = 0; col < GrateLayout.Columns; col++)
                {
                    var front = fire.Zones[(int)GrateLayout.At(2, col)];
                    var back = fire.Zones[(int)GrateLayout.At(0, col)];
                    frontCoal += front.Coal;
                    frontClinker += front.Clinker;
                    backCoal += back.Coal;
                }
                s.FrontCoalKg = frontCoal;
                s.BackCoalKg = backCoal;
                s.FrontClinkerKg = frontClinker;

                var dropGrateOpen = coal.DropGrateOpening > 0.05f
                    || (SafeTryGetPort(flow, RealisticFirebox.DropGrateControl.PortId, out var dgPort) && dgPort.Value > 0.05f);
                s.SlicedRecently = s.NeedsCleaning && coal.SlicedRecently;
                if (car != null)
                {
                    var now = SafeUnscaledTime();
                    if (dropGrateOpen)
                        NoteDropGrateAt(car, now);
                    s.DropGrateOpenedRecently = dropGrateOpen || ReadRecentDropGrate(car, now, s.NeedsCleaning);
                }
                else
                {
                    s.DropGrateOpenedRecently = dropGrateOpen;
                }

                if (coal.LastStoker is StokerFlow stokerFlow)
                {
                    s.HasStoker = true;
                    s.StokerJammed = stokerFlow.Jammed;
                    s.StokerMasterBar = stokerFlow.ManifoldPressure;
                    s.StokerFrontBar = 0.5f * (stokerFlow.LeftFrontPressure + stokerFlow.RightFrontPressure);
                    s.StokerBackBar = 0.5f * (stokerFlow.LeftBackPressure + stokerFlow.RightBackPressure);
                    s.StokerFinesBar = stokerFlow.FinesPressure;
                    s.StokerSpeed = coal.StokerEngineSpeed;
                    if (SafeTryGetPort(flow, StokerJetControls.Reverse.PortId, out var revPort))
                        s.StokerForward = StokerJetControls.Direction(revPort.Value) == StokerDirection.Forward;
                    var minCorner = Mathf.Min(fire.Zones[(int)GrateZone.BackLeft].Coal, fire.Zones[(int)GrateZone.BackRight].Coal);
                    s.BackCornersCovered = minCorner >= 0.35f * fire.ReferenceZoneMass;
                }
            }
            else if (oil != null)
            {
                s.IsOil = true;
                s.FireOn = oil.Model.FireOn;
                s.HasDoor = true;
                s.DoorOpening = oil.DoorOpening;
                s.Oil = FireController.ReadOil(oil);
            }

            // Each engine's cold start, held until its list is done (ColdStartLatch).
            if (coal != null && car != null)
                ColdStarts.GetValue(car, _ => new ColdStartLatch()).Apply(ref s, SafeUnscaledTime());
            return s;
        }

        private static bool SafeTryGetPort(SimulationFlow flow, string portId, out Port port)
        {
            port = null;
            if (flow == null)
                return false;
            try
            {
                return flow.TryGetPort(portId, out port, canBeNullOrEmpty: true) && port != null;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private static float SafeForwardSpeed(TrainCar car)
        {
            if (car == null)
                return 0f;
            try
            {
                return car.GetForwardSpeed();
            }
            catch (MissingMethodException)
            {
                return 0f;
            }
        }

        private static float SafeUnscaledTime()
        {
            try
            {
                return Time.unscaledTime;
            }
            catch (MissingMethodException)
            {
                return 0f;
            }
        }
    }
}
