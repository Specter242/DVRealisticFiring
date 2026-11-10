using RealisticFiring.Core;

namespace RealisticFiring
{
    /// <summary>The current session's restriction; never changes local display preferences.</summary>
    internal static class HudPolicy
    {
        private static readonly SessionHudPolicy Policy = new SessionHudPolicy();
        public static bool Hidden => Policy.Hidden(Net.InSession, Net.IsClient, Main.Settings.HostHidesHud);
        public static void Reset() => Policy.Reset();
        public static void Receive(bool hide) => Policy.Receive(hide);
        public static bool Received => Policy.Received;
    }
}
