using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>The cars coupled to a car, found without garbage (a tended engine is looked for several times a frame).</summary>
    internal static class CarCouplings
    {
        /// <summary>The car on the other side of <paramref name="coupler"/>, or null.</summary>
        public static TrainCar CoupledCar(this Coupler coupler) =>
            coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;

        /// <summary>
        /// The car whose engine the player tends from <paramref name="car"/>,
        /// of those <paramref name="registered"/> holds (<see cref="TendedEngine"/>):
        /// the car itself, or one coupled to it, preferring the engine the
        /// player was last in between two. Null when there is none.
        /// </summary>
        public static TrainCar Tended<T>(TrainCar car, Dictionary<TrainCar, T> registered)
        {
            if (car == null)
                return null;
            return TendedEngine.Pick(Held(car, registered), Held(car.frontCoupler.CoupledCar(), registered),
                Held(car.rearCoupler.CoupledCar(), registered), Held(PlayerManager.LastLoco, registered));
        }

        // The car when it is live and held, else a real null: Unity's == calls
        // a deleted car null, but the Core rule's checks don't.
        private static TrainCar Held<T>(TrainCar car, Dictionary<TrainCar, T> registered) =>
            car != null && registered.ContainsKey(car) ? car : null;
    }
}
