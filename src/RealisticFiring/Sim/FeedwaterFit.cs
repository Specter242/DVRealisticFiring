using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The locos fitted with a feedwater heater and pump, as their
    /// prototypes were, and the pump's operating valve. The live-steam
    /// injector stays as the second feed. Per-locomotive fits live in
    /// <see cref="LocomotiveProfiles"/>.
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

        public static bool TryGet(string liveryId, out Fit fit)
        {
            fit = null;
            Fit found;
            if (LocomotiveProfiles.TryGet(liveryId, out var profile) && profile.Feedwater != null)
                found = profile.Feedwater;
            else if (!MakerFitRegistry.TryGetFeedPump(liveryId, out found))
                return false;
            if (!(found.Upgrade ? Main.Session.S282FeedPump : Main.Session.FitFeedPumps))
                return false;
            fit = found;
            return true;
        }
    }
}
