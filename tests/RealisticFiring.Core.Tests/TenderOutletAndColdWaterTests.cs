using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A nearly empty tender loses its water off the outlet when it is pulled
/// away from it or stands nose-up; cold water stands lower than hot.
/// </summary>
public class TenderOutletAndColdWaterTests
{
    // Climbing 2 % with the tender's front (its outlet) uphill.
    private const float UphillPull = -TenderOutlet.Gravity * 0.02f;

    [Fact]
    public void A_full_enough_tank_keeps_its_outlet_covered_on_a_grade_and_starting()
    {
        Assert.True(TenderOutlet.OutletDepth(0.2f, UphillPull - 0.5f) > 0f);
    }

    [Fact]
    public void A_nearly_empty_tank_uncovers_it_climbing_or_pulling_away_but_not_braking()
    {
        Assert.True(TenderOutlet.OutletDepth(0.03f, 0f) > 0f);
        Assert.True(TenderOutlet.OutletDepth(0.03f, UphillPull - 0.3f) < 0f);
        Assert.True(TenderOutlet.OutletDepth(0.03f, UphillPull + 0.6f) > 0f);
    }

    [Fact]
    public void Cold_water_stands_four_percent_lower_than_boiling()
    {
        Assert.InRange(ColdWater.Shrink(25f), 0.035f, 0.042f);
        Assert.Equal(0f, ColdWater.Shrink(100f), 4);
        Assert.True(ColdWater.Shrink(60f) < ColdWater.Shrink(20f));
    }
}
