using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class InjectorValveTests
{
    private const float Dt = 0.05f;
    private const float Off = 0f;
    private const float Prime = InjectorSpec.SteamPrime;
    private const float Feed = 1f;

    private sealed class Steam : ISteamProperties
    {
        public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
        public float WaterEnthalpy(float temperature) => 4.19f * temperature;
        public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
        public float SaturationTemperature(float pressure) => 100f + 8f * (pressure - 1f);
    }

    private static readonly Steam Water = new Steam();

    private static Injector NonLifter() => new Injector(new InjectorSpec(4.6f, 15f));

    private static Injector Lifter() => new Injector(new InjectorSpec(4f, 15f) { Lifting = true });

    private static InjectorFlow Run(Injector injector, float steam, float water, float seconds, float pressure = 14f)
    {
        var flow = default(InjectorFlow);
        for (var t = 0f; t < seconds - 1e-4f; t += Dt)
            flow = injector.Step(Dt, new InjectorInputs
            {
                SteamValve = steam,
                WaterValve = water,
                BoilerPressure = pressure,
                TenderWater = 10_000f,
                BoilerSpace = 5_000f,
                TenderWaterTemperature = 15f,
            }, Water);
        return flow;
    }

    [Fact]
    public void ANonLiftersWaterRunsOutOfTheOverflowUntilItIsStarted()
    {
        var injector = NonLifter();
        var shut = Run(injector, Off, 0f, 1f);
        Assert.Equal(InjectorState.Off, shut.State);
        Assert.Equal(0f, shut.Overflow);

        var water = Run(injector, Off, 1f, 1f);
        Assert.Equal(InjectorState.Off, water.State);
        Assert.True(water.Overflow > 0.5f, $"gravity runs {water.Overflow:F2} kg/s out of the overflow");
        Assert.Equal(water.Overflow, water.TenderDraw);
        Assert.Equal(0f, water.WaterIn);
    }

    [Fact]
    public void PrimedThenOpenedItFeedsAsTheWaterValveSetsWithAQuietOverflow()
    {
        var injector = NonLifter();
        var priming = Run(injector, Prime, 0.6f, 0.5f);
        Assert.Equal(InjectorState.Priming, priming.State);
        Assert.True(priming.Overflow > 0f);
        Assert.Equal(0f, priming.OverflowSteam);
        var picked = Run(injector, Prime, 0.6f, 2f);
        Assert.Equal(InjectorState.Working, picked.State);

        var full = Run(injector, Feed, 1f, 2f);
        var part = Run(injector, Feed, 0.6f, 2f);
        Assert.Equal(InjectorState.Working, part.State);
        Assert.Equal(0f, part.Overflow);
        Assert.Equal(0f, part.OverflowSteam);
        Assert.InRange(part.WaterIn / full.WaterIn, 0.55f, 0.65f);
    }

    [Fact]
    public void OpenedWideBeforeItPicksUpTheJetBreaks()
    {
        var injector = NonLifter();
        var flow = Run(injector, Feed, 1f, 1f);
        Assert.Equal(InjectorState.Broken, flow.State);
        Assert.True(flow.OverflowSteam > 0f);
    }

    [Fact]
    public void SteamWithTheWaterShutBlowsFromTheOverflowAsTheSteamValveIsOpen()
    {
        var primed = Run(NonLifter(), Prime, 0f, 1f);
        var wide = NonLifter();
        Run(wide, Prime, 1f, 3f);
        var dry = Run(wide, Feed, 0f, 1f);
        Assert.Equal(InjectorState.NoWater, primed.State);
        Assert.Equal(InjectorState.NoWater, dry.State);
        Assert.Equal(0f, dry.WaterIn);
        Assert.True(dry.OverflowSteam > 3f * primed.OverflowSteam, $"wide {dry.OverflowSteam:F2} kg/s against primed {primed.OverflowSteam:F2}");
    }

    [Fact]
    public void ALifterRunsNoWaterUntilItsSteamLiftsIt()
    {
        var injector = Lifter();
        var shut = Run(injector, Off, 1f, 1f);
        Assert.Equal(0f, shut.Overflow);
        var lifting = Run(injector, Prime, 1f, 0.5f);
        Assert.Equal(InjectorState.Priming, lifting.State);
        Assert.True(lifting.Overflow > 0f);
        Run(injector, Prime, 1f, 2f);
        var working = Run(injector, Feed, 1f, 2f);
        Assert.Equal(InjectorState.Working, working.State);
        Assert.True(working.WaterIn > 3f);
    }

    [Fact]
    public void TurnedDownBelowItsLeastTheWaterValveTrickles()
    {
        var injector = NonLifter();
        Run(injector, Prime, 1f, 3f);
        var least = injector.Spec.LeastShare(14f);
        var low = Run(injector, Feed, 0.5f * least, 2f);
        Assert.Equal(InjectorState.Working, low.State);
        Assert.True(low.Overflow > 0f);
        Assert.Equal(0f, low.OverflowSteam);
    }
}
