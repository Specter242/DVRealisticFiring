using System;

namespace RealisticFiring.Core
{
    /// <summary>
    /// How much fire one hold of the dump lever may drop. A hold over the dump
    /// point (<see cref="GrateLever"/>) dumps at the stock rate until it has
    /// taken <see cref="Share"/> of the coal that lay on the grate when it began,
    /// or <see cref="Floor"/> of the firebox's capacity if that is more, so a
    /// low fire still goes in a hold or two. Then the dump stops until the
    /// lever comes back to center and is held over again. Dumping the whole
    /// fire so takes a few deliberate holds, and a lever reading that sticks or
    /// lingers over the dump point (a multiplayer desync, Torun, 2026-11-22)
    /// drops one hold's share, not the fire. Ash and clinker are not counted:
    /// with the coal gone, a hold keeps clearing them as long as it lasts.
    /// </summary>
    public sealed class DumpStroke
    {
        /// <summary>Of the coal on the grate when a hold began, the most that hold dumps.</summary>
        public const float Share = 1f / 3f;

        /// <summary>Of the firebox's coal capacity, the least a hold may dump.</summary>
        public const float Floor = 0.1f;

        /// <summary>Coal this hold may still dump, in kg; 0 before any hold.</summary>
        public float Left { get; private set; }

        /// <summary>Whether this hold has dumped all it may while coal is left.</summary>
        public bool Spent => !(Left > Negligible);

        // Under a gram left counts as none, so rounding leaves no trickle.
        private const float Negligible = 0.001f;

        /// <summary>Starts a hold's allowance from the coal on the grate and the firebox's capacity, in kg.</summary>
        public void Begin(float coalMass, float capacity)
        {
            Left = Math.Max(Share * Math.Max(0f, Finite(coalMass)), Floor * Math.Max(0f, Finite(capacity)));
        }

        /// <summary>
        /// The dump flow, 0 to 1, this tick may run at: <paramref name="flow"/>,
        /// cut so that at <paramref name="dumpRate"/> kg/s it dumps no more coal
        /// than the hold has left. With no coal on the grate nothing is at stake,
        /// and the flow runs on to clear the ash.
        /// </summary>
        public float Allow(float flow, float coalMass, float dumpRate, float dt)
        {
            if (!(flow > 0f) || !(dt > 0f))
                return 0f;
            if (!(coalMass > 0f))
                return flow;
            if (Spent)
                return 0f;
            var full = dumpRate * dt;
            return full > Left ? Math.Min(flow, Left / full) : flow;
        }

        /// <summary>Counts the coal a tick's dump took against the hold's allowance, in kg.</summary>
        public void Took(float coal)
        {
            if (coal > 0f)
                Left = Math.Max(0f, Left - coal);
        }

        private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }
}
