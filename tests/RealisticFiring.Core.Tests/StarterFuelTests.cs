using System;
using System.Linq;
using Xunit;
namespace RealisticFiring.Core.Tests;

public class StarterFuelTests
{
    private static readonly FireboxParameters S282 = StarterTimingReport.S282;

    private static StarterFuel Bed(int pieces, bool rag)
    {
        var bed = new StarterFuel();
        bed.AddWood(pieces * StarterFuel.PieceMass, 10);
        if (rag) bed.AddRag(RagFuel.OilCapacity, true);
        return bed;
    }

    private static void Run(StarterFuel bed, float seconds, float air = 0.07f, float cover = 0, float coalFlux = 0, float boiler = 20, float dt = 0.05f)
    {
        for (var t = 0f; t < seconds; t += dt) bed.Step(dt, air, cover, coalFlux, 0, boiler, false);
    }

    private static float IgnitionTime(StarterFuel bed, float limit, float boiler = 20, float dt = 0.05f)
    {
        for (var t = 0f; t < limit; t += dt)
        {
            bed.Step(dt, 0.07f, 0, 0, 0, boiler, false);
            if (bed.Involved > 0) return t;
        }
        return -1;
    }

    private static FireModel Fire(int pieces, bool rag, GrateZone zone = GrateZone.UnderDoor)
    {
        var fire = new FireModel(S282);
        fire.AddWood(pieces * StarterFuel.PieceMass, zone);
        if (rag) fire.AddRag(RagFuel.OilCapacity, true, zone);
        return fire;
    }

    private static FireInputs Cold(float speedup = 1f)
    {
        var inputs = FireTestKit.Inputs(0.6f);
        inputs.BoilerTemperature = 20;
        inputs.StartupMultiplier = speedup;
        return inputs;
    }

    [Fact] public void RagHasFiniteOilAndTransferDoesNotRefillIt()
    {
        var rag = new RagFuel();
        Assert.False(rag.Ignite());
        Assert.Equal(RagFuel.OilCapacity, rag.Soak(1));
        Assert.True(rag.Ignite());
        Assert.Equal(0, rag.Soak(1));
        rag.Burn(10);
        var restored = new RagFuel();
        restored.Restore(rag.Oil, rag.Lit);
        Assert.Equal(190, restored.RemainingSeconds, 1);
        restored.Burn(1000);
        Assert.False(restored.Lit);
        Assert.False(restored.Ignite());
    }

    [Fact] public void RagBurnsAtTheSameRateBeforeAndAfterTransfer()
    {
        var rag = new RagFuel(); rag.Soak(1); rag.Ignite(); rag.Burn(30);
        var bed = new StarterFuel(); bed.AddRag(rag.Oil, rag.Lit);
        rag.Burn(20);
        Run(bed, 20, air: 1f);
        Assert.Equal(rag.Oil, bed.RagOil, 4);
    }

    [Fact] public void ColdWoodNeedsAPilotEvenInAHotBoiler()
    {
        foreach (var boiler in new[] { 20f, 200f })
        {
            var bed = Bed(8, false);
            Run(bed, 600, boiler: boiler);
            Assert.Equal(8 * StarterFuel.PieceMass, bed.Wood, 4);
            Assert.False(bed.Alight);
            Assert.Equal(0, bed.Heat);
        }
    }

    [Fact] public void DryOrSpentRagIsAcceptedAsClothButCannotPilot()
    {
        var bed = Bed(4, false);
        Assert.True(bed.AddRag(0, true));
        Assert.Equal(0, bed.RagOil);
        Run(bed, 300);
        Assert.False(bed.Alight);
        // An unlit oiled rag stays unlit with nothing burning beside it.
        Assert.True(bed.AddRag(RagFuel.OilCapacity, false));
        Run(bed, 300);
        Assert.False(bed.RagLit);
        Assert.Equal(RagFuel.OilCapacity, bed.RagOil, 5);
    }

    [Fact] public void RagIgnitesTheWoodByHeatingItNotAtOnce()
    {
        // Wood Handbook: piloted ignition takes tens of seconds at 20-30 kW/m².
        var cold = IgnitionTime(Bed(8, true), 300);
        Assert.InRange(cold, 30, 90);
        // A warm firebox loses less heat from the wood surface: it catches sooner.
        var warm = IgnitionTime(Bed(8, true), 300, boiler: 180);
        Assert.InRange(warm, 5, cold - 10);
        // Too little flux (the rag shaded under deep cold coal) does not ignite it.
        var buried = Bed(8, true);
        Run(buried, 300, cover: 8);
        Assert.Equal(0, buried.Involved);
    }

    [Fact] public void OnePieceDiesWhenTheRagIsSpentButAPileKeepsBurning()
    {
        var one = Bed(1, true);
        Run(one, 150);
        Assert.True(one.Involved > 0);
        Run(one, 450);
        Assert.Equal(0, one.Involved);
        Assert.True(one.Wood > 0.5f * StarterFuel.PieceMass);

        var pile = Bed(6, true);
        Run(pile, 400);
        Assert.Equal(0, pile.RagOil);
        Assert.True(pile.Flaming > 0.8f);
        Assert.True(pile.MassFlux >= StarterFuel.CriticalMassFlux);
        // Consumed at the game's pace (StarterFuel.Pace), its heat with it.
        Assert.InRange(pile.Heat, 15000, 80000 * StarterFuel.Pace);
    }

    [Fact] public void HotBoilerDoesNotKeepUnsupportedFlamesAlight()
    {
        var one = Bed(1, true);
        Run(one, 900, boiler: 190);
        Assert.Equal(0, one.Involved);
        Assert.False(one.RagLit);
    }

    [Fact] public void WoodCatchesOnBurningCoalWithoutARag()
    {
        var bed = Bed(2, false);
        Run(bed, 120, coalFlux: 40000, boiler: 150);
        Assert.True(bed.Involved > 0);
        Assert.True(bed.Heat > 1000);
    }

    [Fact] public void EstablishmentIsGradualAndExcludesTheRagsFlame()
    {
        var fire = Fire(8, true);
        var inputs = Cold();
        FireTestKit.Run(fire, inputs, 40);
        // The rag alone gives no coal-ignition support.
        Assert.Equal(0, fire.WoodIgnitionSupport(GrateZone.UnderDoor));
        FireTestKit.Run(fire, inputs, 30);
        var early = fire.WoodIgnitionSupport(GrateZone.UnderDoor);
        FireTestKit.Run(fire, inputs, 120);
        var later = fire.WoodIgnitionSupport(GrateZone.UnderDoor);
        Assert.InRange(early, 0.001f, 0.25f);
        Assert.InRange(later, 0.15f, 1f);
        Assert.True(later > 2f * early);
    }

    [Fact] public void CoalOnAYoungWoodFireSmothersItButAnEstablishedFireLightsIt()
    {
        var young = Fire(8, true);
        var inputs = Cold();
        FireTestKit.Run(young, inputs, 45);
        young.AddCoal(3, GrateZone.UnderDoor);
        FireTestKit.Run(young, inputs, 600);
        Assert.True(young.Starters[(int)GrateZone.UnderDoor].Wood > 0.95f * 8 * StarterFuel.PieceMass);
        Assert.True(young.BurningMass < 0.05f);
        Assert.False(young.StarterAlight);

        // A heavy charge before the wood has caught smothers it too. (Once
        // caught, the wood burns at the game's pace, StarterFuel.Pace, hot
        // enough within seconds to light coal laid on it.)
        var heavy = Fire(8, true);
        FireTestKit.Run(heavy, inputs, 45);
        Assert.Equal(0, heavy.Starters[(int)GrateZone.UnderDoor].Involved);
        heavy.AddCoal(8, GrateZone.UnderDoor);
        FireTestKit.Run(heavy, inputs, 600);
        Assert.True(heavy.BurningMass < 0.05f);
        Assert.False(heavy.StarterAlight);

        var established = Fire(8, true);
        FireTestKit.Run(established, inputs, 300);
        established.AddCoal(3, GrateZone.UnderDoor);
        FireTestKit.Run(established, inputs, 15);
        // It catches progressively, not at once.
        Assert.True(established.BurningMass < 1f);
        FireTestKit.Run(established, inputs, 105);
        Assert.True(established.BurningMass > 0.8f);
    }

    [Fact] public void APileBurnsAwayInGameTime()
    {
        var fire = Fire(8, true);
        var inputs = Cold();
        var zone = fire.Starters[(int)GrateZone.UnderDoor];
        FireTestKit.Run(fire, inputs, 240);
        Assert.InRange(zone.Wood, 0.2f * 8 * StarterFuel.PieceMass, 0.7f * 8 * StarterFuel.PieceMass);
        FireTestKit.Run(fire, inputs, 300);
        Assert.True(zone.Wood < 0.05f * 8 * StarterFuel.PieceMass);
        FireTestKit.Run(fire, inputs, 600);
        Assert.True(zone.Wood + zone.Char < 0.1f);
        Assert.False(zone.Burning && zone.Flaming > 0.1f);
    }

    [Fact] public void StartupSpeedupHurriesTheCoalFireButNotTheWood()
    {
        var normal = Fire(8, true);
        var fast = Fire(8, true);
        var slowInputs = Cold();
        var fastInputs = Cold(25);
        for (var t = 0f; t < 300; t += FireTestKit.Dt)
        {
            normal.Step(FireTestKit.Dt, slowInputs);
            fast.Step(FireTestKit.Dt, fastInputs);
        }
        Assert.Equal(normal.Starters[0].Save(), fast.Starters[0].Save());
        Assert.Equal(normal.WoodIgnitionSupport(GrateZone.UnderDoor), fast.WoodIgnitionSupport(GrateZone.UnderDoor));
        normal.AddCoal(3, GrateZone.UnderDoor);
        fast.AddCoal(3, GrateZone.UnderDoor);
        float normalHeat = 0, fastHeat = 0;
        for (var t = 0f; t < 60; t += FireTestKit.Dt)
        {
            normal.Step(FireTestKit.Dt, slowInputs); normalHeat += normal.Heat;
            fast.Step(FireTestKit.Dt, fastInputs); fastHeat += fast.Heat;
        }
        // The coal still catches under the speedup, and then burns faster.
        Assert.True(fast.CoalMass < normal.CoalMass);
        Assert.True(fastHeat > 1.5f * normalHeat);
    }

    // Intended change, kept narrow: the per-zone ramp of the startup speedup
    // applies only where wood or a rag lies in the zone or beside it. A coal
    // fire with no wood near burns with the whole stock multiplier at any size.
    // About 0.3 kg burning in each zone, under full ignition support, its burning rate settled.
    private static FireModel Settled()
    {
        var fire = FireTestKit.BurningFire(S282, 3.6f);
        FireTestKit.Run(fire, Cold(), 30);
        return fire;
    }

    [Fact] public void SmallCoalBedWithoutWoodKeepsTheWholeStartupSpeedup()
    {
        float Burned(float speedup)
        {
            var fire = Settled();
            var before = fire.BurningMass;
            for (var t = 0f; t < 0.2f; t += FireTestKit.Dt) fire.Step(FireTestKit.Dt, Cold(speedup));
            return before - fire.BurningMass;
        }
        var ratio = Burned(25) / Burned(1);
        Assert.InRange(ratio, 20f, 26f);
    }

    [Fact] public void SpeedupRampsOnlyInZonesWithWoodInOrBeside()
    {
        var fire = Settled();
        // Unlit wood in the back left corner; the front right corner is two zones away.
        fire.AddWood(StarterFuel.PieceMass, GrateZone.BackLeft);
        var stock = Settled();
        var near = fire.Zones[(int)GrateZone.BackLeft].Burning;
        var far = fire.Zones[(int)GrateZone.FrontRight].Burning;
        for (var t = 0f; t < 0.2f; t += FireTestKit.Dt)
        {
            fire.Step(FireTestKit.Dt, Cold(25));
            stock.Step(FireTestKit.Dt, Cold(25));
        }
        var nearBurned = near - fire.Zones[(int)GrateZone.BackLeft].Burning;
        var farBurned = far - fire.Zones[(int)GrateZone.FrontRight].Burning;
        var stockFar = far - stock.Zones[(int)GrateZone.FrontRight].Burning;
        // The far zone burns as a plain coal fire; the zone under the wood at
        // 1 + 24 x 0.3 / 0.5, about 15 times, not 25.
        Assert.InRange(farBurned / stockFar, 0.95f, 1.05f);
        Assert.InRange(nearBurned / farBurned, 0.45f, 0.75f);
    }

    [Fact] public void FireSpreadsToWoodInTheNextZoneButNotAcrossTheGrate()
    {
        var next = Fire(8, true);
        next.AddWood(4 * StarterFuel.PieceMass, GrateZone.Center);
        FireTestKit.Run(next, Cold(), 420);
        Assert.True(next.Starters[(int)GrateZone.Center].Involved > 0);
        // The front of the grate, with bare bars between, stays cold.
        var far = Fire(8, true);
        far.AddWood(4 * StarterFuel.PieceMass, GrateZone.Front);
        FireTestKit.Run(far, Cold(), 900);
        Assert.False(far.Starters[(int)GrateZone.Front].Alight);
    }

    [Fact] public void AirIsNeverCreatedAndShortAirMakesSmoke()
    {
        var open = Bed(8, true);
        var shut = Bed(8, true);
        var openSmoke = 0f; var shutSmoke = 0f;
        for (var i = 0; i < 6000; i++)
        {
            open.Step(0.05f, 0.07f, 0, 0, 0, 20, false);
            shut.Step(0.05f, 0.004f, 0, 0, 0, 20, false);
            Assert.InRange(open.AirUsed, 0, 0.070001f);
            Assert.InRange(shut.AirUsed, 0, 0.004001f);
            openSmoke += open.Smoke; shutSmoke += shut.Smoke;
        }
        Assert.True(shutSmoke > openSmoke);
        Assert.True(shut.Wood > open.Wood);
    }

    [Fact] public void StoichiometryMatchesDryWood()
    {
        Assert.Equal(StarterFuel.WoodAir, StarterFuel.CharYield * StarterFuel.CharAir + (1 - StarterFuel.CharYield) * StarterFuel.VolatileAir, 3);
        Assert.InRange(StarterFuel.VolatileAir, 4.2f, 4.6f);
        Assert.InRange(StarterFuel.CharYield * StarterFuel.CharHeat + (1 - StarterFuel.CharYield) * StarterFuel.VolatileHeat, StarterFuel.WoodHeat - 1e3f, StarterFuel.WoodHeat + 1e3f);
        // Huggett's 13.1 MJ per kg of oxygen, within a few per cent, for the whole wood.
        Assert.InRange(StarterFuel.WoodHeat / (StarterFuel.WoodAir * 0.232f), 12.7e6f, 14e6f);
    }

    [Fact] public void HeatReleasedIsTheFuelsChemicalEnergyLessTheWater()
    {
        var bed = Bed(8, true);
        var energy = 0.0;
        for (var t = 0f; t < 6000; t += 0.25f)
        {
            bed.Step(0.25f, 0.5f, 0, 0, 0, 180, false);
            energy += bed.Heat * 0.25;
        }
        var dry = 8 * StarterFuel.PieceMass * (1 - StarterFuel.Moisture) - bed.Wood * (1 - StarterFuel.Moisture);
        var charBurnt = dry * StarterFuel.CharYield - bed.Char;
        var water = dry / (1 - StarterFuel.Moisture) * StarterFuel.Moisture;
        var expected = dry * (1 - StarterFuel.CharYield) * StarterFuel.VolatileHeat + charBurnt * StarterFuel.CharHeat
                       + RagFuel.OilCapacity * StarterFuel.OilHeat - water * StarterFuel.EvaporationEnergy;
        Assert.True(dry > 4f);
        Assert.Equal(1, energy / expected, 2);
    }

    [Fact] public void TimestepDoesNotChangeTheBurn()
    {
        StarterFuel run(float dt) { var b = Bed(6, true); Run(b, 600, dt: dt); return b; }
        var fine = run(0.02f);
        foreach (var dt in new[] { 0.2f, 1f })
        {
            var coarse = run(dt);
            Assert.Equal(fine.Wood, coarse.Wood, 1);
            Assert.InRange(coarse.Wood / fine.Wood, 0.97f, 1.03f);
            Assert.InRange(coarse.Char / fine.Char, 0.97f, 1.03f);
            Assert.Equal(fine.Burning, coarse.Burning);
        }
    }

    [Fact] public void DrowningQuenchesEverything()
    {
        var bed = Bed(8, true);
        Run(bed, 200);
        Assert.True(bed.Burning);
        bed.Step(1, 1, 0, 0, 0, 20, true);
        Assert.False(bed.Alight);
        Assert.Equal(0, bed.Heat);
        Run(bed, 120);
        Assert.False(bed.Alight);
    }

    [Fact] public void SaveRestoresTheSameBurnWithoutNewFuel()
    {
        var bed = Bed(6, true); Run(bed, 110);
        Assert.True(bed.Involved > 0 && bed.RagOil > 0);
        var copy = new StarterFuel(); copy.Restore(bed.Save());
        Run(bed, 300); Run(copy, 300);
        Assert.Equal(bed.Save(), copy.Save());
    }

    [Fact] public void FirstPreviewSavesStillLoad()
    {
        var legacy = new[] { 2.6f, 0.05f, 700f, 0.03f, 1f, 1f };
        var bed = new StarterFuel();
        bed.Restore(legacy);
        Assert.Equal(2.6f, bed.Wood);
        Assert.True(bed.RagLit && bed.Burning && bed.Involved > 0);
        Assert.Equal(1, bed.RagCount);

        var fire = new FireModel(S282);
        fire.RestoreStarters(Enumerable.Range(0, GrateLayout.ZoneCount).SelectMany(i => i == 0 ? legacy : new float[6]).ToArray());
        Assert.True(fire.StarterAlight);
        Assert.Equal(2.6f, fire.Starters[0].Wood);
        var current = fire.SaveStarters();
        Assert.Equal(GrateLayout.ZoneCount * StarterFuel.StateLength, current.Length);
        var copy = new FireModel(S282);
        copy.RestoreStarters(current);
        Assert.Equal(current, copy.SaveStarters());
        copy.RestoreStarters(new float[7]);
        Assert.Equal(0, copy.StarterMass);
    }

    [Fact] public void InvalidStarterStateCannotIntroduceNonfiniteFuel()
    {
        var bed = Bed(3, true);
        bed.Restore(new[] { float.NaN, 0f, 20f, 0f, 0f, 0f, 0f, 0f, 0f, 0f });
        Assert.Equal(0, bed.Mass);
        Assert.False(bed.Alight);
        Assert.Equal(0, bed.AddWood(float.PositiveInfinity, 10));
        bed.Step(float.NaN, 1, 0, 0, 0, float.NaN, false);
        bed.Step(1, float.PositiveInfinity, float.NaN, float.NaN, float.NaN, float.PositiveInfinity, false);
        Assert.False(float.IsNaN(bed.Heat));
    }

    [Fact] public void RagsPoolUpToFourAndALitRagLightsAnOiledOne()
    {
        var bed = Bed(4, false);
        Assert.True(bed.AddRag(RagFuel.OilCapacity, false));
        Assert.True(bed.AddRag(RagFuel.OilCapacity, true));
        Assert.True(bed.RagLit);
        Assert.Equal(2, bed.RagCount);
        Assert.True(bed.AddRag(0.01f, false));
        Assert.True(bed.AddRag(0.01f, false));
        Assert.False(bed.AddRag(0.01f, false));
        Run(bed, 50, air: 1f);
        // Four rags burn four times as fast as one.
        Assert.Equal(2 * RagFuel.OilCapacity + 0.02f - 4 * RagFuel.OilBurnRate * 50, bed.RagOil, 3);
    }

    [Fact] public void CapacityIsSharedAndDumpRemovesStarterFuel()
    {
        var fire = new FireModel(S282);
        Assert.Equal(StarterFuel.ZoneCapacity, fire.AddWood(100, GrateZone.UnderDoor));
        Assert.Equal(fire.Parameters.MaxCoalCapacity - fire.StarterMass, fire.AddCoal(10000, GrateZone.Center), 3);
        var before = fire.StarterMass;
        fire.Dump(1, 1);
        Assert.True(fire.StarterMass < before);
        Assert.True(fire.Ashpan > 0);
    }

    [Fact] public void OilTorchKeepsOnlyTheRemainingRagTime()
    {
        var fire = new OilFire(new FireboxParameters());
        fire.OfferRag(7);
        Assert.Equal(7, fire.Torch);
        fire.OfferRag(3);
        Assert.Equal(7, fire.Torch);
        fire.Extinguish();
        fire.OfferRag(float.NaN);
        Assert.Equal(0, fire.Torch);
    }

    [Fact] public void StarterStepDoesNotAllocate()
    {
        var fire = Fire(8, true);
        fire.AddWood(4 * StarterFuel.PieceMass, GrateZone.Center);
        var inputs = Cold();
        FireTestKit.Run(fire, inputs, 5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 200; i++) fire.Step(FireTestKit.Dt, inputs);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact] public void AdviceFollowsTheWoodFireThroughItsStages()
    {
        var injectors = new InjectorReading[0];
        var fire = new FireModel(S282);
        Assert.Contains("four or more", FiringAdvice.ForCoal(fire, 1, 1, injectors).Text);
        fire.AddWood(8 * StarterFuel.PieceMass, GrateZone.UnderDoor);
        Assert.Contains("oil a rag", FiringAdvice.ForCoal(fire, 1, 1, injectors).Text);
        fire.AddRag(RagFuel.OilCapacity, true, GrateZone.UnderDoor);
        var inputs = Cold();
        FireTestKit.Run(fire, inputs, 20);
        Assert.Contains("smother", FiringAdvice.ForCoal(fire, 1, 1, injectors).Text);
        FireTestKit.Run(fire, inputs, 45);
        Assert.Contains("has caught", FiringAdvice.ForCoal(fire, 1, 1, injectors).Text);
        FireTestKit.Run(fire, inputs, 240);
        Assert.Contains("burning well", FiringAdvice.ForCoal(fire, 1, 1, injectors).Text);
    }
}
