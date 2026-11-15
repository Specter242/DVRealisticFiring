namespace RealisticFiring.Core
{
    /// <summary>
    /// The stock lighter lighting a cold coal bed straight away, as the stock
    /// game does, with no wood or rag: a setting, off by default (kadengamesj,
    /// DV Discord, 2026-11-14: "i'd rather just have the lighter light the
    /// coal like vanilla", the rest of the mod kept). Since 1.18 the lighter
    /// lights only the rag; the wood and rag still light a fire either way.
    /// </summary>
    public static class LighterLighting
    {
        /// <summary>
        /// Water at the grate over this keeps the fire from lighting, as the
        /// stock firebox's ignition takes it (its intake water content, 0 to 1).
        /// </summary>
        public const float WetGrate = 0.5f;

        /// <summary>
        /// Whether the lighter's ignition request lights this fire: with the
        /// setting on, coal laid on the grate (the stock lighter's own test is
        /// coal in the box), the grate no wetter than the stock firebox
        /// lights, and the fire out. A burning fire, wood alone, or a drowned
        /// grate is left as it is.
        /// </summary>
        public static bool LightsCoal(bool enabled, float coal, float intakeWater, bool fireOn) =>
            enabled && coal > 0f && intakeWater <= WetGrate && !fireOn;
    }

    /// <summary>
    /// Whose lighter setting a session plays by: the host's, which it sends
    /// its guests with its display rule; alone or hosting, the player's own.
    /// A guest whose host's rule has not come plays by the mod's default,
    /// the wood and rag: the host's sim decides what lights.
    /// </summary>
    public sealed class SessionLighterRule
    {
        private bool received, lightsCoal;

        public void Reset() { received = lightsCoal = false; }
        public void Receive(bool lights) { lightsCoal = lights; received = true; }

        public bool Received => received;

        public bool LightsCoal(bool inSession, bool isClient, bool own) =>
            inSession && isClient ? received && lightsCoal : own;
    }
}
