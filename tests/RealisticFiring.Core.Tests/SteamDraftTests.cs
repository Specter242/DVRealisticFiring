using RealisticFiring.Core;
using RealisticStackEffects;

namespace RealisticFiring.Core.Tests;

 public class SteamDraftTests
{
    // The S060 as fitted: a 0.4 kg/s blower at 13.4 bar, natural 0.3 kg/s, its exhaust 1.95.
    private static readonly SteamDraft S060 = new SteamDraft(0.4f, 0.3f, 1.95f, 13.4f);

    [Fact]
    public void ABlowerKgDrawsMoreAirThanAnExhaustKg()
    {
        var byBlower = S060.Air(0f, 0.4f, 1f) - S060.Air(0f, 0f, 1f);
        var byExhaust = S060.Air(0.4f, 0f, 1f) - S060.Air(0f, 0f, 1f);
        Assert.Equal(0.4f * SteamDraft.BlowerEntrainment, byBlower, 3);
        Assert.Equal(0.4f * 1.95f, byExhaust, 3);
        // Standing with the blower wide open, enough air for a bright fire: 2.3 kg/s, where the game's gave 0.59.
        Assert.Equal(2.3f, S060.Air(0f, S060.BlowerSteam(13.4f, 1f), 1f), 2);
    }

    [Fact]
    public void BlowerSteamRisesWithPressureToWorkingPressure()
    {
        Assert.Equal(0f, S060.BlowerSteam(1f, 1f));
        Assert.Equal(0.2f, S060.BlowerSteam(7.2f, 1f), 3);
        Assert.Equal(0.4f, S060.BlowerSteam(13.4f, 1f), 3);
        Assert.Equal(0.4f, S060.BlowerSteam(15f, 1f), 3);
        Assert.Equal(0.1f, S060.BlowerSteam(13.4f, 0.25f), 3);
    }

    [Fact]
    public void AShutDamperLeavesATenth()
    {
        Assert.Equal(0.1f * S060.Air(2f, 0.4f, 1f), S060.Air(2f, 0.4f, 0f), 3);
    }
}
