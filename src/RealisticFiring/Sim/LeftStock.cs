using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
 internal static class LeftStock
    {
        private static readonly HashSet<string> Types = new HashSet<string> { "S040F" };
        private static readonly object Mark = new object();
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, object> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, object>();

        /// <summary>Whether the mod leaves <paramref name="car"/> as the game and its own mod made it.</summary>
        public static bool Is(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            return livery != null && (Types.Contains(livery.id) || livery.parentType != null && Types.Contains(livery.parentType.id));
        }

        /// <summary>Marks a stock loco's engines, so the realistic cylinders pass them by.</summary>
        public static void Keep(SimulationFlow flow)
        {
            if (flow == null)
                return;
            foreach (var engine in flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>())
                Keep(engine);
        }

        internal static void Keep(ReciprocatingSteamEngine engine) => Engines.GetValue(engine, _ => Mark);

        public static bool Kept(ReciprocatingSteamEngine engine) => engine != null && Engines.TryGetValue(engine, out _);
    }
}
