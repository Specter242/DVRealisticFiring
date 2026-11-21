namespace RealisticFiring.Core
{
    /// <summary>
    /// One coal engine's cold start, as its checklist follows it. From a fire
    /// that is out, the Cold Start list holds until all its steps have stood
    /// done for <see cref="DoneSeconds"/>, so 7/7 is seen (job 3120: let go on
    /// the reading it was done, the list went from 6/7 to the next), or the
    /// coal has burned <see cref="HoldSeconds"/>; once the coal has caught,
    /// "First coal lit" stays ticked and the list doesn't come back when the
    /// burning coal dips, until the fire goes out again. In game the list left
    /// as soon as half a kilogram burned, so "Fire door shut" was never seen
    /// ticked, and flipped back on a dip (job 3112, 3115).
    /// </summary>
    public sealed class ColdStartLatch
    {
        public const float HoldSeconds = 60f;
        public const float DoneSeconds = 10f;

        private bool held, caught;
        private float caughtAt;
        private float? doneAt;

        /// <summary>Reads <paramref name="s"/> at <paramref name="now"/>, seconds, and marks the cold start on it.</summary>
        public void Apply(ref ChecklistSnapshot s, float now)
        {
            if (s.IsOil)
            {
                held = caught = false;
                return;
            }
            var burning = s.FireOn && s.BurningCoalMass >= FiringChecklists.EstablishedCoalKg;
            if (!s.FireOn)
            {
                held = true;
                caught = false;
                doneAt = null;
            }
            else if (!caught)
            {
                if (burning)
                {
                    caught = true;
                    caughtAt = now;
                }
                else
                {
                    held = true;
                }
            }
            s.CoalCaught = caught;
            s.ColdStartHeld = held;
            if (held && caught)
            {
                if (FiringChecklists.Evaluate(s, ChecklistId.CoalColdStart).AllDone)
                    doneAt = doneAt ?? now;
                else
                    doneAt = null;
                if (now - caughtAt >= HoldSeconds || (doneAt.HasValue && now - doneAt.Value >= DoneSeconds))
                {
                    held = false;
                    doneAt = null;
                }
            }
            s.ColdStartHeld = held;
        }
    }
}
