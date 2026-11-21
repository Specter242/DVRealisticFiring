using System.Linq;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
    internal sealed partial class InjectorRig
    {
        private FeedPump pump;
        private bool pumpUpgrade;
        private int pumpFittedVersion = -1;
        private string pumpFittedGuid;
        private bool pumpFitted;
        private Port pumpValve;
        private Port exhaustFlow;
        private Port fullExhaustFlow;
        private Port chestPressure;
        private float pendingPumpSteam;
        private FeedPumpState lastPumpState;

        /// <summary>
        /// The feedwater heater and pump, on a loco whose prototype had one or
        /// that has bought one (the S282's upgrade); else null. An upgrade not
        /// yet bought is built but idle, and null here, so the cab, fireman,
        /// and overlay see no pump until it is fitted.
        /// </summary>
        public FeedPump Pump => pump != null && PumpFitted ? pump : null;

        // Whether the pump is on the engine now: always, but for an upgrade.
        private bool PumpFitted
        {
            get
            {
                if (!pumpUpgrade)
                    return true;
                var guid = Upgrades.Fittings.CarGuidOf(Car);
                if (pumpFittedVersion != Upgrades.Fittings.Version || pumpFittedGuid != guid)
                {
                    pumpFittedVersion = Upgrades.Fittings.Version;
                    pumpFittedGuid = guid;
                    pumpFitted = Upgrades.Fittings.HasFeedPump(Car);
                }
                return pumpFitted;
            }
        }

        /// <summary>Whether this rig carries a pump not yet bought (built, idle until fitted).</summary>
        internal bool PumpWaiting => pump != null && !PumpFitted;

        /// <summary>Its make and type.</summary>
        public string PumpName { get; private set; }

        /// <summary>What the pump did on the last tick.</summary>
        public FeedPumpFlow PumpFlow { get; internal set; }

        /// <summary>The pump's operating valve, 0 to 1.</summary>
        public float PumpValve => pumpValve != null ? pumpValve.Value : 0f;

        /// <summary>The feed pump's cab valve port, for the fireman's hand; null without a pump.</summary>
        internal Port PumpValvePort => pumpValve;

        // Fits the heater and pump when the loco's prototype had one and its
        // sim has the operating valve. Returns a line for the log, or null.
        private string FitPump(SimulationFlow flow)
        {
            if (Car == null || flow == null || !FeedwaterFit.TryGet(Car.carLivery.id, out var fit)
                || !flow.TryGetPort(FeedwaterFit.Valve.PortId, out var valve))
                return null;
            var engine = flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault();
            AttachPump(new FeedPump(new FeedPumpSpec(fit.Type, fit.Capacity), TenderWaterTemperature), fit.Name, valve,
                engine?.exhaustFlowReadOut, engine?.maxFlowReadOut, engine?.steamChestPressureReadOut);
            pumpUpgrade = fit.Upgrade;
            return $"{fit.Name} feedwater heater and pump: {pump.Spec.Capacity:F1} kg/s, worked by '{valve.id}'" +
                   (fit.Upgrade ? $", an upgrade {(PumpFitted ? "fitted" : "not yet fitted")}" : "") +
                   (engine == null ? "; no steam engine to heat it" : "");
        }

        /// <summary>Gives the rig a pump directly, for checks outside the game.</summary>
        internal void AttachPump(FeedPump pump, string name, Port valve, Port exhaust, Port fullExhaust, Port chest)
        {
            this.pump = pump;
            PumpName = name;
            pumpValve = valve;
            exhaustFlow = exhaust;
            fullExhaustFlow = fullExhaust;
            chestPressure = chest;
        }

        private void StepPump(WaterPressureVessel vessel, float delta, bool broken, float tender, ref float space, ref float drawn)
        {
            if (Pump != null)
            {
                var pumpFlow = Pump.Step(delta, new FeedPumpInputs
                {
                    Valve = broken ? 0f : PumpValve,
                    BoilerPressure = vessel.pressure,
                    BoilerSpace = space,
                    TenderWater = Mathf.Max(0f, tender - drawn),
                    TenderWaterTemperature = TenderWaterTemperature,
                    ExhaustFlow = exhaustFlow != null ? exhaustFlow.Value : 0f,
                    FullExhaustFlow = fullExhaustFlow != null ? fullExhaustFlow.Value : 0f,
                    ChestPressure = chestPressure != null ? chestPressure.Value : vessel.pressure,
                }, Steam);
                LogPumpChange(pumpFlow);
                PumpFlow = pumpFlow;
                var pumped = pumpFlow.WaterIn * delta;
                if (pumped > 0f)
                    Feed(vessel, pumped, pumpFlow.FeedTemperature);
                space = Mathf.Max(0f, space - pumped);
                drawn += pumpFlow.TenderDraw * delta;
                // A boiler's mass is too big for a float to take a few grams a tick.
                pendingPumpSteam += pumpFlow.SteamUsed * delta;
                if (pendingPumpSteam >= RealisticStoker.SteamLot)
                {
                    vessel.RemoveSteam(Mathf.Min(pendingPumpSteam, vessel.mass));
                    pendingPumpSteam = 0f;
                }
            }
            else if (PumpWaiting && !Net.IsClient)
                PumpFlow = default;
        }

        private void LogPumpChange(FeedPumpFlow flow)
        {
            if (flow.State == lastPumpState)
                return;
            var from = lastPumpState;
            lastPumpState = flow.State;
#if !PUBLIC_RELEASE
            if (Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{Car?.ID}: feed pump {from} -> {flow.State} at {BoilerPressure:F1} bar, valve {PumpValve:F2}, " +
                                    $"feed {flow.WaterIn:F2} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s, heater {flow.HeaterTemperature:F0} C");
#endif
        }
    }
}
