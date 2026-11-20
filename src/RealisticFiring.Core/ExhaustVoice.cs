using System;
using System.Diagnostics;
using System.Threading;

namespace RealisticFiring.Core
{
    /// <summary>The engine as the game last saw it. Its crank in turns and the time on the audio clock it was read.</summary>
    public struct ExhaustDrive
    {
        public double Crank;

        /// <summary>The crank's turns a second; negative running backward.</summary>
        public float TurnsPerSecond;

        public float Cutoff;

        /// <summary>The pressure acting on the pistons, bar absolute.</summary>
        public float Chest;

        public double Time;
    }

    /// <summary>
    /// An <see cref="ExhaustSynth"/> played live: the game posts the engine as
    /// it goes, and the audio thread asks for sound. Each beat is kept on the
    /// game's: the synthesizer's crank runs a little fast or slow, never
    /// jumping while it sounds, until each face's release falls where the game
    /// counts a chuff, so the sound stays with the chimney's puffs. In the cab
    /// the sound is shadowed as the chuff lab's cab listener shadows it. The
    /// sound multiplies what the audio engine passes in, which carries the
    /// source's distance and panning.
    /// </summary>
    public sealed class ExhaustVoice
    {
        // The game counts a chuff each quarter turn of a two-cylinder engine.
        private const double BeatTurns = 0.25;
        private const double CatchTime = 0.5;
        private const double MostTrim = 0.08;
        private const double RestTurns = 0.02;
        private const double LevelTime = 0.03;
        private const double CabTime = 0.25;
        private const double CabCutoff = 400.0;
        private const double CabDirect = 0.25;
        private const double OutsideEchoes = 0.4;
        private const float Silent = 1e-4f;
        public const double LiftTurns = 1.5;
        public const double MostLift = 10.0;
        private const double LiftPower = 1.5;
        private const double LiftTime = 0.3;

        private readonly ExhaustSynth synth;
        private readonly double rate;
        private readonly double levelStep, cabStep, liftStep;
        private readonly double b0, b1, b2, a1, a2;
        private readonly ExhaustDrive[] posted = new ExhaustDrive[2];
        private int newest;
        private float open, inCab, linesideGain = 1f, cabGain = 1f;

        private float[] mono = new float[0];
        private ExhaustState last;
        private bool asleep = true;
        private double opening, cabShare, lift = 1.0;
        private double x1, x2, y1, y2;
        private long busyTicks;
        private long heardFrames;
        private double worstBlock;
        private double phaseError;
        private double lastLevel;

        public ExhaustVoice(ExhaustSpec spec, ExhaustTuning tuning, int sampleRate, int seed)
        {
            synth = new ExhaustSynth(spec, tuning, sampleRate, seed);
            rate = sampleRate;
            levelStep = 1.0 - Math.Exp(-1.0 / (rate * LevelTime));
            cabStep = 1.0 - Math.Exp(-1.0 / (rate * CabTime));
            liftStep = 1.0 - Math.Exp(-1.0 / (rate * LiftTime));
            var w = 2.0 * Math.PI * CabCutoff / rate;
            var alpha = Math.Sin(w) / (2.0 * Math.Sqrt(0.5));
            var a0 = 1.0 + alpha;
            b0 = (1.0 - Math.Cos(w)) / 2.0 / a0;
            b1 = (1.0 - Math.Cos(w)) / a0;
            b2 = b0;
            a1 = -2.0 * Math.Cos(w) / a0;
            a2 = (1.0 - alpha) / a0;
        }

        /// <summary>Whether it sounds, 0 or 1; it fades over a few hundredths of a second, and closed costs nothing.</summary>
        public float Open { get => Volatile.Read(ref open); set => Volatile.Write(ref open, value); }

        /// <summary>The listener in the engine's cab, 0 or 1; the shadow comes and goes over a quarter second.</summary>
        public float InCab { get => Volatile.Read(ref inCab); set => Volatile.Write(ref inCab, value); }

        /// <summary>The gain heard from outside the cab, set to match the recordings' loudness.</summary>
        public float LinesideGain { get => Volatile.Read(ref linesideGain); set => Volatile.Write(ref linesideGain, value); }

        /// <summary>The gain heard in the cab.</summary>
        public float CabGain { get => Volatile.Read(ref cabGain); set => Volatile.Write(ref cabGain, value); }

        /// <summary>Seconds spent making sound, on the audio thread.</summary>
        public double BusySeconds => (double)Volatile.Read(ref busyTicks) / Stopwatch.Frequency;

        /// <summary>Seconds of sound made.</summary>
        public double HeardSeconds => Volatile.Read(ref heardFrames) / rate;

        /// <summary>The largest share of a block's own length spent making it.</summary>
        public double WorstBlock => Volatile.Read(ref worstBlock);

        /// <summary>The last block's RMS as made, before the audio engine's distance and panning.</summary>
        public double LastLevel => Volatile.Read(ref lastLevel);

        /// <summary>The lift slow beats get now (1 at and above <see cref="LiftTurns"/>).</summary>
        public double Lift => Volatile.Read(ref lift);

        /// <summary>The lift for a crank at <paramref name="turnsPerSecond"/>.</summary>
        public static double LiftFor(double turnsPerSecond)
        {
            var turns = Math.Abs(turnsPerSecond);
            return turns >= LiftTurns ? 1.0 : Math.Min(MostLift, Math.Pow(LiftTurns / Math.Max(turns, 1e-6), LiftPower));
        }

        /// <summary>How far the beats last were from the game's, turns.</summary>
        public double PhaseError => Volatile.Read(ref phaseError);

        /// <summary>
        /// The engine now, from the game's thread. Two slots turn about, so a
        /// post never writes the one being read; a read overlapped by two
        /// posts costs at most one block's small error in the beat's place.
        /// </summary>
        public void Post(ExhaustDrive drive)
        {
            var next = 1 - Volatile.Read(ref newest);
            posted[next] = drive;
            Volatile.Write(ref newest, next);
        }

        /// <summary>Multiplies <paramref name="data"/>, interleaved, by the next samples, at <paramref name="now"/> on the audio clock.</summary>
        public void Fill(float[] data, int channels, double now)
        {
            if (data == null || channels < 1 || data.Length < channels)
                return;
            var started = Stopwatch.GetTimestamp();
            var frames = data.Length / channels;
            var target = Open > 0f ? 1.0 : 0.0;
            if (target == 0.0 && opening < Silent)
            {
                opening = 0.0;
                asleep = true;
                Array.Clear(data, 0, data.Length);
                return;
            }
            if (mono.Length < frames)
                mono = new float[frames];
            var state = Drive(now);
            var woke = asleep;
            if (asleep)
                last = state;
            asleep = false;
            synth.EchoScale = OutsideEchoes + (1.0 - OutsideEchoes) * cabShare;
            synth.Render(mono, 0, frames, last, state);
            last = state;

            var cabTarget = InCab > 0f ? 1.0 : 0.0;
            var liftTarget = LiftFor(state.TurnsPerSecond);
            // Waking, at once: the beat was not sounding.
            if (woke)
                lift = liftTarget;
            double lineside = LinesideGain, cab = CabGain;
            double squares = 0;
            for (var f = 0; f < frames; f++)
            {
                double x = mono[f];
                var low = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1;
                x1 = x;
                y2 = y1;
                y1 = low;
                opening += (target - opening) * levelStep;
                cabShare += (cabTarget - cabShare) * cabStep;
                if (lift != liftTarget)
                    lift = Math.Abs(liftTarget - lift) < 1e-6 ? liftTarget : lift + (liftTarget - lift) * liftStep;
                var shadowed = (low + CabDirect * x) / (1.0 + CabDirect);
                var heard = (x + (shadowed - x) * cabShare) * opening * lift * (lineside + (cab - lineside) * cabShare);
                squares += heard * heard;
                for (var c = 0; c < channels; c++)
                    data[f * channels + c] *= (float)heard;
            }

            Volatile.Write(ref lastLevel, Math.Sqrt(squares / frames));
            var ticks = Stopwatch.GetTimestamp() - started;
            Volatile.Write(ref busyTicks, busyTicks + ticks);
            Volatile.Write(ref heardFrames, heardFrames + frames);
            var share = (double)ticks / Stopwatch.Frequency / (frames / rate);
            if (share > worstBlock)
                Volatile.Write(ref worstBlock, share);
        }

        // The state to run to: the game's, with the crank's speed trimmed toward the game's beat.
        private ExhaustState Drive(double now)
        {
            var drive = posted[Volatile.Read(ref newest)];
            var turns = Math.Abs((double)drive.TurnsPerSecond);
            var crank = drive.Crank + drive.TurnsPerSecond * Math.Max(0.0, now - drive.Time);
            var forward = drive.TurnsPerSecond >= 0f ? crank : -crank;
            var offBy = forward + synth.ReleaseTurns(drive.Cutoff) - synth.Crank;
            offBy -= BeatTurns * Math.Round(offBy / BeatTurns);
            Volatile.Write(ref phaseError, offBy);
            var trim = 0.0;
            if (asleep || turns < RestTurns)
                synth.Crank += offBy;
            else
                trim = Math.Max(-MostTrim * turns, Math.Min(MostTrim * turns, offBy / CatchTime));
            return new ExhaustState { TurnsPerSecond = (float)(turns + trim), Cutoff = drive.Cutoff, Chest = drive.Chest };
        }
    }
}
