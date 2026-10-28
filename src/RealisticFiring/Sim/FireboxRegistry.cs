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
        /// Liveries whose coal firebox the mod replaces: the stock coal locos
        /// and the custom (CCL) locos with compatible controls and interiors.
        /// </summary>
        public static readonly IReadOnlyCollection<string> SupportedLiveries = new HashSet<string>
        {
            "LocoS282A",
            "LocoS060",
            // Lilla and Cruzer's hand-fired JK3 (Nexus 1334, 1.0.1), read
            // from its release bundle 2026-10-10. Jk3B is its tender.
            "Jk3A",
            // LittleNiko36's NYC Dreyfuss Hudson (Nexus 1688; measured
            // 2026-10-08) and 4-8-4 (Nexus 1494), a UP FEF-3 in a made-up
            // road's livery ("Black Arrow", per its bundle).
            "S464-730",
            "S484-840A",
            // Cypress and LittleNiko36's UP FEF-3 No. 844 (Nexus 1590, 1.0.58),
            // read from its bundle and seated in game 2026-10-09.
            "FEF-3",
            // Emerald's LMS 8F (Nexus 1360, 1.2.1), read from its bundle
            // 2026-10-14; the WD Photographic scheme is a paint, not a livery.
            "em_lms_8f_loc_black",
            "em_lms_8f_loc_grey",
        };

        private static readonly Dictionary<TrainCar, RealisticFirebox> Fireboxes = new Dictionary<TrainCar, RealisticFirebox>();

        /// <summary>Every realistic firebox, by car.</summary>
        public static IEnumerable<RealisticFirebox> All => Fireboxes.Where(pair => pair.Key != null).Select(pair => pair.Value).ToList();

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
