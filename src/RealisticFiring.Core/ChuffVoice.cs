using System;

namespace RealisticFiring.Core
{
    /// <summary>One beat's character: how much louder, later, and higher it sounds than the engine's plain beat.</summary>
    public struct BeatCharacter
    {
        /// <summary>Loudness, × (about 0.85 to 1.1).</summary>
        public float Volume;

        /// <summary>Lateness, a share of the time between beats (0 to about 0.06).</summary>
        public float Lateness;

        /// <summary>Pitch, × (about 0.97 to 1.03).</summary>
        public float Pitch;
    }

 public static class ChuffVoice
    {
        /// <summary>Beats a second below which the single beats play in full, and above which the loops take over by <see cref="BeatsGone"/>.</summary>
        public const float BeatsFull = 3f;

        /// <summary>Beats a second by which the single beats have handed over to the game's loops.</summary>
        public const float BeatsGone = 5f;

        /// <summary>The exhaust's gauge pressure, bar, at which a beat is at its loudest: where the game's loops are.</summary>
        public const float LoudestGauge = 6f;

        /// <summary>Loudness's rise with the exhaust: a little more than linear at the soft end, so a hooked-up beat is quiet but there.</summary>
        public const float LoudnessPower = 0.75f;

        /// <summary>Half the span, bar, over which one clip set gives way to the next around the game's thresholds.</summary>
        public const float TierBand = 1f;

 public const float SharpestPitch = 0.06f;

        /// <summary>A drifting engine's panting at the drag of drifting hooked up, against a working beat's loudness.</summary>
        public const float BreathLoudness = 0.12f;

        /// <summary>The panting's pitch: lower and duller than a beat of steam.</summary>
        public const float BreathPitch = 0.8f;

        /// <summary>The speed of sound, m/s.</summary>
        public const float SoundSpeed = 343f;

        /// <summary>The share of a beat a near wall gives back as an echo (inferred).</summary>
        public const float Reflection = 0.35f;

        /// <summary>Walls nearer than this, m, are too close to hear as an echo apart from the beat itself.</summary>
        public const float NearestEcho = 8f;

        /// <summary>Walls farther than this, m, give back too little to hear.</summary>
        public const float FarthestEcho = 80f;

        /// <summary>The highest frequency, Hz, an echo brings back: the wall and the longer way take the highs (inferred).</summary>
        public const float WallCutoff = 5000f;

        /// <summary>The tunnel's first returns of a beat, off its walls and roof close round the chimney.</summary>
        public const int TunnelEchoes = 3;

        /// <summary>A wind this strong, m/s, or more, carries a beat least far.</summary>
        public const float CarryWind = 10f;

        private static readonly float[] TunnelDelays = { 0.03f, 0.06f, 0.1f };
        private static readonly float[] TunnelLoudness = { 0.45f, 0.3f, 0.18f };

        /// <summary>The time, s, of the tunnel's <paramref name="i"/>th return after the beat (inferred: a bore about 5 m round the chimney).</summary>
        public static float TunnelEchoDelay(int i) => TunnelDelays[i];

        /// <summary>The tunnel's <paramref name="i"/>th return's loudness against the beat's (inferred).</summary>
        public static float TunnelEchoLoudness(int i) => TunnelLoudness[i];

        /// <summary>The share of the single beats' loudness at <paramref name="beatsPerSecond"/>: in full to <see cref="BeatsFull"/>, gone by <see cref="BeatsGone"/>.</summary>
        public static float BeatShare(float beatsPerSecond) =>
            1f - LoopShare(beatsPerSecond);

        /// <summary>The share of the game's loops at <paramref name="beatsPerSecond"/>, the rest of <see cref="BeatShare"/>.</summary>
        public static float LoopShare(float beatsPerSecond) =>
            Clamp01((Math.Abs(beatsPerSecond) - BeatsFull) / (BeatsGone - BeatsFull));

        /// <summary>A beat's loudness, 0 to 1, by the exhaust's pressure at release, bar absolute.</summary>
        public static float Loudness(float releasePressure)
        {
            var gauge = Clamp01((releasePressure - 1f) / LoudestGauge);
            return gauge <= 0f ? 0f : (float)Math.Pow(gauge, LoudnessPower);
        }

        /// <summary>
        /// The shares of the soft, medium, and hard clip sets at
        /// <paramref name="releasePressure"/>: each gives way to the next over
        /// <see cref="TierBand"/> either side of the game's thresholds, keeping
        /// the sum of their squares (the power) at one.
        /// </summary>
        public static (float Soft, float Medium, float Hard) Tiers(float releasePressure, float mediumAt, float hardAt)
        {
            var toMedium = Clamp01((releasePressure - (mediumAt - TierBand)) / (2f * TierBand));
            var toHard = Clamp01((releasePressure - (hardAt - TierBand)) / (2f * TierBand));
            var soft = (float)Math.Cos(toMedium * Math.PI / 2);
            var medium = (float)Math.Sin(toMedium * Math.PI / 2) * (float)Math.Cos(toHard * Math.PI / 2);
            var hard = (float)Math.Sin(toHard * Math.PI / 2);
            return (Clean(soft), Clean(medium), Clean(hard));
        }

        /// <summary>A beat's pitch rising with the beats' rate, to <see cref="SharpestPitch"/> by the hand-over to the loops.</summary>
        public static float Sharpen(float beatsPerSecond) =>
            1f + SharpestPitch * Clamp01((Math.Abs(beatsPerSecond) - 1f) / (BeatsGone - 1f));

        /// <summary>
        /// Each beat of a cycle's character for an engine, from
        /// <paramref name="seed"/> (its car, so each engine always sounds the
        /// same): the two sides of a two-cylinder engine differ a little in
        /// loudness and one side is a little late, as real valve gear set
        /// slightly out was; each end of a cylinder a little different again.
        /// Beats alternate sides (0 and 2 one cylinder's ends, 1 and 3 the
        /// other's); an engine with more cylinders takes them in turn.
        /// </summary>
        public static BeatCharacter[] Character(int seed, int beatsPerCycle)
        {
            var random = new Random(seed);
            float Spread(float half) => (float)(random.NextDouble() * 2.0 - 1.0) * half;
            var cylinders = Math.Max(1, beatsPerCycle / 2);
            var sideVolume = new float[cylinders];
            var sideLateness = new float[cylinders];
            for (var c = 0; c < cylinders; c++)
            {
                sideVolume[c] = 1f + Spread(0.08f);
                sideLateness[c] = c == 0 ? 0f : (float)random.NextDouble() * 0.05f;
            }
            var beats = new BeatCharacter[Math.Max(1, beatsPerCycle)];
            for (var b = 0; b < beats.Length; b++)
            {
                var side = b % cylinders;
                beats[b] = new BeatCharacter
                {
                    Volume = sideVolume[side] * (1f + Spread(0.05f)),
                    Lateness = Clamp01(sideLateness[side] + (float)random.NextDouble() * 0.015f),
                    Pitch = 1f + Spread(0.02f),
                };
            }
            return beats;
        }

        /// <summary>
        /// A drifting engine's panting, 0 to about <see cref="BreathLoudness"/>
        /// × 1.5: as loud as its cylinders pump (<paramref name="pumpingMep"/>,
        /// bar, the drifting drag's mean pressure, against the drag hooked up),
        /// a little louder while the snifting valves draw in air.
        /// </summary>
        public static float Breath(float pumpingMep, bool sniftingOpen)
        {
            var share = Math.Min(1.5f, Math.Abs(pumpingMep) / Drifting.DragHookedUp);
            return BreathLoudness * share * (sniftingOpen ? 1.3f : 1f);
        }

        /// <summary>The time, s, an echo off a wall <paramref name="distance"/> m from the chimney comes after the beat, heard by the engine.</summary>
        public static float EchoDelay(float distance) => 2f * Math.Max(0f, distance) / SoundSpeed;

        /// <summary>An echo's loudness against its beat's, off a wall <paramref name="distance"/> m away: none nearer than <see cref="NearestEcho"/> or past <see cref="FarthestEcho"/>.</summary>
        public static float EchoLoudness(float distance)
        {
            if (!(distance >= NearestEcho) || distance > FarthestEcho)
                return 0f;
            return Reflection * NearestEcho / distance;
        }

        /// <summary>
        /// The highest frequency, Hz, that reaches a listener
        /// <paramref name="distance"/> m away: the air and the ground take the
        /// highs first, less on a still, damp day (<paramref name="carry"/> 1
        /// still and damp, 0 windy and dry; inferred).
        /// </summary>
        public static float HeardCutoff(float distance, float carry)
        {
            var reach = 600f + 1200f * Clamp01(carry);
            var share = Clamp01(distance / reach);
            return 22000f * (float)Math.Pow(2500f / 22000f, share);
        }

        /// <summary>
        /// How far the beats carry today, 0 to 1 (<see cref="HeardCutoff"/>):
        /// farthest on a still, damp day, least in a strong, dry wind
        /// (<paramref name="windSpeed"/> m/s, <paramref name="humidity"/> 0 to 1; inferred).
        /// </summary>
        public static float Carry(float windSpeed, float humidity) =>
            Clamp01(0.5f * Clamp01(humidity) + 0.5f * (1f - Clamp01(Math.Abs(windSpeed) / CarryWind)));

        /// <summary>
        /// The game's loops' level for an exhaust at <paramref name="releasePressure"/>
        /// (bar absolute): their curve rises evenly over <see cref="LoudestGauge"/>
        /// bar, so this sets them as loud as the beats they take over from.
        /// </summary>
        public static float LoopLevel(float releasePressure) => LoudestGauge * Loudness(releasePressure);

        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : float.IsNaN(x) ? 0f : x;

        private static float Clean(float x) => Math.Abs(x) < 1e-6f ? 0f : x;
    }
}
