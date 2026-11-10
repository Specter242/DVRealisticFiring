using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
 internal static class LeftStock
    {
        private static readonly HashSet<string> Types = new HashSet<string> { "S040F", "S484-841A" };

        // Liveries the mod is set up for and was checked on without one of
        // its coal fireboxes (FireboxRegistry.SupportedLiveries): the Santa
        // Fe 2901's oil burner, and GWI's 40xx and Manor (2.0.0), whose
        // injector levers it was fitted to.
        private static readonly HashSet<string> Checked = new HashSet<string> { "A.T.&S.F. 2901-A", "GWI40xx", "GWI_Manor" };

        /// <summary>Whether the mod lists <paramref name="car"/>'s livery: set up for it, and checked on it.</summary>
        public static bool Listed(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            return livery != null && Listed(livery.id);
        }

        internal static bool Listed(string livery) =>
            livery != null && (FireboxRegistry.SupportedLiveries.Contains(livery) || Checked.Contains(livery));

        private static readonly object Mark = new object();
        private static readonly ConditionalWeakTable<ReciprocatingSteamEngine, object> Engines =
            new ConditionalWeakTable<ReciprocatingSteamEngine, object>();

        /// <summary>Whether the mod leaves <paramref name="car"/> as the game and its own mod made it.</summary>
        public static bool Is(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            return livery != null && Is(livery.id, livery.parentType != null ? livery.parentType.id : null, Main.Session.UnlistedEngines);
        }

        /// <summary>
        /// By name: the types left out stay out whatever the player asks;
        /// an unlisted livery is left alone unless <paramref name="unlistedToo"/>.
        /// </summary>
        internal static bool Is(string livery, string carType, bool unlistedToo) =>
            Types.Contains(livery) || carType != null && Types.Contains(carType) || !unlistedToo && !Listed(livery);

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
