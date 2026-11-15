namespace RealisticFiring
{
    /// <summary>
    /// The settings that shape a loco's sim, controls, and fittings, frozen
    /// when the game loads. They need a restart: read live, a change made
    /// mid-session would build new locos differently from those already
    /// built (audit 2026-10-10). The multiplayer handshake compares these.
    /// </summary>
    internal sealed class Session
    {
        public bool RealisticFireboxes { get; private set; }
        public bool RealisticSmoke { get; private set; }
        public bool CustomSmokeEffects { get; private set; }
        public bool RealisticInjectors { get; private set; }
        public bool InjectorSteamValves { get; private set; }
        public bool FusiblePlugs { get; private set; }
        public bool RealisticOilBurners { get; private set; }
        public bool FitBoosters { get; private set; }
        public bool FitFeedPumps { get; private set; }
        /// <summary>Whether the stock S282 is built with the mod's stoker: unless its upgrade is never fitted. Whether it works is <see cref="Upgrades.Fittings.Stoker"/>.</summary>
        public bool RetrofitS282Stoker { get; private set; }
        public bool StokerJams { get; private set; }
        /// <summary>Whether the stock S282 is built with the feed pump upgrade: unless it is never fitted. Whether it works is <see cref="Upgrades.Fittings.FeedPump"/>.</summary>
        public bool S282FeedPump { get; private set; }
        /// <summary>Whether the stock S282 is built with the booster upgrade: unless it is never fitted. Whether it works is <see cref="Upgrades.Fittings.Booster"/>.</summary>
        public bool S282Booster { get; private set; }
        public bool UnlistedEngines { get; private set; }

        public static Session From(Settings settings) => new Session
        {
            RealisticFireboxes = settings.ReplaceStockFireboxes,
            RealisticSmoke = settings.ReplaceStockFireboxes && settings.RealisticSmoke,
            CustomSmokeEffects = settings.CustomSmokeEffects,
            RealisticInjectors = settings.RealisticInjectors,
            InjectorSteamValves = settings.RealisticInjectors && settings.SeparateInjectorSteamValve,
            FusiblePlugs = settings.FusiblePlugs,
            RealisticOilBurners = settings.RealisticOilBurners,
            FitBoosters = settings.FitBoosters,
            FitFeedPumps = settings.FitFeedPumps,
            RetrofitS282Stoker = settings.S282Stoker != Upgrades.UpgradeRule.NeverFitted,
            StokerJams = settings.StokerJams,
            S282Booster = settings.S282Booster != Upgrades.UpgradeRule.NeverFitted,
            S282FeedPump = settings.RealisticInjectors && settings.S282FeedPump != Upgrades.UpgradeRule.NeverFitted,
            UnlistedEngines = settings.UnlistedLocomotives,
        };
    }
}
