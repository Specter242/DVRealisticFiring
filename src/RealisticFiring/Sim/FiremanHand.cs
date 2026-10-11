using LocoSim.Implementations;
using UnityEngine;

namespace RealisticFiring.Sim
{
    /// <summary>
    /// One cab control in the fireman's hands. He moves it through its port,
    /// and the lever follows (the game's port feeder sets the control), at a
    /// hand's pace rather than in one jump; a notched lever may settle beside
    /// where he put it, and he takes that.
    /// A change he did not make, once his own move has settled, is the
    /// player's hand, and he leaves the control alone for
    /// <see cref="YieldTime"/>.
    /// </summary>
    internal sealed class FiremanHand
    {
        /// <summary>A control the player moves is theirs for this long, in s.</summary>
        public const float YieldTime = 60f;

        // A lever he moves may settle onto a notch for this long, in s; a
        // change after that by more than this is the player's hand.
        public const float SettleTime = 1.5f;
        public const float Moved = 0.03f;

        // On the way he leaves a control alone within this of where his hand
        // is. Arrived, he takes the notch it settles on, and asks again only
        // when it stayed this far off (it did not move), at most this often, in s.
        public const float Deadband = 0.02f;
        private const float RetryBeyond = 0.1f;
        private const float Retry = 5f;

        private float seen;
        private bool watching;
        private float settleUntil;
        private float asked = float.NaN;
        private float askAgain;
        private float moving = float.NaN;
        private float lastNow = float.NaN;

        /// <param name="pace">How fast he moves it, in its travel per second.</param>
        public FiremanHand(string name, Port port, float pace)
        {
            Name = name;
            Port = port;
            Pace = pace;
        }

        /// <summary>How fast he moves it, in its travel per second.</summary>
        public float Pace { get; }

        public string Name { get; }

        /// <summary>The control's port; null when the loco has no such control he can work.</summary>
        public Port Port { get; }

        public float YieldUntil { get; private set; } = float.NegativeInfinity;

        public bool Yielded(float now) => now < YieldUntil;

        /// <summary>Takes the control back, as when he is given a new mode.</summary>
        public void Release()
        {
            watching = false;
            asked = float.NaN;
            moving = float.NaN;
            YieldUntil = float.NegativeInfinity;
        }

        /// <summary>Puts the control at <paramref name="want"/>, unless the player has it.</summary>
        public void Work(float want, float now)
        {
            if (Port == null)
                return;
            var dt = float.IsNaN(lastNow) ? 0f : Mathf.Max(0f, now - lastNow);
            lastNow = now;
            var value = Port.Value;
            if (watching && now >= settleUntil && Mathf.Abs(value - seen) > Moved)
            {
                YieldUntil = now + YieldTime;
                watching = false;
                asked = float.NaN;
                moving = float.NaN;
                if (Main.Settings != null && Main.Settings.LogCoalDeliveries)
                    Main.Mod.Logger.Log($"fireman: the player has the {Name} ({seen:F2} -> {value:F2})");
            }
            if (Yielded(now))
                return;

            // His hand travels toward where he wants it, from where the control was.
            if (float.IsNaN(moving))
                moving = value;
            moving = Mathf.MoveTowards(moving, want, Pace * dt);
            var arrived = moving == want || moving == 0f || moving == 1f;
            var fresh = float.IsNaN(asked) || (arrived ? asked != moving : Mathf.Abs(moving - asked) > Deadband);
            var stuck = arrived && Mathf.Abs(value - moving) > RetryBeyond && now >= askAgain;
            if (value != moving && (fresh || stuck))
            {
                Port.ExternalValueUpdate(moving);
                asked = moving;
                askAgain = now + Retry;
                settleUntil = now + SettleTime;
            }
            // Where he left it, once it has settled: a slow drag by the
            // player adds up against this.
            if (!watching || now < settleUntil)
                seen = Port.Value;
            watching = true;
        }
    }
}
