using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

public class GrateLayoutTests
{
    [Fact]
    public void NeighborsAreOrthogonalWithinTheGrate()
    {
        Assert.Equal(new[] { GrateZone.UnderDoor, GrateZone.MidLeft }, GrateLayout.Neighbors(GrateZone.BackLeft).OrderBy(z => z));
        Assert.Equal(4, GrateLayout.Neighbors(GrateZone.Center).Count);
    }

    [Theory]
    [InlineData(0.5f, 0.1f, GrateZone.UnderDoor)]
    [InlineData(0.1f, 0.1f, GrateZone.BackLeft)]
    [InlineData(0.9f, 0.5f, GrateZone.MidRight)]
    [InlineData(0.5f, 0.5f, GrateZone.Center)]
    [InlineData(0.5f, 0.9f, GrateZone.Front)]
    [InlineData(0.2f, 0.8f, GrateZone.FrontLeft)]
    public void DoorAimMapsThePointInTheDoorToZones(float across, float height, GrateZone expected)
    {
        Assert.Equal(expected, DoorAim.ZoneFor(across, height));
    }

    [Theory]
    [InlineData(-0.5f, -2f, GrateZone.BackLeft)]
    [InlineData(1.5f, 3f, GrateZone.FrontRight)]
    [InlineData(float.NaN, float.NaN, GrateZone.Center)]
    public void DoorAimClampsPointsOutsideTheDoor(float across, float height, GrateZone expected)
    {
        Assert.Equal(expected, DoorAim.ZoneFor(across, height));
    }

    [Theory]
    [InlineData(GrateZone.FrontLeft, "front left")]
    [InlineData(GrateZone.UnderDoor, "under the door")]
    [InlineData(GrateZone.Center, "center")]
    [InlineData(GrateZone.MidRight, "right side")]
    public void ZonesHavePlainNames(GrateZone zone, string expected)
    {
        Assert.Equal(expected, GrateLayout.Describe(zone));
    }
}
