using System.Collections.Generic;
using System.Linq;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Replacement fireboxes by car, so controls and the overlay can find the
    /// one on the loco the player is in.
    /// </summary>
    internal static class FireboxRegistry
    {
        internal static readonly HashSet<string> BuiltInLiveries = new HashSet<string>(LocomotiveProfiles.CoalLiveries);

        /// <summary>
        /// Liveries whose coal firebox the mod replaces: the stock coal locos,
        /// built-in custom (CCL) locos, and any loaded coal maker-fit profiles.
        /// </summary>
        public static IReadOnlyCollection<string> SupportedLiveries => MakerFitRegistry.AllCoalLiveries(BuiltInLiveries);

        private static readonly Dictionary<TrainCar, RealisticFirebox> Fireboxes = new Dictionary<TrainCar, RealisticFirebox>();

        /// <summary>Every realistic firebox, by car.</summary>
        public static IEnumerable<RealisticFirebox> All => Fireboxes.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

        internal static bool IsSupported(string livery, string carType = null) =>
            livery != null && !LeftStock.IsExcluded(livery, carType)
            && (BuiltInLiveries.Contains(livery) || MakerFitRegistry.IsCoalLivery(livery));

        public static bool IsSupported(TrainCar car) =>
            car != null && car.carLivery != null && !LeftStock.IsExcluded(car)
            && IsSupported(car.carLivery.id, car.carLivery.parentType != null ? car.carLivery.parentType.id : null);

        public static void Register(RealisticFirebox firebox)
        {
            PruneDestroyedCars();
            Fireboxes[firebox.Car] = firebox;
        }

        public static bool TryGet(TrainCar car, out RealisticFirebox firebox)
        {
            firebox = null;
            // Unity's overloaded null check: destroyed cars compare equal to null.
            return car != null && Fireboxes.TryGetValue(car, out firebox);
        }

        /// <summary>
        /// The firebox the player tends from <paramref name="car"/>: its own,
        /// or that of a loco coupled to it, so standing on the S282's tender
        /// counts as being on the footplate; between two locos, the one the
        /// player was last in (<see cref="CarCouplings.Tended"/>).
        /// </summary>
        public static bool TryGetTended(TrainCar car, out RealisticFirebox firebox) =>
            TryGet(CarCouplings.Tended(car, Fireboxes), out firebox);

        private static void PruneDestroyedCars()
        {
            foreach (var car in Fireboxes.Keys.Where(car => car == null).ToList())
                Fireboxes.Remove(car);
        }
    }
}
