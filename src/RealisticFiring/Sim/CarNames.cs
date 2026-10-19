namespace RealisticFiring.Sim
{
    /// <summary>
    /// A car's name for the log. The game numbers a car (its logic car's ID)
    /// only after building its sim graph, and reading <c>TrainCar.ID</c>
    /// before then logs an error ("logicCar not set, but ID accessed!"), which
    /// players' logs showed twice for every steam loco spawned (2026-10-19).
    /// Until it is numbered, a car goes by its livery.
    /// </summary>
    internal static class CarNames
    {
        public static string LogName(this TrainCar car) =>
            car == null ? ""
            : car.logicCar != null ? car.logicCar.ID
            : car.carLivery != null ? car.carLivery.id
            : car.name;
    }
}
