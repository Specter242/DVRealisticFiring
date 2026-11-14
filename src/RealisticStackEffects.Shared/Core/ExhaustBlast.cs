using System;

namespace RealisticStackEffects.Core
{
    /// <summary>
    /// One exhaust beat's particles, let out as its steam leaves the
    /// cylinder (<see cref="ExhaustBlast"/>): those of the blowdown by the
    /// time since release, those of the return stroke by the crank.
    /// </summary>
    public struct BeatRelease
    {
        /// <summary>Particles leaving in the blowdown, and how many have.</summary>
        public int Blown;
        public int BlownOut;

        /// <summary>Particles pushed out on the return stroke, and how many have.</summary>
        public int Displaced;
        public int DisplacedOut;

        /// <summary>Since the valve opened to exhaust, s.</summary>
        public float Age;

        /// <summary>The return stroke done, 0 to 1.</summary>
        public float Stroke;

        /// <summary>The return stroke's pace, strokes a second, as last stepped.</summary>
        public float StrokeRate;

        /// <summary>The blowdown's time constant, s.</summary>
        public float BlowdownTime;

        public bool Done => BlownOut >= Blown && DisplacedOut >= Displaced;
    }

 public static class ExhaustBlast
    {
        /// <summary>The pressure the cylinder blows down to, bar.</summary>
        public const float BackPressure = 1f;

        // Steam expanding as it blows down: p v^n constant.
        private const float Expansion = 1.3f;

        /// <summary>
        /// The blowdown's time constant, s: the cylinder and its exhaust
        /// passage (about 0.2 m³ on a large engine) emptying through the
        /// blast nozzle (about 0.02 m²) at the steam's speed of sound.
        /// </summary>
        public const float BlowdownTime = 0.06f;

        // A beat barely over the back pressure still leaves a little of
        // its steam at once.
        private const float LeastBlowdown = 0.1f;

        // Steam still in a cylinder whose crank has stopped is not kept
        // waiting longer than this, s.
        private const float Stale = 3f;

        /// <summary>
        /// The share of the cylinder's steam that leaves in the blowdown,
        /// released at <paramref name="pressure"/> bar: what the fall to the
        /// back pressure lets out, the rest filling the cylinder at the back
        /// pressure for the piston to push out.
        /// </summary>
        public static float BlowdownShare(float pressure)
        {
            if (!(pressure > BackPressure))
                return LeastBlowdown;
            var left = (float)Math.Pow(BackPressure / pressure, 1.0 / Expansion);
            return Math.Max(LeastBlowdown, 1f - left);
        }

        /// <summary>
        /// A beat of <paramref name="particles"/> released at
        /// <paramref name="pressure"/> bar, <paramref name="since"/> s ago.
        /// </summary>
        public static BeatRelease Begin(int particles, float pressure, float since = 0f, float blowdownTime = BlowdownTime)
        {
            var count = Math.Max(0, particles);
            var blown = count == 0 ? 0 : Math.Min(count, Math.Max(1, (int)Math.Round(count * BlowdownShare(pressure))));
            return new BeatRelease
            {
                Blown = blown,
                Displaced = count - blown,
                Age = Math.Max(0f, since),
                BlowdownTime = Math.Max(1e-3f, blowdownTime),
            };
        }

        /// <summary>
        /// Carries the beat on <paramref name="seconds"/>, the crank turning
        /// <paramref name="turned"/> revolutions meanwhile (the return stroke
        /// is half of one).
        /// </summary>
        public static void Step(ref BeatRelease beat, float seconds, float turned)
        {
            var dt = Math.Max(0f, seconds);
            var strokes = 2f * Math.Abs(turned);
            beat.Age += dt;
            beat.Stroke = Math.Min(1f, beat.Stroke + strokes);
            beat.StrokeRate = dt > 1e-5f ? strokes / dt : 0f;
            if (beat.Age > Stale)
            {
                beat.BlownOut = beat.Blown;
                beat.DisplacedOut = beat.Displaced;
            }
        }

        /// <summary>
        /// The beat's next particle due by now, if any: the stack's
        /// <paramref name="flow"/> as it leaves, against the blowdown's
        /// first surge (0 to 1), and how long ago it was <paramref name="due"/>, s.
        /// Call until false after each <see cref="Step"/>.
        /// </summary>
        public static bool Next(ref BeatRelease beat, out float flow, out float due)
        {
            flow = 0f;
            due = 0f;
            // The blowdown dies away as exp(-t/T): its particles leave at
            // even shares of it, the k-th when 1 - exp(-t/T) passes its
            // share, the flow then what is left of the surge.
            if (beat.BlownOut < beat.Blown)
            {
                var share = (beat.BlownOut + 0.5f) / beat.Blown;
                var at = -beat.BlowdownTime * (float)Math.Log(1f - share);
                if (at <= beat.Age)
                {
                    beat.BlownOut++;
                    flow = 1f - share;
                    due = beat.Age - at;
                    return true;
                }
            }
            // The piston's speed over its stroke is a half sine: its
            // particles leave at even shares of (1 - cos(pi x)) / 2.
            if (beat.DisplacedOut < beat.Displaced)
            {
                var share = (beat.DisplacedOut + 0.5f) / beat.Displaced;
                var at = (float)(Math.Acos(1.0 - 2.0 * share) / Math.PI);
                if (at <= beat.Stroke)
                {
                    beat.DisplacedOut++;
                    // Its flow against the surge's: particles a second
                    // here, over the blowdown's first.
                    var rate = beat.Displaced * 0.5f * (float)(Math.PI * Math.Sin(Math.PI * at)) * beat.StrokeRate;
                    var surge = Math.Max(1, beat.Blown) / beat.BlowdownTime;
                    flow = Math.Min(1f, rate / surge);
                    due = beat.StrokeRate > 1e-5f ? (beat.Stroke - at) / beat.StrokeRate : 0f;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The speed a particle leaves the stack at, m/s, for the stack's
        /// <paramref name="flow"/> (0 to 1 of the surge's): the blast's
        /// <paramref name="peak"/> at the surge, a drifting engine's
        /// <paramref name="least"/> with no flow behind it.
        /// </summary>
        public static float Speed(float flow, float peak, float least) =>
            least + (Math.Max(least, peak) - least) * Math.Min(1f, Math.Max(0f, flow));
    }
}
