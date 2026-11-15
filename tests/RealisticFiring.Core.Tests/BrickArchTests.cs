using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class BrickArchTests
{
    private readonly ITestOutputHelper output;
    public BrickArchTests(ITestOutputHelper output) => this.output = output;

    private static readonly GrateCharacter S282 = new GrateCharacter { OverFireMixing = 0.95f };
    private static readonly FireboxParameters Stock = new FireboxParameters();

    private static (float Coal, float Steam, float Low) Haul(GrateCharacter character, float steam)
    {
        var engine = new Footplate(engine: FootplateEngine.S282Like with { Character = character });
        engine.Standing = true;
        engine.Run(90f);
        engine.StartComing = true;
        engine.Run(30f);
        engine.Standing = engine.StartComing = false;
        engine.EngineSteam = steam;
        engine.Run(300f);
        var coal = engine.CoalFired;
        var made = engine.SteamMade;
        var low = float.MaxValue;
        engine.Run(1200f, e => low = Math.Min(low, e.Pressure));
        return (engine.CoalFired - coal, engine.SteamMade - made, low);
    }

    [Theory]
    [InlineData(0.25f, 0.06f, 0.09f)]
    [InlineData(0.5f, 0.07f, 0.11f)]
    [InlineData(0.75f, 0.08f, 0.13f)]
    public void WithoutItsArchTheBoxBurnsMoreCoalForTheSameSteamTheHarderItIsFired(float work, float least, float most)
    {
        var steam = work * Footplate.FullWorkSteam;
        var arched = Haul(S282, steam);
        var bare = Haul(S282.WithoutArch(), steam);
        var more = bare.Coal / bare.Steam / (arched.Coal / arched.Steam) - 1f;

        output.WriteLine($"work {work:P0}: {arched.Coal:F0} kg with the arch, {bare.Coal:F0} kg without, {more:P1} more a kg of steam; pressure low {arched.Low:F2} and {bare.Low:F2} bar");
        Assert.InRange(more, least, most);
        Assert.InRange(bare.Steam, 0.98f * arched.Steam, 1.02f * arched.Steam);
        Assert.True(bare.Low > arched.Low - 0.5f, $"pressure fell to {bare.Low:F2} bar without the arch, {arched.Low:F2} with");
    }

    [Fact]
    public void WithoutItsArchTheBoilerSteamsLessAtItsMost()
    {
        var arched = new FireModel(Stock) { Character = S282 };
        var bare = new FireModel(Stock) { Character = S282.WithoutArch() };

        Assert.Equal(1.155, AutoFireman.MostSteam(arched.Efficiency) / AutoFireman.MostSteam(bare.Efficiency), 3);
        Assert.Equal(AutoFireman.MostSteam(Stock), AutoFireman.MostSteam(arched.Efficiency), 3);
    }

    [Fact]
    public void WithoutItsArchTheFrontDrawsHardest()
    {
        var bare = new FireModel(Stock) { Character = S282.WithoutArch() };
        var arched = new FireModel(Stock) { Character = S282 };

        Assert.True(bare.DraftWeight(GrateZone.Front) > bare.DraftWeight(GrateZone.UnderDoor));
        Assert.True(arched.DraftWeight(GrateZone.Front) < arched.DraftWeight(GrateZone.UnderDoor));
    }

    [Theory]
    [InlineData(0.5f, 0.1f)]
    [InlineData(0.5f, 0.25f)]
    [InlineData(0.8f, 0.25f)]
    public void WithoutItsArchARoundSmokesMore(float load, float round)
    {
        float Soot(GrateCharacter character)
        {
            var tended = new TendedFire(Stock, load, tuning: FireTestKit.CleanCoal);
            tended.Fire.Character = character;
            tended.Run(60f);
            tended.Fire.AddCoalEvenly(round * Stock.MaxCoalCapacity);
            var soot = 0f;
            for (var i = 0; i < 60; i++)
            {
                tended.Run(1f);
                soot += tended.Fire.SootDensity;
            }
            return soot;
        }

        var arched = Soot(S282);
        var bare = Soot(S282.WithoutArch());
        output.WriteLine($"load {load:P0}, a round of {round:P0} of the box: soot-seconds {arched:F1} with the arch, {bare:F1} without");
        // A third to a half less smoke with the arch: half as much again or more without.
        Assert.True(bare >= 1.45f * arched && bare > 1f, $"soot-seconds {arched:F1} with the arch, {bare:F1} without");
    }

    [Fact]
    public void AShortArchSavesLess()
    {
        var full = S282.WithoutArch();
        var half = S282.WithoutArch(0.5f);

        Assert.True(half.EfficiencyKeptForced > full.EfficiencyKeptForced && half.EfficiencyKeptForced < 1f);
        Assert.True(half.OverFireMixing > full.OverFireMixing && half.OverFireMixing < S282.OverFireMixing);
        Assert.Equal(1f, half.BackRowDraft + half.FrontRowDraft - 1f, 3);
    }
}
