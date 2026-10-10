using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class BoilerWaterTests
{
    private const float Water = 8000f;
    private const float Evaporation = 3f;
    private const float Dt = 0.1f;

    // Works the boiler with the feed replacing what boils off, blowing down
    // the given share of the feed in three-second bursts at 100 kg/s.
    private static void Run(BoilerWater water, float boilerfuls, float work = 1f, float fullness = 0.5f, float blowdownShare = 0f)
    {
        var seconds = boilerfuls * Water / Evaporation;
        var owed = 0f;
        var burst = 0f;
        for (var t = 0f; t < seconds; t += Dt)
        {
            var fed = Evaporation * Dt;
            owed += fed * blowdownShare;
            if (burst <= 0f && owed >= 300f)
                burst = 3f;
            var blown = 0f;
            if (burst > 0f)
            {
                blown = 100f * Dt;
                owed -= blown;
                burst -= Dt;
                fed += blown;
            }
            water.Step(Dt, new BoilerWaterInputs { WaterMass = Water, Fed = fed, BlownDown = blown, Work = work, Fullness = fullness });
        }
    }

    private static void Stand(BoilerWater water, float seconds, float work = 0f, float fullness = 0.5f)
    {
        for (var t = 0f; t < seconds; t += Dt)
            water.Step(Dt, new BoilerWaterInputs { WaterMass = Water, Work = work, Fullness = fullness });
    }

    [Fact]
    public void SolidsConcentrateByTheFeedWithEachBoilerfulEvaporated()
    {
        var water = new BoilerWater(FeedWaterQuality.Average, Water);
        Assert.Equal(water.FeedConcentration, water.FoamingConcentration, 1f);

        Run(water, boilerfuls: 3f);

        Assert.Equal(4f * water.FeedConcentration, water.FoamingConcentration, 0.02f * 4f * water.FeedConcentration);
        Assert.Equal(4f * (1f - BoilerWater.SludgeShare) * water.FeedConcentration, water.Concentration, 0.02f * 4f * water.FeedConcentration);
    }

    [Fact]
    public void AverageWaterStaysQuietForSeveralBoilerfulsThenFoams()
    {
        var water = new BoilerWater(FeedWaterQuality.Average, Water);

        Run(water, boilerfuls: 5f);
        Assert.Equal(0f, water.Foam);

        Run(water, boilerfuls: 7f);
        Assert.True(water.Foam > 0.5f, $"foam {water.Foam:F2} at {water.FoamingConcentration:F0} ppm");
    }

    [Fact]
    public void BadWaterFoamsWithinAFewBoilerfuls()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);

        Run(water, boilerfuls: 1.5f);
        Assert.Equal(0f, water.Foam);

        Run(water, boilerfuls: 2.5f);
        Assert.True(water.Foam > 0.6f, $"foam {water.Foam:F2} at {water.FoamingConcentration:F0} ppm");
    }

    [Fact]
    public void GoodWaterStaysQuietAllDay()
    {
        var water = new BoilerWater(FeedWaterQuality.Good, Water);

        Run(water, boilerfuls: 12f);

        Assert.Equal(0f, water.Foam);
    }

    [Fact]
    public void EasingTheThrottleSettlesTheFoamAndHighWaterRaisesIt()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        Run(water, boilerfuls: 3f);
        var working = water.Foam;

        Stand(water, 10f, work: 0.1f);
        Assert.True(water.Foam < 0.6f * working, $"foam {working:F2} -> {water.Foam:F2} with the throttle eased");

        Stand(water, 30f, work: 0.5f, fullness: 0.5f);
        var halfGlass = water.Foam;
        Stand(water, 30f, work: 0.5f, fullness: 0.95f);
        Assert.True(water.Foam > 1.4f * halfGlass, $"foam {halfGlass:F2} at half a glass, {water.Foam:F2} near the top");
    }

    [Fact]
    public void TheGlassShowsFoamBeforeWaterIsCarriedOver()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        while (water.Foam < 0.1f)
            Run(water, boilerfuls: 0.02f);

        var low = float.MaxValue;
        var high = float.MinValue;
        for (var t = 0f; t < 5f; t += Dt)
        {
            Stand(water, Dt, work: 1f);
            low = Math.Min(low, water.GlassRise);
            high = Math.Max(high, water.GlassRise);
        }
        Assert.True(high - low > 0.012f, $"the glass surges {high - low:F3} with foam {water.Foam:F2}");
        Assert.True(water.Carryover < 0.015f, $"carryover {water.Carryover:F3} with foam {water.Foam:F2}");

        Run(water, boilerfuls: 3f);
        Assert.True(water.Carryover > 0.2f, $"carryover {water.Carryover:F3} with foam {water.Foam:F2}");
    }

    [Fact]
    public void SludgeSettlesStandingAndHardWorkStirsItUp()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        Run(water, boilerfuls: 2f);
        var stirred = water.FoamingConcentration;

        Stand(water, 3600f);
        Assert.True(water.SettledSludge > 3f * water.SuspendedSludge, $"settled {water.SettledSludge:F2} kg, suspended {water.SuspendedSludge:F2} kg after standing");
        Assert.True(water.FoamingConcentration < 0.85f * stirred);

        Stand(water, 900f, work: 1f);
        Assert.True(water.SuspendedSludge > 3f * water.SettledSludge, $"settled {water.SettledSludge:F2} kg, suspended {water.SuspendedSludge:F2} kg after hard work");
        Assert.Equal(stirred, water.FoamingConcentration, 0.02f * stirred);
    }

    [Fact]
    public void RunningBlowdownsHoldTheWaterDownButNeverMakeItFresh()
    {
        var neglected = new BoilerWater(FeedWaterQuality.Average, Water);
        var tended = new BoilerWater(FeedWaterQuality.Average, Water);

        Run(neglected, boilerfuls: 20f);
        Run(tended, boilerfuls: 20f, blowdownShare: 0.2f);

        Assert.True(neglected.Foam > 0.9f);
        Assert.Equal(0f, tended.Foam);
        Assert.True(tended.FoamingConcentration > 3f * tended.FeedConcentration, $"{tended.FoamingConcentration:F0} ppm");
    }

    [Fact]
    public void ShortBurstsTakeMoreSludgeThanOneLongBlow()
    {
        float SludgeLeft(bool bursts)
        {
            var water = new BoilerWater(FeedWaterQuality.Bad, Water);
            Run(water, boilerfuls: 2f);
            Stand(water, 3600f);
            var blown = 0f;
            for (var t = 0f; blown < 900f; t += Dt)
            {
                var open = !bursts || t % 9f < 3f;
                var kg = open ? 100f * Dt : 0f;
                blown += kg;
                water.Step(Dt, new BoilerWaterInputs { WaterMass = Water - blown, BlownDown = kg, Fullness = 0.5f });
            }
            return water.SettledSludge;
        }

        var bursts = SludgeLeft(bursts: true);
        var longBlow = SludgeLeft(bursts: false);
        Assert.True(bursts < 0.7f * longBlow, $"settled sludge left {bursts:F2} kg after bursts, {longBlow:F2} kg after one long blow");
    }

    [Fact]
    public void DrainingAndRefillingFreshensTheWaterButLeavesTheSediment()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        Run(water, boilerfuls: 3f);
        Stand(water, 3600f);
        var sediment = water.SettledSludge;

        for (var mass = Water; mass > 0f; mass -= 100f * Dt)
            water.Step(Dt, new BoilerWaterInputs { WaterMass = Math.Max(0f, mass - 100f * Dt), BlownDown = Math.Min(mass, 100f * Dt) });
        for (var mass = 0f; mass < Water; mass += 15f * Dt)
            water.Step(Dt, new BoilerWaterInputs { WaterMass = mass + 15f * Dt, Fed = 15f * Dt });

        Assert.Equal((1f - BoilerWater.SludgeShare) * water.FeedConcentration, water.Concentration, 0.05f * water.FeedConcentration);
        Assert.True(water.SettledSludge > 0.2f * sediment, $"sediment {sediment:F2} -> {water.SettledSludge:F2} kg");
    }

    [Fact]
    public void AWashoutMakesItAllFresh()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        Run(water, boilerfuls: 4f);
        Stand(water, 600f);

        water.Washout(Water);

        Assert.Equal(0f, water.SettledSludge);
        Assert.Equal(0f, water.Foam);
        Assert.Equal(water.FeedConcentration, water.FoamingConcentration, 1f);
    }

    [Fact]
    public void ACleanerFeedFreshensTheWaterOnlyAsItIsUsed()
    {
        var water = new BoilerWater(FeedWaterQuality.Bad, Water);
        Run(water, boilerfuls: 2f);
        var before = water.FoamingConcentration;

        water.Quality = FeedWaterQuality.Good;

        Assert.Equal(before, water.FoamingConcentration);
    }
}

/// <summary>The fusible plug and the low-water alarm.</summary>
public class FusiblePlugTests
{
    private const float Dt = 0.1f;
    private const float WaterTemperature = 198f;

    // The stock crown sheet: the water's temperature while covered, heating
    // toward the fire's with a 60-second time constant while bared.
    private static float Crown(float crown, bool bared, float fire = 1000f) =>
        crown + ((bared ? Math.Max(WaterTemperature, fire) : WaterTemperature) - crown) * Dt / 60f;

    [Fact]
    public void ACoveredPlugHolds()
    {
        var plug = new FusiblePlug(WaterTemperature);
        var crown = WaterTemperature;
        for (var t = 0f; t < 3600f; t += Dt)
        {
            crown = Crown(crown, bared: false);
            plug.Step(Dt, false, crown, WaterTemperature);
        }
        Assert.False(plug.Melted);
    }

    [Fact]
    public void ABaredPlugMeltsInSeveralSeconds()
    {
        var plug = new FusiblePlug(WaterTemperature);
        var crown = WaterTemperature;
        var t = 0f;
        while (!plug.Melted && t < 120f)
        {
            crown = Crown(crown, bared: true);
            plug.Step(Dt, true, crown, WaterTemperature);
            t += Dt;
        }
        Assert.InRange(t, 5f, 15f);
        Assert.True(crown < 600f, "it melts long before the crown sheet fails");
    }

    [Fact]
    public void ASurgeOffTheCrownDoesNotMeltIt()
    {
        var plug = new FusiblePlug(WaterTemperature);
        var crown = WaterTemperature;
        for (var t = 0f; t < 60f; t += Dt)
        {
            var bared = t % 20f < 2f;
            crown = Crown(crown, bared);
            plug.Step(Dt, bared, crown, WaterTemperature);
        }
        Assert.False(plug.Melted);
    }

    [Fact]
    public void AMeltedPlugDrownsTheFireWhileTheBoilerHasPressure()
    {
        var plug = new FusiblePlug(WaterTemperature);
        Assert.Equal(0f, plug.Drowning(14f));

        while (!plug.Melted)
            plug.Step(Dt, true, 2000f, WaterTemperature);

        Assert.Equal(1f, plug.Drowning(14f));
        Assert.InRange(plug.Vent(14f), 0.8f, 1.3f);
        Assert.True(plug.Drowning(2.5f) > 0.5f, "the fire cannot be relit to raise steam");
        Assert.True(plug.Drowning(1.5f) < 0.5f);

        plug.Renew(20f);
        Assert.False(plug.Melted);
        Assert.Equal(0f, plug.Vent(14f));
    }

    [Fact]
    public void TheAlarmBlowsWithAnInchInTheGlassAndStopsWhenTheWaterIsBack()
    {
        var alarm = new LowWaterAlarm();
        const float crown = 0.6f;

        Assert.False(alarm.Step(0.8f, crown));
        Assert.True(alarm.Step(crown + LowWaterAlarm.Margin - 0.005f, crown));
        Assert.True(alarm.Step(crown + LowWaterAlarm.Margin + 0.005f, crown));
        Assert.False(alarm.Step(crown + LowWaterAlarm.Margin + 0.02f, crown));
    }
}
