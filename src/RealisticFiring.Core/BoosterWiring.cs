using System;
using System.Collections.Generic;

namespace RealisticFiring.Core
{
 public static class BoosterWiring
    {
        /// <summary>
        /// The index in <paramref name="order"/> (component IDs, in execution
        /// order) the booster is inserted at: just after the component that owns
        /// <paramref name="drivePortOut"/> (the "component.PORT" feeding the
        /// traction's torque), else just after <paramref name="engineId"/>, else
        /// -1 when neither is in the order.
        /// </summary>
        public static int InsertAt(IList<string> order, string drivePortOut, string engineId)
        {
            if (order == null)
                return -1;
            var source = ComponentOf(drivePortOut);
            var at = source != null ? order.IndexOf(source) : -1;
            if (at < 0 && engineId != null)
                at = order.IndexOf(engineId);
            return at < 0 ? -1 : at + 1;
        }

        /// <summary>The component ID of a "component.PORT" port ID; null without one.</summary>
        public static string ComponentOf(string fullPortId)
        {
            if (string.IsNullOrEmpty(fullPortId))
                return null;
            var dot = fullPortId.IndexOf('.');
            return dot > 0 ? fullPortId.Substring(0, dot) : null;
        }
    }

    /// <summary>
    /// The booster's share of the loco's powered-axle count: one axle added
    /// while it is in gear and taken off again after, on whatever count the
    /// loco has, never an absolute number written over another mod's
    /// (JimTaplin's Rearranged S282 sets the count for each wheel
    /// arrangement). A count someone else wrote since stands as theirs. The
    /// loco's configured count (the stock powered-axle port's definition,
    /// which Rearranged sets with the port), where it is known, tells ours
    /// from theirs when they write the very number we did.
    /// </summary>
    public sealed class TrailerAxle
    {
        private const float Same = 0.01f;
        private float written;

        /// <summary>Whether the booster's axle is in the count now.</summary>
        public bool Added { get; private set; }

        /// <summary>
        /// The loco's own powered axles, the booster's left out: its
        /// <paramref name="configured"/> count where known, else read from the
        /// port's <paramref name="count"/>.
        /// </summary>
        public float Own(float count, float? configured = null)
        {
            Forget(count, configured);
            return configured ?? (Added ? count - 1f : count);
        }

        /// <summary>
        /// The count to write for <paramref name="inGear"/> given the port's
        /// <paramref name="count"/> now, or null to leave it as it is.
        /// </summary>
        public float? Next(float count, bool inGear, float? configured = null)
        {
            Forget(count, configured);
            if (inGear == Added)
                return null;
            var next = inGear ? count + 1f : Math.Max(0f, count - 1f);
            Added = inGear;
            written = next;
            return next;
        }

        // Another writer's count, since ours, holds no axle of ours.
        private void Forget(float count, float? configured)
        {
            if (Added && (Math.Abs(count - written) > Same
                          || configured.HasValue && Math.Abs(count - (configured.Value + 1f)) > Same))
                Added = false;
        }
    }
}
