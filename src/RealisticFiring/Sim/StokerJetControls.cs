using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// The stoker's jet valves as cab controls: one external control each in
    /// the loco's sim graph, saved with the car. Their defaults are the
    /// working setting, so a loco behaves as before until the player moves a
    /// valve.
    /// </summary>
    internal static class StokerJetControls
    {
        public sealed class Valve
        {
            public Valve(string id, string label, StokerJet? jet, float defaultValue)
            {
                Id = id;
                Label = label;
                Jet = jet;
                Default = defaultValue;
            }

            /// <summary>Sim component ID; the control's port is <c>{Id}.EXT_IN</c>.</summary>
            public string Id { get; }

            /// <summary>Name on the cab label and under the cursor.</summary>
            public string Label { get; }

            /// <summary>The jet it works; null for the master (intermediate) valve.</summary>
            public StokerJet? Jet { get; }

            public float Default { get; }

            public string PortId => Id + ".EXT_IN";
        }

        public static readonly IReadOnlyList<Valve> Valves = BuildValves();

        /// <summary>
        /// The stoker engine's throttle, for locos fitted with the mod's own
        /// stoker (the S282); CCL locos have their own stoker valve.
        /// </summary>
        public static readonly Valve Engine = new Valve("rfStokerEngine", "Stoker engine", null, 0f);

        /// <summary>
        /// The stoker engine's operating lever: 0 reverse, ½ neutral, 1
        /// forward; forward by default, so a loco feeds as before.
        /// </summary>
        public static readonly Valve Reverse = new Valve("rfStokerReverse", "Stoker engine", null, 1f);

        /// <summary>The operating lever's position for a control value.</summary>
        public static Core.StokerDirection Direction(float value) =>
            value < 0.25f ? Core.StokerDirection.Reverse : value > 0.75f ? Core.StokerDirection.Forward : Core.StokerDirection.Neutral;

        /// <summary>The mod's own stoker's component ID.</summary>
        public const string StokerId = "rfStoker";

        public static Valve Master => Valves[0];

        private static IReadOnlyList<Valve> BuildValves()
        {
            var working = StokerControls.Working(1f);
            return new[]
            {
                new Valve("rfStokerJetMaster", "Stoker master jet", null, working.MasterJet),
                new Valve("rfStokerJetLeftBack", "Left back jet", StokerJet.LeftBack, working.LeftBack),
                new Valve("rfStokerJetRightBack", "Right back jet", StokerJet.RightBack, working.RightBack),
                new Valve("rfStokerJetLeftFront", "Left front jet", StokerJet.LeftFront, working.LeftFront),
                new Valve("rfStokerJetRightFront", "Right front jet", StokerJet.RightFront, working.RightFront),
                new Valve("rfStokerJetFines", "Fine-coal jet", StokerJet.Fines, working.Fines),
            };
        }
    }
}
