using System;

namespace RealisticFiring.Core
{
 public sealed class HeldSetting
    {
        // Within this of shut or wide open it counts as there.
        private const float AtEnd = 0.005f;

        private readonly float step;
        private readonly float notice;
        private readonly float dwell;
        private float away;
        private float since = float.PositiveInfinity;

        /// <param name="step">The least change he makes, 0 to 1; to shut or wide open he goes from any distance.</param>
        /// <param name="notice">How long, in s, what he wants must stay a step off before he moves it.</param>
        /// <param name="dwell">How long, in s, he leaves it after a move before another.</param>
        public HeldSetting(float step, float notice, float dwell)
        {
            if (!(step > 0f) || notice < 0f || dwell < 0f)
                throw new ArgumentOutOfRangeException(nameof(step));
            this.step = step;
            this.notice = notice;
            this.dwell = dwell;
        }

        /// <summary>Where he has it, 0 to 1; NaN before he first sets it.</summary>
        public float Value { get; private set; } = float.NaN;

        /// <summary>
        /// What he wants it at this tick; returns where he has it. The first
        /// wish is set at once, as is any with <paramref name="now"/>.
        /// </summary>
        public float Hold(float wanted, float dt, bool now = false)
        {
            if (float.IsNaN(wanted) || float.IsInfinity(wanted))
                return Idle(dt);
            wanted = Math.Max(0f, Math.Min(1f, wanted));
            since += Math.Max(0f, dt);
            // His first wish is where he takes it up, not a move to wait after.
            if (float.IsNaN(Value))
            {
                Value = wanted;
                away = 0f;
                return Value;
            }
            if (now && wanted != Value)
            {
                Set(wanted);
                return Value;
            }
            var off = Math.Abs(wanted - Value);
            var toEnd = (wanted <= 0f || wanted >= 1f) && off > AtEnd;
            away = off >= step || toEnd ? away + Math.Max(0f, dt) : 0f;
            if (away >= notice && since >= dwell)
                Set(wanted);
            return Value;
        }

        /// <summary>
        /// A tick with the control held aside (the door wide for a throw):
        /// the time since his last move passes; what he wanted before waits
        /// for the next wish.
        /// </summary>
        public float Idle(float dt)
        {
            since += Math.Max(0f, dt);
            return Value;
        }

        /// <summary>Forgets it, as when he stops or starts work: the next wish is set at once.</summary>
        public void Reset()
        {
            Value = float.NaN;
            away = 0f;
            since = float.PositiveInfinity;
        }

        private void Set(float wanted)
        {
            Value = wanted;
            away = 0f;
            since = 0f;
        }
    }
}
