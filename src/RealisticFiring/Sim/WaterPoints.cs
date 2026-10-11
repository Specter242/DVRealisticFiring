using System.Linq;
using DV.ThingTypes;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class WaterPoints
    {
        /// <summary>How players are told where these are.</summary>
        public const string Where = "a service track (a service station with water, a loco parking track, or a roundhouse stall by a turntable)";

        // A roundhouse's stalls radiate from its turntable: within this of its
        // middle, in m, an engine stands in one.
        private const float RoundhouseReach = 50f;

        // The game's locomotive parking tracks end in this type letter.
        private const string ParkingType = "P";

        public static bool Serves(TrainCar car) => car != null && (AtServiceStation(car) || OnParkingTrack(car) || ByTurntable(car));

        /// <summary>Whether the engine stands in a service station with water, where the game refills and repairs.</summary>
        public static bool AtServiceStation(TrainCar car)
        {
            if (car == null)
                return false;
            foreach (var station in Object.FindObjectsOfType<PitStopStation>())
            {
                var pitStop = station.pitstop;
                if (pitStop == null || !pitStop.IsCarInPitStop())
                    continue;
                var modules = station.locoResourceModules != null ? station.locoResourceModules.resourceModules : null;
                if (modules == null || !modules.Any(m => m != null && m.resourceType == ResourceType.Water))
                    continue;
                if (pitStop.CurrentCar == car || (pitStop.CurrentSet != null && pitStop.CurrentSet.cars.Contains(car)))
                    return true;
            }
            return false;
        }

        private static bool OnParkingTrack(TrainCar car)
        {
            var id = car.logicCar != null && car.logicCar.CurrentTrack != null ? car.logicCar.CurrentTrack.ID : null;
            if (id == null || id.IsGeneric())
                return false;
            var parts = id.FullID.Split('-');
            return parts.Length > 0 && parts[parts.Length - 1] == ParkingType;
        }

        private static bool ByTurntable(TrainCar car)
        {
            var turntable = TurntableController.FindClosestTo(car.transform.position);
            return turntable != null && Vector3.Distance(turntable.transform.position, car.transform.position) <= RoundhouseReach;
        }
    }
}
