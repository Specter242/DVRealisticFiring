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

        /// <summary>The stock lever: the steam valve and feed together, or the water valve where <see cref="Steam"/> is fitted.</summary>
        public Port Control { get; }

        /// <summary>The three-position steam valve, where fitted (<see cref="InjectorValves"/>); null for the one lever.</summary>
        public Port Steam { get; internal set; }
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
