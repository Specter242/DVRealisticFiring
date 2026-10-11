using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class InjectorValves
    {
        public static readonly StokerJetControls.Valve Steam = new StokerJetControls.Valve("rfInjectorSteam", "Injector steam", null, 0f);
        public static readonly StokerJetControls.Valve SteamLeft = new StokerJetControls.Valve("rfInjectorSteam2", "Left injector steam", null, 0f);

        /// <summary>The steam lever's notches, at 0, <see cref="InjectorSpec.SteamPrime"/>, and 1.</summary>
        public static readonly string[] Notches = { "Off", "Prime", "Feed" };

        private static readonly HashSet<string> Fitted = new HashSet<string>
        {
            "LocoS282A", "LocoS060", "Jk3A", "S464-730", "FEF-3", "S484-840A", "A.T.&S.F. 2901-A",
        };

        // Lifting injectors, above the tender's water: the JK3's, as Finnish
        // engines carried (Friedmann). The American engines' are non-lifters.
        private static readonly HashSet<string> Lifters = new HashSet<string> { "Jk3A" };

        /// <summary>Whether this session fits <paramref name="livery"/> with steam valves.</summary>
        public static bool Fits(string livery) => livery != null && Main.Session.InjectorSteamValves && Fitted.Contains(livery);

        public static bool Lifting(string livery) => livery != null && Lifters.Contains(livery);

        /// <summary>How many injectors <paramref name="livery"/> has, and so steam valves.</summary>
        public static int Count(string livery) => livery == "Jk3A" ? 2 : 1;

        /// <summary>The steam valve of the injector at <paramref name="index"/> (the JK3's right is 0, its left 1).</summary>
        public static StokerJetControls.Valve For(int index) => index == 0 ? Steam : SteamLeft;

        /// <summary>The steam lever's position, for the cab and the overlay.</summary>
        public static string Position(float value) =>
            value < InjectorSpec.SteamPrimeFrom ? "Off" : value < InjectorSpec.SteamFeedFrom ? "Prime" : "Feed";
    }
}
