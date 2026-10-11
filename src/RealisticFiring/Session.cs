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
        public bool FusiblePlugs { get; private set; }
        public bool RealisticOilBurners { get; private set; }
        public bool FitBoosters { get; private set; }
        public bool FitFeedPumps { get; private set; }
        public bool RetrofitS282Stoker { get; private set; }
        public bool StokerJams { get; private set; }

        public static Session From(Settings settings) => new Session
        {
            RealisticFireboxes = settings.ReplaceStockFireboxes,
            RealisticSmoke = settings.ReplaceStockFireboxes && settings.RealisticSmoke,
            CustomSmokeEffects = settings.CustomSmokeEffects,
            RealisticInjectors = settings.RealisticInjectors,
            FusiblePlugs = settings.FusiblePlugs,
            RealisticOilBurners = settings.RealisticOilBurners,
            FitBoosters = settings.FitBoosters,
            FitFeedPumps = settings.FitFeedPumps,
            RetrofitS282Stoker = settings.RetrofitS282Stoker,
            StokerJams = settings.StokerJams,
        };
    }
}
