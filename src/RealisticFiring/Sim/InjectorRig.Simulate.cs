using System.Linq;
using LocoSim.Implementations;
using LocoSim.Resources;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    internal sealed partial class InjectorRig
    {
        // A sudden jolt is a coupler's, not a pull the water follows, m/s².
        private const float MostOutletAcceleration = 5f;

        // The tender coupled behind, whose water valves were looked for.
        private TrainCar valveTender;

        // The tender's tank the injectors draw from, and its water over the
        // outlet: the pull along the tank, settling as the shallow water runs.
        private TrainCar tankCar;
        private ResourceContainer tank;
        private float outletPull;
        private bool outletCovered = true;
        private Vector3 lastTankVelocity;
        private bool tankVelocityKnown;

        /// <summary>Whether the tender's outlet is under water: a nearly empty tank uncovers it on a grade or pulling away.</summary>
        public bool TenderOutletCovered => outletCovered;

        // The tender coupled behind, or null; a deleted one counts as none.
        // Callers compare it by reference: Unity's == calls a deleted car
        // null, yet also calls it the same as a real null, so a cache keyed
        // on a deleted tender kept its tank (assassin4ever96's log,
        // 2026-10-21: StepOutlet then threw on every step).
        private TrainCar CoupledTender()
        {
            var tender = Car != null && Car.rearCoupler != null && Car.rearCoupler.coupledTo != null ? Car.rearCoupler.coupledTo.train : null;
            return tender != null ? tender : null;
        }

        // The coupled tender's water valve to each injector, looked for again
        // when the tender changes or its sim is not yet built.
        private void FindTenderValves()
        {
            var tender = CoupledTender();
            if (ReferenceEquals(tender, valveTender))
                return;
            var flow = tender != null && tender.SimController != null ? tender.SimController.simFlow : null;
            var valved = tender != null && tender.carLivery != null && !LeftStock.IsExcluded(tender)
                && InjectorValves.HasTenderValves(tender.carLivery.id);
            if (valved && flow == null)
                return;
            valveTender = tender;
            for (var i = 0; i < Circuits.Count; i++)
                Circuits[i].TenderValve = valved && flow.TryGetPort(InjectorValves.TenderValve(i).PortId, out var port) ? port : null;
        }

        /// <summary>Opens the tender's water valve to the injector at <paramref name="index"/>, as the fireman would; whether it was shut.</summary>
        public bool OpenTenderValve(int index)
        {
            FindTenderValves();
            if (index >= Circuits.Count || !Circuits[index].TenderValveShut)
                return false;
            Circuits[index].TenderValve.ExternalValueUpdate(1f);
            return true;
        }

        private bool StepOutlet(float delta)
        {
            var car = CoupledTender();
            if (!ReferenceEquals(car, tankCar))
            {
                tankCar = car;
                var flow = car != null && car.SimController != null ? car.SimController.simFlow : null;
                tank = flow?.OrderedSimComps.OfType<ResourceContainer>().FirstOrDefault(c => c.resourceType == ResourceContainerType.WATER);
                tankVelocityKnown = false;
                outletPull = 0f;
                outletCovered = true;
            }
            // A tank engine's own tanks, or a tender not yet built: as before.
            if (tank == null || !(delta > 0f))
                return outletCovered = true;
            var toEngine = Car.transform.position - car.transform.position;
            var along = Vector3.Dot(toEngine, car.transform.forward) >= 0f ? car.transform.forward : -car.transform.forward;
            var velocity = car.GetVelocity();
            var acceleration = tankVelocityKnown ? (velocity - lastTankVelocity) / delta : Vector3.zero;
            lastTankVelocity = velocity;
            tankVelocityKnown = true;
            acceleration = Vector3.ClampMagnitude(acceleration, MostOutletAcceleration);
            var pull = -TenderOutlet.Gravity * Vector3.Dot(Vector3.up, along) - Vector3.Dot(acceleration, along);
            outletPull += (pull - outletPull) * Mathf.Min(1f, delta / TenderOutlet.SloshTime);
            var depth = TenderOutlet.OutletDepth(tank.normalizedReadOutPort.Value, outletPull);
            outletCovered = outletCovered ? depth > 0f : depth > TenderOutlet.CoverAgain;
            return outletCovered;
        }

        /// <summary>Runs in place of the stock injector step.</summary>
        public void Simulate(float delta)
        {
            fedMass = fedHeat = 0f;
            var vessel = Vessel(boiler);
            if (vessel == null || !(delta > 0f))
                return;

            var multiplier = WaterConsumptionMultiplier(boiler) > 0f ? WaterConsumptionMultiplier(boiler) : 1f;
            // Uncovered, the tender's outlet gives nothing until the water runs back.
            var tender = StepOutlet(delta) ? Mathf.Max(0f, boiler.water.Value / multiplier) : 0f;
            var space = Mathf.Max(0f, TotalVolume(boiler) - vessel.waterVolume);
            var broken = boiler.isBrokenReadOut.Value != 0f;

            FindTenderValves();
            var drawn = 0f;
            var workingValve = 0f;
            var total = default(InjectorFlow);
            var feedEnthalpy = 0f;
            for (var i = 0; i < Circuits.Count; i++)
            {
                var circuit = Circuits[i];
                circuit.Injector.Spec.TechniqueAndHeat = Main.Settings.InjectorTechniqueAndHeat;
                // A shut injector cools to the air's temperature.
                circuit.Injector.Spec.AmbientTemperature = Ambient.Air;
                var flow = circuit.Injector.Step(delta, new InjectorInputs
                {
                    SteamValve = broken ? 0f : (circuit.SteamValve ?? circuit.Control).Value,
                    WaterValve = circuit.WaterInput,
                    BoilerPressure = vessel.pressure,
                    TenderWater = Mathf.Max(0f, tender - drawn),
                    BoilerSpace = space,
                    TenderWaterTemperature = TenderWaterTemperature,
                }, Steam);
                circuit.Flow = flow;
                var fed = flow.WaterIn * delta;
                if (fed > 0f)
                    Feed(vessel, fed, flow.FeedTemperature);
                var steam = Mathf.Min(flow.SteamUsed * delta, vessel.mass);
                if (steam > 0f)
                    vessel.RemoveSteam(steam);
                drawn += flow.TenderDraw * delta;
                space = Mathf.Max(0f, space - fed);
                if (flow.WaterIn > 0f)
                    workingValve += circuit.Feed.Value / Circuits.Count;
                total.WaterIn += flow.WaterIn;
                total.SteamUsed += flow.SteamUsed;
                total.TenderDraw += flow.TenderDraw;
                total.Overflow += flow.Overflow;
                total.OverflowSteam += flow.OverflowSteam;
                feedEnthalpy += flow.WaterIn * Steam.WaterEnthalpy(flow.FeedTemperature);
                LogStateChange(circuit);
            }
            total.State = Circuits[0].Flow.State;
            total.InjectorTemperature = Circuits[0].Flow.InjectorTemperature;
            total.RestartLimit = Circuits[0].Flow.RestartLimit;
            total.FeedTemperature = total.WaterIn > 0f ? Steam.WaterTemperature(feedEnthalpy / total.WaterIn) : TenderWaterTemperature;
            Flow = Circuits.Count == 1 ? Circuits[0].Flow : total;
            StepPump(vessel, delta, broken, tender, ref space, ref drawn);

            // Filling with no steam: the shed's hose from the mains, or the hand pump from the tender.
            if (!Net.IsClient)
                StepHose(broken);
            FillRate = 0f;
            var hotFed = 0f;
            if (HoseFilling && !broken && CanHoseFill)
            {
                var hose = Mathf.Min(HoseRate * HoseOpening * delta, space);
                Feed(vessel, hose, HoseHot ? HotWaterTemperature : TenderWaterTemperature);
                space = Mathf.Max(0f, space - hose);
                FillRate = hose / delta;
                if (HoseHot)
                    hotFed = hose;
            }
            else if ((HandPumping || RemoteHandPumping) && !broken && CanHandPump)
            {
                var pumped = Mathf.Min(HandPumpRate * delta, space, Mathf.Max(0f, tender - drawn));
                Feed(vessel, pumped, TenderWaterTemperature);
                space = Mathf.Max(0f, space - pumped);
                drawn += pumped;
                FillRate = pumped / delta;
            }
            StepDirectSteam(vessel, delta, space, broken);
            ChargePlant(delta, SteamedLastStep, hotFed);

            boiler.injectorFlowNormalizedReadOut.Value = workingValve;
            if (drawn > 0f)
            {
                var gameParams = GameParams(boiler);
                boiler.waterConsumption.Value = drawn * multiplier * (gameParams != null ? gameParams.ResourceConsumptionModifier : 1f);
            }
        }

        private void LogStateChange(InjectorCircuit circuit)
        {
            var flow = circuit.Flow;
            if (flow.State == circuit.LastLoggedState)
                return;
            var from = circuit.LastLoggedState;
            circuit.LastLoggedState = flow.State;
#if !PUBLIC_RELEASE
            if (!Main.Settings.LogCoalDeliveries)
                return;
            var name = string.IsNullOrEmpty(circuit.Name) ? "injector" : circuit.Name + " injector";
            Main.Mod.Logger.Log($"{Car?.ID}: {name} {from} -> {flow.State} at {BoilerPressure:F1} bar, lever {circuit.Control.Value:F2}, " +
                                $"feed {flow.WaterIn:F2} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s, " +
                                $"overflow {flow.Overflow:F2} kg/s water, {flow.OverflowSteam:F2} kg/s steam, " +
                                $"injector {flow.InjectorTemperature:F0} C (takes below {flow.RestartLimit:F0} C)");
#endif
        }
    }
}
