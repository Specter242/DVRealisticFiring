using System;
using System.Globalization;

namespace RealisticFiring.Core
{
    /// <summary>
    /// The booster's row on the Engine page (F6 and the tablet): where the
    /// latch is and what the booster is doing, in the states the cab latch's
    /// hover names, short enough for the row. In gear it gives its pull and
    /// any warning, out of gear how warm it is.
    /// </summary>
    public static class BoosterReadout
    {
        /// <summary>Newtons in a pound-force.</summary>
        public const float NewtonsPerPound = 4.44822f;

        /// <param name="flow">What it did on the last tick.</param>
        /// <param name="latched">Whether the cab's latch is up.</param>
        /// <param name="warmth">How warm its cylinders are, 0 cold to 1 warm.</param>
        public static string Row(BoosterFlow flow, bool latched, float warmth)
        {
            switch (flow.State)
            {
                case BoosterState.Working:
                    return "up, working · " + Pull(flow.Force) + Warning(flow);
                case BoosterState.CocksOpen:
                    return "up, cocks open · " + Pull(flow.Force) + Warning(flow);
                case BoosterState.Idling:
                    return "down, idling · " + Warm(warmth);
                default:
                    // The latch is up only for the tick it takes to drop again.
                    return (latched ? "up, out of gear · " : "down, cut out · ") + Warm(warmth);
            }
        }

        /// <summary>Whether a warning wants the crew now (overspeed, backing in gear).</summary>
        public static bool Urgent(BoosterFlow flow) => flow.Overspeed || flow.Backing;

        /// <summary>Whether something wants watching (slipping, priming).</summary>
        public static bool Watch(BoosterFlow flow) => flow.Slipping || flow.Priming;

        // The latch's hover's warnings, the most pressing, cut to a word.
        private static string Warning(BoosterFlow flow) =>
            flow.Overspeed ? " · OVERSPEED"
            : flow.Backing ? " · BACKING"
            : flow.Slipping ? " · slipping"
            : flow.Priming ? " · priming"
            : "";

        private static string Pull(float newtons) =>
            Math.Max(0f, newtons / NewtonsPerPound).ToString("#,0", CultureInfo.InvariantCulture) + " lb";

        private static string Warm(float warmth) =>
            "warm " + ((int)Math.Round(Math.Max(0f, Math.Min(1f, warmth)) * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
    }
}
