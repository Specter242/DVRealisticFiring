namespace RealisticFiring.Core
{
    /// <summary>Host-owned display policy, separate from a player's saved display preferences.</summary>
    public sealed class SessionHudPolicy
    {
        public const string Protocol = "session HUD 1";
        private bool received, hidden;

        // A new client waits for the host rather than showing locally enabled
        // helpers for the first few frames. Reset on every join and leave.
        public void Reset() { received = hidden = false; }
        public void Receive(bool hide) { hidden = hide; received = true; }

        /// <summary>Whether the host's rule has come: a guest that waited long enough shows its own.</summary>
        public bool Received => received;
        public bool Hidden(bool inSession, bool isClient, bool hostHides) =>
            inSession && (isClient ? !received || hidden : hostHides);
    }
}
