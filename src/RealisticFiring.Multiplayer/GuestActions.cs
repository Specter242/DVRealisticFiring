using System.Collections.Generic;

namespace RealisticFiring.Multiplayer
{
    /// <summary>
    /// Which of a guest's fire actions the host carries out (audit
    /// 2026-11-09). A guest in a public session is not trusted, and the
    /// Multiplayer mod delivers reliably but not in order. So: a value that
    /// is no number is dropped; a hand pump's On and Off are taken in the
    /// order they were sent (a quick tap landing Off first left the host
    /// pumping); and the fire iron, the shovel, the grate, and the sand are
    /// taken no faster than a hand works them. Kept apart from the
    /// Multiplayer mod's types, so the checks run it without a session.
    /// </summary>
    internal sealed class GuestActions
    {
        /// <summary>The least time between two of the same action from one guest, in s.</summary>
        public const float LeastInterval = 0.2f;

        private readonly Dictionary<byte, ushort> pumpHeard = new Dictionary<byte, ushort>();
        private readonly Dictionary<int, uint> lastAt = new Dictionary<int, uint>();

        /// <summary>
        /// Whether to carry out <paramref name="action"/> from
        /// <paramref name="player"/>, sent as their <paramref name="seq"/>th,
        /// at the session's <paramref name="tick"/> of
        /// <paramref name="ticksPerSecond"/>.
        /// </summary>
        public bool Accept(byte player, FireAction action, float value, ushort seq, uint tick, uint ticksPerSecond)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return false;
            switch (action)
            {
                case FireAction.HandPumpOn:
                case FireAction.HandPumpOff:
                    if (pumpHeard.TryGetValue(player, out var heard) && !Shed.ShedNet.Newer(seq, heard))
                        return false;
                    pumpHeard[player] = seq;
                    return true;
                case FireAction.Shake:
                case FireAction.Wiggle:
                case FireAction.Rake:
                case FireAction.Slice:
                case FireAction.Push:
                case FireAction.Shovelful:
                case FireAction.Sand:
                    var key = player << 8 | (byte)action;
                    var least = (uint)(LeastInterval * (ticksPerSecond > 0u ? ticksPerSecond : 1u));
                    if (lastAt.TryGetValue(key, out var last) && tick - last < least)
                        return false;
                    lastAt[key] = tick;
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>Forgets a guest who left.</summary>
        public void Forget(byte player)
        {
            pumpHeard.Remove(player);
            for (var action = 0; action < 256; action++)
                lastAt.Remove(player << 8 | action);
        }

        public void Clear()
        {
            pumpHeard.Clear();
            lastAt.Clear();
        }
    }
}
