using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// The boiler hand pump's rules and what a press that pumps nothing is told
/// (a Nexus report, audit 2026-11-21: it seemed to do nothing, and only too
/// much pressure was ever said). Pressures are absolute bar; it pumps up to 4
/// (3 on the gauge).
/// </summary>
public class HandPumpTests
{
    private const float Most = 4f;
    private const float MostGauge = Most - 1f;

    private static HandPumpStop Stop(bool broken = false, bool hose = false, float pressure = 1f, bool noTender = false, float water = 20000f, float room = 5000f) =>
        HandPump.Stop(broken, hose, pressure, Most, noTender, water, room);

    [Fact]
    public void AColdBoilerWithATenderOfWaterPumps()
    {
        Assert.Equal(HandPumpStop.None, Stop());
        Assert.Equal(HandPumpStop.None, Stop(pressure: Most));
    }

    [Fact]
    public void EachThingThatStopsItIsNamed()
    {
        Assert.Equal(HandPumpStop.BoilerBroken, Stop(broken: true));
        Assert.Equal(HandPumpStop.HoseFilling, Stop(hose: true));
        Assert.Equal(HandPumpStop.Pressure, Stop(pressure: Most + 0.01f));
        Assert.Equal(HandPumpStop.NoTender, Stop(noTender: true, water: 0f));
        Assert.Equal(HandPumpStop.NoWater, Stop(water: 0f));
        Assert.Equal(HandPumpStop.BoilerFull, Stop(room: 0f));
    }

    [Fact]
    public void TheFirstThatAppliesIsNamedInTheOrderTheBoilersStepTakesThem()
    {
        // Broken, nothing fills it; a filling hose stands in for the pump; then its pressure; then its water; then room.
        Assert.Equal(HandPumpStop.BoilerBroken, Stop(broken: true, hose: true, pressure: 9f, noTender: true, water: 0f, room: 0f));
        Assert.Equal(HandPumpStop.HoseFilling, Stop(hose: true, pressure: 9f, noTender: true, water: 0f, room: 0f));
        Assert.Equal(HandPumpStop.Pressure, Stop(pressure: 9f, noTender: true, water: 0f, room: 0f));
        Assert.Equal(HandPumpStop.NoTender, Stop(noTender: true, water: 0f, room: 0f));
        Assert.Equal(HandPumpStop.NoWater, Stop(water: 0f, room: 0f));
        Assert.Equal(HandPumpStop.NoWater, Stop(water: float.NaN));
    }

    [Fact]
    public void EveryStopHasAShortPlainNotice()
    {
        Assert.Null(HandPump.Notice(HandPumpStop.None, MostGauge));
        Assert.Equal("No hand pump here: board a steam loco or its tender", HandPump.Notice(HandPumpStop.NotAboard, MostGauge));
        Assert.Equal("The hand pump can't fill a broken boiler: have the loco repaired", HandPump.Notice(HandPumpStop.BoilerBroken, MostGauge));
        Assert.Equal("The shed's water hose is already filling the boiler", HandPump.Notice(HandPumpStop.HoseFilling, MostGauge));
        Assert.Equal("The hand pump cannot force water in above 3 bar", HandPump.Notice(HandPumpStop.Pressure, MostGauge));
        Assert.Equal("The hand pump has no water: couple the tender", HandPump.Notice(HandPumpStop.NoTender, MostGauge));
        Assert.Equal("The hand pump has no water: fill the tender", HandPump.Notice(HandPumpStop.NoWater, MostGauge));
        Assert.Equal("The hand pump has no water: fill the tanks", HandPump.Notice(HandPumpStop.NoWater, MostGauge, ownTanks: true));
        Assert.Equal("The boiler is full: the hand pump can't put in more", HandPump.Notice(HandPumpStop.BoilerFull, MostGauge));
        foreach (HandPumpStop stop in Enum.GetValues(typeof(HandPumpStop)))
            if (stop != HandPumpStop.None)
                Assert.InRange(HandPump.Notice(stop, MostGauge)!.Length, 20, 70);
    }

    [Fact]
    public void TheWaterPagesRowGivesItsRateOrWhatStopsIt()
    {
        Assert.Equal(1.5f.ToString("F1") + " kg/s", HandPump.Row(HandPumpStop.None, 1.5f, MostGauge));
        Assert.Equal("over 3 bar", HandPump.Row(HandPumpStop.Pressure, 0f, MostGauge));
        Assert.Equal("no tender", HandPump.Row(HandPumpStop.NoTender, 0f, MostGauge));
        Assert.Equal("no water", HandPump.Row(HandPumpStop.NoWater, 0f, MostGauge));
        Assert.Equal("hose filling", HandPump.Row(HandPumpStop.HoseFilling, 4f, MostGauge));
        Assert.Equal("boiler full", HandPump.Row(HandPumpStop.BoilerFull, 0f, MostGauge));
        Assert.Equal("boiler broken", HandPump.Row(HandPumpStop.BoilerBroken, 0f, MostGauge));
        foreach (HandPumpStop stop in Enum.GetValues(typeof(HandPumpStop)))
            Assert.InRange(HandPump.Row(stop, 1.5f, MostGauge).Length, 4, 14);
    }
}
