using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class StokerHandlingTests
{
    private static readonly FireboxParameters Box = new FireboxParameters(180f, 90f, 0.6f);
    private static Stoker New(float fines = 0.25f) => new Stoker(new StokerSpec(Box, 2.5f)
        { FinesShare = fines, MeanCoalBetweenJams = 0f }, Box, new Random(4));
    private static StokerInputs Input(float moisture = CoalMoisture.Delivered, float throttle = 1f, float jets = 0.35f) => new StokerInputs
    {
        Controls = new StokerControls { Direction = StokerDirection.Forward, Throttle = throttle, MasterJet = jets,
            LeftBack = 0.6f, RightBack = 0.6f, LeftFront = 0.6f, RightFront = 0.6f, Fines = 0.5f },
        BoilerPressure = 15f, TenderCoal = 10000f, CoalMoisture = moisture,
    };

    [Theory]
    [InlineData(0.03f, 0.1f)]
    [InlineData(0.03f, 0.5f)]
    [InlineData(0.06f, 0.25f)]
    [InlineData(0.10f, 0.25f)]
    [InlineData(0.10f, 0.5f)]
    public void FuelIsEitherOnTheTableOnTheGrateOrLost(float moisture, float fines)
    {
        var stoker = New(fines);
        var fire = FireTestKit.BurningFire(Box, 20f);
        double drawn = 0, landed = 0, lost = 0;
        for (var n = 0; n < 3000; n++)
        {
            var before = fire.CoalMass;
            var flow = stoker.Step(0.02f, Input(moisture, jets: 0.12f), fire);
            Assert.InRange(fire.CoalMass - before - flow.Fed, -0.0001f, 0.0001f);
            drawn += flow.Drawn; landed += flow.Fed; lost += flow.CinderRate * 0.02f;
            fire.RemoveCoal(flow.Fed);
        }
        Assert.InRange(Math.Abs(drawn - landed - lost - stoker.TableCoal), 0, 0.005);
    }

    [Fact]
    public void DryCoalScoursPackingAndDisablingJamsClearsSavedPacking()
    {
        var s = New(); s.Spec.CoalToPack = 100f;
        s.RestoreState(new StokerState { Packing = .7f });
        var fire = FireTestKit.BurningFire(Box, 20f);
        for (int i = 0; i < 3000; i++) { var f = s.Step(.02f, Input(), fire); fire.RemoveCoal(f.Fed); }
        Assert.Equal(0f, s.Packing);
        s.Spec.PackingJams = false;
        s.RestoreState(new StokerState { Packing = 1f });
        for (int i = 0; i < 3000; i++) { var f = s.Step(.02f, Input(.1f), fire); fire.RemoveCoal(f.Fed); }
        Assert.Equal(0f, s.Packing);
    }

    [Fact]
    public void HotDryTableWearsCleanAndAnEmptyTableCanBeScraped()
    {
        var s = New(); var fire = FireTestKit.BurningFire(Box, 20f);
        s.RestoreState(new StokerState { TableWarmth = 1f, TableCake = 1f });
        var empty = Input(throttle: 0f); empty.TenderCoal = 0f;
        for (int i = 0; i < 2000; i++) s.Step(.1f, empty, fire);
        Assert.Equal(0f, s.CaptureState().TableCake);
        s.RestoreState(new StokerState { TableCake = 1f });
        Assert.Equal(0f, s.ClearTable(fire));
        Assert.Equal(0f, s.CaptureState().TableCake);
    }

    [Fact]
    public void DryFeedRecoversFromABakedWetTableWithoutRepeatedRaking()
    {
        var s = New(.5f); s.Spec.PackingJams = false;
        s.RestoreState(new StokerState { TableCoal = 7f, TableFines = 3.5f,
            TableWater = 7f * (CoalMoisture.WaterPerKg(.1f) - CoalMoisture.WaterPerKg(.03f)),
            TableWarmth = 1f, TableCake = 1f });
        var fire = FireTestKit.BurningFire(Box, 20f); StokerFlow flow = default;
        for (int i = 0; i < 9000; i++)
        {
            flow = s.Step(.1f, Input(), fire);
            fire.RemoveCoal(flow.Fed);
        }
        Assert.InRange(s.CaptureState().TableCake, 0f, .001f);
        Assert.InRange(s.TableCoal, 0f, .1f);
        Assert.InRange(flow.Drawn / .1f, 2.49f, 2.501f);
    }

    [Fact]
    public void ClosingTheFineJetDoesNotWeakenTheLumpJetsOrViceVersa()
    {
        var s = New(); var controls = Input(jets: .15f).Controls;
        var lumps = new float[9]; var fines = new float[9];
        var otherLumps = new float[9]; var otherFines = new float[9];
        s.DistributeAtRate(controls, 15f, 1f, lumps, fines, 2.5f);
        var closed = controls; closed.Fines = 0f;
        s.DistributeAtRate(closed, 15f, 1f, otherLumps, otherFines, 2.5f);
        Assert.Equal(lumps, otherLumps);
        closed = controls; closed.LeftFront = 0f;
        s.DistributeAtRate(closed, 15f, 1f, otherLumps, otherFines, 2.5f);
        Assert.Equal(fines, otherFines);
    }

    [Fact]
    public void RetainedCoalDrainsGraduallyAndLandsConsistentlyAcrossFrameRates()
    {
        float[] Run(float dt)
        {
            var s = New(); s.RestoreState(new StokerState { TableCoal = 7.5f, TableFines = 1.875f });
            var fire = new FireModel(Box); var input = Input(throttle: 0f); input.TenderCoal = 0f;
            var first = s.Step(dt, input, fire);
            double lost = first.CinderRate * dt;
            Assert.InRange(first.Fed, 0f, 2.5f * dt + .00001f);
            Assert.True(s.TableCoal > 7f);
            for (int i = 1; i < (int)Math.Round(30f / dt); i++) lost += s.Step(dt, input, fire).CinderRate * dt;
            Assert.InRange(fire.CoalMass + s.TableCoal + lost, 7.499, 7.501);
            return fire.Zones.Select(z => z.Coal).ToArray();
        }
        var fine = Run(1f / 144f);
        foreach (var dt in new[] { .02f, .1f })
        {
            var other = Run(dt);
            for (int i = 0; i < 9; i++) Assert.InRange(Math.Abs(other[i] - fine[i]), 0f, .04f);
        }
    }

    [Theory]
    [InlineData(.04f)]
    [InlineData(.06f)]
    [InlineData(.10f)]
    public void RunningWetTableHoldUpDoesNotDependOnFrameRate(float moisture)
    {
        float Run(float dt)
        {
            var s = New(); s.Spec.PackingJams = false;
            s.RestoreState(new StokerState { TableWarmth = 1f, EngineSpeed = 1f });
            var fire = FireTestKit.BurningFire(Box, 20f);
            for (int i = 0; i < (int)Math.Round(30f / dt); i++)
            {
                var flow = s.Step(dt, Input(moisture), fire);
                fire.RemoveCoal(flow.Fed);
            }
            return s.TableCoal;
        }
        var fine = Run(1f / 144f);
        Assert.InRange(Math.Abs(Run(.02f) - fine), 0f, .02f);
        Assert.InRange(Math.Abs(Run(.1f) - fine), 0f, .02f);
    }

    [Theory]
    [InlineData(.03f)]
    [InlineData(.10f)]
    public void AStoppedScrewAndShutJetsLeaveTheTableFuelWhereItIs(float moisture)
    {
        var s = New(); var fire = new FireModel(Box);
        var water = 4f * (CoalMoisture.WaterPerKg(moisture) - CoalMoisture.WaterPerKg(.03f));
        s.RestoreState(new StokerState { TableCoal = 4f, TableFines = 1f, TableWater = water, TableWarmth = 1f });
        var stopped = Input(moisture, throttle: 0f, jets: 0f);
        stopped.TenderCoal = 0f; stopped.Controls.Direction = StokerDirection.Neutral;
        for (int i = 0; i < 1000; i++) Assert.Equal(0f, s.Step(.02f, stopped, fire).EjectedRate);
        Assert.Equal(4f, s.TableCoal);
        Assert.Equal(water, s.CaptureState().TableWater);
        Assert.Equal(0f, fire.CoalMass);
    }

    [Fact]
    public void StrongerJetsClearAStoppedTableFasterThanCrackedJets()
    {
        float Held(float jets)
        {
            var s = New(); var fire = new FireModel(Box);
            s.RestoreState(new StokerState { TableCoal = 4f, TableFines = 1f, TableWarmth = 1f });
            var input = Input(throttle: 0f, jets: jets); input.TenderCoal = 0f;
            input.Controls.Direction = StokerDirection.Neutral;
            for (int i = 0; i < 50; i++) s.Step(.02f, input, fire);
            return s.TableCoal;
        }
        Assert.Equal(4f, Held(0f));
        Assert.True(Held(.05f) > Held(.35f) + .5f);
    }

    [Fact]
    public void TableNoticeUsesTheFuelLeftAfterDeliveryRatherThanTheMidTickPeak()
    {
        var s = New();
        s.RestoreState(new StokerState { TableCoal = 2.6f, TableFines = .65f, EngineSpeed = 1f });
        Assert.False(s.TableNeedsClearing);
        var flow = s.Step(.1f, Input(), new FireModel(Box));
        Assert.InRange(s.TableFill, .2f, .35f);
        Assert.False(flow.TableNeedsClearing);
    }

    [Fact]
    public void TableNoticeHasHysteresisAndFlowCarriesTheVisibleReading()
    {
        var s = New(); s.RestoreState(new StokerState { TableCoal = 3f });
        Assert.True(s.TableNeedsClearing);
        var fire = new FireModel(Box);
        s.ClearTable(fire, 1f);
        Assert.True(s.TableNeedsClearing);
        var restored = New(); restored.RestoreState(s.CaptureState());
        Assert.True(restored.TableNeedsClearing);
        s.ClearTable(fire, 1f);
        Assert.False(s.TableNeedsClearing);
        StokerFlow flow = default;
        s.ReadGauges(Input().Controls, 15f, ref flow);
        Assert.Equal(s.TableCoal, flow.TableCoal);
        Assert.Equal(s.TableFill, flow.TableFill);
        Assert.Equal(s.TableNeedsClearing, flow.TableNeedsClearing);
    }

    [Fact]
    public void NormalDryCoalKeepsItsRatedFeedAndDoesNotBuildPackingOrTableCoal()
    {
        var stoker = New(); var fire = FireTestKit.BurningFire(Box, 20f); StokerFlow flow = default;
        for (int n = 0; n < 1500; n++) { flow = stoker.Step(.02f, Input(throttle: .5f), fire); fire.RemoveCoal(flow.Fed); }
        Assert.InRange(flow.Drawn / .02f, 1.24f, 1.251f);
        Assert.Equal(0f, stoker.Packing);
        Assert.InRange(stoker.TableCoal, 0f, 0.00001f);
    }

    [Fact]
    public void MoreFeedOverloadsTheSameWeakJets()
    {
        var stoker = New(); var low = new float[9]; var high = new float[9]; var spare = new float[9];
        var controls = Input(jets: .15f).Controls;
        stoker.DistributeAtRate(controls, 15f, 0f, low, spare, feedRate: .4f);
        stoker.DistributeAtRate(controls, 15f, 0f, high, spare, feedRate: 2.5f);
        float Front(float[] a) => a[(int)GrateZone.FrontLeft] + a[(int)GrateZone.Front] + a[(int)GrateZone.FrontRight];
        Assert.True(Front(high) < Front(low));
    }

    [Fact]
    public void WetFinesBindWithoutCreatingOrDestroyingFuel()
    {
        var stoker = New(); var dryL = new float[9]; var dryF = new float[9]; var wetL = new float[9]; var wetF = new float[9];
        var controls = Input().Controls;
        var dryLost = stoker.Distribute(controls, 15f, 0f, dryL, dryF, CoalMoisture.Delivered);
        var wetLost = stoker.Distribute(controls, 15f, 0f, wetL, wetF, CoalMoisture.Soaked);
        Assert.True(wetF.Sum() < dryF.Sum() && wetL.Sum() > dryL.Sum());
        Assert.InRange(wetL.Sum() + wetF.Sum() + wetLost, .99999f, 1.00001f);
        Assert.InRange(dryL.Sum() + dryF.Sum() + dryLost, .99999f, 1.00001f);
    }

    [Fact]
    public void WetConveyorLoadCutsFeedAndRaisesEnginePressureWithoutFalsifyingJetPressure()
    {
        (StokerFlow Flow, Stoker Stoker) Run(float moisture)
        {
            var stoker = New(.45f); var fire = FireTestKit.BurningFire(Box, 20f); StokerFlow flow = default;
            for (int n = 0; n < 2000; n++) { flow = stoker.Step(.02f, Input(moisture), fire); fire.RemoveCoal(flow.Fed); }
            return (flow, stoker);
        }
        var dry = Run(.03f); var wet = Run(.10f);
        Assert.True(wet.Flow.Drawn < dry.Flow.Drawn);
        Assert.True(wet.Flow.EnginePressure > dry.Flow.EnginePressure);
        Assert.Equal(dry.Flow.ManifoldPressure, wet.Flow.ManifoldPressure);
        Assert.Equal(dry.Flow.LeftFrontPressure, wet.Flow.LeftFrontPressure);
    }

    [Fact]
    public void PackingNeedsCoalToPassAndReversingRelievesIt()
    {
        var stoker = New(.5f); stoker.Spec.CoalToPack = 10f;
        var fire = FireTestKit.BurningFire(Box, 20f);
        var stopped = Input(.1f, throttle: 0);
        for (int n = 0; n < 1000; n++) stoker.Step(.02f, stopped, fire);
        Assert.Equal(0f, stoker.Packing);
        for (int n = 0; n < 10000 && !stoker.Jammed; n++) { var f = stoker.Step(.02f, Input(.1f), fire); fire.RemoveCoal(f.Fed); }
        Assert.True(stoker.Jammed && stoker.Packing > .9f);
        var reverse = Input(.1f); reverse.Controls.Direction = StokerDirection.Reverse;
        for (int n = 0; n < 1000 && stoker.Jammed; n++) stoker.Step(.02f, reverse, fire);
        Assert.False(stoker.Jammed);
        Assert.InRange(stoker.Packing, 0f, .25f);
    }

    [Fact]
    public void RetainedFuelAndItsWaterSurviveARefillAndARecreatedStoker()
    {
        var stoker = New(.5f); var fire = new FireModel(Box);
        double drawn = 0, lost = 0;
        for (int n = 0; n < 300; n++)
        {
            var flow = stoker.Step(.02f, Input(.1f, jets: .08f), fire);
            drawn += flow.Drawn; lost += flow.CinderRate * .02;
        }
        Assert.True(stoker.TableCoal > 0f);
        var state = stoker.CaptureState(); var restored = New(.5f); restored.RestoreState(state);
        Assert.Equal(stoker.TableCoal, restored.TableCoal);
        Assert.Equal(stoker.Packing, restored.Packing);
        var empty = Input(.03f); empty.TenderCoal = 0; empty.Controls.Throttle = 0;
        for (int n = 0; n < 6000; n++) lost += restored.Step(.02f, empty, fire).CinderRate * .02;
        var water = CoalMoisture.WaterPerKg(.1f) - CoalMoisture.WaterPerKg(.03f);
        // A cold fire stores landed water with its fresh coal; any coal
        // lost over the arch carries its share of rain water away.
        Assert.InRange(Math.Abs(drawn - lost - fire.CoalMass - restored.TableCoal), 0, .002);
        Assert.InRange(Math.Abs((drawn - lost) * water - fire.FreshWater - restored.CaptureState().TableWater), 0, .002);
    }

    [Fact]
    public void TheHookMovesRetainedCoalIntoTheFireWithoutTakingMoreFromTheTender()
    {
        var stoker = New(.5f); var fire = new FireModel(Box);
        for (int n = 0; n < 300; n++) stoker.Step(.02f, Input(.1f, jets: .04f), fire);
        var table = stoker.TableCoal; var coal = fire.CoalMass;
        var moved = stoker.ClearTable(fire, 1f);
        Assert.InRange(moved, 0.01f, 1f);
        Assert.Equal(table - moved, stoker.TableCoal, 4);
        Assert.Equal(coal + moved, fire.CoalMass, 4);
    }

    [Fact]
    public void SaveStateCannotInjectNonfiniteOrUnboundedFuel()
    {
        var stoker = New();
        stoker.RestoreState(new StokerState { TableCoal = float.PositiveInfinity, TableFines = float.NaN, TableWater = float.PositiveInfinity, Packing = float.NaN });
        Assert.Equal(0f, stoker.TableCoal); Assert.Equal(0f, stoker.Packing);
        stoker.RestoreState(new StokerState { TableCoal = 1e20f, TableFines = 1e20f, TableWater = 1e20f, Packing = 10 });
        Assert.InRange(stoker.TableCoal, 0f, stoker.Spec.MaxFeedRate * stoker.Spec.TableCapacitySeconds);
        Assert.InRange(stoker.CaptureState().TableFines, 0f, stoker.TableCoal);
    }

    [Fact]
    public void DifferentTickLengthsGiveTheSameWetFuelAccountingAndPacking()
    {
        (double Drawn, float Held, float Packing) Run(float dt)
        {
            var s = New(.4f); var fire = FireTestKit.BurningFire(Box, 20f); double drawn = 0;
            var steps = (int)Math.Round(60f / dt);
            for (int i = 0; i < steps; i++) { var f = s.Step(dt, Input(.1f, jets: .15f), fire); drawn += f.Drawn; fire.RemoveCoal(f.Fed); }
            return (drawn, s.TableCoal, s.Packing);
        }
        var fine = Run(.01f);
        foreach (var dt in new[] { .05f, .2f })
        {
            var coarse = Run(dt);
            Assert.True(coarse.Drawn / fine.Drawn >= .98 && coarse.Drawn / fine.Drawn <= 1.02, $"dt={dt} fine={fine}, coarse={coarse}");
            Assert.InRange(coarse.Held / fine.Held, .95, 1.05);
            Assert.InRange(coarse.Packing / fine.Packing, .96, 1.04);
        }
    }

    [Fact]
    public void DisablingJamsStillAllowsWetCoalToFeedWithoutAnUnclearablePackingStall()
    {
        var s = New(.5f); s.Spec.CoalToPack = 1f; s.Spec.PackingJams = false;
        var fire = FireTestKit.BurningFire(Box, 20f); double drawn = 0;
        for (int i = 0; i < 1000; i++) { var f = s.Step(.02f, Input(.1f), fire); drawn += f.Drawn; fire.RemoveCoal(f.Fed); }
        Assert.False(s.Jammed);
        Assert.True(drawn > 1f);
        Assert.Equal(0f, s.Packing);
    }

    [Fact]
    public void HotTableShedsWetCoalBetterAndStrongerJetsClearItsBacklog()
    {
        var cold = New(.4f); var hot = New(.4f);
        hot.RestoreState(new StokerState { TableWarmth = 1f });
        var a = new FireModel(Box); var b = new FireModel(Box);
        for (int i = 0; i < 200; i++) { cold.Step(.02f, Input(.1f, jets: .15f), a); hot.Step(.02f, Input(.1f, jets: .15f), b); }
        Assert.True(hot.TableCoal < cold.TableCoal);
        var held = cold.TableCoal;
        var stronger = Input(.1f, throttle: 0f, jets: .5f); stronger.TenderCoal = 0f;
        for (int i = 0; i < 500; i++) cold.Step(.02f, stronger, a);
        Assert.True(cold.TableCoal < .1f * held);
    }

    [Fact]
    public void RunningStokerHandlingAllocatesNoGarbage()
    {
        var s = New(); var fire = FireTestKit.BurningFire(Box, 20f); var input = Input(.1f);
        for (int i = 0; i < 100; i++) { var f = s.Step(.02f, input, fire); fire.RemoveCoal(f.Fed); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++) { var f = s.Step(.02f, input, fire); fire.RemoveCoal(f.Fed); }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void CoarseDryCoalNeedsMoreCrushingEffortThanSlack()
    {
        StokerFlow Run(float fineShare)
        {
            var s = New(fineShare); var fire = FireTestKit.BurningFire(Box, 20f); StokerFlow flow = default;
            for (int i = 0; i < 1000; i++) { flow = s.Step(.02f, Input(throttle: .5f), fire); fire.RemoveCoal(flow.Fed); }
            return flow;
        }
        var coarse = Run(.1f); var slack = Run(.5f);
        Assert.True(coarse.EnginePressure > slack.EnginePressure);
        Assert.True(coarse.Drawn < slack.Drawn);
    }

    [Fact]
    public void BoundWetFinesJoinTheFreshCoalInsteadOfInstantlyBurning()
    {
        var s = New(); var a = FireTestKit.BurningFire(Box, 20f); var b = FireTestKit.BurningFire(Box, 20f);
        var lumps = new float[9]; var fines = new float[9];
        var dryLost = s.Distribute(Input().Controls, 15f, 0f, lumps, fines, .03f); a.AddStoked(lumps, fines, .03f);
        var wetLost = s.Distribute(Input().Controls, 15f, 0f, lumps, fines, .1f); b.AddStoked(lumps, fines, .1f);
        Assert.True(b.FreshMass > a.FreshMass + .09f);
        Assert.True(b.FreshWater > 0f);
        Assert.InRange(Math.Abs(a.CoalMass + dryLost - b.CoalMass - wetLost), 0f, .0001f);
    }

    [Fact]
    public void StandaloneEngineDoesNotStallAgainstTableFuelFromTheZonedMode()
    {
        var s = New(); var held = s.Spec.MaxFeedRate * s.Spec.TableCapacitySeconds;
        s.RestoreState(new StokerState { TableCoal = held, TableFines = held * .25f, Packing = .8f });
        float rate = 0f;
        for (int i = 0; i < 1000; i++) rate = s.RunEngine(.02f, 1f, 15f);
        Assert.InRange(rate, 2.49f, 2.501f);
        Assert.Equal(held, s.TableCoal);
    }

    [Theory]
    [InlineData(-1f)]
    [InlineData(2f)]
    public void BindingCannotCreateNegativeFineFractions(float boundShare)
    {
        var s = New(); s.Spec.DampFinesHold = boundShare;
        var coarse = new float[9]; var fine = new float[9];
        var lost = s.Distribute(Input().Controls, 15, 0, coarse, fine, .1f);
        Assert.All(fine, v => Assert.InRange(v, 0f, 1f));
        Assert.InRange(coarse.Sum() + fine.Sum() + lost, .99999f, 1.00001f);
    }
}
