using System.Linq;
using DV.ThingTypes;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// Where a boiler can be filled from the mains: the game has no separate
    /// water towers, so every water refill point is a pit stop with a water
    /// module, and a pit stop knows which car (and train) stands in it.
    /// </summary>
    internal static class WaterPoints
    {
        public static bool Serves(TrainCar car)
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
    }
}
