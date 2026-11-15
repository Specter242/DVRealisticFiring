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
            public Fit(FeedHeaterType type, string name, float capacity, bool onInjectorLever, bool upgrade = false)
            {
                Type = type;
                Name = name;
                Capacity = capacity;
                OnInjectorLever = onInjectorLever;
                Upgrade = upgrade;
            }

            /// <summary>
            /// Bought at a career manager (<see cref="Upgrades.Fittings.FeedPump"/>),
            /// not the prototype's own: built into the engine but idle until
            /// fitted, its valve and gauge on the backhead in a seat of their
            /// own, whether or not a stoker manifold is fitted beside them.
            /// </summary>
            public bool Upgrade { get; }

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
            // China Railways QJ: an open heater on part of the exhaust, its
            // mixer at the top front of the boiler (a Worthington SA copy, by
            // one account), and a piston hot-water pump under the smokebox
            // that the fireman works by its own valve; it ran at about 400
            // L/min in service (Schaefer). Its capacity not found: the SA's
            // 9,000 gal/h, above that and the boiler's tested 8 kg/s (inferred).
            ["S2102-870"] = new Fit(FeedHeaterType.Open, "open heater (Worthington SA type)", 9.46f, onInjectorLever: false),
            // Prussian T 20 as built: "links Kolbenspeisewasserpumpe Knorr,
            // Abdampfvorwärmer Knorr" (Hanomag-Lehrblatt 131, 1924), a surface
            // heater on the exhaust and a piston pump on the smokebox, set
            // from the cab by its own handwheel and gauge (Lehrblatt 081). Its
            // capacity not found: the injector's 250 L/min, as German practice
            // sized each feed for the whole boiler (inferred).
            ["S2102-720"] = new Fit(FeedHeaterType.Closed, "Knorr surface heater and pump", 4.17f, onInjectorLever: false),
            ["LocoS282A"] = new Fit(FeedHeaterType.Open, "Worthington SA", 6.94f, onInjectorLever: false, upgrade: true),
        };

        public static bool TryGet(string liveryId, out Fit fit)
        {
            fit = null;
            if (liveryId == null || !Fits.TryGetValue(liveryId, out var found))
                return false;
            if (!(found.Upgrade ? Main.Session.S282FeedPump : Main.Session.FitFeedPumps))
                return false;
            fit = found;
            return true;
        }
    }
}
