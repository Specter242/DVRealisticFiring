using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

 public class TendedEngineTests
{
    private sealed class Car
    {
        public Car(string name) => Name = name;
        public string Name { get; }
        public override string ToString() => Name;
    }

    private static readonly Car First = new("first engine");
    private static readonly Car Second = new("second engine");
    private static readonly Car? None = null;

    [Fact]
    public void InAnEngineItIsThatEngineWhicheverTheyWereLastIn()
    {
        Assert.Same(Second, TendedEngine.Pick(Second, First, None, First));
        Assert.Same(Second, TendedEngine.Pick(Second, First, First, First));
    }

    [Fact]
    public void OnATenderItIsTheEngineCoupledToIt()
    {
        Assert.Same(First, TendedEngine.Pick(None, First, None, None));
        Assert.Same(First, TendedEngine.Pick(None, None, First, Second));
    }

    [Fact]
    public void BetweenTwoEnginesItIsTheOneTheyWereLastIn()
    {
        // The first engine's tender, the second engine coupled behind it.
        Assert.Same(Second, TendedEngine.Pick(None, First, Second, Second));
        Assert.Same(First, TendedEngine.Pick(None, First, Second, First));
    }

    [Fact]
    public void BetweenTwoEnginesTheyWereInNeitherOfItIsTheFrontOne()
    {
        var third = new Car("third engine");
        Assert.Same(First, TendedEngine.Pick(None, First, Second, None));
        Assert.Same(First, TendedEngine.Pick(None, First, Second, third));
    }

    [Fact]
    public void WithNoEngineAboutItIsNone()
    {
        Assert.Null(TendedEngine.Pick(None, None, None, First));
    }
}
