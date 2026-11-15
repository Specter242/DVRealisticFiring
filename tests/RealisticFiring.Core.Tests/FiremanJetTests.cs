using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class FiremanJetTests
{
    private readonly ITestOutputHelper output;
    public FiremanJetTests(ITestOutputHelper output) => this.output = output;

    private sealed class Run
    {
        public float Black, Coal, Made, Low = float.MaxValue, JetSeconds, Blowback, DoorWithJets;
        public readonly List<float> Runs = new List<float>();
    }

    private Run Session(bool jets)
    {
        var engine = new Footplate();
        if (jets)
            engine.Fire.Jets = new OverFireJets(6, JetTube.Large);
        var run = new Run();
        var open = 0f;
        void Watch(Footplate e)
        {
            run.Black += e.Fire.SootDensity > AutoFireman.BlackSmoke ? Footplate.Dt : 0f;
            run.Low = Math.Min(run.Low, e.Pressure);
            if (e.JetValve > 0f)
            {
                run.JetSeconds += Footplate.Dt;
                open += Footplate.Dt;
                if (e.Door > 0.5f * AutoFireman.DoorCrack)
                    run.DoorWithJets += Footplate.Dt;
            }
            else if (open > 0f)
            {
                run.Runs.Add(open);
                open = 0f;
            }
            run.Blowback += e.Fire.Blowback > 0f ? Footplate.Dt : 0f;
        }

        engine.Standing = true;
        engine.Run(300f, Watch);
        engine.StartComing = true;
        engine.Run(30f, Watch);
        engine.Standing = engine.StartComing = false;
        foreach (var (steam, seconds) in new[] { (2.5f, 400f), (4f, 300f), (0.2f, 200f), (2.5f, 300f) })
        {
            engine.EngineSteam = steam;
            engine.Run(seconds, Watch);
        }
        engine.Standing = true;
        engine.EngineSteam = 0f;
        engine.Run(300f, Watch);
        run.Coal = engine.CoalFired;
        run.Made = engine.SteamMade;
        return run;
    }

    [Fact]
    public void HeWorksTheJetsWithTheDoorShutAndTheyCutTheSmoke()
    {
        var without = Session(false);
        var with = Session(true);
        output.WriteLine($"black smoke {without.Black:F0} s without the jets, {with.Black:F0} s with; coal {without.Coal:F0} and {with.Coal:F0} kg; " +
                         $"pressure low {without.Low:F2} and {with.Low:F2} bar; jets open {with.JetSeconds:F0} s in {with.Runs.Count} runs " +
                         $"({(with.Runs.Count > 0 ? with.Runs.Min() : 0f):F0} to {(with.Runs.Count > 0 ? with.Runs.Max() : 0f):F0} s); blowback {with.Blowback:F0} s");

        Assert.Equal(0f, with.DoorWithJets);
        Assert.True(with.JetSeconds > 0f, "he never opened the jets");
        Assert.True(with.Black <= 0.6f * without.Black + 1f, $"black smoke {without.Black:F0} s without, {with.Black:F0} s with");
        Assert.True(with.Coal <= 1.05f * without.Coal, $"coal {without.Coal:F0} kg without, {with.Coal:F0} with");
        Assert.True(with.Low > without.Low - 0.5f, $"pressure fell to {with.Low:F2} bar with the jets, {without.Low:F2} without");
        Assert.True(with.Blowback < 5f, $"the box blew back for {with.Blowback:F0} s");
    }
}
