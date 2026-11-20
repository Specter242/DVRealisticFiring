using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring
{
    /// <summary>The tablet and F7 panel issue the same orders to the engine's fireman.</summary>
    internal static class FiremanOrders
    {
        // On a guest the fireman's mode and call are the host's, mirrored
        // four times a second (ClientMirror). A guest's panel once showed
        // its own last order, Off until it gave one: with the host's fireman
        // on, Off was what it showed and what it would not send (audit
        // 2026-11-09). An order shows at once here, and the next mirror
        // puts it right if the host did otherwise.
        internal static FiremanMode ModeOf(FiremanRig rig) => rig.Fireman.Mode;

        internal static void Order(FiremanRig rig, FiremanMode mode)
        {
            if (Net.TrySend(rig.Car, FireAction.FiremanMode, value: (float)mode))
            {
                rig.Fireman.Mode = mode;
                return;
            }
            if (mode != rig.Fireman.Mode)
                rig.SetMode(mode);
        }

        /// <summary>The order after <paramref name="mode"/>: off, keep it alive, shunting, road, and round again.</summary>
        internal static FiremanMode Next(FiremanMode mode) =>
            mode == FiremanMode.Off ? FiremanMode.KeepAlive
            : mode == FiremanMode.KeepAlive ? FiremanMode.Shunting
            : mode == FiremanMode.Shunting ? FiremanMode.Road : FiremanMode.Off;

        /// <summary>An order as the notice says it.</summary>
        internal static string Said(FiremanMode mode) =>
            mode == FiremanMode.KeepAlive ? "keep it alive" : mode == FiremanMode.Shunting ? "shunting" : mode == FiremanMode.Road ? "road" : "off";

        /// <summary>A call as the notice says it.</summary>
        internal static string Said(FiremanCall call) =>
            call == FiremanCall.HeavyAhead ? "heavy work ahead" : call == FiremanCall.EasingOff ? "easing off" : "no call";

        internal static bool CallActive(FiremanRig rig, FiremanCall call) => rig.Fireman.Call == call;

        internal static void Call(FiremanRig rig, FiremanCall call)
        {
            var next = CallActive(rig, call) ? FiremanCall.None : call;
            Net.TrySend(rig.Car, FireAction.FiremanCall, value: (float)next);
            rig.Fireman.Call = next;
        }
    }
}
