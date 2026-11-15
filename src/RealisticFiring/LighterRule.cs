using RealisticFiring.Core;

namespace RealisticFiring
{
    /// <summary>
    /// Whether the lighter lights a cold coal bed here: the player's setting,
    /// or a guest's host's (<see cref="SessionLighterRule"/>).
    /// </summary>
    internal static class LighterRule
    {
        private static readonly SessionLighterRule Rule = new SessionLighterRule();
        public static bool LightsCoal => Rule.LightsCoal(Net.InSession, Net.IsClient, Main.Settings.LighterLightsCoal);
        public static void Reset() => Rule.Reset();
        public static void Receive(bool lights) => Rule.Receive(lights);
    }
}
