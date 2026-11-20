using System;

namespace RealisticFiring.Core
{
    /// <summary>Where an automatic fire door is in its cycle.</summary>
    public enum AutoFireDoorPhase
    {
        /// <summary>Not working: the door is the player's.</summary>
        Idle,

        /// <summary>Swinging open.</summary>
        Opening,

        /// <summary>Standing open for the shovel.</summary>
        Open,

        /// <summary>Swinging back to where it rested.</summary>
        Closing,
    }

    /// <summary>What the door's air does as the cycle moves on.</summary>
    public enum AutoFireDoorAir
    {
        /// <summary>Nothing to hear.</summary>
        None,

        /// <summary>The pedal lets air into the door's cylinder.</summary>
        Admitted,

        /// <summary>The cylinder blows its air out, and the door shuts.</summary>
        Exhausted,
    }

 public sealed class AutoFireDoor
    {
        /// <summary>A full swing, in s.</summary>
        public const float Stroke = 0.4f;

        /// <summary>The opening the game wants for a shovel with the mouse.</summary>
        public const float ShovelOpening = 0.6f;

        private float travel;
        private float lastNow;

        public AutoFireDoorPhase Phase { get; private set; }

        /// <summary>Where the door goes back to.</summary>
        public float Rest { get; private set; }

        public bool Working => Phase != AutoFireDoorPhase.Idle;

        /// <summary>
        /// What the air does between two phases of the cycle: in as the door
        /// starts open, out as the pedal comes up (a tap's little swing can be
        /// back at rest within the frame).
        /// </summary>
        public static AutoFireDoorAir AirFor(AutoFireDoorPhase before, AutoFireDoorPhase after)
        {
            var wasOpening = before == AutoFireDoorPhase.Opening || before == AutoFireDoorPhase.Open;
            var isOpening = after == AutoFireDoorPhase.Opening || after == AutoFireDoorPhase.Open;
            if (!wasOpening && isOpening)
                return AutoFireDoorAir.Admitted;
            return wasOpening && !isOpening ? AutoFireDoorAir.Exhausted : AutoFireDoorAir.None;
        }

        /// <summary>The pedal goes down, with the door at <paramref name="door"/>.</summary>
        public void Press(float door, float now)
        {
            if (Phase == AutoFireDoorPhase.Idle)
            {
                Rest = door < ShovelOpening ? Math.Max(0f, door) : 0f;
                travel = door;
                lastNow = now;
            }
            if (Phase != AutoFireDoorPhase.Open)
                Phase = AutoFireDoorPhase.Opening;
        }

        /// <summary>Ends the cycle, leaving the door as it is.</summary>
        public void Stop() => Phase = AutoFireDoorPhase.Idle;

        /// <summary>
        /// One frame. Returns where to put the door, or null to leave it.
        /// </summary>
        /// <param name="door">The door's opening now.</param>
        /// <param name="held">Whether the pedal is still down.</param>
        /// <param name="now">The time, in s.</param>
        public float? Step(float door, bool held, float now)
        {
            var swing = Math.Max(0f, now - lastNow) / Stroke;
            lastNow = now;
            switch (Phase)
            {
                case AutoFireDoorPhase.Opening:
                    if (!held)
                    {
                        Phase = AutoFireDoorPhase.Closing;
                        goto case AutoFireDoorPhase.Closing;
                    }
                    travel = Toward(travel, 1f, swing);
                    if (travel >= 1f)
                        Phase = AutoFireDoorPhase.Open;
                    return travel;

                case AutoFireDoorPhase.Open:
                    // Left alone while held, so a notched lever may settle;
                    // shut from wherever it stands once let go.
                    if (held)
                        return null;
                    Phase = AutoFireDoorPhase.Closing;
                    travel = door;
                    goto case AutoFireDoorPhase.Closing;

                case AutoFireDoorPhase.Closing:
                    travel = Toward(travel, Rest, swing);
                    if (travel == Rest)
                        Phase = AutoFireDoorPhase.Idle;
                    return travel;

                default:
                    return null;
            }
        }

        private static float Toward(float from, float to, float step) =>
            Math.Abs(to - from) <= step ? to : from + Math.Sign(to - from) * step;
    }
}
