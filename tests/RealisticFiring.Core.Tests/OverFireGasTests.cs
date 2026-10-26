using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class OverFireGasTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);
    private const float Dt = 0.1f;

    private static void Run(FireModel fire, FireInputs inputs, float seconds)
    {
        for (var t = 0f; t < seconds; t += Dt)
            fire.Step(Dt, inputs);
    }

    [Fact]
    public void FreshCoalGivesItsZoneTallFlamesUntilItHasCaught()
    {
        var fire = FireTestKit.BurningFire(S282, 60f, FireTestKit.CleanCoal);
        var inputs = FireTestKit.Inputs(0.6f);
        Run(fire, inputs, 300f);
        var settled = fire.ZoneOverFireGas(GrateZone.MidRight);

        fire.AddCoal(5f, GrateZone.MidLeft);
        Run(fire, inputs, 10f);
        var fresh = fire.ZoneOverFireGas(GrateZone.MidLeft);
        var burnedDown = fire.ZoneOverFireGas(GrateZone.MidRight);

        Assert.True(fresh > 3f * burnedDown, $"fresh {fresh * 1000f:F1} g/s against burned-down {burnedDown * 1000f:F1} g/s");
        Assert.True(burnedDown < 1.5f * settled + 1e-4f, $"burned-down {burnedDown * 1000f:F1} g/s, {settled * 1000f:F1} before");

        Run(fire, inputs, 300f);
        var caught = fire.ZoneOverFireGas(GrateZone.MidLeft);
        Assert.True(caught < 0.3f * fresh, $"{caught * 1000f:F1} g/s once caught, {fresh * 1000f:F1} fresh");
    }
}
