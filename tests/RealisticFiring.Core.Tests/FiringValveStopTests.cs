using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class FiringValveStopTests
{
    [Fact]
    public void WithTheStopOutTheValveIsTheLever()
    {
        var stop = new FiringValveStop();
        Assert.False(stop.In);
        Assert.Equal(0f, stop.Limit(0f), 4);
        Assert.Equal(0.4f, stop.Limit(0.4f), 4);
        Assert.Equal(1f, stop.Limit(1.3f), 4);
    }

    [Fact]
    public void AClickSetsTheStopWhereTheValveStands()
    {
        var stop = new FiringValveStop();
        Assert.True(stop.Toggle(0.15f));
        Assert.Equal(0.15f, stop.At, 4);
        Assert.Equal(0.15f, stop.Limit(0f), 4);
        Assert.Equal(0.15f, stop.Limit(0.1f), 4);
        Assert.Equal(0.5f, stop.Limit(0.5f), 4);
    }

    [Fact]
    public void AClickWithTheStopInTakesItOut()
    {
        var stop = new FiringValveStop();
        stop.Toggle(0.15f);
        Assert.False(stop.Toggle(0.6f));
        Assert.False(stop.In);
        Assert.Equal(0f, stop.Limit(0f), 4);
    }

    [Fact]
    public void AShutValveSetsNoStop()
    {
        var stop = new FiringValveStop();
        Assert.False(stop.Toggle(0f));
        Assert.False(stop.Toggle(0.005f));
        Assert.False(stop.In);
    }

    [Fact]
    public void TheFiremanWorksAboveTheStopAndTakesItOutToShut()
    {
        var stop = new FiringValveStop();
        stop.Toggle(0.2f);
        Assert.Equal(0.2f, stop.ForFireman(0.1f), 4);
        Assert.Equal(0.5f, stop.ForFireman(0.5f), 4);
        Assert.True(stop.In);
        Assert.Equal(0f, stop.ForFireman(0f), 4);
        Assert.False(stop.In);
    }

    [Fact]
    public void ASavedStopComesBackAndADamagedOneStaysOut()
    {
        var stop = new FiringValveStop();
        stop.Restore(0.2f);
        Assert.Equal(0.2f, stop.At, 4);
        stop.Restore(float.NaN);
        Assert.False(stop.In);
        stop.Restore(1.5f);
        Assert.Equal(1f, stop.At, 4);
        stop.Restore(-0.3f);
        Assert.False(stop.In);
    }
}
