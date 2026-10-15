using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FireboxCharacterTests
{
    private static readonly FireboxParameters Stock = new FireboxParameters();
    private static readonly float HalfLoadAir = 0.5f * Stock.MaxCombustionRate * FireTestKit.StockAirPerCoal;

    private static readonly GrateCharacter Switcher = new GrateCharacter
        { BackRowDraft = 1.04f, FrontRowDraft = 0.96f, SideDraft = 1.04f, OverFireMixing = 0.8f };

    private static readonly GrateCharacter BigChamber = new GrateCharacter
        { BackRowDraft = 1.08f, FrontRowDraft = 0.92f, OverFireMixing = 1.25f, ClinkerFactor = 0.7f, AshFactor = 0.6f };

    private static float FrontToBack(FireModel fire) =>
        fire.DraftWeight(GrateZone.Front) / fire.DraftWeight(GrateZone.UnderDoor);

    [Fact]
    public void ASwitchersNarrowBoxDrawsMoreEvenlyEndToEnd()
    {
        var typical = new FireModel(Stock);
        var switcher = new FireModel(Stock) { Character = Switcher };
        var big = new FireModel(Stock) { Character = BigChamber };

        // A sealed arch draws hardest at the back; the switcher's and the big
        // chamber's boxes less unevenly.
        Assert.True(FrontToBack(typical) < 0.85f);
        Assert.True(FrontToBack(switcher) > FrontToBack(typical) + 0.1f && FrontToBack(switcher) < 1f);
        Assert.True(FrontToBack(big) > FrontToBack(typical) && FrontToBack(big) < 1f);
        var mean = Enumerable.Range(0, GrateLayout.ZoneCount).Average(i => switcher.DraftWeight((GrateZone)i));
        Assert.Equal(1.0, mean, 3);
    }

    [Fact]
    public void ABoxWithoutAChamberSmokesAfterFiringWhereABigOneBurnsItOff()
    {
        // A quarter of the box fired at once on a fire tended at half load,
        // the door shut, as in the fire model's own smoke test.
        float SootAfterFiring(GrateCharacter character)
        {
            var tended = new TendedFire(Stock, 0.5f, tuning: FireTestKit.CleanCoal);
            tended.Fire.Character = character;
            tended.Run(60f);
            tended.Fire.AddCoalEvenly(0.25f * Stock.MaxCoalCapacity);
            tended.Run(10f);
            return tended.Fire.SootDensity;
        }

        var switcher = SootAfterFiring(Switcher);
        var typical = SootAfterFiring(GrateCharacter.Typical);
        var big = SootAfterFiring(BigChamber);

        Assert.True(switcher > typical && typical > big, $"soot after firing: switcher {switcher:F2}, typical {typical:F2}, big chamber {big:F2}");
    }

    [Fact]
    public void ARoadsCoalSetsHowSoonTheFireClinkersAndTheAshBuilds()
    {
        (float Clinker, float Ash) HardHour(GrateCharacter character)
        {
            var rate = 0.6f * Stock.MaxCombustionRate;
            var target = rate * Stock.BurnTime;
            var fire = new FireModel(Stock) { Character = character, GrateArea = 2.12f };
            fire.Restore(fireOn: true, coalMass: target);
            var inputs = FireTestKit.Inputs(rate * FireModel.AirPerCoal);
            for (var t = 0f; t < 3600f; t += FireTestKit.Dt)
            {
                while (fire.CoalMass + 1.5f <= target)
                    fire.AddCoal(3f, fire.NeediestZone());
                fire.Step(FireTestKit.Dt, inputs);
            }
            return (fire.ClinkerMass, fire.AshMass + fire.ClinkerMass + fire.Ashpan);
        }

        var typical = HardHour(GrateCharacter.Typical);
        var rockSprings = HardHour(BigChamber);

        Assert.True(rockSprings.Clinker < 0.8f * typical.Clinker, $"clinker {typical.Clinker:F2} -> {rockSprings.Clinker:F2} kg");
        Assert.True(rockSprings.Ash < 0.7f * typical.Ash, $"ash {typical.Ash:F2} -> {rockSprings.Ash:F2} kg");
    }

    private static float[] Landing(StokerSpec spec)
    {
        var lumps = new float[GrateLayout.ZoneCount];
        var fines = new float[GrateLayout.ZoneCount];
        new Stoker(spec, Stock).Distribute(StokerControls.Working(0.5f), 15f, HalfLoadAir, lumps, fines);
        return lumps.Zip(fines, (l, f) => l + f).ToArray();
    }

    [Fact]
    public void AStokersHabitsStarveItsBackCornersOrOneSide()
    {
        var typical = Landing(new StokerSpec(Stock, 2f));
        var bk = Landing(new StokerSpec(Stock, 2f) { BackLaneShare = 0.7f * new StokerSpec(Stock, 1f).BackLaneShare });
        var ht = Landing(new StokerSpec(Stock, 2f) { LumpsLeftBias = 0.1f });

        Assert.True(bk[(int)GrateZone.BackLeft] < 0.85f * typical[(int)GrateZone.BackLeft]);
        Assert.True(bk[(int)GrateZone.BackRight] < 0.85f * typical[(int)GrateZone.BackRight]);
        Assert.True(ht[(int)GrateZone.BackRight] < 0.95f * ht[(int)GrateZone.BackLeft],
            $"HT back corners: left {ht[(int)GrateZone.BackLeft]:F3}, right {ht[(int)GrateZone.BackRight]:F3}");
        Assert.Equal(typical.Sum(), ht.Sum(), 3);
    }
}
