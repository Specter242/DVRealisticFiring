namespace RealisticFiring.Sim
{
    /// <summary>The cars coupled to a car, found without garbage (a tended engine is looked for several times a frame).</summary>
    internal static class CarCouplings
    {
        /// <summary>The car on the other side of <paramref name="coupler"/>, or null.</summary>
        public static TrainCar CoupledCar(this Coupler coupler) =>
            coupler != null && coupler.coupledTo != null ? coupler.coupledTo.train : null;
    }
}
