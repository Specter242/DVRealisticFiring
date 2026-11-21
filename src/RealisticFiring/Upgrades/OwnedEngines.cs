using System;
using System.Collections.Generic;
using DV.LocoRestoration;
using DV.ServicePenalty;
using DV.Utils;
using RealisticFiring.Core;

namespace RealisticFiring.Upgrades
{
 internal static class OwnedEngines
    {
        /// <summary>The owned eligible locomotives now, each with its car GUID.</summary>
        internal static List<TrainCar> Cars()
        {
            var listed = new HashSet<TrainCar>();
            var owned = SingletonBehaviour<OwnedCarsStateController>.Instance;
            if (owned != null)
                foreach (var state in owned.existingOwnedCarStates)
                    if (state?.car != null)
                        listed.Add(state.car);
            var locos = SingletonBehaviour<CarSpawner>.Instance?.AllLocos;
            return Select(locos ?? new List<TrainCar>(), listed, Restored);
        }

        /// <summary>
        /// Of <paramref name="locos"/> and the game's owned list <paramref name="listed"/>, the eligible
        /// locomotives with a car GUID that the game shows as owned, or that <paramref name="restored"/> says
        /// the player has restored.
        /// </summary>
        internal static List<TrainCar> Select(IEnumerable<TrainCar> locos, ICollection<TrainCar> listed, Func<TrainCar, bool> restored)
        {
            var found = new List<TrainCar>();
            var seen = new HashSet<TrainCar>();
            foreach (var car in Both(locos, listed))
            {
                if (car == null || !seen.Add(car) || car.carLivery == null || !UpgradeKeys.IsEligibleLivery(car.carLivery.id)
                    || Fittings.CarGuidOf(car) == null)
                    continue;
                if ((listed.Contains(car) && !car.preventDebtDisplay) || (restored != null && restored(car)))
                    found.Add(car);
            }
            return found;
        }

        private static IEnumerable<TrainCar> Both(IEnumerable<TrainCar> locos, IEnumerable<TrainCar> listed)
        {
            foreach (var car in listed)
                yield return car;
            foreach (var car in locos)
                yield return car;
        }

        private static bool Restored(TrainCar car)
        {
            var restoration = LocoRestorationController.GetForTrainCar(car);
            return restoration != null && restoration.State >= LocoRestorationController.RestorationState.S9_LocoServiced;
        }
    }
}
