using System;

namespace RealisticStackEffects.Core
{
 public sealed class AutoSmokeQuality
    {
        /// <summary>Slow: under this many frames a second (or 0.8 of the rate the game is held to, if less).</summary>
        public const float SlowBelow = 45f;

        /// <summary>Running well: at least this many frames a second (or 0.93 of the rate the game is held to, if less).</summary>
        public const float WellFrom = 55f;

        /// <summary>Slow this long, s, before a step down; a step down judged after this long.</summary>
        public const float SlowFor = 3f;
        public const float TrialFor = 8f;

        /// <summary>A step down is kept if the frames are this much shorter for it.</summary>
        public const float Helped = 0.95f;

        /// <summary>After a step down that did not help, the smoke is left alone this long, s.</summary>
        public const float LeftAloneFor = 120f;

        /// <summary>Running well this long, s, before a step up, doubling to the most each time a step up is undone within <see cref="RaiseOnTrial"/> s.</summary>
        public const float WellFor = 30f;
        public const float MostWellFor = 480f;
        public const float RaiseOnTrial = 20f;

        /// <summary>A frame longer than this, s, is a hitch or a loading screen: not counted.</summary>
        public const float Hitch = 0.25f;

        // The frame time smoothed over about this long, s.
        private const float Smoothing = 1f;

        /// <summary>The quality to draw at: High, Medium, or Low.</summary>
        public SmokeQuality Quality { get; private set; } = SmokeQuality.High;

        /// <summary>Why the quality last changed, for the log; null if it has not.</summary>
        public string Why { get; private set; }

        private float frame;
        private float slow;
        private float well;
        private float wellFor = WellFor;
        private float leftAlone;
        // A step down on trial: how long ago, and the frame time before it. A step up: how long ago.
        private float trial = -1f;
        private float before;
        private float raised = -1f;

        /// <summary>Back to High, as when the player picks a quality himself.</summary>
        public void Reset()
        {
            Quality = SmokeQuality.High;
            Why = null;
            frame = slow = well = leftAlone = 0f;
            wellFor = WellFor;
            trial = raised = -1f;
        }

        /// <summary>
        /// A frame of <paramref name="seconds"/>, with <paramref name="smoke"/>
        /// in the air or not, the game held to <paramref name="heldTo"/>
        /// frames a second (0: not held). True when the quality changed.
        /// </summary>
        public bool Step(float seconds, bool smoke, float heldTo = 0f)
        {
            if (!(seconds > 0f) || seconds > Hitch)
                return false;
            frame = frame <= 0f ? seconds : frame + (seconds - frame) * (1f - (float)Math.Exp(-seconds / Smoothing));
            var slowFrame = 1f / (heldTo > 0f ? Math.Min(SlowBelow, 0.8f * heldTo) : SlowBelow);
            var wellFrame = 1f / (heldTo > 0f ? Math.Min(WellFrom, 0.93f * heldTo) : WellFrom);
            var isSlow = frame > slowFrame;
            slow = isSlow ? slow + seconds : 0f;
            well = frame <= wellFrame ? well + seconds : 0f;
            if (leftAlone > 0f)
                leftAlone -= seconds;

            // A step down on trial: kept if it shortened the frames.
            if (trial >= 0f)
            {
                trial += seconds;
                if (trial < TrialFor)
                    return false;
                trial = -1f;
                slow = well = 0f;
                if (frame <= before * Helped)
                    return false;
                leftAlone = LeftAloneFor;
                return Set(Quality - 1, "a step down did not help: slow for another reason");
            }

            // A step up on trial: undone at once if the slowness comes back.
            if (raised >= 0f)
            {
                raised += seconds;
                if (slow >= SlowFor && raised <= RaiseOnTrial)
                {
                    raised = -1f;
                    wellFor = Math.Min(MostWellFor, wellFor * 2f);
                    slow = well = 0f;
                    return Set(Quality + 1, "slow again after a step up");
                }
                if (raised > RaiseOnTrial)
                {
                    raised = -1f;
                    wellFor = WellFor;
                }
            }

            if (slow >= SlowFor && smoke && Quality != SmokeQuality.Low && leftAlone <= 0f)
            {
                trial = 0f;
                before = frame;
                raised = -1f;
                well = 0f;
                return Set(Quality + 1, $"{1f / frame:F0} frames a second with smoke in the air");
            }
            if (well >= wellFor && Quality != SmokeQuality.High)
            {
                raised = 0f;
                slow = well = 0f;
                return Set(Quality - 1, $"{1f / frame:F0} frames a second for {wellFor:F0} s");
            }
            return false;
        }

        private bool Set(SmokeQuality quality, string why)
        {
            Quality = quality < SmokeQuality.High ? SmokeQuality.High : quality > SmokeQuality.Low ? SmokeQuality.Low : quality;
            Why = why;
            return true;
        }
    }
}
