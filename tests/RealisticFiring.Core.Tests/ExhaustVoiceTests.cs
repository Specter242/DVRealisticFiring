using System;
using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>The synthesizer played live: on the game's beat, in the source's level and panning, shadowed in the cab, free when closed.</summary>
public class ExhaustVoiceTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public ExhaustVoiceTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;
    private const int Rate = 48000;
    private const int Block = 1024;

    private static ExhaustVoice Voice(float open = 1f) => new ExhaustVoice(new ExhaustSpec(), new ExhaustTuning(), Rate, 7) { Open = open };

    // Runs the voice against a game crank turning steadily; the two-channel sound, each input sample as given.
    private static float[] Run(ExhaustVoice voice, float turns, double seconds, ref double time, double startCrank = 0.1, float left = 1f, float right = 1f)
    {
        var blocks = (int)(seconds * Rate / Block);
        var all = new float[blocks * Block * 2];
        var data = new float[Block * 2];
        for (var b = 0; b < blocks; b++)
        {
            voice.Post(new ExhaustDrive { Crank = Wrap(startCrank + turns * time), TurnsPerSecond = turns, Cutoff = 0.4f, Chest = 13f, Time = time });
            for (var i = 0; i < Block; i++)
            {
                data[2 * i] = left;
                data[2 * i + 1] = right;
            }
            voice.Fill(data, 2, time);
            Array.Copy(data, 0, all, b * Block * 2, data.Length);
            time += (double)Block / Rate;
        }
        return all;
    }

    private static double Wrap(double x) => x - Math.Floor(x);

    private static double Rms(float[] x, int from = 0) => Math.Sqrt(x.Skip(from).Average(v => (double)v * v));

    [Theory]
    [InlineData(1.5f)]
    [InlineData(-1.5f)]
    [InlineData(0.4f)]
    public void Its_beats_settle_on_the_games_going_either_way(float turns)
    {
        var voice = Voice();
        var time = 0.0;
        Run(voice, turns, 1.0, ref time);
        Assert.InRange(Math.Abs(voice.PhaseError), 0.0, 0.01);
    }

    [Fact]
    public void A_late_start_catches_up_by_trimming_the_speed()
    {
        var voice = Voice();
        var time = 0.0;
        Run(voice, 1.5f, 0.2, ref time);
        // The game's crank jumps a tenth of a turn, as after a stumble: the voice is still off just after, then back.
        Run(voice, 1.5f, 0.05, ref time, startCrank: 0.2);
        Assert.InRange(Math.Abs(voice.PhaseError), 0.05, 0.125);
        Run(voice, 1.5f, 6.0, ref time, startCrank: 0.2);
        Assert.InRange(Math.Abs(voice.PhaseError), 0.0, 0.01);
    }

    [Fact]
    public void It_keeps_the_sources_level_and_panning()
    {
        var voice = Voice();
        var time = 0.0;
        var sound = Run(voice, 1.5f, 1.0, ref time, left: 0.5f, right: 0.125f);
        Assert.True(Rms(sound) > 0.0);
        for (var i = 0; i < sound.Length; i += 2)
            Assert.Equal(sound[i] / 4f, sound[i + 1], 6);
    }

    [Fact]
    public void Closed_it_is_silent_and_costs_nothing()
    {
        var voice = Voice(0f);
        var time = 0.0;
        var sound = Run(voice, 1.5f, 1.0, ref time);
        Assert.All(sound, x => Assert.Equal(0f, x));
        Assert.Equal(0.0, voice.HeardSeconds);
    }

    [Fact]
    public void Opened_again_it_starts_on_the_beat()
    {
        var voice = Voice();
        var time = 0.0;
        Run(voice, 1.5f, 0.5, ref time);
        voice.Open = 0f;
        Run(voice, 1.5f, 1.0, ref time);
        voice.Open = 1f;
        Run(voice, 1.5f, 0.05, ref time);
        Assert.InRange(Math.Abs(voice.PhaseError), 0.0, 0.01);
    }

    [Fact]
    public void The_cab_takes_the_highs()
    {
        double Edge(float inCab)
        {
            var voice = Voice();
            voice.InCab = inCab;
            var time = 0.0;
            var sound = Run(voice, 1.5f, 4.0, ref time);
            var skip = Rate * 2 * 2;
            var change = 0.0;
            var level = 0.0;
            for (var i = skip + 2; i < sound.Length; i += 2)
            {
                change += Math.Pow(sound[i] - sound[i - 2], 2);
                level += sound[i] * sound[i];
            }
            return change / level;
        }

        Assert.True(Edge(1f) < Edge(0f) / 1.5, $"{Edge(1f):F3} against {Edge(0f):F3}");
        Assert.True(Edge(1f) > Edge(0f) / 6.0, $"{Edge(1f):F3} against {Edge(0f):F3}");
    }

    [Fact]
    public void Its_reflections_can_be_turned_down_and_at_full_change_nothing()
    {
        float[] Render(double? scale)
        {
            var synth = new ExhaustSynth(new ExhaustSpec(), new ExhaustTuning(), Rate, 7);
            if (scale.HasValue)
                synth.EchoScale = scale.Value;
            var state = new ExhaustState { TurnsPerSecond = 1.5f, Cutoff = 0.4f, Chest = 13f };
            var sound = new float[Rate];
            synth.Render(sound, 0, sound.Length, state, state);
            return sound;
        }

        Assert.Equal(Render(null), Render(1.0));
        Assert.NotEqual(Render(null), Render(0.4));
    }

    [Fact]
    public void A_crank_spinning_up_loses_its_lift_at_once()
    {
        // A slip: a crawl's lift, then the crank at 4 turns a second; within 0.2 s no lift is left (job 3080: eased
        // over 0.3 s, 0.6 s into a slip it was still 2.5).
        var voice = Voice();
        var time = 0.0;
        Run(voice, 0.4f, 2.0, ref time);
        Assert.True(voice.Lift > 5.0, $"lift at a crawl {voice.Lift:0.00}");
        Run(voice, 4f, 0.1, ref time);
        Assert.InRange(voice.Lift, 1.0, 1.15);
        Run(voice, 4f, 0.1, ref time);
        Assert.InRange(voice.Lift, 1.0, 1.01);
    }

    [Fact]
    public void Slow_beats_are_lifted_and_brisk_ones_left_alone()
    {
        Assert.Equal(1.0, ExhaustVoice.LiftFor(1.7));
        Assert.Equal(1.0, ExhaustVoice.LiftFor(-2.0));
        Assert.Equal(Math.Pow(ExhaustVoice.LiftTurns / 0.5, 1.5), ExhaustVoice.LiftFor(0.5), 6);
        Assert.Equal(ExhaustVoice.MostLift, ExhaustVoice.LiftFor(0.0));
        double Level(float turns)
        {
            var spec = new ExhaustSpec { Cylinders = 2, Bore = 0.66f, Stroke = 0.762f, Clearance = 0.09f, NozzleDiameter = 0.15f, ChimneyLength = 1.6f,
                ChimneyDiameter = 0.48f, Superheated = true, SteamTemperature = 330f, Gear = new ValveGear { Lap = 1.5f, Lead = 0.25f, ExhaustLap = 0f, PortWidth = 2f } };
            var voice = new ExhaustVoice(spec, new ExhaustTuning(), Rate, 7) { Open = 1f };
            var buffer = new float[512];
            double squares = 0, t = 0, crank = 0;
            long n = 0;
            for (var b = 0; b < Rate * 5 / buffer.Length; b++)
            {
                voice.Post(new ExhaustDrive { Crank = crank, TurnsPerSecond = turns, Cutoff = 0.75f, Chest = 8f, Time = t });
                for (var i = 0; i < buffer.Length; i++) buffer[i] = 1f;
                voice.Fill(buffer, 1, t);
                if (b > Rate / buffer.Length)
                    foreach (var v in buffer) { squares += v * v; n++; }
                t += buffer.Length / (double)Rate;
                crank += turns * buffer.Length / (double)Rate;
            }
            return 10.0 * Math.Log10(squares / n);
        }
        var gap = Level(1.7f) - Level(0.4f);
        output.WriteLine($"1.7 against 0.4 turns a second: {gap:F1} dB");
        Assert.InRange(gap, 0.0, 8.0);
    }
}
