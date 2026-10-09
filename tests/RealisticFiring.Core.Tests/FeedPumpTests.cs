using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// A feedwater heater and pump, as on a Worthington SA (open) or an Elesco
/// (closed): fed evenly with water heated near exhaust saturation, about
/// 115 °C at 15 psi in the heater, for some 2.5 % of its feed in steam;
/// minutes to warm; the open type's drifting valve halves it with the
/// throttle shut, and it races if left open through a hook-up; low tender
/// water loses the suction, and a closed heater's turbine trips.
/// </summary>
public class FeedPumpTests
{
    private const float Dt = 0.05f;
    private const float Capacity = 10f;
    private const float FullExhaust = 8f;
    private const float Chest = 15f;

    // Set to feed what the engine evaporates: as much water as it exhausts.
    private const float Matched = FullExhaust / Capacity;

    /// <summary>The game's own steam tables.</summary>
    private sealed class GameSteam : ISteamProperties
    {
        public float SteamEnthalpy(float p) => 2658f + p * (26.9f + p * (-2.16f + p * (0.0792f + p * -0.00106f)));
        public float WaterEnthalpy(float temperature) => 4.39f * temperature - 24f;
        public float WaterTemperature(float enthalpy) => (enthalpy + 24f) / 4.39f;
        public float SaturationTemperature(float pressure) => WaterTemperature(422f * (float)Math.Pow(pressure, 0.256));
    }

    private static readonly GameSteam Steam = new GameSteam();

    private static FeedPump Open() => new FeedPump(new FeedPumpSpec(FeedHeaterType.Open, Capacity));

    private static FeedPump Closed() => new FeedPump(new FeedPumpSpec(FeedHeaterType.Closed, Capacity));

    private static FeedPumpInputs In(float valve = 1f, float exhaust = FullExhaust, float chest = Chest, float tender = 20_000f, float space = 5_000f) =>
        new FeedPumpInputs
        {
            Valve = valve,
            BoilerPressure = 16f,
            BoilerSpace = space,
            TenderWater = tender,
            TenderWaterTemperature = 15f,
            ExhaustFlow = exhaust,
            FullExhaustFlow = FullExhaust,
            ChestPressure = chest,
        };

    private static FeedPumpFlow Run(FeedPump pump, FeedPumpInputs inputs, float seconds)
    {
        var flow = default(FeedPumpFlow);
        for (var t = 0f; t < seconds; t += Dt)
            flow = pump.Step(Dt, inputs, Steam);
        return flow;
    }

    [Fact]
    public void ShutItDoesNothing()
    {
        var flow = Run(Open(), In(valve: 0f), 5f);

        Assert.Equal(FeedPumpState.Off, flow.State);
        Assert.Equal(0f, flow.WaterIn);
        Assert.Equal(0f, flow.SteamUsed);
    }

    [Fact]
    public void WorkingAndWarmItFeedsNearExhaustSaturationForAFewPercentSteam()
    {
        var flow = Run(Open(), In(valve: Matched), 900f);

        Assert.Equal(FeedPumpState.Feeding, flow.State);
        Assert.Equal(FullExhaust, flow.WaterIn, 2);
        // 238–242 °F at 15 psi in the heater: about 115 °C; the PRR's road tests 191–211 °F.
        Assert.InRange(flow.FeedTemperature, 105f, 118f);
        Assert.InRange(flow.SteamUsed / flow.WaterIn, 0.015f, 0.03f);
        Assert.True(flow.Condensed > 0f && flow.TenderDraw < flow.WaterIn);
    }

    [Fact]
    public void ItTakesMinutesForTheHeaterToWarm()
    {
        var pump = Open();

        var first = Run(pump, In(valve: Matched), 30f);
        var later = Run(pump, In(valve: Matched), 420f - 30f);

        Assert.True(first.FeedTemperature < 60f, $"cold at first ({first.FeedTemperature:F0} C)");
        Assert.True(later.FeedTemperature > 100f, $"hot after about seven minutes ({later.FeedTemperature:F0} C)");
    }

    [Fact]
    public void ShutOffTheDriftingValveHalvesAnOpenHeatersPumpAndTheFeedCools()
    {
        var pump = Open();
        Run(pump, In(), 900f);

        var drifting = Run(pump, In(valve: 0.25f, exhaust: 0f, chest: 1f), 10f);
        Assert.Equal(FeedPumpState.Choked, drifting.State);
        Assert.Equal(0.25f * Capacity * 0.5f, drifting.WaterIn, 2);

        var later = Run(pump, In(valve: 0.25f, exhaust: 0f, chest: 1f), 600f);
        Assert.True(later.FeedTemperature < 80f, $"cooling with no exhaust ({later.FeedTemperature:F0} C)");
    }

    [Fact]
    public void AClosedHeatersPumpKeepsItsRateDrifting()
    {
        var pump = Closed();
        Run(pump, In(), 900f);

        var drifting = Run(pump, In(exhaust: 0f, chest: 1f), 5f);

        Assert.Equal(FeedPumpState.Feeding, drifting.State);
        Assert.Equal(Capacity, drifting.WaterIn, 2);
    }

    [Fact]
    public void LeftOpenThroughAShutOffAnOpenHeatersPumpRacesButEasedFirstItDoesNot()
    {
        var wide = Open();
        Run(wide, In(valve: Matched), 900f);
        var racing = Run(wide, In(valve: Matched, exhaust: 0.5f), 0.5f);
        Assert.Equal(FeedPumpState.Racing, racing.State);
        Assert.True(racing.WaterIn < 0.3f * Capacity);
        Assert.True(racing.VentSteam > 0f, "the flashing water vents as steam");

        var eased = Open();
        Run(eased, In(valve: Matched), 900f);
        Run(eased, In(valve: 0.25f), 5f);
        Assert.NotEqual(FeedPumpState.Racing, Run(eased, In(valve: 0.25f, exhaust: 0.5f), 0.5f).State);
    }

    [Fact]
    public void ALowTenderLosesTheSuctionUntilItIsShutRefilledAndPrimed()
    {
        var pump = Open();
        Run(pump, In(), 60f);

        var lost = Run(pump, In(tender: 100f), 2f);
        Assert.Equal(FeedPumpState.LostSuction, lost.State);
        Assert.Equal(0f, lost.WaterIn);
        Assert.True(lost.Speed > 1f, "the cold pump runs away");

        Assert.Equal(FeedPumpState.LostSuction, Run(pump, In(), 2f).State);
        Run(pump, In(valve: 0f), 1f);
        Assert.Equal(FeedPumpState.Feeding, Run(pump, In(), 2f).State);
    }

    [Fact]
    public void AClosedHeatersTurbineTripsOnLostWaterAndResetsWhenShutAWhile()
    {
        var pump = Closed();
        Run(pump, In(), 60f);

        Assert.Equal(FeedPumpState.Tripped, Run(pump, In(tender: 100f), 1f).State);
        Run(pump, In(valve: 0f), 2f);
        Assert.Equal(FeedPumpState.Tripped, Run(pump, In(), 0.5f).State);

        Run(pump, In(valve: 0f), 6f);
        Assert.Equal(FeedPumpState.Feeding, Run(pump, In(), 1f).State);
    }

    [Fact]
    public void ALightExhaustCannotHeatAHeavyFeedAllTheWay()
    {
        var light = Run(Open(), In(valve: Matched, exhaust: 2f), 900f);
        var full = Run(Open(), In(valve: Matched), 900f);

        Assert.True(light.FeedTemperature < full.FeedTemperature - 20f,
            $"light exhaust {light.FeedTemperature:F0} C against full {full.FeedTemperature:F0} C");
    }

    [Fact]
    public void ItTakesAFewSecondsToComeUpToSpeedAndRunDown()
    {
        var pump = Open();

        var starting = Run(pump, In(valve: Matched), 1f);
        var running = Run(pump, In(valve: Matched), 15f);
        Assert.True(starting.WaterIn < 0.5f * running.WaterIn, $"{starting.WaterIn:F1} kg/s a second after opening, {running.WaterIn:F1} once up to speed");

        var shut = Run(pump, In(valve: 0f), 1f);
        Assert.Equal(0f, shut.WaterIn);
        Assert.True(shut.Speed > 0.3f * running.Speed, "it runs down, not stops dead");
    }

    [Fact]
    public void AFullBoilerTakesNoMore()
    {
        var flow = Run(Open(), In(space: 0f), 5f);

        Assert.Equal(0f, flow.WaterIn);
        Assert.Equal(0f, flow.TenderDraw);
    }
}
