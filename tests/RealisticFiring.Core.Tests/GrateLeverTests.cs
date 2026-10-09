using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The cab's fire-dump lever also rocks the grate, as on a real loco: a quick
/// rock off center and back shakes the ash through; holding it over past the
/// stock dump point dumps the fire. Works for notched, sprung, and free levers.
/// </summary>
public class GrateLeverTests
{
    private const float Dt = 0.05f;

    private static (int shakes, float maxDump) Move(GrateLever lever, float value, float seconds)
    {
        int shakes = 0;
        float maxDump = 0f;
        for (var t = 0f; t < seconds; t += Dt)
        {
            var state = lever.Update(value, Dt);
            shakes += state.Shakes;
            maxDump = Math.Max(maxDump, state.Dump);
        }
        return (shakes, maxDump);
    }

    [Fact]
    public void AtCenterNothingHappens()
    {
        var lever = new GrateLever();

        Assert.Equal((0, 0f), Move(lever, 0.5f, 5f));
    }

    [Fact]
    public void AQuickRockShakesOnceAndDoesNotDump()
    {
        var lever = new GrateLever();

        var over = Move(lever, 1f, 0.4f);
        var back = Move(lever, 0.5f, 0.4f);

        Assert.Equal(0f, over.maxDump);
        Assert.Equal(1, over.shakes + back.shakes);
    }

    [Theory]
    [InlineData(0.62f, 1, 0)]
    [InlineData(0.38f, 1, 0)]
    [InlineData(0.75f, 0, 1)]
    [InlineData(0.25f, 0, 1)]
    public void AShortRockWigglesAndAHalfStrokeShakes(float reach, int wiggles, int shakes)
    {
        // "Wiggling" and "shaking – half stroke" (1944 NY firing course p.88).
        var lever = new GrateLever();
        int w = 0, s = 0;
        foreach (var value in new[] { reach, 0.5f })
        {
            for (var t = 0f; t < 0.4f; t += Dt)
            {
                var state = lever.Update(value, Dt);
                w += state.Wiggles;
                s += state.Shakes;
            }
        }

        Assert.Equal((wiggles, shakes), (w, s));
    }

    [Fact]
    public void RockingSideToSideShakesOnEachPass()
    {
        var lever = new GrateLever();
        var shakes = 0;
        for (var i = 0; i < 3; i++)
        {
            shakes += Move(lever, 0.2f, 0.3f).shakes;
            shakes += Move(lever, 0.8f, 0.3f).shakes;
        }
        shakes += Move(lever, 0.5f, 0.3f).shakes;

        Assert.True(shakes >= 5, $"{shakes} shakes");
    }

    [Fact]
    public void HoldingItOverDumpsLikeStockAndIsNotAShake()
    {
        var lever = new GrateLever();

        var held = Move(lever, 0f, 3f);
        var back = Move(lever, 0.5f, 1f);

        Assert.Equal(1f, held.maxDump, 3);
        Assert.Equal(0, held.shakes + back.shakes);
        Assert.Equal(0f, lever.Update(0.5f, Dt).Dump);
    }

    [Fact]
    public void HoldingItPartlyOverOnlyRocks()
    {
        // Inside the stock dead zone the lever never dumps, however long it is held.
        var lever = new GrateLever();

        Assert.Equal(0f, Move(lever, 0.65f, 5f).maxDump);
    }
}
