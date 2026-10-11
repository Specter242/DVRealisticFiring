using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A live-steam injector worked by one lever, as on a real loco: cracked, it
/// spills water from the overflow until it primes, then sings while it feeds
/// the boiler with warm water and a little live steam, and can be opened up.
/// Opened before it primes, the jet breaks and blows steam until the lever is
/// shut. Steam blowing through heats it, and a hot injector will not take
/// hold until water running through it cools it. Once it has taken, it
/// restarts by itself after sagging pressure or a dry tender. Pressures are
/// absolute bar, as the game's boiler gives them.
/// </summary>
public class InjectorTests
{
    private const float Dt = 0.05f;
    private const float MaxDelivery = 4f;    // kg/s at rated pressure, the stock injector's full rate
    private const float RatedPressure = 15f; // bar, absolute
    private const float Crack = 0.1f;

    /// <summary>Textbook-like steam properties; the game passes its own steam tables.</summary>
    private sealed class Steam : ISteamProperties
    {
        public float SteamEnthalpy(float pressure) => 2675f + 9f * (pressure - 1f);
        public float WaterEnthalpy(float temperature) => 4.19f * temperature;
        public float WaterTemperature(float enthalpy) => enthalpy / 4.19f;
        public float SaturationTemperature(float pressure) => 100f + 8f * (pressure - 1f);
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

    // Cracks it until it primes, then opens it to the lever given.
    private static InjectorFlow Start(Injector injector, float pressure, float lever = 1f)
    {
        Run(injector, At(pressure, Crack), 2f);
        return Run(injector, At(pressure, lever), 3f);
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
    public void CrackedItSpillsWaterWhilePrimingThenFeeds()
    {
        var injector = NewInjector();

        var priming = Run(injector, At(12f, Crack), 0.5f);
        Assert.Equal(InjectorState.Priming, priming.State);
        Assert.Equal(0f, priming.WaterIn);
        Assert.True(priming.Overflow > 0f);
        Assert.Equal(0f, priming.OverflowSteam);

        // Singing at the crack, below its least, it feeds but wastes at the
        // overflow: water only, and well under the priming spill, so the
        // overflow quiets as it picks up, the cue to open it.
        var working = Run(injector, At(12f, Crack), 1.5f);
        Assert.Equal(InjectorState.Working, working.State);
        Assert.True(working.WaterIn > 0f);
        Assert.True(working.Overflow > 0f);
        Assert.True(working.Overflow < 0.6f * priming.Overflow, $"spills {working.Overflow:F2} kg/s singing against {priming.Overflow:F2} priming");
        Assert.Equal(0f, working.OverflowSteam);
        Assert.True(working.SteamUsed > 0f);

        // Opened, it feeds clean.
        var open = Run(injector, At(12f), 1f);
        Assert.Equal(InjectorState.Working, open.State);
        Assert.Equal(0f, open.Overflow);
        Assert.Equal(0f, open.OverflowSteam);
    }

    [Fact]
    public void ItTurnsDownToAboutTwoFifthsItsDeliveryLessAtHighPressure()
    {
        // Sellers' self-acting injectors under test: 37 % from 60 to 120 psi,
        // 45 % at 200, 49 % at 225 (Kneass 1910; RME 1939).
        var spec = new InjectorSpec(MaxDelivery, RatedPressure);
        Assert.InRange(spec.LeastShare(6f), 0.36f, 0.38f);
        Assert.InRange(spec.LeastShare(14.8f), 0.44f, 0.46f);
        Assert.InRange(spec.LeastShare(16.5f), 0.48f, 0.50f);
        Assert.True(spec.LeastShare(14f) > spec.LeastShare(10f));
        spec.TechniqueAndHeat = false;
        Assert.Equal(0f, spec.LeastShare(14f));
    }

    [Fact]
    public void TurnedDownBelowItsLeastItHoldsTheJetButWastesAtTheOverflow()
    {
        var injector = NewInjector();
        var full = Start(injector, 12f);
        var least = injector.Spec.LeastShare(12f);

        var atLeast = Run(injector, At(12f, least + 0.01f), 2f);
        Assert.Equal(InjectorState.Working, atLeast.State);
        Assert.Equal(0f, atLeast.Overflow);

        var under = Run(injector, At(12f, 0.5f * least), 2f);
        Assert.Equal(InjectorState.Working, under.State);
        Assert.True(under.Overflow > 0f, "water wastes at the overflow");
        Assert.Equal(0f, under.OverflowSteam);
        // It feeds less than its least, and less than the lever's share of the full jet.
        Assert.True(under.WaterIn < 0.5f * least * full.WaterIn);
        // The feed is as warm as ever.
        Assert.Equal(atLeast.FeedTemperature, under.FeedTemperature, 1);
    }

    [Fact]
    public void OpenedBeforeItPrimesTheJetBreaksUntilTheLeverIsShut()
    {
        var injector = NewInjector();

        var broken = Run(injector, At(12f), 0.5f);
        Assert.Equal(InjectorState.Broken, broken.State);
        Assert.Equal(0f, broken.WaterIn);
        Assert.True(broken.OverflowSteam > 0f, "steam blows from the overflow");

        Assert.Equal(InjectorState.Broken, Run(injector, At(12f, Crack), 1f).State);

        Assert.Equal(InjectorState.Off, Run(injector, At(12f, lever: 0f), 0.5f).State);
        Assert.Equal(InjectorState.Working, Run(injector, At(12f, Crack), 20f).State);
    }

    [Fact]
    public void OpenedThroughTheCrackInOneMovementTheJetBreaks()
    {
        var injector = NewInjector();

        injector.Step(Dt, At(12f, Crack), Water);
        injector.Step(Dt, At(12f, 0.5f), Water);

        Assert.Equal(InjectorState.Broken, Run(injector, At(12f), 1f).State);
    }

    [Fact]
    public void BelowItsStartingPressureItSpillsWaterAndPicksUpWhenPressureRises()
    {
        var injector = NewInjector();

        var low = Run(injector, At(2f, Crack), 3f);
        Assert.Equal(InjectorState.LowPressure, low.State);
        Assert.Equal(0f, low.WaterIn);
        Assert.True(low.Overflow > 0f);
        Assert.Equal(0f, low.OverflowSteam);

        Assert.Equal(InjectorState.Working, Run(injector, At(12f, Crack), 2f).State);
    }

    [Fact]
    public void AfterSaggingPressureItRestartsByItself()
    {
        var injector = NewInjector();
        Start(injector, 12f);

        var sagged = Run(injector, At(2f), 1f);
        Assert.Equal(InjectorState.LowPressure, sagged.State);
        Assert.True(sagged.Overflow > 0f);

        Assert.Equal(InjectorState.Working, Run(injector, At(12f), 3f).State);
    }

    [Fact]
    public void ItHoldsOnBelowItsStartingPressure()
    {
        // Once singing it keeps going down to a lower pressure than it needs to start.
        var injector = NewInjector();
        Start(injector, 12f);

        Assert.Equal(InjectorState.Working, Run(injector, At(InjectorSpec.DefaultStartPressure - 0.5f), 2f).State);
    }

    [Fact]
    public void DeliveryRisesWithPressureToTheFullRate()
    {
        var atLow = Start(NewInjector(), 5f);
        var atRated = Start(NewInjector(), RatedPressure);

        Assert.True(atLow.WaterIn < atRated.WaterIn);
        Assert.Equal(MaxDelivery, atRated.WaterIn, 3);
        Assert.Equal(0.5f * MaxDelivery, Start(NewInjector(), RatedPressure, lever: 0.5f).WaterIn, 3);
    }

    [Fact]
    public void ItFeedsWarmWaterAndAboutOneKiloOfSteamPerTwelveOfWater()
    {
        var flow = Start(NewInjector(), 12f);

        Assert.InRange(flow.FeedTemperature, 55f, 95f);
        Assert.InRange(flow.WaterIn / flow.SteamUsed, 11f, 15f);
        Assert.Equal(flow.WaterIn - flow.SteamUsed, flow.TenderDraw, 4);
    }

    [Fact]
    public void ADryTenderBlowsSteamUntilItIsRefilledThenItRestarts()
    {
        var injector = NewInjector();
        Start(injector, 12f);

        var dry = Run(injector, At(12f, tender: 0f), 0.5f);
        Assert.Equal(InjectorState.NoWater, dry.State);
        Assert.Equal(0f, dry.WaterIn);
        Assert.Equal(0f, dry.TenderDraw);
        Assert.True(dry.OverflowSteam > 0f);

        Assert.Equal(InjectorState.Working, Run(injector, At(12f), 3f).State);
    }

    [Fact]
    public void BlowingSteamHeatsItTooHotToRestartUntilWaterRunningThroughCoolsIt()
    {
        var injector = NewInjector();
        Start(injector, 12f);
        Run(injector, At(12f, tender: 0f), 10f);

        Assert.True(injector.Temperature > injector.RestartLimitAt(12f, 1f));
        Assert.Equal(InjectorState.Broken, Run(injector, At(12f), 0.5f).State);

        Run(injector, At(12f, lever: 0f), 0.5f);
        var hot = Run(injector, At(12f, Crack), 0.5f);
        Assert.Equal(InjectorState.Hot, hot.State);
        Assert.True(hot.Overflow > 0f && hot.OverflowSteam > 0f, "steam and hot water spill");
        Assert.Equal(0f, hot.WaterIn);

        Assert.Equal(InjectorState.Working, Run(injector, At(12f, Crack), 20f).State);
    }

    [Fact]
    public void AShutInjectorCoolsOverMinutes()
    {
        var injector = NewInjector();
        Run(injector, At(12f), 10f);
        Run(injector, At(12f, lever: 0f), 30f);

        Assert.True(injector.Temperature > injector.RestartLimitAt(12f, Crack), "still too hot after half a minute");

        Run(injector, At(12f, lever: 0f), 600f);
        Assert.True(injector.Temperature < injector.RestartLimitAt(12f, 1f), "cool enough to take hold wide open after ten minutes");
    }

    [Fact]
    public void TheHottestItTakesHoldFallsWithPressureAtTheNozzle()
    {
        var injector = NewInjector();
        const float psi = 1f / 14.5038f;

        // Kneass's Sellers non-lifter: 135 °F at 60 psi, 106 °F at 200 psi.
        Assert.Equal(57.2f, injector.RestartLimitAt(1f + 60f * psi, 1f), 0);
        Assert.Equal(41.1f, injector.RestartLimitAt(1f + 200f * psi, 1f), 0);
        Assert.True(injector.RestartLimitAt(12f, 0.5f) > injector.RestartLimitAt(12f, 1f), "a part-open lever throttles the steam");
    }

    [Fact]
    public void WithoutTechniqueAndHeatItOnlyNeedsPressureAndWater()
    {
        var injector = NewInjector();
        injector.Spec.TechniqueAndHeat = false;

        Assert.Equal(InjectorState.Priming, Run(injector, At(12f), 0.5f).State);
        Assert.Equal(InjectorState.Working, Run(injector, At(12f), 2f).State);

        Run(injector, At(12f, tender: 0f), 30f);
        Assert.Equal(InjectorState.Working, Run(injector, At(12f), 3f).State);
    }

    [Fact]
    public void AFullBoilerTakesNoMoreWater()
    {
        var injector = NewInjector();
        Start(injector, 12f);

        var flow = Run(injector, At(12f, space: 0f), 0.5f);

        Assert.Equal(0f, flow.WaterIn);
        Assert.True(flow.Overflow > 0f, "the water it cannot deliver goes out the overflow");
    }

    [Fact]
    public void DeliveryNeverExceedsWhatTheTenderAndBoilerAllow()
    {
        var injector = NewInjector();
        Start(injector, 12f);

        var flow = injector.Step(Dt, At(12f, tender: 0.01f, space: 0.02f), Water);

        Assert.True(flow.WaterIn * Dt <= 0.02f + 1e-6f);
        Assert.True(flow.TenderDraw * Dt <= 0.01f + 1e-6f);
    }
}
