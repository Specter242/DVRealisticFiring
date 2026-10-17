using System.Runtime.CompilerServices;
using DV.Simulation.Brake;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class BailOffs
    {
        private static readonly ConditionalWeakTable<BrakeSystem, BailOff> ByBrakes = new ConditionalWeakTable<BrakeSystem, BailOff>();

        public static bool TryGet(BrakeSystem brakes, out BailOff bail) => ByBrakes.TryGetValue(brakes, out bail);

        /// <summary>Bails off <paramref name="car"/>'s own brakes from the train brake's application now on.</summary>
        public static void Bail(TrainCar car)
        {
            var brakes = car != null ? car.brakeSystem : null;
            if (brakes == null || !brakes.hasCompressor)
                return;
            ByBrakes.GetValue(brakes, _ => new BailOff()).Request();
        }

        /// <summary>What a bail just taken did, for the notice under the crosshair.</summary>
        public static string Notice(BailOff bail) => bail.Bailed > MinNoticed
            ? "Bailed off: the engine's brakes release, the train's stay on"
            : "Nothing to bail off: the train brake isn't applied";

        // Less than this, in bar, is nothing bailed.
        private const float MinNoticed = 0.05f;
    }
}
