using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Smoky, slow firing soots the tubes and hard work scours them; hard water
/// scales the heating surface as it boils off; drifting hooked up with the
/// throttle shut wears the engine.
/// </summary>
public class BoilerFoulingTests
{
    private const float Dt = 1f;
    private const float Surface = 280f;

    private static float SootAfter(float hours, float soot, float flow, BoilerFouling? fouling = null)
    {
        fouling ??= new BoilerFouling();
        for (var t = 0f; t < hours * 3600f; t += Dt)
            fouling.Step(Dt, new BoilerFoulingInputs { Soot = soot, GasFlow = flow });
        return fouling.SootLoss;
    }

    [Fact]
    public void Black_smoke_at_half_flow_soots_the_tubes_in_an_hour_and_a_clean_fire_hardly()
    {
        Assert.InRange(SootAfter(1f, 0.5f, 0.5f), 0.03f, 0.06f);
        Assert.True(SootAfter(1f, 0.05f, 0.7f) < 0.01f);
    }

    [Fact]
    public void Hard_clean_work_scours_sooted_tubes()
    {
        var fouling = new BoilerFouling();
        fouling.Restore(0f, 0.08f);
        Assert.True(SootAfter(1f, 0.05f, 1f, fouling) < 0.02f);
    }

    [Fact]
    public void Hard_water_scales_the_surface_slowly_and_a_washout_loosens_half()
    {
        var fouling = new BoilerFouling();
        // A hundred hours of hard work on bad water: 6 kg/s boiled at 800 ppm.
        fouling.Step(Dt, new BoilerFoulingInputs { Boiled = 6f * 3600f * 100f, FeedSolids = 800f, HeatingSurface = Surface });
        var loss = fouling.Loss(Surface);
        Assert.InRange(loss, 0.02f, 0.06f);
        fouling.Washout();
        Assert.Equal(loss / 2f, fouling.Loss(Surface), 3);
        fouling.Clean(1f);
        Assert.Equal(0f, fouling.Loss(Surface));
    }

    [Fact]
    public void Drifting_hooked_up_with_the_throttle_shut_wears_the_engine_but_not_in_full_gear_or_with_steam()
    {
        Assert.True(Drifting.Wear(0f, 0.2f, 20f) > 0f);
        Assert.Equal(0f, Drifting.Wear(0f, 0.9f, 20f));
        Assert.Equal(0f, Drifting.Wear(0.05f, 0.2f, 20f));
        Assert.Equal(0f, Drifting.Wear(0f, 0.2f, 3f));
        Assert.InRange(Drifting.Wear(0f, 0f, 20f) * 3600f, 0.049f, 0.051f);
    }
}
