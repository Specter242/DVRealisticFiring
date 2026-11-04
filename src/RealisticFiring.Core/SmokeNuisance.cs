using System;
using RealisticStackEffects.Core;

namespace RealisticFiring.Core
{
    public enum SmokeDistrict { None, HarborTown, CityWest, CitySouth, MachineFactoryTown, FoodFactoryTown }

    public struct SmokeNuisanceState
    {
        public SmokeDistrict District;
        public double SmokeSeconds;
        public double AwaySeconds;
        public double Charged;
        public bool Warned;
    }

    public struct SmokeNuisanceFlow
    {
        public double Fee;
        public bool Warning;
        public bool FirstCharge;
    }

    /// <summary>Sustained visible smoke in a town, in real simulation seconds and base-price dollars.</summary>
    public sealed class SmokeNuisance
    {
        public const float HeavySoot = .65f;
        public const double WarningSeconds = 60;
        public const double GraceSeconds = 180;
        public const double DollarsPerMinute = 100;
        public const double VisitCap = 500;
        public const double LeaveSeconds = 600;
        public SmokeNuisanceState State { get; private set; }

        /// <summary>
        /// Tar soot relative to the shared black-smoke reference, before
        /// weather, white steam or graphics settings. Bulk firebox gas can
        /// contain invisible CO and must not itself incur a smoke fee.
        /// </summary>
        public static float CoalSmoke(FireModel fire) => fire.SootDensity / PlumeModel.SootForBlack;

        public static float OilSmoke(OilFire fire) => OilPlume.Stack(fire.StackSmoke).Soot / PlumeModel.SootForBlack;

        public SmokeNuisanceFlow Step(double seconds, SmokeDistrict district, float soot, bool burning, bool exempt)
        {
            if (!(seconds > 0) || !Finite(seconds)) return default;
            if (!Town(district)) district = SmokeDistrict.None;
            var state = State;
            if (district == SmokeDistrict.None)
            {
                if (state.District == SmokeDistrict.None) return default;
                state.SmokeSeconds = Math.Max(0, state.SmokeSeconds - 2 * seconds);
                state.AwaySeconds = Math.Min(LeaveSeconds, state.AwaySeconds + seconds);
                State = state.AwaySeconds >= LeaveSeconds ? default : state;
                return default;
            }
            if (state.District != district) state = new SmokeNuisanceState { District = district };
            state.AwaySeconds = 0;
            if (!burning || exempt || !Finite(soot) || soot < HeavySoot)
            {
                state.SmokeSeconds = Math.Max(0, state.SmokeSeconds - 2 * seconds);
                State = state;
                return default;
            }

            var before = state.SmokeSeconds;
            state.SmokeSeconds = Math.Min(GraceSeconds + VisitCap * 60 / DollarsPerMinute, before + seconds);
            var warning = !state.Warned && state.SmokeSeconds >= WarningSeconds;
            state.Warned |= warning;
            // Whole-dollar additions avoid losing tiny per-frame increments in
            // the game's single-precision cumulative environmental meter. A
            // clean spell forgives any unfinished fraction of a dollar.
            var bill = DollarsAfterGrace(state.SmokeSeconds) - DollarsAfterGrace(before);
            var fee = Math.Min(VisitCap - state.Charged, bill);
            var firstCharge = fee > 0 && state.Charged == 0;
            state.Charged += fee;
            State = state;
            return new SmokeNuisanceFlow { Fee = fee, Warning = warning, FirstCharge = firstCharge };
        }

        private static double DollarsAfterGrace(double seconds) =>
            Math.Floor(Math.Max(0, seconds - GraceSeconds) * DollarsPerMinute / 60 + 1e-7);

        private static bool Town(SmokeDistrict district) => district >= SmokeDistrict.HarborTown
            && district <= SmokeDistrict.FoodFactoryTown;
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public void Restore(SmokeNuisanceState state)
        {
            if (!Town(state.District) || !Finite(state.SmokeSeconds) || !Finite(state.AwaySeconds)
                || !Finite(state.Charged) || state.SmokeSeconds < 0 || state.AwaySeconds < 0
                || state.Charged < 0 || state.Charged > VisitCap || state.AwaySeconds >= LeaveSeconds
                || state.SmokeSeconds > GraceSeconds + VisitCap * 60 / DollarsPerMinute)
            {
                State = default;
                return;
            }
            state.Warned |= state.SmokeSeconds >= WarningSeconds || state.Charged > 0;
            State = state;
        }
    }
}
