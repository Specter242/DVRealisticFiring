using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class FireModelTests
{
    private static readonly FireboxParameters Stock = new FireboxParameters();

    // Half the stock maximum rate, with the air the stock model needs for it.
    private static readonly float HalfLoadAir = 0.5f * Stock.MaxCombustionRate * FireTestKit.StockAirPerCoal;
    private static readonly float HalfLoadCoal = 0.5f * Stock.MaxCombustionRate * Stock.BurnTime;

    [Fact]
    public void AddingCoalClampsToCapacityAndFavorsTheTargetZone()
    {
        var fire = new FireModel(Stock);

        var accepted = fire.AddCoal(10f, GrateZone.BackLeft);

        Assert.Equal(10f, accepted, 4);
        Assert.Equal(10f, fire.CoalMass, 4);
        var target = fire.Zones[(int)GrateZone.BackLeft].Fresh;
        Assert.True(target >= 0.75f * 10f, $"target zone got {target} kg");
        Assert.All(fire.Zones, z => Assert.Equal(0f, z.Burning));

        var overflow = fire.AddCoal(1000f, GrateZone.Center);
        Assert.Equal(Stock.MaxCoalCapacity - 10f, overflow, 3);
        Assert.Equal(Stock.MaxCoalCapacity, fire.CoalMass, 3);
    }

    [Fact]
    public void RemovingCoalTakesItFromAllZonesAndNeverGoesNegative()
    {
        var fire = new FireModel(Stock);
        fire.AddCoalEvenly(9f);

        Assert.Equal(4f, fire.RemoveCoal(4f), 4);
        Assert.Equal(5f, fire.CoalMass, 4);
        Assert.Equal(5f, fire.RemoveCoal(50f), 4);
        Assert.Equal(0f, fire.CoalMass);
    }

    [Fact]
    public void UnlitFireDoesNotBurn()
    {
        var fire = new FireModel(Stock);
        fire.AddCoalEvenly(30f);

        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 60f);

        Assert.Equal(30f, fire.CoalMass, 4);
        Assert.Equal(0f, fire.Heat);
        Assert.False(fire.FireOn);
    }

    [Fact]
    public void LitFireCatchesFromFreshCoalAndBuildsUp()
    {
        var fire = new FireModel(Stock);
        fire.AddCoalEvenly(30f);
        fire.Ignite();

        fire.Step(FireTestKit.Dt, FireTestKit.Inputs(HalfLoadAir));
        var heatAtStart = fire.Heat;
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 60f);

        Assert.True(fire.FireOn);
        Assert.True(fire.Zones.Sum(z => z.Fresh) < 10f, "most fresh coal should have caught");
        Assert.True(fire.Heat > 10 * heatAtStart + 1e5f, $"heat {fire.Heat} should build from {heatAtStart}");
    }

    [Fact]
    public void FreshCoalDelaysHeatComparedWithBurningCoal()
    {
        var fresh = new FireModel(Stock);
        fresh.Restore(fireOn: true, coalMass: 5f);
        fresh.AddCoalEvenly(20f);

        var burning = FireTestKit.BurningFire(Stock, 25f);

        var inputs = FireTestKit.Inputs(HalfLoadAir);
        FireTestKit.Run(fresh, inputs, 10f);
        FireTestKit.Run(burning, inputs, 10f);

        Assert.True(fresh.Heat < 0.8f * burning.Heat, $"fresh {fresh.Heat} vs burning {burning.Heat}");
    }

    [Fact]
    public void CoalMassIsConserved()
    {
        var fire = FireTestKit.BurningFire(Stock, HalfLoadCoal);
        var fireman = new Fireman(shakeInterval: 120f);
        var inputs = FireTestKit.Inputs(HalfLoadAir);
        double burned = 0;
        for (var t = 0f; t < 900f; t += FireTestKit.Dt)
        {
            fireman.Tend(fire, HalfLoadCoal, FireTestKit.Dt);
            burned += fire.Step(FireTestKit.Dt, inputs);
        }

        var balance = HalfLoadCoal + fireman.CoalFed - burned - fire.CoalDroppedThroughGrate - fire.CoalMass;
        Assert.InRange(balance, -0.01, 0.01);
    }

    [Fact]
    public void ThickFireSmokesAndWastesCoalAndDoorAirClearsIt()
    {
        var even = new TendedFire(Stock, 0.5f);
        var thick = new TendedFire(Stock, 0.5f, coalFactor: 1.5f);
        var thickDoorCracked = new TendedFire(Stock, 0.5f, coalFactor: 1.5f);
        thickDoorCracked.Inputs.DoorOpening = 0.3f;
        foreach (var fire in new[] { even, thick, thickDoorCracked })
            fire.Run(60f);

        Assert.True(thick.Fire.SmokeDensity > 0.3f, $"thick smoke {thick.Fire.SmokeDensity}");
        Assert.True(even.Fire.SmokeDensity < 0.1f, $"even smoke {even.Fire.SmokeDensity}");
        Assert.True(thick.Fire.OxygenAvailability < 0.8f);
        Assert.True(thick.Fire.HeatPerCoal < 0.9f * even.Fire.HeatPerCoal,
            $"thick {thick.Fire.HeatPerCoal} J/kg vs even {even.Fire.HeatPerCoal} J/kg");
        Assert.True(thickDoorCracked.Fire.SmokeDensity < 0.5f * thick.Fire.SmokeDensity,
            $"door air smoke {thickDoorCracked.Fire.SmokeDensity} vs {thick.Fire.SmokeDensity}");
    }

    [Fact]
    public void AirStarvedBurningCoalMakesCarbonMonoxideNotSoot()
    {
        // A deep bed of lit coal short of air burns to CO, which is invisible.
        var fire = FireTestKit.BurningFire(Stock, Stock.MaxCoalCapacity);

        FireTestKit.Run(fire, FireTestKit.Inputs(0.2f * HalfLoadAir), 60f);

        Assert.True(fire.SmokeDensity > 0.5f, $"smoke {fire.SmokeDensity}");
        Assert.True(fire.SootDensity < 0.05f, $"soot {fire.SootDensity}");
    }

    [Fact]
    public void FreshCoalSmokesBlackUntilDoorAirBurnsIt()
    {
        var closed = new TendedFire(Stock, 0.5f);
        var cracked = new TendedFire(Stock, 0.5f);
        cracked.Inputs.DoorOpening = 0.5f;
        foreach (var fire in new[] { closed, cracked })
        {
            fire.Fire.AddCoalEvenly(0.25f * Stock.MaxCoalCapacity);
            fire.Run(10f);
        }

        // Visibly dark smoke; 0.34 on the draft-shaped bed a fireman following the fire builds.
        Assert.True(closed.Fire.SootDensity > 0.3f, $"door shut soot {closed.Fire.SootDensity}");
        Assert.True(cracked.Fire.SootDensity < 0.5f * closed.Fire.SootDensity,
            $"door cracked soot {cracked.Fire.SootDensity} vs {closed.Fire.SootDensity}");
    }

    [Fact]
    public void SmokeAndSootStayBetweenZeroAndOne()
    {
        // Long frames during a fast cold start, with the target swinging
        // between a fresh charge and a wide-open door: the smoothing must not
        // carry either reading past its range (319 % was seen in game).
        var fire = new FireModel(Stock);
        fire.Ignite();
        var inputs = FireTestKit.Inputs(0.3f * HalfLoadAir);
        inputs.StartupMultiplier = 25f;
        float min = 0f, max = 0f;
        for (var step = 0; step < 200; step++)
        {
            if (step % 4 == 0)
                fire.AddCoalEvenly(20f);
            inputs.DoorOpening = step % 4 == 2 ? 1f : 0f;
            fire.Step(0.5f, inputs);
            min = Math.Min(min, Math.Min(fire.SmokeDensity, fire.SootDensity));
            max = Math.Max(max, Math.Max(fire.SmokeDensity, fire.SootDensity));
        }

        Assert.InRange(min, 0f, 1f);
        Assert.InRange(max, 0f, 1f);
    }

    [Fact]
    public void HoleInTheFireStealsAirAndCoolsTheFirebox()
    {
        var even = new TendedFire(Stock, 0.5f);
        var holed = new TendedFire(Stock, 0.5f);
        var hole = holed.Fire.Zones[(int)GrateZone.FrontLeft];
        var center = holed.Fire.Zones[(int)GrateZone.Center];
        center.Burning += hole.Burning;
        center.Fresh += hole.Fresh;
        hole.Burning = hole.Fresh = 0f;

        FireTestKit.Run(even.Fire, even.Inputs, 30f);
        FireTestKit.Run(holed.Fire, holed.Inputs, 30f);

        var holeShare = holed.Fire.Zones[(int)GrateZone.FrontLeft].AirFlow / holed.Fire.PrimaryAirFlow;
        Assert.True(holeShare > 2f / GrateLayout.ZoneCount, $"hole takes {holeShare:P0} of the air");
        Assert.True(holed.Fire.ExcessAirFlow > even.Fire.ExcessAirFlow + 0.1f);
        Assert.True(holed.Fire.Heat < 0.95f * even.Fire.Heat, $"holed {holed.Fire.Heat} vs even {even.Fire.Heat}");
    }

    [Fact]
    public void OpenDoorPullsAirOverTheFireAndCostsHeat()
    {
        var closed = new TendedFire(Stock, 0.5f);
        var open = new TendedFire(Stock, 0.5f);
        open.Inputs.DoorOpening = 1f;

        closed.Run(60f);
        open.Run(60f);

        Assert.True(open.Fire.DoorAirFlow > 0.2f * HalfLoadAir, $"door air {open.Fire.DoorAirFlow}");
        Assert.True(open.Fire.PrimaryAirFlow < closed.Fire.PrimaryAirFlow);
        Assert.True(open.Fire.Heat < 0.85f * closed.Fire.Heat, $"open {open.Fire.Heat} vs closed {closed.Fire.Heat}");
        Assert.True(open.Fire.Temperature < closed.Fire.Temperature);
    }

    [Fact]
    public void AshChokesTheDraftAndShakingClearsIt()
    {
        var fire = FireTestKit.BurningFire(Stock, HalfLoadCoal);
        var fireman = new Fireman(shakeInterval: float.PositiveInfinity);
        var inputs = FireTestKit.Inputs(HalfLoadAir);
        for (var t = 0f; t < 1800f; t += FireTestKit.Dt)
        {
            fireman.Tend(fire, HalfLoadCoal, FireTestKit.Dt);
            fire.Step(FireTestKit.Dt, inputs);
        }

        var ashBefore = fire.AshMass;
        var airBefore = fire.PrimaryAirFlow;
        Assert.True(ashBefore > 1f, $"ash {ashBefore} kg after 30 minutes");
        Assert.True(airBefore < 0.97f * HalfLoadAir, $"air {airBefore} of {HalfLoadAir}");

        fire.ShakeGrate();
        fire.Step(FireTestKit.Dt, inputs);

        Assert.True(fire.AshMass < 0.5f * ashBefore);
        Assert.True(fire.PrimaryAirFlow > airBefore);
        Assert.True(fire.CoalDroppedThroughGrate > 0f, "shaking drops a little live coal");
    }

    [Fact]
    public void HardFiringFormsClinkerThatShakingBarelyMovesButDumpingClears()
    {
        var p = Stock;
        var fullLoadAir = 0.95f * p.MaxCombustionRate * FireTestKit.StockAirPerCoal;
        var fullLoadCoal = 0.95f * p.MaxCombustionRate * p.BurnTime;
        var fire = FireTestKit.BurningFire(p, fullLoadCoal);
        var fireman = new Fireman(shakeInterval: 300f);
        var inputs = FireTestKit.Inputs(fullLoadAir);
        for (var t = 0f; t < 3600f; t += FireTestKit.Dt)
        {
            fireman.Tend(fire, fullLoadCoal, FireTestKit.Dt);
            fire.Step(FireTestKit.Dt, inputs);
        }

        var clinker = fire.ClinkerMass;
        Assert.True(clinker > 0.5f, $"clinker {clinker} kg after an hour of hard firing");

        fire.ShakeGrate();
        Assert.True(fire.ClinkerMass > 0.7f * clinker);

        for (var t = 0f; t < 60f; t += FireTestKit.Dt)
            fire.Dump(1f, FireTestKit.Dt);
        Assert.True(fire.ClinkerMass < 0.1f * clinker);
        Assert.Equal(0f, fire.CoalMass, 3);
    }

    [Fact]
    public void RakingAPiledZoneSpreadsItOntoItsNeighbors()
    {
        // In game a 33 kg pile sat in one zone with 0-3 kg around it.
        var fire = new FireModel(Stock);
        fire.AddCoal(30f, GrateZone.Front);
        var before = fire.Zones.Select(z => z.Coal).ToArray();

        fire.Rake(GrateZone.Front);

        var front = fire.Zones[(int)GrateZone.Front].Coal;
        Assert.True(front < 0.75f * before[(int)GrateZone.Front], $"front {front} kg");
        foreach (var neighbor in GrateLayout.Neighbors(GrateZone.Front))
            Assert.True(fire.Zones[(int)neighbor].Coal > before[(int)neighbor]);
        Assert.Equal(before[(int)GrateZone.BackLeft], fire.Zones[(int)GrateZone.BackLeft].Coal, 4);
        Assert.Equal(before.Sum(), fire.CoalMass, 3);
    }

    [Fact]
    public void RakingAHoleDrawsCoalIntoIt()
    {
        var fire = FireTestKit.BurningFire(Stock, 60f);
        var hole = fire.Zones[(int)GrateZone.Center];
        fire.Zones[(int)GrateZone.MidLeft].Burning += hole.Burning;
        hole.Burning = 0f;
        var total = fire.CoalMass;

        fire.Rake(GrateZone.Center);

        Assert.True(fire.Zones[(int)GrateZone.Center].Coal > 1f, $"hole now {fire.Zones[(int)GrateZone.Center].Coal} kg");
        Assert.Equal(total, fire.CoalMass, 3);
    }

    [Fact]
    public void RepeatedRakingLevelsTheFire()
    {
        var fire = new FireModel(Stock);
        fire.AddCoal(30f, GrateZone.Front);
        for (var i = 0; i < 20; i++)
            fire.Rake(GrateZone.Front);

        var local = new[] { GrateZone.Front }.Concat(GrateLayout.Neighbors(GrateZone.Front)).Select(z => fire.Zones[(int)z].Coal).ToList();
        Assert.True(local.Max() - local.Min() < 2f, $"zones {string.Join(", ", local.Select(c => c.ToString("F1")))}");
    }

    [Fact]
    public void RakingBreaksUpSomeClinker()
    {
        var fire = FireTestKit.BurningFire(Stock, 60f);
        fire.Zones[(int)GrateZone.Center].Clinker = 2f;

        fire.Rake(GrateZone.Center);

        Assert.True(fire.Zones[(int)GrateZone.Center].Clinker < 2f);
        Assert.True(fire.Zones[(int)GrateZone.Center].Ash > 0f);
    }

    [Fact]
    public void FireGoesOutWithoutCoalOnceCold()
    {
        var fire = FireTestKit.BurningFire(Stock, 2f);

        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 600f);

        Assert.Equal(0f, fire.CoalMass);
        Assert.False(fire.FireOn);
        Assert.Equal(0f, fire.Heat);
    }

    [Fact]
    public void WaterInTheIntakeDrownsTheFire()
    {
        var fire = FireTestKit.BurningFire(Stock, HalfLoadCoal);
        var inputs = FireTestKit.Inputs(HalfLoadAir);
        inputs.IntakeWater = 0.6f;

        fire.Step(FireTestKit.Dt, inputs);

        Assert.False(fire.FireOn);
    }

    [Fact]
    public void ExtinguishStopsCombustion()
    {
        var fire = FireTestKit.BurningFire(Stock, HalfLoadCoal);
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 30f);

        fire.Extinguish();
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 60f);

        Assert.False(fire.FireOn);
        Assert.True(fire.Heat < 1f);
        Assert.True(fire.CoalMass > 0f);
    }

    [Fact]
    public void StartupMultiplierSpeedsUpAColdStart()
    {
        var normal = new FireModel(Stock);
        var fast = new FireModel(Stock);
        foreach (var fire in new[] { normal, fast })
        {
            fire.AddCoalEvenly(20f);
            fire.Ignite();
        }
        var fastInputs = FireTestKit.Inputs(HalfLoadAir);
        fastInputs.StartupMultiplier = 25f;

        FireTestKit.Run(normal, FireTestKit.Inputs(HalfLoadAir), 5f);
        FireTestKit.Run(fast, fastInputs, 5f);

        Assert.True(20f - fast.CoalMass > 3f * (20f - normal.CoalMass),
            $"fast burned {20f - fast.CoalMass} kg, normal {20f - normal.CoalMass} kg");
        Assert.True(fast.Temperature > normal.Temperature);
    }

    [Fact]
    public void RestoringStockSaveSpreadsCoalAsBurningOnlyWhenLit()
    {
        var lit = new FireModel(Stock);
        lit.Restore(fireOn: true, coalMass: 27f);
        var cold = new FireModel(Stock);
        cold.Restore(fireOn: false, coalMass: 27f);

        Assert.True(lit.FireOn);
        Assert.Equal(27f, lit.CoalMass, 3);
        Assert.All(lit.Zones, z => Assert.Equal(3f, z.Burning, 3));
        Assert.False(cold.FireOn);
        Assert.All(cold.Zones, z => Assert.Equal(3f, z.Fresh, 3));
    }

    [Fact]
    public void ZoneSnapshotRoundTrips()
    {
        var fire = FireTestKit.BurningFire(Stock, HalfLoadCoal);
        fire.AddCoal(4f, GrateZone.UnderDoor);
        FireTestKit.Run(fire, FireTestKit.Inputs(HalfLoadAir), 120f);

        var snapshot = fire.SaveZones();
        var copy = new FireModel(Stock);
        Assert.True(copy.RestoreZones(true, snapshot));

        Assert.Equal(fire.CoalMass, copy.CoalMass, 4);
        Assert.Equal(fire.AshMass, copy.AshMass, 4);
        Assert.Equal(fire.Zones[(int)GrateZone.UnderDoor].Fresh, copy.Zones[(int)GrateZone.UnderDoor].Fresh, 4);
        Assert.False(copy.RestoreZones(true, new float[3]));
    }
}
