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
        /// <summary>
        /// Liveries whose firebox the mod replaces: the stock coal locos, and
        /// custom (CCL) locos under test for phase 2.
        /// </summary>
        public static readonly IReadOnlyCollection<string> SupportedLiveries = new HashSet<string>
        {
            "LocoS282A",
            "LocoS060",
            // LittleNiko36's NYC Dreyfuss Hudson (Nexus 1688; measured
            // 2026-10-08) and 4-8-4 (Nexus 1494), a UP FEF-3 in a made-up
            // road's livery ("Black Arrow", per its bundle).
            "S464-730",
            "S484-840A",
            // Cypress and LittleNiko36's UP FEF-3 No. 844 (Nexus 1590, 1.0.58),
            // read from its bundle and seated in game 2026-10-09.
            "FEF-3",
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
