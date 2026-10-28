using System.Runtime.CompilerServices;
using RealisticFiring.Core;
using RealisticFiring.Sim;

namespace RealisticFiring
{
    /// <summary>The tablet and F7 panel issue the same orders to the engine's fireman.</summary>
    internal static class FiremanOrders
    {
        private sealed class SentOrder
        {
            internal FiremanMode Mode;
        }

        // The host does not synchronize his fireman's internal state. Both
        // interfaces therefore show the client's last order, not a live mode.
        private static readonly ConditionalWeakTable<TrainCar, SentOrder> Sent = new ConditionalWeakTable<TrainCar, SentOrder>();

        internal static FiremanMode ModeOf(FiremanRig rig) => Net.IsClient
            ? Sent.TryGetValue(rig.Car, out var sent) ? sent.Mode : FiremanMode.Off
            : rig.Fireman.Mode;

        internal static void Order(FiremanRig rig, FiremanMode mode)
        {
            if (mode == ModeOf(rig)) return;
            if (Net.TrySend(rig.Car, FireAction.FiremanMode, value: (float)mode))
                Sent.GetValue(rig.Car, _ => new SentOrder()).Mode = mode;
            else
                rig.SetMode(mode);
        }

        internal static bool CallActive(FiremanRig rig, FiremanCall call) => !Net.IsClient && rig.Fireman.Call == call;

        internal static void Call(FiremanRig rig, FiremanCall call)
        {
            var next = CallActive(rig, call) ? FiremanCall.None : call;
            if (!Net.TrySend(rig.Car, FireAction.FiremanCall, value: (float)next))
                rig.Fireman.Call = next;
        }
    }
}
