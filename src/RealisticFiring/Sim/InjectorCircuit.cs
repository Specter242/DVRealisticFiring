using LocoSim.Implementations;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>One injector and its cab valves, with its own jet and body temperature.</summary>
    internal sealed class InjectorCircuit
    {
        public InjectorCircuit(Port control, Injector injector, string name, Port workingReadout = null)
        {
            Control = control;
            Injector = injector;
            Name = name;
            this.workingReadout = workingReadout;
        }

        /// <summary>
        /// The stock lever: the steam valve and feed together, or with
        /// <see cref="Added"/>, the water valve (or the steam valve where
        /// <see cref="OwnLeverIsSteam"/>).
        /// </summary>
        public Port Control { get; }

        /// <summary>The valve the mod adds (<see cref="InjectorValves"/>); null for the one lever.</summary>
        public Port Added { get; internal set; }

        /// <summary>Whether the stock lever is the steam valve and <see cref="Added"/> the water regulator (the LMS 8F).</summary>
        public bool OwnLeverIsSteam { get; internal set; }

        /// <summary>The steam valve, where the injector has two; null for the one lever.</summary>
        public Port SteamValve => Added == null ? null : OwnLeverIsSteam ? Control : Added;

        /// <summary>The water valve, where the injector has two; null for the one lever.</summary>
        public Port WaterValve => Added == null ? null : OwnLeverIsSteam ? Added : Control;

        /// <summary>The valve that sets the feed: the water valve, or the one lever.</summary>
        public Port Feed => WaterValve ?? Control;

        /// <summary>
        /// The coupled tender's water valve to this injector, where the tender
        /// has one (<see cref="InjectorValves.TenderWater"/>); null otherwise.
        /// </summary>
        public Port TenderValve { get; internal set; }

        /// <summary>Whether the tender's water valve to this injector is shut.</summary>
        public bool TenderValveShut => TenderValve != null && TenderValve.Value <= InjectorSpec.OpenThreshold;

        /// <summary>
        /// The water the injector's own valve lets through, as far as the
        /// tender's valve lets water to it; null for the one lever.
        /// </summary>
        public float? WaterInput => WaterValve == null ? (float?)null
            : TenderValve == null ? WaterValve.Value : System.Math.Min(WaterValve.Value, TenderValve.Value);
        public Injector Injector { get; }
        public string Name { get; }
        private readonly Port workingReadout;
        private InjectorFlow flow;
        public InjectorFlow Flow
        {
            get => flow;
            internal set
            {
                flow = value;
                // JK3's stock particle calculations run before the boiler.
                // Their final readouts must follow this injector, otherwise
                // a failed valve appears to feed when the other jet works.
                if (workingReadout != null)
                    workingReadout.Value = value.WaterIn > 0f ? Control.Value : 0f;
            }
        }
        internal InjectorState LastLoggedState { get; set; }
    }
}
