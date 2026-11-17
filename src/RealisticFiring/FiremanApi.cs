using System;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring
{
 public static class FiremanApi
    {
        /// <summary>Raised when an entry here changes in a way a caller must know of.</summary>
        public const int Version = 1;

        /// <summary>Whether <paramref name="loco"/> has this mod's fireman (a coal or oil engine it fires).</summary>
        public static bool Has(TrainCar loco) => FiremanRig.TryGet(loco, out _);

        /// <summary>His mode: "Off", "KeepAlive", "Shunting", or "Road"; null if the loco has no fireman.</summary>
        public static string ModeOf(TrainCar loco) =>
            FiremanRig.TryGet(loco, out var rig) ? FiremanOrders.ModeOf(rig).ToString() : null;

        /// <summary>
        /// The engineer's call standing with him: "None", "HeavyAhead", or
        /// "EasingOff"; null if the loco has no fireman. Read it before
        /// <see cref="SetCall"/> to give it back afterwards (Sol, reviewing
        /// AI Engineer, 2026-11-17: without it a player's HeavyAhead was
        /// overwritten by EasingOff, then set to None on release).
        /// </summary>
        public static string CallOf(TrainCar loco) =>
            FiremanRig.TryGet(loco, out var rig) ? rig.Fireman.Call.ToString() : null;

        /// <summary>Sets his mode by name ("Off", "KeepAlive", "Shunting", "Road"); false if there is no fireman or no such mode.</summary>
        public static bool SetMode(TrainCar loco, string mode)
        {
            if (!FiremanRig.TryGet(loco, out var rig) || !Enum.TryParse(mode, out FiremanMode m))
                return false;
            FiremanOrders.Order(rig, m);
            return true;
        }

        /// <summary>The engineer's call to him: "HeavyAhead", "EasingOff", or "None" to take it back; false if there is no fireman or no such call.</summary>
        public static bool SetCall(TrainCar loco, string call)
        {
            if (!FiremanRig.TryGet(loco, out var rig) || !Enum.TryParse(call, out FiremanCall c))
                return false;
            var now = rig.Fireman.Call;
            if (now == c)
                return true;
            // A call pressed again is taken back: take back the one standing, then make the one wanted.
            if (now != FiremanCall.None)
                FiremanOrders.Call(rig, now);
            if (c != FiremanCall.None)
                FiremanOrders.Call(rig, c);
            return true;
        }
    }
}
