using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A live-steam injector, as on a real loco: it needs pressure to pick up,
/// spills from the overflow until it does, sings while it feeds the boiler
/// with warm water and a little live steam, and knocks off when pressure sags
/// or the tender runs dry, after which only closing and reopening it restarts
/// it. Pressures are absolute bar, as the game's boiler gives them.
/// </summary>
public class InjectorTests
{
    private const float Dt = 0.05f;
    private const float MaxDelivery = 4f;    // kg/s at rated pressure, the stock injector's full rate
    private const float RatedPressure = 15f; // bar, absolute

    /// <summary>Textbook-like steam properties; the game passes its own steam tables.</summary>
    private sealed class Steam : ISteamProperties
    {
        public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
        public float WaterEnthalpy(float temperature) => 4.19f * temperature;
        public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
    }

    private static readonly Steam Water = new Steam();

    private static Injector NewInjector() => new Injector(new InjectorSpec(MaxDelivery, RatedPressure));

    private static InjectorInputs At(float pressure, float lever = 1f, float tender = 10_000f, float space = 5_000f) => new InjectorInputs
    {
        SteamValve = lever,
        BoilerPressure = pressure,
        TenderWater = tender,
        BoilerSpace = space,
        TenderWaterTemperature = 15f,
    };

    private static InjectorFlow Run(Injector injector, InjectorInputs inputs, float seconds)
    {
        var flow = default(InjectorFlow);
        for (var t = 0f; t < seconds; t += Dt)
            flow = injector.Step(Dt, inputs, Water);
        return flow;
    }

    [Fact]
    public void AClosedInjectorDoesNothing()
    {
        var flow = Run(NewInjector(), At(12f, lever: 0f), 2f);

        Assert.Equal(InjectorState.Off, flow.State);
        Assert.Equal(0f, flow.WaterIn);
        Assert.Equal(0f, flow.TenderDraw);
        Assert.Equal(0f, flow.SteamUsed);
    }

    [Fact]
    public void AtWorkingPressureItSpillsWhilePickingUpThenFeeds()
    {
        var injector = NewInjector();

        var starting = Run(injector, At(12f), 0.5f);
        Assert.Equal(InjectorState.PickingUp, starting.State);
        Assert.Equal(0f, starting.WaterIn);
        Assert.True(starting.Overflow > 0f);

        var working = Run(injector, At(12f), 2f);
        Assert.Equal(InjectorState.Working, working.State);
        Assert.True(working.WaterIn > 0f);
        Assert.Equal(0f, working.Overflow);
        Assert.True(working.SteamUsed > 0f);
    }

    [Fact]
    public void BelowItsStartingPressureItOnlySpills()
    {
        var flow = Run(NewInjector(), At(2f), 3f);

        Assert.Equal(InjectorState.KnockedOff, flow.State);
        Assert.Equal(0f, flow.WaterIn);
        Assert.True(flow.Overflow > 0f);
        Assert.True(flow.OverflowSteam > 0f);
    }

    [Fact]
    public void SaggingPressureKnocksItOffUntilItIsClosedAndReopened()
    {
        var injector = NewInjector();
        Run(injector, At(12f), 3f);

        Assert.Equal(InjectorState.KnockedOff, Run(injector, At(2f), 1f).State);
        Assert.Equal(InjectorState.KnockedOff, Run(injector, At(12f), 3f).State);

        Assert.Equal(InjectorState.Off, Run(injector, At(12f, lever: 0f), 0.5f).State);
        Assert.Equal(InjectorState.Working, Run(injector, At(12f), 3f).State);
    }

    [Fact]
    public void ItHoldsOnBelowItsStartingPressure()
    {
        // Once singing it keeps going down to a lower pressure than it needs to start.
        var injector = NewInjector();
        Run(injector, At(12f), 3f);

        Assert.Equal(InjectorState.Working, Run(injector, At(InjectorSpec.DefaultStartPressure - 0.5f), 2f).State);
    }

    [Fact]
    public void DeliveryRisesWithPressureToTheFullRate()
    {
        var low = NewInjector();
        var rated = NewInjector();

        var atLow = Run(low, At(5f), 3f);
        var atRated = Run(rated, At(RatedPressure), 3f);

        Assert.True(atLow.WaterIn < atRated.WaterIn);
        Assert.Equal(MaxDelivery, atRated.WaterIn, 3);
        Assert.Equal(0.5f * MaxDelivery, Run(NewInjector(), At(RatedPressure, lever: 0.5f), 3f).WaterIn, 3);
    }

    [Fact]
    public void ItFeedsWarmWaterAndAboutOneKiloOfSteamPerTwelveOfWater()
    {
        var flow = Run(NewInjector(), At(12f), 3f);

        Assert.InRange(flow.FeedTemperature, 55f, 95f);
        Assert.InRange(flow.WaterIn / flow.SteamUsed, 11f, 15f);
        Assert.Equal(flow.WaterIn - flow.SteamUsed, flow.TenderDraw, 4);
    }

    [Fact]
    public void ADryTenderKnocksItOff()
    {
        var injector = NewInjector();
        Run(injector, At(12f), 3f);

        var flow = Run(injector, At(12f, tender: 0f), 0.5f);

        Assert.Equal(InjectorState.KnockedOff, flow.State);
        Assert.Equal(0f, flow.WaterIn);
        Assert.Equal(0f, flow.TenderDraw);
    }

    [Fact]
    public void AFullBoilerTakesNoMoreWater()
    {
        var injector = NewInjector();
        Run(injector, At(12f), 3f);

        var flow = Run(injector, At(12f, space: 0f), 0.5f);

        Assert.Equal(0f, flow.WaterIn);
        Assert.True(flow.Overflow > 0f, "the water it cannot deliver goes out the overflow");
    }

    [Fact]
    public void DeliveryNeverExceedsWhatTheTenderAndBoilerAllow()
    {
        var injector = NewInjector();
        Run(injector, At(12f), 3f);

        var flow = injector.Step(Dt, At(12f, tender: 0.01f, space: 0.02f), Water);

        Assert.True(flow.WaterIn * Dt <= 0.02f + 1e-6f);
        Assert.True(flow.TenderDraw * Dt <= 0.01f + 1e-6f);
    }
}
