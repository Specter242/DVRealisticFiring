namespace RealisticFiring.Core
{
    /// <summary>
    /// The aim of a shovel's throw or rake, taken as the player clicks and
    /// kept for the end of the thrust, when the game puts the coal in (or the
    /// mod rakes). Firing fast, a player clicks at the door and turns back to
    /// the coal while the thrust plays (Torun, 2026-11-22): the look has left
    /// the aimed spot, or the door, by the time the coal lands, so the last
    /// hover's aim is wherever the look crossed the door's edge on its way out,
    /// or an empty shovel's. Each click replaces the aim; the end of a thrust
    /// takes it once, and only for the same shovel and target, of the same kind
    /// (a loaded shovel's throw, an empty one's rake), within the hold.
    /// </summary>
    public sealed class ClickedAim<T> where T : struct
    {
        private object tool;
        private object target;
        private T aim;
        private DoorAimKind kind;
        private float time;
        private bool held;

        /// <summary>Whether an aim is held.</summary>
        public bool Held => held;

        /// <summary>Holds <paramref name="value"/> as the aim of the click <paramref name="by"/> made on <paramref name="on"/> at <paramref name="at"/>.</summary>
        public void Click(object by, object on, T value, DoorAimKind what, float at)
        {
            tool = by;
            target = on;
            aim = value;
            kind = what;
            time = at;
            held = by != null && on != null;
        }

        /// <summary>Drops the held aim.</summary>
        public void Clear()
        {
            held = false;
            tool = null;
            target = null;
            aim = default;
        }

        /// <summary>
        /// The held aim for <paramref name="by"/>'s use on <paramref name="on"/>
        /// at <paramref name="now"/>: true, and the aim dropped, when the click
        /// was that tool's on that target, of kind <paramref name="what"/>, and
        /// no older than <paramref name="hold"/> s. A use the aim does not fit
        /// leaves it held, for its own use's end.
        /// </summary>
        public bool TryTake(object by, object on, DoorAimKind what, float now, float hold, out T value)
        {
            value = default;
            if (!held || by == null || on == null || !ReferenceEquals(by, tool) || !ReferenceEquals(on, target))
                return false;
            var age = now - time;
            if (kind != what || !(age >= 0f && age <= hold))
            {
                // That shovel's thrust at that target ended on this aim: it can never be used now.
                Clear();
                return false;
            }
            value = aim;
            Clear();
            return true;
        }
    }
}
