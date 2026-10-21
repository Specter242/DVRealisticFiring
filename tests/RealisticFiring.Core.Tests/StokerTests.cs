using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class StokerTests
{
    // The NYC Dreyfuss Hudson's firebox, with CCL's 10 kg/s over its multiplier of 4.
    private static readonly FireboxParameters Hudson = new FireboxParameters(180f, 90f, 0.6f);
    private const float MaxFeed = 2.5f;
    private const float Boiler = 15f;

    // Jams are off unless a test is about them, so random stalls can't upset the others.
    private static Stoker NewStoker(float meanCoalBetweenJams = 0f, int seed = 1) =>
        new Stoker(new StokerSpec(Hudson, MaxFeed) { MeanCoalBetweenJams = meanCoalBetweenJams }, Hudson, new Random(seed));

    private static float Air(float load) => load * Hudson.MaxCombustionRate * FireModel.AirPerCoal;

    private static StokerInputs Inputs(StokerControls controls, float load, float boiler = Boiler, float tender = 1e5f) => new StokerInputs
    {
        Controls = controls,
        BoilerPressure = boiler,
        TenderCoal = tender,
        AirFlow = Air(load),
    };

    private static float[] Landing(StokerControls controls, float load, out float lost)
    {
        var lumps = new float[GrateLayout.ZoneCount];
        var fines = new float[GrateLayout.ZoneCount];
        lost = NewStoker().Distribute(controls, Boiler, Air(load), lumps, fines);
        return lumps.Zip(fines, (l, f) => l + f).ToArray();
    }

    private static float Row(float[] shares, int row) =>
        Enumerable.Range(0, GrateLayout.Columns).Sum(column => shares[(int)GrateLayout.At(row, column)]);

    [Fact]
    public void TheEngineNeedsSteamPressure()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 40f);
        StokerFlow flow = default;
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            flow = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0.5f, boiler: 1.5f), fire);
        Assert.Equal(0f, stoker.EngineSpeed);
        Assert.Equal(0f, flow.Drawn);
    }

    [Fact]
    public void TheFeedFollowsTheThrottleAndEasesToSpeed()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 20f);
        var first = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(0.5f), 0.5f), fire);
        Assert.True(first.Drawn < 0.05f * 0.5f * MaxFeed * FireTestKit.Dt, "the engine starts from rest");

        StokerFlow flow = default;
        for (var t = 0f; t < 20f; t += FireTestKit.Dt)
            flow = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(0.5f), 0.5f), fire);
        Assert.InRange(flow.Drawn / FireTestKit.Dt, 0.97f * 0.5f * MaxFeed, 1.001f * 0.5f * MaxFeed);
        Assert.True(flow.Fed < flow.Drawn, "some fines leave as cinders");
    }

    [Fact]
    public void TheFeedStopsAtAnEmptyTenderOrAFullFirebox()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 20f);
        var drawn = 0f;
        for (var t = 0f; t < 30f; t += FireTestKit.Dt)
            drawn += stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0.5f, tender: Math.Max(0f, 5f - drawn)), fire).Drawn;
        Assert.InRange(drawn, 4.99f, 5.001f);

        var full = FireTestKit.BurningFire(Hudson, Hudson.MaxCoalCapacity);
        var fullStoker = NewStoker();
        var fullDrawn = 0f;
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            fullDrawn += fullStoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0f), full).Drawn;
        Assert.True(fullDrawn < 0.5f, $"a full box stops the conveyor ({fullDrawn:F2} kg drawn)");
    }

    [Fact]
    public void TheStokerUsesOneToTwoPercentOfTheBoilersSteam()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 40f);
        StokerFlow flow = default;
        for (var t = 0f; t < 20f; t += FireTestKit.Dt)
            flow = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0.5f), fire);
        var evaporation = Hudson.MaxHeatOutput / StokerSpec.SteamHeat;
        Assert.InRange(flow.SteamUsed / evaporation, 0.01f, 0.025f);

        var shut = NewStoker().Step(FireTestKit.Dt, Inputs(new StokerControls(), 0.5f), fire);
        Assert.Equal(0f, shut.SteamUsed);
    }

    [Fact]
    public void ShutJetsRollAHeelUnderTheDoor()
    {
        var shares = Landing(new StokerControls { Throttle = 1f }, 0.5f, out _);
        Assert.True(Row(shares, 0) > 0.75f, $"back row {Row(shares, 0):P0}");
        Assert.Equal(GrateZone.UnderDoor, (GrateZone)Array.IndexOf(shares, shares.Max()));
    }

    [Fact]
    public void LowJetsBuildTheBackAndHighJetsTheFront()
    {
        var low = StokerControls.Working(1f);
        low.MasterJet = 0.15f;
        var high = StokerControls.Working(1f);
        high.MasterJet = 0.45f;
        var lowShares = Landing(low, 0.5f, out _);
        var highShares = Landing(high, 0.5f, out _);
        Assert.True(Row(lowShares, 0) > Row(highShares, 0) + 0.2f);
        Assert.True(Row(highShares, 2) > Row(lowShares, 2) + 0.3f);
    }

    [Fact]
    public void HardWorkCarriesCoalForwardAndLosesMoreFines()
    {
        var easy = Landing(StokerControls.Working(1f), 0.2f, out var easyLost);
        var hard = Landing(StokerControls.Working(1f), 0.95f, out var hardLost);
        Assert.True(Row(hard, 2) / (1f - hardLost) > Row(easy, 2) / (1f - easyLost));
        Assert.True(hardLost > 2f * easyLost);
    }

    [Fact]
    public void TooMuchJetBlowsCoalOverTheArch()
    {
        Landing(StokerControls.Working(1f), 0.5f, out var working);
        var wide = StokerControls.Working(1f);
        wide.MasterJet = 1f;
        var shares = Landing(wide, 0.5f, out var lost);
        Assert.True(lost > working + 0.15f, $"lost {lost:P0} against {working:P0}");
        Assert.True(Row(shares, 2) > Row(shares, 1), "and banks the front");
    }

    [Fact]
    public void TheWorkingSettingFeedsTheFrontButLeavesTheBackShort()
    {
        // The back draws hardest under a sealed arch, and the stoker cannot
        // reach it: the coal it misses there heaps up in the middle.
        var shares = Landing(StokerControls.Working(1f), 0.5f, out var lost);
        var fire = new FireModel(Hudson);
        var need = Enumerable.Range(0, GrateLayout.ZoneCount).Select(i => Square(fire.DraftWeight((GrateZone)i))).ToArray();
        var needSum = need.Sum();
        for (var i = 0; i < GrateLayout.ZoneCount; i++)
        {
            var landed = shares[i] / (1f - lost);
            var wanted = need[i] / needSum;
            var zone = (GrateZone)i;
            var row = GrateLayout.Row(zone);
            if (row == 0)
                Assert.True(landed < wanted, $"{zone}: {landed:P1} should be short of {wanted:P1}");
            else if (row == GrateLayout.Rows - 1)
                Assert.InRange(landed, 0.8f * wanted, 1.3f * wanted);
            else
                Assert.True(landed > wanted, $"{zone}: {landed:P1} should be over {wanted:P1}");
        }
    }

    [Fact]
    public void AShutBackJetStarvesItsCorner()
    {
        var controls = StokerControls.Working(1f);
        controls.LeftBack = 0f;
        var shares = Landing(controls, 0.5f, out _);
        Assert.True(shares[(int)GrateZone.BackLeft] < 0.5f * shares[(int)GrateZone.BackRight]);
    }

    [Theory]
    [InlineData(0.5f, 0.03f, 0.09f)]
    [InlineData(0.95f, 0.09f, 0.2f)]
    public void StokedCoalGivesLessHeatPerKgThanHandFiring(float load, float minLoss, float maxLoss)
    {
        // The stoker carries the fire; the fireman keeps the back up by hand.
        // Clean coal: this is about what crushing costs, not clinker.
        var targetCoal = load * Hudson.MaxCombustionRate * Hudson.BurnTime;
        var fire = FireTestKit.BurningFire(Hudson, targetCoal, FireTestKit.CleanCoal);
        var stoker = NewStoker();
        var inputs = FireTestKit.Inputs(Air(load));
        double drawn = 0, burned = 0, heat = 0;
        var nextShovel = 0f;
        for (var t = 0f; t < 2400f; t += FireTestKit.Dt)
        {
            // Fire to hold the coal: throttle opens as the box runs down.
            var throttle = Smoothing.Clamp01((targetCoal - fire.CoalMass) / (0.1f * targetCoal));
            var flow = stoker.Step(FireTestKit.Dt, new StokerInputs
            {
                Controls = StokerControls.Working(throttle),
                BoilerPressure = Boiler,
                TenderCoal = 1e5f,
                AirFlow = inputs.AirFlow,
            }, fire);
            var shoveled = HandFireTheBack(fire, ref nextShovel, t);
            var consumed = fire.Step(FireTestKit.Dt, inputs);
            if (t < 1200f)
                continue;
            drawn += flow.Drawn + shoveled;
            burned += consumed;
            heat += fire.Heat * FireTestKit.Dt;
        }

        var burnRate = burned / 1200.0;
        var handHeatPerCoal = FireTestKit.CurveHeat(Hudson, burnRate) / burnRate;
        var loss = 1.0 - heat / drawn / handHeatPerCoal;
        Assert.InRange(loss, minLoss, maxLoss);
    }

    [Fact]
    public void OverfeedingMakesBlackSmoke()
    {
        static float Soot(float throttle)
        {
            var fire = FireTestKit.BurningFire(Hudson, 40f);
            var stoker = NewStoker();
            var inputs = FireTestKit.Inputs(Air(0.3f));
            for (var t = 0f; t < 60f; t += FireTestKit.Dt)
            {
                stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(throttle), 0.3f), fire);
                fire.Step(FireTestKit.Dt, inputs);
            }
            return fire.SootDensity;
        }

        var matched = Soot(0.25f);
        var overfed = Soot(1f);
        Assert.True(overfed > matched + 0.2f, $"soot {overfed:P0} overfed against {matched:P0} matched");
    }

    [Fact]
    public void AJamStallsTheEngineAndPegsItsGauge()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 20f);
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(0.6f), 0.5f), fire);
        stoker.Jam();

        var flow = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(0.6f), 0.5f), fire);
        Assert.True(flow.Jammed);
        Assert.Equal(0f, flow.Drawn);
        Assert.Equal(0f, stoker.EngineSpeed);
        Assert.InRange(flow.EnginePressure, 0.59f * Boiler, 0.61f * Boiler);
    }

    [Fact]
    public void ReversingAFewTurnsClearsAJam()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 20f);
        stoker.Jam();
        var reverse = StokerControls.Working(1f);
        reverse.Direction = StokerDirection.Reverse;
        var seconds = 0f;
        for (; stoker.Jammed && seconds < 30f; seconds += FireTestKit.Dt)
        {
            var backing = stoker.Step(FireTestKit.Dt, Inputs(reverse, 0.5f), fire);
            Assert.Equal(0f, backing.Drawn);
        }
        Assert.False(stoker.Jammed);
        // Four to six turns at up to 1.5 a second, after the engine comes up to speed.
        Assert.InRange(seconds, 2.5f, 12f);

        StokerFlow flow = default;
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            flow = stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0.5f), fire);
        Assert.True(flow.Drawn > 0f, "forward again, it feeds");
    }

    [Fact]
    public void NeutralStopsTheStoker()
    {
        var stoker = NewStoker();
        var fire = FireTestKit.BurningFire(Hudson, 20f);
        var neutral = StokerControls.Working(1f);
        neutral.Direction = StokerDirection.Neutral;
        StokerFlow flow = default;
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            flow = stoker.Step(FireTestKit.Dt, Inputs(neutral, 0.5f), fire);
        Assert.Equal(0f, flow.Drawn);
        Assert.Equal(0f, stoker.EngineSpeed);
    }

    [Fact]
    public void JamsComeEveryFewTonnes()
    {
        var stoker = NewStoker(meanCoalBetweenJams: 2500f, seed: 7);
        var fire = FireTestKit.BurningFire(Hudson, 40f);
        var inputs = FireTestKit.Inputs(Air(0.9f));
        var reverse = StokerControls.Working(1f);
        reverse.Direction = StokerDirection.Reverse;
        double drawn = 0;
        var jams = 0;
        while (drawn < 25000)
        {
            if (stoker.Jammed)
            {
                jams++;
                while (stoker.Jammed)
                    stoker.Step(FireTestKit.Dt, Inputs(reverse, 0.9f), fire);
            }
            drawn += stoker.Step(FireTestKit.Dt, Inputs(StokerControls.Working(1f), 0.9f), fire).Drawn;
            fire.Step(FireTestKit.Dt, inputs);
        }
        // Ten expected in 25 t; a Poisson count lands within this range almost always.
        Assert.InRange(jams, 3, 20);
    }

    private static float Square(float x) => x * x;

    // As the manuals say: the fireman keeps a heel under the door and the
    // back corners up by hand, a shovelful at a time.
    // Returns the coal shoveled, in kg.
    internal static float HandFireTheBack(FireModel fire, ref float nextShovel, float t)
    {
        if (t < nextShovel)
            return 0f;
        var middle = (fire.Zones[(int)GrateZone.MidLeft].Coal + fire.Zones[(int)GrateZone.Center].Coal + fire.Zones[(int)GrateZone.MidRight].Coal) / 3f;
        var back = new[] { GrateZone.BackLeft, GrateZone.UnderDoor, GrateZone.BackRight }.OrderBy(z => fire.Zones[(int)z].Coal).First();
        if (fire.Zones[(int)back].Coal >= 0.8f * middle)
            return 0f;
        nextShovel = t + 4f;
        return fire.AddCoal(3f, back);
    }
}
