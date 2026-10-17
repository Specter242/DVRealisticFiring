using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class BailOffTests
{
    [Fact]
    public void UntouchedItPassesTheAutomaticThrough()
    {
        var bail = new BailOff();
        Assert.Equal(3.2f, bail.Apply(3.2f), 4);
        Assert.Equal(BailOff.Released, bail.Apply(BailOff.Released), 4);
    }

    [Fact]
    public void BailedOffTheEngineStaysReleasedWhileTheApplicationHolds()
    {
        var bail = new BailOff();
        bail.Apply(3f);
        bail.Request();
        Assert.Equal(BailOff.Released, bail.Apply(3f), 4);
        for (var i = 0; i < 100; i++)
            Assert.Equal(BailOff.Released, bail.Apply(3f), 4);
    }

    [Fact]
    public void AFurtherReductionAppliesTheEngineByThatMuch()
    {
        var bail = new BailOff();
        bail.Request();
        bail.Apply(2.5f);
        Assert.Equal(BailOff.Released + 0.7f, bail.Apply(3.2f), 4);
    }

    [Fact]
    public void ReleasingTheTrainBrakeClearsTheBail()
    {
        var bail = new BailOff();
        bail.Request();
        bail.Apply(3f);
        bail.Apply(BailOff.Released);
        Assert.Equal(0f, bail.Bailed, 4);
        // The next application applies the engine in full.
        Assert.Equal(2.4f, bail.Apply(2.4f), 4);
    }

    [Fact]
    public void BailingWithNothingAppliedDoesNothing()
    {
        var bail = new BailOff();
        bail.Request();
        Assert.Equal(BailOff.Released, bail.Apply(BailOff.Released), 4);
        Assert.Equal(2f, bail.Apply(2f), 4);
    }
}
