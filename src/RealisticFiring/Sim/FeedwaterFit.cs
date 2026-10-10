using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The locos fitted with a feedwater heater and pump, as their
    /// prototypes were, and the pump's operating valve. The live-steam
    /// injector stays as the second feed.
    /// </summary>
    internal static class FeedwaterFit
    {
        /// <summary>The pump's operating valve: shut 0, wide open 1.</summary>
        public static readonly StokerJetControls.Valve Valve = new StokerJetControls.Valve("rfFeedPump", "Feed pump", null, 0f);

        internal sealed class Fit
        {
            public Fit(FeedHeaterType type, string name, float capacityShare, bool onInjectorLever)
            {
                Type = type;
                Name = name;
                CapacityShare = capacityShare;
                OnInjectorLever = onInjectorLever;
            }

            public FeedHeaterType Type { get; }

            /// <summary>Its make and type, for the log and overlay.</summary>
            public string Name { get; }

            /// <summary>Its capacity as a share of the loco's injector: a 9,000 gal/h 5-SA beside a 10,000 gal/h injector.</summary>
            public float CapacityShare { get; }

            /// <summary>
            /// The fireman's injector lever works the pump instead (a loco with
            /// an injector lever each side): on the NYC the fireman ran the pump
            /// and the engineman the injector.
            /// </summary>
            public bool OnInjectorLever { get; }
        }

        private static readonly Dictionary<string, Fit> Fits = new Dictionary<string, Fit>
        {
            // NYC J-3a: "Elesco type K40 feedwater heater", a centrifugal
            // turbine pump on the left, the injector on the right.
            ["S464-730"] = new Fit(FeedHeaterType.Closed, "Elesco K40", 0.9f, onInjectorLever: true),
            // UP 844 as preserved: a Worthington 5-SA from the early 1950s.
            ["FEF-3"] = new Fit(FeedHeaterType.Open, "Worthington 5-SA", 0.9f, onInjectorLever: false),
            // An FEF-3 on a made-up road ("Black Arrow"): a Worthington SA.
            ["S484-840A"] = new Fit(FeedHeaterType.Open, "Worthington SA", 0.9f, onInjectorLever: false),
        };

        public static bool TryGet(string liveryId, out Fit fit)
        {
            fit = null;
            return Main.Settings.FitFeedPumps && liveryId != null && Fits.TryGetValue(liveryId, out fit);
        }
    }
}
