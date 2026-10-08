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
        /// <summary>Stock liveries whose firebox the mod replaces.</summary>
        public static readonly IReadOnlyCollection<string> SupportedLiveries = new HashSet<string>
        {
            "LocoS282A",
            "LocoS060",
        };

        private static readonly Dictionary<TrainCar, RealisticFirebox> Fireboxes = new Dictionary<TrainCar, RealisticFirebox>();

        public static bool IsSupported(TrainCar car) =>
            car != null && car.carLivery != null && SupportedLiveries.Contains(car.carLivery.id);

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
        /// counts as being on the footplate.
        /// </summary>
        public static bool TryGetTended(TrainCar car, out RealisticFirebox firebox)
        {
            if (TryGet(car, out firebox))
                return true;
            if (car == null)
                return false;
            foreach (var coupler in new[] { car.frontCoupler, car.rearCoupler })
            {
                var neighbor = coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;
                if (TryGet(neighbor, out firebox))
                    return true;
            }
            return false;
        }

        private static void PruneDestroyedCars()
        {
            foreach (var car in Fireboxes.Keys.Where(car => car == null).ToList())
                Fireboxes.Remove(car);
        }
    }
}
