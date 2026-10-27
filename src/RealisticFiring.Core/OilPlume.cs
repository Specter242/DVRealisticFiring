using System;
using RealisticStackEffects.Core;

namespace RealisticFiring.Core
{
    /// <summary>An oil fire's stack as the chimney plume takes it.</summary>
    public static class OilPlume
    {
        /// <summary>
        /// An oil fire's stack (<see cref="OilFire.StackSmoke"/>: clear, a
        /// well-set fire's haze at 0.2, black at 1) as the plume's soot and
        /// clear stack: the haze is a clean fire's, clearing below it with too
        /// much air, and darker the plume darkens as a coal fire's soot does.
        /// </summary>
        public static (float Soot, float ClearStack) Stack(float stackDarkness)
        {
            var dark = Clamp01((stackDarkness - OilFire.HazeSmoke) / (1f - OilFire.HazeSmoke));
            return (PlumeModel.SootForBlack * (float)Math.Pow(dark, 1.0 / 1.3), 1f - Clamp01(stackDarkness / OilFire.HazeSmoke));
        }

        /// <summary>The plume's inputs for an oil fire's stack, its beats carrying its haze as a coal fire's do.</summary>
        public static PlumeInputs Inputs(PlumeTuning tuning, float stackDarkness, float exhaustPressure, float totalFlowNormalized, bool fireOn)
        {
            var (soot, clear) = Stack(stackDarkness);
            return new PlumeInputs
            {
                ExhaustPressure = exhaustPressure,
                TotalFlowNormalized = totalFlowNormalized,
                Soot = soot,
                FireOn = fireOn,
                ClearStack = clear,
                PuffHaze = PlumeModel.WellFiredHaze(tuning, clear),
            };
        }

        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));
    }
}
