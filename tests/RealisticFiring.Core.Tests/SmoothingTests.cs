using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class SmoothingTests
{
    [Fact]
    public void SmoothDampConvergesWithoutOvershoot()
    {
        float value = 0f, velocity = 0f, max = 0f;
        for (var i = 0; i < 400; i++)
        {
            value = Smoothing.SmoothDamp(value, 10f, ref velocity, 1f, 0.02f);
            max = Math.Max(max, value);
        }

        Assert.Equal(10f, value, 3);
        Assert.True(max <= 10f);
    }

    [Fact]
    public void SmoothDampWithZeroDeltaKeepsTheValue()
    {
        var velocity = 0f;
        Assert.Equal(3f, Smoothing.SmoothDamp(3f, 10f, ref velocity, 1f, 0f));
    }

    // Rising fast under the startup speedup (smooth time 0.2 s), then the
    // speedup ends (5 s) as the target drops: SmoothDamp's leftover momentum
    // ran the smoke reading from 0.12 to over 3 (319 % was seen in game).
    private static (float start, float peak, float end) SlowDownAfterFastRise(Func<float, float, float, float, float> step)
    {
        var value = 0f;
        for (var i = 0; i < 3; i++)
            value = step(value, 1f, 0.2f, 0.02f);
        var start = value;
        var peak = value;
        for (var i = 0; i < 500; i++)
        {
            value = step(value, 0f, 5f, 0.02f);
            peak = Math.Max(peak, value);
        }
        return (start, peak, value);
    }

    [Fact]
    public void SmoothDampRunsAwayWhenItsSmoothingSlowsMidRise()
    {
        var velocity = 0f;
        var (_, peak, _) = SlowDownAfterFastRise((v, t, s, dt) => Smoothing.SmoothDamp(v, t, ref velocity, s, dt));

        Assert.True(peak > 1f, $"peak {peak}");
    }

    [Fact]
    public void SmoothApproachNeverMovesAwayFromItsTarget()
    {
        var velocity = 0f;
        var (start, peak, end) = SlowDownAfterFastRise((v, t, s, dt) => Smoothing.SmoothApproach(v, t, ref velocity, s, dt));

        Assert.True(peak <= start + 1e-6f, $"rose from {start} to {peak}");
        Assert.True(end < start);
    }

    [Fact]
    public void SmoothApproachMatchesSmoothDampWhileHeadingForTheTarget()
    {
        float a = 0f, b = 0f, va = 0f, vb = 0f;
        for (var i = 0; i < 100; i++)
        {
            a = Smoothing.SmoothDamp(a, 10f, ref va, 1f, 0.02f);
            b = Smoothing.SmoothApproach(b, 10f, ref vb, 1f, 0.02f);
        }

        Assert.Equal(a, b, 5);
    }
}
