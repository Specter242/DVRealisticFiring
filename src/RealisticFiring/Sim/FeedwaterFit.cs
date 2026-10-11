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
            public Fit(FeedHeaterType type, string name, float capacity, bool onInjectorLever)
            {
                Type = type;
                Name = name;
                Capacity = capacity;
                OnInjectorLever = onInjectorLever;
            }

            public FeedHeaterType Type { get; }

            /// <summary>Its make and type, for the log and overlay.</summary>
            public string Name { get; }

            /// <summary>Its pump's rated capacity, in kg/s (US gal/h × 3.785 / 3600).</summary>
            public float Capacity { get; }

            /// <summary>
            /// The fireman's injector lever works the pump instead (a loco with
            /// an injector lever each side): on the NYC the fireman ran the pump
            /// and the engineman the injector.
            /// </summary>
            public bool OnInjectorLever { get; }
        }

        private static readonly Dictionary<string, Fit> Fits = new Dictionary<string, Fit>
        {
            // NYC J-3a: "Elesco type K40 feedwater heater" (RME May 1938
            // p.167), its pump on the left, the injector on the right.
            ["S464-730"] = new Fit(FeedHeaterType.Closed, "Elesco K40", 8.4f, onInjectorLever: true),
            // UP 844 as preserved: a Worthington 5-SA from 1953, replacing
            // its exhaust steam injector.
            ["FEF-3"] = new Fit(FeedHeaterType.Open, "Worthington 5-SA", 9.46f, onInjectorLever: false),
            // An FEF-3 on a made-up road ("Black Arrow"): a Worthington SA, as the 844's.
            ["S484-840A"] = new Fit(FeedHeaterType.Open, "Worthington SA", 9.46f, onInjectorLever: false),
            // Santa Fe 2900 class: "a Worthington Type 6-SA feed water heater"
            // (No. 2921). The cab has an injector handwheel each side; as on
            // the NYC, the fireman's works the pump and the engineman's the
            // injector.
            ["A.T.&S.F. 2901-A"] = new Fit(FeedHeaterType.Open, "Worthington 6-SA", 12.6f, onInjectorLever: true),
        };

        public static bool TryGet(string liveryId, out Fit fit)
        {
            fit = null;
            return Main.Session.FitFeedPumps && liveryId != null && Fits.TryGetValue(liveryId, out fit);
        }
    }
}
