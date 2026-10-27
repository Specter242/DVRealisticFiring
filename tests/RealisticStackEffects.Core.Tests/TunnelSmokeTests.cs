using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// Smoke under a tunnel's roof: held under it and between the walls, and
/// spread along it by a ceiling jet that slows.
/// </summary>
public class TunnelSmokeTests
{
    [Fact]
    public void A_particle_sits_under_the_roof_by_a_share_of_its_size_up_to_a_metre()
    {
        Assert.Equal(6.4f, TunnelSmoke.Cap(7f, 2f, 0f), 3);
        Assert.Equal(6f, TunnelSmoke.Cap(7f, 5f, 0f), 3);
    }

    [Fact]
    public void A_low_roof_still_lets_the_smoke_leave_the_stack()
    {
        Assert.Equal(5f, TunnelSmoke.Cap(5.2f, 4f, 5f), 3);
    }

    [Fact]
    public void A_particle_keeps_off_the_walls_by_a_share_of_its_size()
    {
        Assert.Equal(TunnelSmoke.HalfWidth, TunnelSmoke.WallReach(0f), 3);
        Assert.True(TunnelSmoke.WallReach(4f) < TunnelSmoke.HalfWidth);
        Assert.Equal(0f, TunnelSmoke.WallReach(100f));
    }

    [Fact]
    public void The_ceiling_jet_slows_as_it_goes()
    {
        var first = TunnelSmoke.JetSpeed(3f, 0f);
        Assert.Equal(TunnelSmoke.JetShare * 3f, first, 3);
        Assert.True(TunnelSmoke.JetSpeed(3f, TunnelSmoke.JetSlowing) < 0.4f * first);
        Assert.Equal(0f, TunnelSmoke.JetSpeed(-1f, 0f));
    }
}
