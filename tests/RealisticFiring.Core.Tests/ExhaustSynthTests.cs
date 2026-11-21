using System;
using System.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>The exhaust synthesizer's physics: a beat per face, steam only while the crank turns, more with more steam.</summary>
public class ExhaustSynthTests
{
    private const int Rate = 48000;

    private static (float[] Sound, float[] Flow) Run(ExhaustSpec spec, ExhaustState state, float seconds, int seed = 1)
    {
        var synth = new ExhaustSynth(spec, new ExhaustTuning(), Rate, seed);
        var n = (int)(seconds * Rate);
        var sound = new float[n];
        var flow = new float[n / 48];
        for (var i = 0; i < flow.Length; i++)
        {
            synth.Render(sound, i * 48, 48, state, state);
            flow[i] = synth.Flow;
        }
        return (sound, flow);
    }

    // Surges past half the largest flow, no two within an eighth of a turn (a slow beat's blowdown and the
    // return stroke's push are one beat).
    private static int Surges(float[] flow, float turnsPerSecond)
    {
        var above = flow.Max() / 2f;
        var gap = (int)(Rate / 48 / turnsPerSecond / 8f);
        var count = 0;
        var last = -gap;
        for (var i = 1; i < flow.Length; i++)
        {
            if (flow[i] > above && flow[i - 1] <= above && i - last >= gap)
            {
                count++;
                last = i;
            }
        }
        return count;
    }

    [Fact]
    public void A_two_cylinder_engine_beats_four_times_a_turn()
    {
        var state = new ExhaustState { TurnsPerSecond = 0.5f, Cutoff = 0.5f, Chest = 12f };
        var (_, flow) = Run(new ExhaustSpec(), state, 8.1f);
        Assert.InRange(Surges(flow, 0.5f), 15, 17);
        var three = new ExhaustSpec { Cylinders = 3 };
        var (_, flow3) = Run(three, state, 8.1f);
        Assert.InRange(Surges(flow3, 0.5f), 23, 25);
    }

    [Fact]
    public void Standing_it_is_silent()
    {
        var (sound, flow) = Run(new ExhaustSpec(), new ExhaustState { TurnsPerSecond = 0f, Cutoff = 0.75f, Chest = 14f }, 1f);
        Assert.All(flow, f => Assert.Equal(0f, f));
        Assert.True(sound.Max(Math.Abs) < 1e-3f);
    }

    [Fact]
    public void More_steam_at_release_blows_harder()
    {
        var hooked = Run(new ExhaustSpec(), new ExhaustState { TurnsPerSecond = 0.75f, Cutoff = 0.25f, Chest = 12f }, 4f).Flow.Max();
        var full = Run(new ExhaustSpec(), new ExhaustState { TurnsPerSecond = 0.75f, Cutoff = 0.75f, Chest = 12f }, 4f).Flow.Max();
        Assert.True(full > 1.5f * hooked, $"full gear {full} kg/s, hooked up {hooked} kg/s");
    }

    [Fact]
    public void Its_sound_is_finite_bounded_and_the_same_from_the_same_seed()
    {
        var state = new ExhaustState { TurnsPerSecond = 2f, Cutoff = 0.4f, Chest = 14f };
        var a = Run(new ExhaustSpec(), state, 2f, seed: 7).Sound;
        var b = Run(new ExhaustSpec(), state, 2f, seed: 7).Sound;
        Assert.All(a, x => Assert.True(float.IsFinite(x) && Math.Abs(x) <= 1f));
        Assert.Equal(a, b);
        Assert.True(a.Max(Math.Abs) > 0.01f);
    }

    // The level's fall, dB, over 80 ms of a beat's tail: from 40 to 80 ms after its surge is spent (the flow back under
    // a quarter of its peak) to 120 to 160 ms after, averaged over the beats.
    private static double TailFall(float turnsPerSecond)
    {
        var state = new ExhaustState { TurnsPerSecond = turnsPerSecond, Cutoff = 0.5f, Chest = 13f };
        var (sound, flow) = Run(new ExhaustSpec(), state, 12f);
        var top = flow.Max();
        double early = 0.0, late = 0.0;
        for (var i = 1; i + 160 < flow.Length; i++)
        {
            if (!(flow[i] < top / 4f && flow[i - 1] >= top / 4f))
                continue;
            for (var k = 40 * 48; k < 80 * 48; k++)
            {
                early += sound[i * 48 + k] * sound[i * 48 + k];
                late += sound[i * 48 + k + 80 * 48] * sound[i * 48 + k + 80 * 48];
            }
        }
        return 10.0 * Math.Log10(early / late);
    }

    private static float[] Sound(ExhaustTuning tuning, ExhaustState state, float seconds)
    {
        var synth = new ExhaustSynth(new ExhaustSpec(), tuning, Rate, 3);
        var sound = new float[(int)(seconds * Rate)];
        for (var i = 0; i + 480 <= sound.Length; i += 480)
            synth.Render(sound, i, 480, state, state);
        return sound;
    }

    // The beat-rate part of the 5 ms loudness, a share of its mean (the chuff lab's measure), after the first second.
    private static double Beat(float[] sound, double beatsPerSecond)
    {
        const int hop = Rate / 200;
        var frames = (sound.Length - Rate) / hop;
        var env = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var k = 0; k < hop; k++)
            {
                double x = sound[Rate + f * hop + k];
                sum += x * x;
            }
            env[f] = Math.Sqrt(sum / hop);
        }
        var mean = env.Average();
        double re = 0, im = 0;
        for (var f = 0; f < frames; f++)
        {
            var angle = 2.0 * Math.PI * beatsPerSecond * f / 200.0;
            re += (env[f] - mean) * Math.Cos(angle);
            im += (env[f] - mean) * Math.Sin(angle);
        }
        return 2.0 * Math.Sqrt(re * re + im * im) / frames / mean;
    }

    private static double Rms(float[] sound) => Math.Sqrt(sound.Skip(Rate).Average(x => (double)x * x));

    [Fact]
    public void At_speed_the_beat_stays_between_a_roar_and_a_machine_gun()
    {
        foreach (var turns in new[] { 4.75f, 5f, 6f })
        {
            var state = new ExhaustState { TurnsPerSecond = turns, Cutoff = 0.22f, Chest = 12.5f };
            var beats = turns * 4.0;
            var kept = Sound(new ExhaustTuning(), state, 5f);
            var roar = Sound(new ExhaustTuning { StaccatoDepth = 0f }, state, 5f);
            var gun = Sound(new ExhaustTuning { StaccatoFrom = 12f, StaccatoFull = 18f, StaccatoDepth = 1.1f }, state, 5f);
            Assert.InRange(Beat(kept, beats), 0.06, 0.14);
            Assert.True(Beat(kept, beats) >= Beat(roar, beats) - 0.005, $"{beats}: {Beat(kept, beats):0.000} under the roar's {Beat(roar, beats):0.000}");
            Assert.True(Beat(kept, beats) < 0.55 * Beat(gun, beats), $"{beats}: {Beat(kept, beats):0.000} against 997's {Beat(gun, beats):0.000}");
            Assert.InRange(20.0 * Math.Log10(Rms(kept) / Rms(roar)), -1.0, 1.0);
        }
        var fastest = new ExhaustState { TurnsPerSecond = 6f, Cutoff = 0.22f, Chest = 12.5f };
        Assert.True(Beat(Sound(new ExhaustTuning { StaccatoDepth = 0f }, fastest, 5f), 24.0) < 0.04);
        Assert.True(Beat(Sound(new ExhaustTuning(), fastest, 5f), 24.0) > 0.08);
    }

    [Fact]
    public void Below_its_pace_the_staccato_changes_nothing()
    {
        var state = new ExhaustState { TurnsPerSecond = 2.5f, Cutoff = 0.3f, Chest = 12.5f };
        Assert.Equal(Sound(new ExhaustTuning(), state, 2f), Sound(new ExhaustTuning { StaccatoDepth = 0f }, state, 2f));
        // Up to 14 beats a second (about 56 km/h on the S282) as well: everything below about 50 km/h is as it was.
        var brisk = new ExhaustState { TurnsPerSecond = 3.5f, Cutoff = 0.22f, Chest = 12.5f };
        Assert.Equal(Sound(new ExhaustTuning(), brisk, 2f), Sound(new ExhaustTuning { StaccatoDepth = 0f }, brisk, 2f));
    }

    [Fact]
    public void A_slip_asks_more_steam_than_the_pipe_passes_and_its_beats_weaken()
    {
        var slip = new ExhaustState { TurnsPerSecond = 8f, Cutoff = 0.75f, Chest = 13f };
        var fed = Rms(Sound(new ExhaustTuning(), slip, 3f));
        var unfed = Rms(Sound(new ExhaustTuning { SupplyFills = 0f }, slip, 3f));
        // Its chest and its sound a second both bounded by the pipe: at about 2.4 times the supply, over 6 dB down.
        Assert.True(20.0 * Math.Log10(unfed / fed) > 6.0, $"limited {fed:0.000}, unlimited {unfed:0.000}");
        // A start in full gear asks about 1.7: untouched.
        var start = new ExhaustState { TurnsPerSecond = 2f, Cutoff = 0.75f, Chest = 13f };
        Assert.Equal(Sound(new ExhaustTuning(), start, 2f), Sound(new ExhaustTuning { SupplyFills = 0f }, start, 2f));
    }

    [Fact]
    public void A_beat_dies_away_at_the_same_steady_rate_at_any_pace()
    {
        // The S282's front end rings down at about 80 dB a second: 6 to 7 dB in 80 ms, whatever the crank does.
        var slow = TailFall(0.4f);
        var faster = TailFall(0.75f);
        Assert.InRange(slow, 4.5, 9.5);
        Assert.InRange(faster, 4.5, 9.5);
        Assert.InRange(slow - faster, -1.5, 1.5);
    }

    [Fact]
    public void The_exhaust_port_opens_at_release_and_shuts_by_compression()
    {
        var events = new ValveGear().Events(0.5f);
        var release = (float)Math.Acos(1.0 - 2.0 * events.Release);
        var compression = (float)(2.0 * Math.PI - Math.Acos(1.0 - 2.0 * events.Compression));
        Assert.Equal(0f, events.ExhaustOpening(release - 0.05f));
        Assert.True(events.ExhaustOpening(release + 0.3f) > 0.3f);
        Assert.Equal(1f, events.ExhaustOpening((float)Math.PI * 1.25f));
        Assert.Equal(0f, events.ExhaustOpening(compression + 0.05f));
    }
}
