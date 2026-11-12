using System.Text;
using Xunit;
using Xunit.Abstractions;
namespace RealisticFiring.Core.Tests;

/// <summary>Prints the wood fire's milestones for the calibration notes; asserts nothing.</summary>
public class StarterTimingReport
{
    private readonly ITestOutputHelper output;
    public StarterTimingReport(ITestOutputHelper output) => this.output = output;
    internal static readonly FireboxParameters S282 = new FireboxParameters(150f, 120f, 0.5f);

    internal sealed class Trace
    {
        public float Ignited = -1, Involved90 = -1, RagOut = -1, FlamesOut = -1, Support30 = -1, Support60 = -1, CoalHalf = -1, PeakKw;
        public float CoalBurningEnd, WoodLeft, CharLeft, HeatMJ;
    }

    internal static Trace Run(int pieces, bool rag, float seconds, float air = 0.6f, float boiler = 20f, float coalAt = -1, float coal = 3f,
        float dt = 0.05f, float speedup = 1f, int neighborPieces = 0)
    {
        var fire = new FireModel(S282);
        fire.AddWood(pieces * StarterFuel.PieceMass, GrateZone.UnderDoor);
        if (neighborPieces > 0) fire.AddWood(neighborPieces * StarterFuel.PieceMass, GrateZone.Center);
        if (rag) fire.AddRag(RagFuel.OilCapacity, true, GrateZone.UnderDoor);
        var inputs = FireTestKit.Inputs(air); inputs.BoilerTemperature = boiler; inputs.StartupMultiplier = speedup;
        var tr = new Trace();
        var s = fire.Starters[(int)GrateZone.UnderDoor];
        var target = neighborPieces > 0 ? fire.Starters[(int)GrateZone.Center] : s;
        var wasFlaming = false;
        for (var t = 0f; t < seconds; t += dt)
        {
            if (coalAt >= 0 && t >= coalAt && t < coalAt + dt) fire.AddCoal(coal, GrateZone.UnderDoor);
            fire.Step(dt, inputs);
            tr.HeatMJ += fire.Heat * dt / 1e6f;
            if (tr.Ignited < 0 && target.Involved > 0) tr.Ignited = t;
            if (tr.Involved90 < 0 && target.Flaming > 0.9f) tr.Involved90 = t;
            if (tr.RagOut < 0 && rag && s.RagOil <= 0) tr.RagOut = t;
            if (wasFlaming && target.Involved <= 0 && tr.FlamesOut < 0) tr.FlamesOut = t;
            wasFlaming |= target.Involved > 0;
            var sup = fire.WoodIgnitionSupport(GrateZone.UnderDoor);
            if (tr.Support30 < 0 && sup >= 0.3f) tr.Support30 = t;
            if (tr.Support60 < 0 && sup >= 0.6f) tr.Support60 = t;
            if (coalAt >= 0 && tr.CoalHalf < 0 && fire.Zones[(int)GrateZone.UnderDoor].Burning > coal * 0.5f * 0.7f) tr.CoalHalf = t;
            tr.PeakKw = System.Math.Max(tr.PeakKw, s.Heat / 1000f);
        }
        tr.CoalBurningEnd = fire.BurningMass; tr.WoodLeft = s.Wood; tr.CharLeft = s.Char;
        return tr;
    }

    private void Print(string name, Trace t) => output.WriteLine(
        $"{name,-44} ign {t.Ignited,6:F0} inv90 {t.Involved90,6:F0} ragOut {t.RagOut,5:F0} flamesOut {t.FlamesOut,6:F0} sup30 {t.Support30,6:F0} sup60 {t.Support60,6:F0} coal50% {t.CoalHalf,6:F0} peak {t.PeakKw,5:F1}kW coalBurning {t.CoalBurningEnd,5:F2} wood {t.WoodLeft:F2} char {t.CharLeft:F2} heat {t.HeatMJ:F1}MJ");

    [Fact]
    public void Report()
    {
        Print("1 piece + rag, cold", Run(1, true, 1800));
        Print("2 pieces + rag, cold", Run(2, true, 1800));
        Print("4 pieces + rag, cold", Run(4, true, 1800));
        Print("8 pieces + rag, cold", Run(8, true, 2400));
        Print("8 pieces + rag, boiler 180 C", Run(8, true, 2400, boiler: 180));
        Print("1 piece + rag, boiler 180 C", Run(1, true, 1800, boiler: 180));
        Print("8 pieces no rag", Run(8, false, 600));
        Print("8 pieces + rag, damper nearly shut", Run(8, true, 1800, air: 0.05f));
        Print("8 + rag, 3 kg coal at 20 s", Run(8, true, 1800, coalAt: 20));
        foreach (var at in new[] { 45f, 60f, 75f, 90f, 120f, 180f })
        {
            Print($"8 + rag, 3 kg coal at {at} s", Run(8, true, 1800, coalAt: at));
            Print($"8 + rag, 8 kg coal at {at} s", Run(8, true, 1800, coalAt: at, coal: 8));
            Print($"4 + rag, 3 kg coal at {at} s", Run(4, true, 1800, coalAt: at));
        }
        Print("8 + rag, 3 kg coal at 300 s", Run(8, true, 1800, coalAt: 300));
        Print("8 + rag, 3 kg coal at 300 s, x25", Run(8, true, 1800, coalAt: 300, speedup: 25));
        Print("8 + rag, 10 kg coal at 300 s", Run(8, true, 1800, coalAt: 300, coal: 10));
        Print("8 + rag, neighbor 4 pieces (neighbor)", Run(8, true, 1800, neighborPieces: 4));
        Print("8 + rag dt 0.02", Run(8, true, 900, dt: 0.02f));
        Print("8 + rag dt 0.2", Run(8, true, 900, dt: 0.2f));
        Print("8 + rag dt 1.0", Run(8, true, 900, dt: 1f));
    }
}
