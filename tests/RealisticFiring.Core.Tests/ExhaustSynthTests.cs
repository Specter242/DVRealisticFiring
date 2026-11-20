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
