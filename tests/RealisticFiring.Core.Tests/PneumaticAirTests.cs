using System;
using System.Linq;
using RealisticFiring.Core;
using Xunit;

namespace RealisticFiring.Core.Tests;

 public class PneumaticAirTests
{
    private const int Rate = 48000;

    private static double Rms(float[] s, int from, int count) => Math.Sqrt(s.Skip(from).Take(count).Sum(x => (double)x * x) / count);

    private static double Crossings(float[] s) => s.Zip(s.Skip(1), (a, b) => a * b < 0f ? 1 : 0).Sum() / (s.Length / (double)Rate);

    [Fact]
    public void The_burst_is_sharp_and_dies_away()
    {
        var burst = PneumaticAir.Burst(Rate, 1);
        Assert.Equal((int)(PneumaticAir.BurstSeconds * Rate), burst.Length);
        Assert.Equal(0.9f, burst.Max(Math.Abs), 3);
        var first = Rms(burst, 0, Rate / 10);
        var last = Rms(burst, burst.Length - Rate / 10, Rate / 10);
        Assert.True(first > 8 * last, $"first tenth {first:F3}, last {last:F3}");
        // Its loudest within the first hundredth of a second: a burst, not a swell.
        var peakAt = Array.IndexOf(burst, burst.OrderByDescending(Math.Abs).First());
        Assert.InRange(peakAt, 0, Rate / 100 * 3);
        Assert.Equal(0f, burst[burst.Length - 1], 4);
        Assert.True(Crossings(burst) > 2000, $"crossings {Crossings(burst):F0} a second: air, not a tone");
    }

    [Fact]
    public void The_hiss_is_steady_high_and_loops_without_a_seam()
    {
        var hiss = PneumaticAir.Hiss(Rate, 2);
        Assert.Equal((int)(PneumaticAir.HissSeconds * Rate), hiss.Length);
        Assert.Equal(0.25, Rms(hiss, 0, hiss.Length), 3);
        // Steady: each tenth within 2 dB of the whole.
        for (var i = 0; i + Rate / 10 <= hiss.Length; i += Rate / 10)
            Assert.InRange(20 * Math.Log10(Rms(hiss, i, Rate / 10) / 0.25), -2.0, 2.0);
        Assert.True(Crossings(hiss) > 3000, $"crossings {Crossings(hiss):F0} a second");
        // The join from its end to its start is no bigger a step than its samples usually take.
        var steps = hiss.Zip(hiss.Skip(1), (a, b) => Math.Abs(b - a)).OrderBy(x => x).ToArray();
        var typical = steps[(int)(steps.Length * 0.99)];
        Assert.True(Math.Abs(hiss[0] - hiss[hiss.Length - 1]) <= typical, $"seam {Math.Abs(hiss[0] - hiss[hiss.Length - 1]):F3} against {typical:F3}");
    }

    [Fact]
    public void The_same_seed_gives_the_same_sound() =>
        Assert.Equal(PneumaticAir.Burst(Rate, 5), PneumaticAir.Burst(Rate, 5));
}
