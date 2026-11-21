namespace RealisticFiring.Core
{
    /// <summary>One frame of the VR automatic fire door trigger pedal.</summary>
    public readonly struct VrFireDoorSignal
    {
        public VrFireDoorSignal(bool pressed, bool held)
        {
            Pressed = pressed;
            Held = held;
        }

        /// <summary>True on the frame the pedal engages and starts opening the door.</summary>
        public bool Pressed { get; }

        /// <summary>True while the pedal is holding the door open.</summary>
        public bool Held { get; }
    }

    /// <summary>
    /// Interprets the VR shovel-hand trigger as the automatic fire door pedal
    /// without colliding with VR scoop-size scrolling (Trigger + Thumbstick).
    ///
    /// A clean trigger hold engages after <see cref="HoldDelay"/> s and stays
    /// latched open until the trigger is released, a VR pointer aims at UI, or
    /// the game pauses (so accidental stick noise while holding the door open
    /// does not snap-cancel it). Deflecting the thumbstick or entering the
    /// game's scroll modifier (<c>UseModified</c>) before latching, pointing
    /// at VR UI, or pausing suppresses the pedal until the trigger is released
    /// and pressed again.
    /// </summary>
    public sealed class VrFireDoorPedal
    {
        /// <summary>
        /// Clean hold time before the trigger opens the fire door, in s: short
        /// enough to feel immediate alongside the 0.4 s door stroke, long enough
        /// for a Trigger + Thumbstick scoop-size flick not to puff the door.
        /// </summary>
        public const float HoldDelay = 0.12f;

        private float downSince = float.NaN;
        private bool active;
        private bool suppressed;

        /// <summary>Whether the pedal is currently holding the door open.</summary>
        public bool Active => active;

        /// <summary>Clears held and suppressed state, e.g. when ungrabbed.</summary>
        public void Reset()
        {
            downSince = float.NaN;
            active = false;
            suppressed = false;
        }

        /// <summary>
        /// Drops any active hold and suppresses the pedal until the trigger is
        /// released, e.g. when paused or when locomotion input is disabled, so
        /// unpausing with the trigger still held does not reopen the door.
        /// </summary>
        public void SuppressUntilRelease()
        {
            downSince = float.NaN;
            active = false;
            suppressed = true;
        }

        /// <summary>
        /// Advances one frame of VR trigger input on the hand(s) holding the shovel.
        /// </summary>
        /// <param name="triggerDown">Whether the use trigger is held on a shovel-holding hand.</param>
        /// <param name="useModified">Whether the game's VR use-scroll modifier is active on a shovel-holding hand.</param>
        /// <param name="stickDeflected">Whether the thumbstick/touchpad on a trigger-pressed shovel hand is deflected past deadzone.</param>
        /// <param name="pointingAtUi">Whether a VR pointer is currently aimed at UI.</param>
        /// <param name="now">Current time, in s.</param>
        public VrFireDoorSignal Step(bool triggerDown, bool useModified, bool stickDeflected, bool pointingAtUi, float now)
        {
            if (!triggerDown)
            {
                Reset();
                return default;
            }

            if (pointingAtUi)
            {
                SuppressUntilRelease();
                return default;
            }

            if (suppressed)
                return default;

            if (active)
                return new VrFireDoorSignal(pressed: false, held: true);

            if (useModified || stickDeflected)
            {
                suppressed = true;
                return default;
            }

            if (float.IsNaN(downSince))
                downSince = now;

            if (now - downSince < HoldDelay - 1e-5f)
                return default;

            active = true;
            return new VrFireDoorSignal(pressed: true, held: true);
        }
    }
}
