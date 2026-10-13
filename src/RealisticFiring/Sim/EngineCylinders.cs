using System.Linq;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal sealed class EngineCylinders
    {
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, EngineCylinders> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, EngineCylinders>();

        /// <summary>How the cylinders are built: one long-lap valve gear for every engine, sized by its bore.</summary>
        public static readonly CylinderSpec Spec = new CylinderSpec();

        private EngineCylinders(ReciprocatingSteamEngine engine)
        {
            Model = new SteamCylinders(Spec, engine.cylinderBore, engine.pistonStroke, engine.throttleMaxFlow);
        }

        public static bool Enabled => Main.Settings != null && Main.Settings.RealisticCylinders;

        /// <summary>Whether the model works <paramref name="engine"/>: the setting is on and its loco is not left stock.</summary>
        public static bool Works(ReciprocatingSteamEngine engine) => Enabled && engine != null && !LeftStock.Kept(engine);

        public SteamCylinders Model { get; }

        /// <summary>This tick's indicator diagram at the pressure acting on the pistons.</summary>
        public CylinderCycle Cycle { get; internal set; }

        /// <summary>Steam admitted a stroke this tick, in kg.</summary>
        public float Admitted { get; internal set; }

        /// <summary>Of that, steam the cylinder walls condensed, in kg.</summary>
        public float Condensed { get; internal set; }

        public static EngineCylinders Of(ReciprocatingSteamEngine engine) =>
            Engines.GetValue(engine, e => new EngineCylinders(e));

        public static bool TryGet(ReciprocatingSteamEngine engine, out EngineCylinders cylinders) =>
            Engines.TryGetValue(engine, out cylinders);

        /// <summary>The engine of <paramref name="car"/> and its cylinders, once the model has worked them.</summary>
        public static bool TryGet(TrainCar car, out ReciprocatingSteamEngine engine, out EngineCylinders cylinders)
        {
            cylinders = null;
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            engine = flow != null ? flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault() : null;
            return Works(engine) && TryGet(engine, out cylinders);
        }

        // The game's steam table, as its steam chest uses it: L/kg from absolute bar, and back.
        internal static float SteamVolume(float pressure) => SteamTables.SteamSpecificVolume(pressure);
        internal static float SteamPressure(float volume) => SteamTables.SteamSpecificVolumeToPressure(volume);
    }
}
