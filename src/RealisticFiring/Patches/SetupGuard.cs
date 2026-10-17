using System;

namespace RealisticFiring.Patches
{
    /// <summary>
    /// Runs one step of a car's setup so that a failure is logged with the
    /// step and the car, and the car loads without it: one fitting that
    /// breaks (another mod, a game or loader update) cannot stop the car
    /// loading or take the steps after it down (a player's S282 came up with
    /// no fireman and the 2901 without its controls, beside a run of
    /// loader failures, 2026-10-16).
    /// </summary>
    internal static class SetupGuard
    {
        public static void Run(TrainCar car, string what, Action step)
        {
            try
            {
                step();
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.LogException($"{Describe(car)}: setting up the {what} failed; the car goes on without it", e);
            }
        }

        /// <summary>Runs a step that returns a line for the log, logged when the setting asks.</summary>
        public static void Logged(TrainCar car, string what, Func<string> step) => Run(car, what, () =>
        {
            var line = step();
            if (line != null && Main.Settings.LogFireboxCreation)
                Main.Mod?.Logger.Log($"{Describe(car)}: {line}");
        });

        private static string Describe(TrainCar car) => car != null && car.carLivery != null ? car.carLivery.id : "a car";
    }
}
