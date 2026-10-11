using LocoSim.Implementations;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>One steam valve and injector, with its own jet and body temperature.</summary>
    internal sealed class InjectorCircuit
    {
        public InjectorCircuit(Port control, Injector injector, string name, Port workingReadout = null)
        {
            Control = control;
            Injector = injector;
            Name = name;
            this.workingReadout = workingReadout;
        }

        public Port Control { get; }
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
