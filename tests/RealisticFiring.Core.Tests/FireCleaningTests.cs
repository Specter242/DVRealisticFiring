using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FireCleaningTests
{
    private static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);

    private static FireModel Clinkered()
    {
        var fire = FireTestKit.BurningFire(S282, 45f);
        foreach (var zone in new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight, GrateZone.MidLeft, GrateZone.Center })
            fire.Zones[(int)zone].Clinker = 3f;
        return fire;
    }

    [Fact]
    public void SlicingBreaksClinkerLooseSoAShakeTakesIt()
    {
        var shaken = Clinkered();
        var sliced = Clinkered();

        shaken.ShakeGrate();
        sliced.Slice(GrateZone.Front);
        sliced.Slice(GrateZone.Front);
        sliced.ShakeGrate();

        Assert.True(shaken.Zones[(int)GrateZone.Front].Clinker > 2.8f, "clinker barely shakes through");
        Assert.True(sliced.Zones[(int)GrateZone.Front].Clinker < 0.8f, $"{sliced.Zones[(int)GrateZone.Front].Clinker} kg left");
        Assert.True(sliced.Zones[(int)GrateZone.Front].Ash < 1.5f, "the broken clinker shakes through as loose ash");
    }

    [Fact]
    public void SlicingStirsAshUpIntoTheFire()
    {
        var fire = Clinkered();

        fire.Slice(GrateZone.Center);

        Assert.True(fire.Zones[(int)GrateZone.Center].Stirred > 0.2f);
        Assert.Equal(0f, fire.Zones[(int)GrateZone.Front].Stirred);
    }

    [Fact]
    public void TheHoeMovesFireAndLooseAshButNotClinker()
    {
        var fire = Clinkered();
        var front = fire.Zones[(int)GrateZone.Front];
        var center = fire.Zones[(int)GrateZone.Center];
        front.Ash = 1f;
        var coal = fire.CoalMass;
        var (frontCoal, centerCoal) = (front.Coal, center.Coal);

        fire.Push(GrateZone.Front, GrateDirection.Back);

        Assert.True(front.Coal < 0.5f * frontCoal);
        Assert.True(center.Coal > centerCoal + 0.5f * frontCoal);
        Assert.True(center.Ash > 0.5f);
        Assert.Equal(3f, front.Clinker, 3);
        Assert.Equal(coal, fire.CoalMass, 3);
    }

    [Fact]
    public void PulledAtTheDoorLooseClinkerComesOutAndTheFireStays()
    {
        var fire = Clinkered();
        var underDoor = fire.Zones[(int)GrateZone.UnderDoor];
        underDoor.Ash = 2f;
        var coal = underDoor.Coal;

        fire.Push(GrateZone.UnderDoor, GrateDirection.Back);

        Assert.Equal(coal, underDoor.Coal, 3);
        Assert.True(underDoor.Ash < 1f);
        Assert.True(fire.HookedOut > 1f);
    }

    [Fact]
    public void AgainstASideWallNothingMoves()
    {
        var fire = Clinkered();
        var before = fire.Zones.Select(z => z.Coal).ToArray();

        fire.Push(GrateZone.MidLeft, GrateDirection.Left);

        Assert.Equal(before, fire.Zones.Select(z => z.Coal).ToArray());
    }

    [Fact]
    public void TheDropGrateDropsWhatLiesOnTheFrontRowOnly()
    {
        var fire = Clinkered();
        var middle = fire.Zones[(int)GrateZone.Center].Coal;

        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            fire.OpenDropGrate(1f, FireTestKit.Dt);

        foreach (var zone in new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight })
        {
            Assert.True(fire.Zones[(int)zone].Coal < 0.05f);
            Assert.True(fire.Zones[(int)zone].Clinker < 0.05f);
        }
        Assert.Equal(middle, fire.Zones[(int)GrateZone.Center].Coal, 3);
        Assert.Equal(3f, fire.Zones[(int)GrateZone.Center].Clinker, 3);
        Assert.True(fire.DroppedAshAndClinker > 8.9f);
        Assert.True(fire.CoalDumped > 0f);
    }

    [Fact]
    public void AnOpenDropGrateLetsColdAirStraightIn()
    {
        var air = 0.3f * S282.MaxCombustionRate * FireModel.AirPerCoal;
        var shut = FireTestKit.BurningFire(S282, 45f);
        var open = FireTestKit.BurningFire(S282, 45f);
        var inputs = FireTestKit.Inputs(air);
        for (var t = 0f; t < 20f; t += FireTestKit.Dt)
        {
            shut.Step(FireTestKit.Dt, inputs);
            open.OpenDropGrate(1f, FireTestKit.Dt);
            open.Step(FireTestKit.Dt, inputs);
        }

        float FrontShare(FireModel fire) =>
            new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight }.Sum(z => fire.Zones[(int)z].AirFlow) / fire.PrimaryAirFlow;
        Assert.True(FrontShare(open) > 1.5f * FrontShare(shut), $"front takes {FrontShare(open):P0} open, {FrontShare(shut):P0} shut");
        Assert.True(open.Heat < shut.Heat, "the cold air and the lost fire cost heat");
    }

    [Fact]
    public void CleaningAClinkeredFireOnTheRoadKeepsMostOfTheFire()
    {
        // Baker pp.70-71: pull the live fire back under the door, drop the
        // front grate and punch the clinkers through, push the clinkers from
        // the middle forward to the opening, close it, spread the fire.
        var fire = Clinkered();
        var coal = fire.CoalMass;
        var clinker = fire.ClinkerMass;
        var front = new[] { GrateZone.FrontLeft, GrateZone.Front, GrateZone.FrontRight };
        var middle = new[] { GrateZone.MidLeft, GrateZone.Center, GrateZone.MidRight };

        for (var pass = 0; pass < 3; pass++)
        {
            foreach (var zone in front)
                fire.Push(zone, GrateDirection.Back);
            foreach (var zone in middle)
                fire.Push(zone, GrateDirection.Back);
        }
        foreach (var zone in front.Concat(middle))
        {
            fire.Slice(zone);
            fire.Slice(zone);
        }
        for (var pass = 0; pass < 3; pass++)
            foreach (var zone in middle)
                fire.Push(zone, GrateDirection.Front);
        for (var t = 0f; t < 10f; t += FireTestKit.Dt)
            fire.OpenDropGrate(1f, FireTestKit.Dt);
        fire.OpenDropGrate(0f, FireTestKit.Dt);
        foreach (var zone in new[] { GrateZone.BackLeft, GrateZone.UnderDoor, GrateZone.BackRight })
            fire.Push(zone, GrateDirection.Front);

        Assert.True(fire.ClinkerMass < 0.3f * clinker, $"clinker {fire.ClinkerMass:F1} of {clinker:F1} kg left");
        Assert.True(fire.CoalMass > 0.6f * coal, $"coal {fire.CoalMass:F1} of {coal:F1} kg kept");
    }
}
