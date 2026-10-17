using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class FiremanStokerAndPumpTests
{
    private readonly ITestOutputHelper output;

    public FiremanStokerAndPumpTests(ITestOutputHelper output) => this.output = output;

    private const float Cruise = 2.5f;
    private const float Climb = 4f;

    // The S282's retrofit stoker, at full speed, in firebox kg/s.
    private const float S282Stoker = 1.56f;

    [Fact]
    public void WithAStokerHeRunsItAndHandFiresOnlyWhatItStarves()
    {
        var engine = new Footplate(stokerFeed: S282Stoker);
        engine.EngineSteam = Cruise;
        engine.Run(300f);
        var stokedBefore = engine.Stoked;
        var firedBefore = engine.CoalFired;
        var throwsBefore = engine.Throws.Count;
        var low = float.MaxValue;
        engine.EngineSteam = Climb;
        engine.Run(600f, e => low = Math.Min(low, e.Pressure));

        var stoked = engine.Stoked - stokedBefore;
        var byHand = engine.CoalFired - firedBefore;
        var rounds = engine.Rounds().Skip(1).ToList();
        output.WriteLine($"stoked {stoked:F0} kg, by hand {byHand:F0} kg in {engine.Throws.Count - throwsBefore} throws; " +
                         $"pressure low {low:F2}; throttle {engine.StokerControls.Throttle:F2}, jets front {engine.StokerControls.LeftFront:F2} back {engine.StokerControls.LeftBack:F2}");
        Assert.True(engine.Fireman.Stoking);
        Assert.True(stoked > 4f * byHand, "the stoker does the work");
        Assert.All(rounds, r => Assert.True(r <= AutoFireman.LightRound));
        Assert.True(low >= AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve) - 2f, $"pressure fell to {low:F2}");
    }

    [Fact]
    public void StandingHeShutsTheStokerAndLeavesTheMasterCracked()
    {
        var engine = new Footplate(stokerFeed: S282Stoker);
        engine.EngineSteam = Cruise;
        engine.Run(120f);
        engine.EngineSteam = 0f;
        engine.Standing = true;
        engine.Run(30f);
        Assert.False(engine.Fireman.Stoking);
        Assert.Equal(0f, engine.StokerControls.Throttle);
        Assert.Equal(AutoFireman.CrackedMaster, engine.StokerControls.MasterJet);
    }

    [Fact]
    public void AJammedStokerIsReversedUntilItClearsThenFeedsAgain()
    {
        var engine = new Footplate(stokerFeed: S282Stoker);
        engine.EngineSteam = Climb;
        engine.Run(120f);
        engine.Stoker!.Jam();
        var reversed = false;
        var cleared = -1f;
        engine.Run(30f, e =>
        {
            reversed |= e.StokerControls.Direction == StokerDirection.Reverse;
            if (cleared < 0f && !e.Stoker!.Jammed)
                cleared = e.Time;
        });
        var stoked = engine.Stoked;
        engine.Run(20f);
        output.WriteLine($"cleared after {cleared - 120f:F1} s; fed {engine.Stoked - stoked:F1} kg in the next 20 s");
        Assert.True(reversed);
        Assert.InRange(cleared - 120f, 0f, 20f);
        Assert.Equal(StokerDirection.Forward, engine.StokerControls.Direction);
        Assert.True(engine.Stoked > stoked);
    }

    [Fact]
    public void OnceTheEngineWorksHeFeedsWithThePumpAndStandingWithTheInjector()
    {
        // A pump of the injector's size, as a 5-SA beside a 10,000 gal/h injector.
        var engine = new Footplate(injectorDelivery: 4.6f, pumpFeed: 4.2f);
        engine.EngineSteam = Cruise;
        var low = 1f;
        var high = 0f;
        engine.Run(60f);
        Assert.False(engine.Fireman.OnPump);
        engine.Run(600f, e =>
        {
            if (e.Time > AutoFireman.PumpAfter + 60f)
            {
                low = Math.Min(low, e.Glass);
                high = Math.Max(high, e.Glass);
            }
        });
        output.WriteLine($"on the pump {engine.Fireman.OnPump}, valve {engine.PumpValve:F2} ({engine.PumpFlow.State}, {engine.PumpFlow.WaterIn:F1} kg/s at {engine.PumpFlow.FeedTemperature:F0} C), " +
                         $"injector {engine.InjectorLever:F2}; glass {low:F2}-{high:F2}");
        Assert.True(engine.Fireman.OnPump);
        Assert.Equal(0f, engine.InjectorLever);
        Assert.True(engine.PumpValve > 0f);
        Assert.InRange(low, AutoFireman.GlassGoal - 0.1f, 1f);
        Assert.InRange(high, 0f, AutoFireman.GlassGoal + 0.1f);

        engine.EngineSteam = 0f;
        engine.Standing = true;
        engine.Run(30f);
        Assert.False(engine.Fireman.OnPump);
        Assert.Equal(0f, engine.PumpValve);
    }

    [Fact]
    public void RunningLightWithSteamOnHeKeepsThePump()
    {
        // Light engine with the throttle cracked: far under the work he
        // counted as working, which shut a warm pump (a player, 2026-10-16).
        var engine = new Footplate(injectorDelivery: 4.6f, pumpFeed: 4.2f) { Throttle = 0.15f };
        engine.EngineSteam = 0.3f;
        var dropped = false;
        engine.Run(AutoFireman.PumpAfter + 60f);
        Assert.True(engine.Fireman.OnPump);
        engine.Run(600f, e => dropped |= !e.Fireman.OnPump);
        Assert.False(dropped, "he shut the pump running light with steam on");

        // Drifting, steam shut: a moment's easing keeps it, a while goes to the injector.
        engine.Throttle = 0f;
        engine.EngineSteam = 0f;
        engine.Run(AutoFireman.PumpDrop / 2f);
        Assert.True(engine.Fireman.OnPump);
        engine.Run(AutoFireman.PumpDrop);
        Assert.False(engine.Fireman.OnPump);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1.56f)]
    public void UnderTheStartUpBoostHeKeepsTheFireUp(float stokerFeed)
    {
        var engine = new Footplate(coal: 30f, pressure: 5f, stokerFeed: stokerFeed) { Standing = true, StartupMultiplier = 10f };
        engine.SafetyValve = 25f;
        engine.Run(30f);
        var low = float.MaxValue;
        var counted = 0;
        engine.Run(150f, e =>
        {
            if (e.Pressure >= e.SafetyValve - 2f || !(e.Fireman.CoalTarget > 0f))
                return;
            counted++;
            low = Math.Min(low, e.Fire.CoalMass / e.Fireman.CoalTarget);
        });
        output.WriteLine($"stoker {stokerFeed}: the fire at least {low:P0} of his mark over {counted} steps, {engine.Pressure:F1} bar at the end, {engine.Throws.Count} throws");
        Assert.True(counted > 300, "the boiler reached the safety valve too soon to tell");
        Assert.True(engine.Fire.FireOn);
        Assert.True(low > 0.5f, $"the fire fell to {low:P0} of his mark");
    }

    [Fact]
    public void TurnedDownThePumpStillTurns()
    {
        var engine = new Footplate(injectorDelivery: 4.6f, pumpFeed: 4.2f) { Throttle = 0.15f };
        engine.EngineSteam = 0.1f;
        engine.Run(AutoFireman.PumpAfter + 60f);
        Assert.True(engine.Fireman.OnPump);
        var stalled = 0;
        var least = 1f;
        engine.Run(900f, e =>
        {
            if (e.PumpValve > 0f)
                least = Math.Min(least, e.PumpValve);
            if (e.PumpValve > 0f && e.PumpValve <= e.Pump!.Spec.OpenThreshold)
                stalled++;
        });
        output.WriteLine($"least opening {least:P0}; steps with the pump stopped under his hand {stalled}");
        Assert.True(least < 0.2f, $"he never turned the pump down (least {least:P0})");
        Assert.Equal(0, stalled);
    }

    [Fact]
    public void APumpThatLosesItsSuctionIsShutAndTheInjectorFeedsUntilItIsTriedAgain()
    {
        var engine = new Footplate(injectorDelivery: 4.6f, pumpFeed: 4.2f);
        engine.EngineSteam = Cruise;
        engine.Run(AutoFireman.PumpAfter + 120f);
        Assert.True(engine.Fireman.OnPump);

        // The suction strainer choked: the pump draws nothing.
        engine.Pump!.Spec.LowSuctionWater = 2f * engine.TenderWater;
        var lost = false;
        var injectorFed = false;
        var low = 1f;
        engine.Run(50f, e =>
        {
            lost |= e.PumpFlow.State == FeedPumpState.LostSuction;
            injectorFed |= e.Injector.State == InjectorState.Working;
            low = Math.Min(low, e.Glass);
        });
        engine.Pump.Spec.LowSuctionWater = 300f;
        output.WriteLine($"lost suction {lost}; the injector fed {injectorFed}; glass low {low:F2}");
        Assert.True(lost);
        Assert.False(engine.Fireman.OnPump);
        Assert.True(injectorFed);
        Assert.Equal(0f, engine.PumpValve);
        Assert.True(low > AutoFireman.GlassGoal - 0.15f, $"the glass fell to {low:F2}");

        engine.Run(60f);
        Assert.True(engine.Fireman.OnPump);
    }
}
