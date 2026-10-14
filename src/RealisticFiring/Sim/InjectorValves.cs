using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class InjectorValves
    {
        public static readonly StokerJetControls.Valve Steam = new StokerJetControls.Valve("rfInjectorSteam", "Injector steam", null, 0f);
        public static readonly StokerJetControls.Valve SteamLeft = new StokerJetControls.Valve("rfInjectorSteam2", "Left injector steam", null, 0f);

 public static readonly StokerJetControls.Valve Water = new StokerJetControls.Valve("rfInjectorWater", "Injector water", null, 0f);
        public static readonly StokerJetControls.Valve WaterLeft = new StokerJetControls.Valve("rfInjectorWater2", "Left injector water", null, 0f);

 public static readonly StokerJetControls.Valve TenderWater = new StokerJetControls.Valve("rfTenderWater", "Right tender water valve", null, 1f);
        public static readonly StokerJetControls.Valve TenderWaterLeft = new StokerJetControls.Valve("rfTenderWater2", "Left tender water valve", null, 1f);

        private static readonly HashSet<string> TenderValved = new HashSet<string> { "em_lms_stanier_tender_1_black", "em_lms_stanier_tender_1_grey" };

        /// <summary>Whether this session fits the tender <paramref name="livery"/> with water valves.</summary>
        public static bool HasTenderValves(string livery) => livery != null && Main.Session.InjectorSteamValves && TenderValved.Contains(livery);

        /// <summary>The tender's water valve to the injector at <paramref name="index"/> (the right is 0, the left 1).</summary>
        public static StokerJetControls.Valve TenderValve(int index) => index == 0 ? TenderWater : TenderWaterLeft;

        private static readonly HashSet<string> OwnLeverSteam = new HashSet<string> { "em_lms_8f_loc_black", "em_lms_8f_loc_grey" };

        /// <summary>Whether <paramref name="livery"/>'s own injector levers are the steam valves, and the added controls the water regulators.</summary>
        public static bool OwnLeverIsSteam(string livery) => livery != null && OwnLeverSteam.Contains(livery);

        /// <summary>The steam lever's notches, at 0, <see cref="InjectorSpec.SteamPrime"/>, and 1.</summary>
        public static readonly string[] Notches = { "Off", "Prime", "Feed" };

        private static readonly HashSet<string> Fitted = new HashSet<string>
        {
            "LocoS282A", "LocoS060", "Jk3A", "S464-730", "FEF-3", "S484-840A", "A.T.&S.F. 2901-A",
            "em_lms_8f_loc_black", "em_lms_8f_loc_grey",
        };

        // Lifting injectors, above the tender's water: the JK3's, as Finnish
        // engines carried (Friedmann). The American engines' are non-lifters,
        // and so are the LMS 8F's, under the cab footsteps.
        private static readonly HashSet<string> Lifters = new HashSet<string> { "Jk3A" };

        /// <summary>Whether this session fits <paramref name="livery"/> with steam valves.</summary>
        public static bool Fits(string livery) => livery != null && Main.Session.InjectorSteamValves && Fitted.Contains(livery);

        public static bool Lifting(string livery) => livery != null && Lifters.Contains(livery);

        /// <summary>How many injectors <paramref name="livery"/> has, and so steam valves.</summary>
        public static int Count(string livery) => InjectorRig.HasTwoValveGraph(livery) ? 2 : 1;

        /// <summary>
        /// The valve added for the injector at <paramref name="index"/> (on
        /// two-injector cabs the right is 0, the left 1): its steam valve, or
        /// its water regulator where the cab's own lever is the steam valve.
        /// </summary>
        public static StokerJetControls.Valve For(int index, string livery) => OwnLeverIsSteam(livery)
            ? index == 0 ? Water : WaterLeft
            : index == 0 ? Steam : SteamLeft;

        /// <summary>The steam lever's position, for the cab and the overlay.</summary>
        public static string Position(float value) =>
            value < InjectorSpec.SteamPrimeFrom ? "Off" : value < InjectorSpec.SteamFeedFrom ? "Prime" : "Feed";
    }
}
