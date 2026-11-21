using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;

namespace RealisticFiring.Sim
{
 internal static class LeftStock
    {
        private static readonly HashSet<string> Types = new HashSet<string> { "S040F", "S484-841A" };

        internal static IReadOnlyCollection<string> ExcludedTypes => Types;

        internal static IReadOnlyCollection<string> BuiltInChecked => LocomotiveProfiles.ListedLiveries;

        /// <summary>Whether <paramref name="livery"/> or its <paramref name="carType"/> is one of the excluded stock types.</summary>
        internal static bool IsExcluded(string livery, string carType = null) =>
            (livery != null && Types.Contains(livery)) || (carType != null && Types.Contains(carType));

        /// <summary>Whether <paramref name="car"/> is an excluded stock type (by livery ID or parent car type).</summary>
        public static bool IsExcluded(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            return livery != null && IsExcluded(livery.id, livery.parentType != null ? livery.parentType.id : null);
        }

        /// <summary>Whether the mod lists <paramref name="car"/>'s livery: set up for it, and checked on it.</summary>
        public static bool Listed(TrainCar car)
        {
            var livery = car != null ? car.carLivery : null;
            return livery != null && !IsExcluded(car) && Listed(livery.id);
        }

        internal static bool Listed(string livery) =>
            livery != null && (LocomotiveProfiles.Contains(livery) || MakerFitRegistry.HasLivery(livery));

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
            IsExcluded(livery, carType) || !unlistedToo && !Listed(livery);

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
