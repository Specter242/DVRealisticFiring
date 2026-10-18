using RealisticFiring.Core;
using Xunit.Abstractions;

namespace RealisticFiring.Core.Tests;

 public class AutoFiremanTests
{
    private readonly ITestOutputHelper output;

    public AutoFiremanTests(ITestOutputHelper output) => this.output = output;

    // Steam the engine draws cruising and on a climb, in kg/s: the climb
    // takes all its injector (4 kg/s) can feed.
    private const float Cruise = 2.5f;
    private const float Climb = 4f;

    [Fact]
    public void OnTheRoadHeHoldsThePressureAndTheWater()
    {
        var engine = new Footplate();
        engine.Standing = true;
        engine.Run(90f);
        engine.StartComing = true;
        engine.Run(30f);

        float low = float.MaxValue, lowGlass = float.MaxValue, highGlass = 0f;
        void Watch(Footplate e)
        {
            low = Math.Min(low, e.Pressure);
            lowGlass = Math.Min(lowGlass, e.Glass);
            highGlass = Math.Max(highGlass, e.Glass);
        }

        engine.Standing = engine.StartComing = false;
        engine.EngineSteam = Cruise;
        engine.Run(300f, Watch);
        engine.EngineSteam = Climb;
        engine.Run(400f, Watch);
        engine.EngineSteam = 0.2f;
        engine.Run(200f, Watch);
        engine.Standing = true;
        engine.EngineSteam = 0f;
        engine.Run(120f, Watch);

        output.WriteLine($"pressure low {low:F2}, glass {lowGlass:F2}-{highGlass:F2}, blown off {engine.BlownOff:F0} of {engine.SteamMade:F0} kg, " +
                         $"coal {engine.CoalFired:F0} kg, trim {engine.Fireman.Trim:F2}, rounds {string.Join(",", engine.Rounds())}");
        var target = AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve);
        Assert.True(low >= target - 1.5f, $"pressure fell to {low:F2}");
        Assert.True(engine.BlownOff <= 0.03f * engine.SteamMade, $"blew off {engine.BlownOff:F0} kg");
        Assert.InRange(lowGlass, 0.3f, 1f);
        Assert.InRange(highGlass, 0f, 0.9f);
        Assert.False(engine.InjectorBroke);
        Assert.True(engine.Fire.FireOn);
    }

    [Theory]
    [InlineData(1.2f, 3.7f, 120f)]
    [InlineData(1.2f, 4.5f, 90f)]
    [InlineData(1.8f, 4.5f, 120f)]
    public void BuildingForAClimbWhileWorkingLightlyHeDoesNotBlowOff(float crawl, float climb, float approach)
    {
        var engine = new Footplate(coal: 20f, pressure: 14.2f);
        engine.Standing = true;
        engine.Run(60f);
        engine.Standing = false;
        engine.EngineSteam = crawl;
        engine.SteamAhead = climb;
        engine.Run(approach);
        var blownApproach = engine.BlownOff;
        engine.EngineSteam = climb;
        var low = float.MaxValue;
        engine.Run(300f, e => low = Math.Min(low, e.Pressure));
        var target = AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve);
        output.WriteLine($"blown off {blownApproach:F0} kg approaching, {engine.BlownOff - blownApproach:F0} on the climb; climb low {low:F2} (target {target:F2})");
        // It blew off 86 to 229 kg; the climb's low was 13.3 to 13.8 bar.
        Assert.True(engine.BlownOff < 60f, $"blew off {engine.BlownOff:F0} kg");
        Assert.True(low >= target - 1.2f, $"pressure fell to {low:F2}");
    }

    [Fact]
    public void FiringAheadOfAClimbHoldsThePressureBetterThanFollowingIt()
    {
        float LowestOnTheClimb(bool ahead)
        {
            var engine = new Footplate();
            engine.EngineSteam = Cruise;
            engine.Run(300f);
            engine.SteamAhead = ahead ? Climb : null;
            engine.Run(90f);
            engine.EngineSteam = Climb;
            var low = float.MaxValue;
            engine.Run(300f, e => low = Math.Min(low, e.Pressure));
            return low;
        }

        var following = LowestOnTheClimb(ahead: false);
        var firingAhead = LowestOnTheClimb(ahead: true);
        output.WriteLine($"lowest on the climb: following {following:F2} bar, firing ahead {firingAhead:F2} bar");
        Assert.True(firingAhead > following + 0.3f);
    }

    [Fact]
    public void HeFiresLightAndOftenWhereTheFireNeedsIt()
    {
        var engine = new Footplate();
        engine.EngineSteam = Cruise;
        var aimedRight = true;
        engine.Run(900f, e =>
        {
            if (e.Last.Shovelful > 0f)
                aimedRight &= e.Last.Door >= 1f;
        });

        var rounds = engine.Rounds().Skip(1).ToList();
        output.WriteLine($"rounds {string.Join(",", rounds)}");
        Assert.True(rounds.Count >= 10);
        Assert.True(rounds.Max() <= AutoFireman.HeavyRound);
        Assert.True(rounds.Average() <= AutoFireman.LightRound);
        Assert.True(aimedRight, "a shovelful went in with the door shut");
        var open = engine.Log.Count(a => a.Door >= 1f) / (float)engine.Log.Count;
        Assert.True(open < 0.25f, $"door open {open:P0} of the time");
    }

    [Fact]
    public void HeCracksTheInjectorUntilItSingsThenOpensIt()
    {
        var engine = new Footplate(glass: 0.4f);
        engine.EngineSteam = Cruise;
        var crackedBeforeSinging = true;
        engine.Run(120f, e =>
        {
            if (e.Injector.State != InjectorState.Working && e.InjectorLever > e.Injector.Spec.CrackOpening)
                crackedBeforeSinging = false;
        });

        Assert.True(crackedBeforeSinging);
        Assert.False(engine.InjectorBroke);
        Assert.Equal(InjectorState.Working, engine.Injector.State);
        Assert.True(engine.InjectorLever > engine.Injector.Spec.CrackOpening);
    }

    [Fact]
    public void OnADamagedBoilerHeHoldsUnderWhereItWouldBurst()
    {
        // The body so damaged the boiler would burst at 12 bar, under its
        // 15-bar safety valve: he fires for 10.5, working and standing.
        var engine = new Footplate(pressure: 9f) { BurstPressure = 12f };
        var high = 0f;
        void Watch(Footplate e) => high = Math.Max(high, e.Pressure);
        engine.EngineSteam = Cruise;
        engine.Run(400f, Watch);
        engine.EngineSteam = 0f;
        engine.Standing = true;
        engine.Run(300f, Watch);

        output.WriteLine($"highest {high:F2} bar, holding {engine.Fireman.PressureTarget:F2}");
        Assert.Equal(12f - AutoFireman.BurstMargin, engine.Fireman.PressureTarget, 3);
        Assert.True(high < 12f - 0.5f, $"pressure reached {high:F2}");
        Assert.True(engine.Fire.FireOn);
    }

    [Fact]
    public void NearTheSafetyValveHeRunsTheInjectorRatherThanFlickingIt()
    {
        // The S282 standing just under its safety valve, its injector good
        // for 20 kg/s: the cold feed pulls the pressure down at once (seen
        // in game, 2026-10-10, started 33 times over a few minutes).
        var engine = new Footplate(glass: 0.66f, pressure: 14.75f, injectorDelivery: 20f);
        engine.Standing = true;
        var runs = new List<float>();
        var run = -1f;
        engine.Run(600f, e =>
        {
            var working = e.Injector.State == InjectorState.Working;
            if (working)
                run = run < 0f ? Footplate.Dt : run + Footplate.Dt;
            else if (run >= 0f)
            {
                runs.Add(run);
                run = -1f;
            }
        });

        output.WriteLine($"runs {string.Join(", ", runs.Select(r => r.ToString("F0")))} s; glass {engine.Glass:F2}, pressure {engine.Pressure:F2}");
        // Its least, 9 kg/s of cold feed, takes the pressure off the valve
        // within half a minute; he stops then, but never flicks it.
        Assert.InRange(runs.Count, 1, 6);
        Assert.All(runs, r => Assert.True(r >= 15f, $"a run of {r:F1} s"));
        Assert.False(engine.InjectorBroke);
    }

    // Cruising after the first five minutes: the share of the time the
    // injector sang, how often it started, and the glass's range.
    private static (float Singing, int Starts, float Low, float High) Cruising(Footplate engine, float minutes)
    {
        engine.EngineSteam = Cruise;
        engine.Run(300f);
        float singing = 0f, low = 1f, high = 0f;
        var starts = 0;
        var was = engine.Injector.State == InjectorState.Working;
        engine.Run(minutes * 60f, e =>
        {
            var working = e.Injector.State == InjectorState.Working;
            singing += working ? Footplate.Dt : 0f;
            starts += working && !was ? 1 : 0;
            was = working;
            low = Math.Min(low, e.Glass);
            high = Math.Max(high, e.Glass);
        });
        return (singing / (minutes * 60f), starts, low, high);
    }

    // A No. 11 injector, the USRA 0-6-0's and 0-8-0's size, about 4,400
    // gallons an hour at 200 psi (Hancock's manual, 1921): 4.6 kg/s.
    private const float No11 = 4.6f;

    [Fact]
    public void CruisingHeRunsAPeriodInjectorSteadilyAtTheRateTheEngineBoils()
    {
        // Turned down to its least, about 2 kg/s, it can match the boiling,
        // so he runs it continuously, as the manuals advised.
        var engine = new Footplate(injectorDelivery: No11);
        var (singing, starts, low, high) = Cruising(engine, 15f);
        output.WriteLine($"singing {singing:P0}, {starts} starts, glass {low:F2}-{high:F2}, lever {engine.InjectorLever:F2} (least {engine.Injector.Spec.LeastShare(engine.Pressure):F2})");
        Assert.True(singing > 0.9f, $"sang {singing:P0} of the time");
        Assert.InRange(starts, 0, 2);
        Assert.InRange(low, AutoFireman.GlassGoal - 0.08f, 1f);
        Assert.InRange(high, 0f, AutoFireman.GlassGoal + 0.08f);
        Assert.True(engine.InjectorLever >= engine.Injector.Spec.LeastShare(engine.Pressure));
    }

    [Fact]
    public void CruisingWithAnInjectorTooBigToTurnDownHeRunsItInLongSpells()
    {
        // The game's S282 injector, 20 kg/s, feeds 9 at its least: far more
        // than the engine boils cruising, so he runs it on and off, never
        // wasting at the overflow below its least.
        var engine = new Footplate(injectorDelivery: 20f);
        var wasted = 0f;
        var runs = new List<float>();
        var run = -1f;
        engine.EngineSteam = Cruise;
        engine.Run(300f);
        engine.Run(900f, e =>
        {
            var working = e.Injector.State == InjectorState.Working;
            if (working)
            {
                run = run < 0f ? Footplate.Dt : run + Footplate.Dt;
                wasted += e.InjectorLever < e.Injector.Spec.LeastShare(e.Pressure) ? Footplate.Dt : 0f;
            }
            else if (run >= 0f)
            {
                runs.Add(run);
                run = -1f;
            }
        });
        output.WriteLine($"runs {string.Join(", ", runs.Select(r => r.ToString("F0")))} s; wasting {wasted:F0} s; glass {engine.Glass:F2}");
        Assert.InRange(runs.Count, 2, 12);
        Assert.All(runs, r => Assert.True(r >= AutoFireman.LeastRun - 1f, $"a run of {r:F1} s"));
        Assert.True(wasted < 3f);
        Assert.InRange(engine.Glass, AutoFireman.GlassGoal - 0.12f, AutoFireman.GlassGoal + 0.12f);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(2f)]
    public void HeJudgesTheWaterByTheGlassNotByTheSteam(float earBias)
    {
        // His ear for the work is half or twice the truth; the glass still holds.
        var engine = new Footplate(injectorDelivery: No11) { WorkHeardBias = earBias };
        var (singing, starts, low, high) = Cruising(engine, 15f);
        output.WriteLine($"ear ×{earBias}: singing {singing:P0}, {starts} starts, glass {low:F2}-{high:F2}");
        Assert.InRange(low, AutoFireman.GlassGoal - 0.1f, 1f);
        Assert.InRange(high, 0f, AutoFireman.GlassGoal + 0.1f);
    }

    [Fact]
    public void ABobbingGlassDoesNotSetHimChasingIt()
    {
        int Moves(float bob)
        {
            var engine = new Footplate(injectorDelivery: No11) { GlassBob = bob };
            engine.EngineSteam = Cruise;
            engine.Run(300f);
            var moves = 0;
            var last = engine.InjectorLever;
            engine.Run(600f, e =>
            {
                if (Math.Abs(e.InjectorLever - last) > 0.02f)
                {
                    moves++;
                    last = e.InjectorLever;
                }
            });
            return moves;
        }

        var still = Moves(0f);
        var bobbing = Moves(0.03f);
        output.WriteLine($"lever moves over 0.02 in ten minutes: {still} with a still glass, {bobbing} with it bobbing 0.03 either way");
        Assert.True(bobbing <= still + 3);
    }

    [Fact]
    public void HardWorkAheadHeCarriesTheGlassHighWhileTheEngineHasSteamToSpare()
    {
        var engine = new Footplate(injectorDelivery: 20f);
        engine.EngineSteam = Cruise;
        engine.Run(300f);
        var glass = engine.Glass;
        engine.WorkAhead = 0.8f;
        var low = float.MaxValue;
        engine.Run(300f, e => low = Math.Min(low, e.Pressure));
        output.WriteLine($"glass {glass:F2} -> {engine.Glass:F2} in five minutes, pressure low {low:F2}");
        // The S282's injector will not turn down to what the fire can heat:
        // he pauses the fill a bar short, and the pressure coasts a little past.
        Assert.True(engine.Glass > AutoFireman.GlassBeforeWork - 0.1f);
        Assert.True(low > AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve) - 1.2f);
    }

    [Fact]
    public void HeOpensTheInjectorWhenHeHearsTheEngineWorkHarder()
    {
        var engine = new Footplate(injectorDelivery: No11);
        engine.EngineSteam = Cruise;
        engine.Run(600f);
        var lever = engine.InjectorLever;
        var glass = engine.Glass;
        engine.EngineSteam = Climb;
        engine.Run(6f);
        output.WriteLine($"lever {lever:F2} -> {engine.InjectorLever:F2}, glass {glass:F3} -> {engine.Glass:F3}");
        // Before the glass has told him anything, his ear has.
        Assert.True(engine.InjectorLever > lever + 0.04f);
        Assert.True(Math.Abs(engine.Glass - glass) < 0.01f);
    }

    [Fact]
    public void ShortOfPressureOnAPullHeLetsTheGlassFallThenFillsItAgain()
    {
        var engine = new Footplate(pressure: 12.5f, glass: 0.6f, injectorDelivery: 20f);
        engine.EngineSteam = Climb;
        engine.Run(5f);
        output.WriteLine($"pulling 1.9 bar short: glass target {engine.Fireman.GlassTarget:F2}");
        Assert.True(engine.Fireman.GlassTarget < AutoFireman.GlassLeast + 0.05f);

        var fed = 0f;
        engine.Run(120f, e => fed += e.Injector.State == InjectorState.Working ? Footplate.Dt : 0f);
        output.WriteLine($"fed {fed:F0} of 120 s, glass {engine.Glass:F2}, pressure {engine.Pressure:F2}");
        Assert.True(engine.Glass < 0.5f, "he spent the water");
        Assert.True(engine.Glass > AutoFireman.GlassLeast - GlassFloorSlack, "not below his floor");

        // Over the top: he fills it again.
        engine.EngineSteam = 0.3f;
        engine.Run(600f);
        output.WriteLine($"drifting: glass {engine.Glass:F2}, pressure {engine.Pressure:F2}");
        Assert.True(engine.Glass > AutoFireman.GlassGoal - 0.06f);
    }

    private const float GlassFloorSlack = 0.08f;

    // A run as in OnTheRoad: cruising, then a climb, then drifting. Returns
    // the lowest pressure from the climb on, the lowest over the climb's
    // last 100 s, the steam blown off, and his trim.
    private static (float Low, float LateLow, float BlownOff, float Steam, float Trim) Run(Footplate engine)
    {
        engine.EngineSteam = Cruise;
        engine.Run(300f);
        var low = float.MaxValue;
        engine.EngineSteam = Climb;
        engine.Run(300f, e => low = Math.Min(low, e.Pressure));
        var late = float.MaxValue;
        engine.Run(100f, e => late = Math.Min(late, e.Pressure));
        low = Math.Min(low, late);
        engine.EngineSteam = 0.2f;
        engine.Run(200f, e => low = Math.Min(low, e.Pressure));
        return (low, late, engine.BlownOff, engine.SteamMade, engine.Fireman.Trim);
    }

    [Theory]
    [InlineData(0.7f)]
    [InlineData(1.4f)]
    public void WithHisEarOffHeFiresByTheGauge(float earBias)
    {
        // He hears the engine working 30 % lighter or 40 % harder than it is;
        // the gauge sets him right.
        var engine = new Footplate { WorkHeardBias = earBias };
        var (low, late, blownOff, steam, trim) = Run(engine);
        output.WriteLine($"ear ×{earBias}: pressure low {low:F2}, late in the climb {late:F2}, blown off {blownOff:F0} of {steam:F0} kg, " +
                         $"trim {AutoFireman.StartTrim:F2} -> {trim:F2}");
        var target = AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve);
        Assert.True(low >= target - 3f, $"pressure fell to {low:F2}");
        Assert.True(late >= target - 1f, $"late in the climb the pressure was down to {late:F2}");
        Assert.True(blownOff <= 0.05f * steam, $"blew off {blownOff:F0} kg");
        Assert.True(earBias < 1f ? trim > AutoFireman.StartTrim : trim < AutoFireman.StartTrim);
    }

    [Fact]
    public void KnowingTheTopIsNearHeEasesTheFireAndBlowsOffLessOverIt()
    {
        // Climbing hard, then over the top to drift. Knowing the road, he
        // sees the top coming 90 s out and fires for the drift beyond it.
        (float CoalAtTop, float BlownOff) OverTheTop(bool knowsTheRoad)
        {
            var engine = new Footplate();
            engine.EngineSteam = Climb;
            engine.SteamAhead = knowsTheRoad ? Climb : null;
            engine.Run(400f);
            engine.SteamAhead = knowsTheRoad ? 0.2f : null;
            engine.Run(90f);
            var coal = engine.Fire.CoalMass;
            var before = engine.BlownOff;
            engine.EngineSteam = 0.2f;
            engine.Run(300f);
            return (coal, engine.BlownOff - before);
        }

        var blind = OverTheTop(knowsTheRoad: false);
        var knowing = OverTheTop(knowsTheRoad: true);
        output.WriteLine($"at the top: coal {blind.CoalAtTop:F0} kg not knowing, {knowing.CoalAtTop:F0} knowing; " +
                         $"blown off over it {blind.BlownOff:F0} and {knowing.BlownOff:F0} kg");
        Assert.True(knowing.CoalAtTop < 0.85f * blind.CoalAtTop);
        Assert.True(knowing.BlownOff <= blind.BlownOff);
    }

    [Fact]
    public void SeeingTheEngineerShutOffAndBrakeHeLetsTheFireBurnDown()
    {
        var engine = new Footplate();
        engine.EngineSteam = Climb;
        engine.Run(300f);
        var coal = engine.Fire.CoalMass;
        engine.EngineSteam = 0.2f;
        engine.Throttle = 0f;
        engine.Braking = true;
        // He finishes the round in hand, then starts no more.
        engine.Run(15f);
        var throws = engine.Throws.Count;
        engine.Run(60f);
        output.WriteLine($"coal {coal:F1} -> {engine.Fire.CoalMass:F1} kg, {engine.Throws.Count - throws} throws, glass target {engine.Fireman.GlassTarget:F2}");
        Assert.True(engine.Fireman.StopComing);
        Assert.Equal(throws, engine.Throws.Count);
        Assert.True(engine.Fireman.GlassTarget >= AutoFireman.GlassEasing);
        Assert.True(engine.Fire.CoalMass < coal);
    }

    [Fact]
    public void ABrokenJetIsShutAndStartedAgain()
    {
        var engine = new Footplate(glass: 0.3f);
        // Opened wide from shut, the jet breaks.
        engine.Fireman.Mode = FiremanMode.Off;
        engine.InjectorLever = 1f;
        engine.Run(1f);
        Assert.Equal(InjectorState.Broken, engine.Injector.State);

        engine.Fireman.Mode = FiremanMode.Road;
        engine.Run(15f);
        Assert.Equal(InjectorState.Working, engine.Injector.State);
    }

    [Fact]
    public void HeBlowsDownInBurstsWhileTheWaterIsDirty()
    {
        var engine = new Footplate(glass: 0.75f);
        engine.Solids = 2400f;
        engine.EngineSteam = Cruise;
        var opened = new List<float>();
        var shut = new List<float>();
        var since = 0f;
        var wasOpen = false;
        engine.Run(240f, e =>
        {
            var open = e.Blowdown > 0f;
            if (open != wasOpen)
            {
                (wasOpen ? opened : shut).Add(since);
                since = 0f;
                wasOpen = open;
            }
            since += Footplate.Dt;
            Assert.True(!open || e.Glass > AutoFireman.GlassGoal - 0.01f, "blew down with the glass low");
        });

        output.WriteLine($"open {string.Join(",", opened.Select(o => o.ToString("F1")))}; solids {engine.Solids:F0}");
        // The last burst may end early, when the glass reaches his goal.
        Assert.True(opened.Count >= 2);
        Assert.All(opened, o => Assert.True(o <= AutoFireman.BurstOpen + 0.1f));
        Assert.All(opened.SkipLast(1), o => Assert.True(o >= AutoFireman.BurstOpen - 0.1f));
        Assert.All(shut.Skip(1), s => Assert.True(s >= AutoFireman.BurstShut - 0.1f));
        Assert.True(engine.Solids < 2400f);
    }

    [Fact]
    public void BlowingDownStopsOnceTheWaterIsClean()
    {
        var engine = new Footplate(glass: 0.8f);
        engine.Solids = AutoFireman.BlowdownTo - 10f;
        engine.Run(30f);
        Assert.True(engine.Log.All(a => a.Blowdown == 0f));
    }

    [Fact]
    public void StandingHeKeepsAThinFireNotABank()
    {
        var engine = new Footplate(coal: 60f);
        engine.Standing = true;
        engine.Run(900f);

        output.WriteLine($"coal {engine.Fire.CoalMass:F1} kg, pressure {engine.Pressure:F2}, blower {engine.Blower:F2}, blown off {engine.BlownOff:F0} kg");
        Assert.True(engine.Fire.CoalMass <= 0.25f * engine.Fire.Parameters.MaxCoalCapacity);
        Assert.True(engine.Pressure >= AutoFireman.PressureFor(FiremanMode.Road, engine.SafetyValve) - 1f);
        Assert.True(engine.Blower > 0f);
        Assert.True(engine.Fire.FireOn);
    }

    [Fact]
    public void KeepItAliveHoldsAThinFireAtAboutHalfPressure()
    {
        var target = AutoFireman.PressureFor(FiremanMode.KeepAlive, 15f);
        var engine = new Footplate(FiremanMode.KeepAlive, coal: 20f, pressure: target - 0.5f);
        engine.Standing = true;
        float low = float.MaxValue, high = 0f;
        engine.Run(3600f, e =>
        {
            low = Math.Min(low, e.Pressure);
            high = Math.Max(high, e.Pressure);
        });

        output.WriteLine($"coal {engine.Fire.CoalMass:F1} kg, pressure {low:F2}-{high:F2} (target {target:F2}), glass {engine.Glass:F2}, " +
                         $"fired {engine.CoalFired:F0} kg");
        Assert.True(engine.Fire.FireOn);
        Assert.True(engine.Fire.CoalMass <= 0.2f * engine.Fire.Parameters.MaxCoalCapacity);
        Assert.InRange(low, target - 1f, target);
        Assert.InRange(high, target, target + 1.5f);
        Assert.InRange(engine.Glass, 0.4f, 0.9f);
    }

    [Fact]
    public void KeepItAliveFromFullPressureLetsItFallWithoutBlowingOff()
    {
        var engine = new Footplate(FiremanMode.KeepAlive, coal: 40f);
        engine.Standing = true;
        engine.Run(1800f);

        output.WriteLine($"coal {engine.Fire.CoalMass:F1} kg, pressure {engine.Pressure:F2}, blown off {engine.BlownOff:F0} kg, damper {engine.Damper:F2}");
        Assert.True(engine.Fire.FireOn);
        Assert.True(engine.BlownOff < 50f);
        Assert.True(engine.Pressure < 14.5f);
    }

    [Fact]
    public void HeavyAheadBuildsTheFireAndTheWaterBeforeTheStart()
    {
        var engine = new Footplate(coal: 20f);
        engine.Standing = true;
        engine.Run(60f);
        var coalBefore = engine.Fire.CoalMass;
        var blowerBefore = engine.Blower;
        engine.Fireman.Call = FiremanCall.HeavyAhead;
        var black = 0f;
        engine.Run(120f, e => black += e.Fire.SootDensity > AutoFireman.BlackSmoke ? Footplate.Dt : 0f);

        output.WriteLine($"standing: coal {coalBefore:F1} -> {engine.Fire.CoalMass:F1} kg, black for {black:F0} s, " +
                         $"glass target {engine.Fireman.GlassTarget:F2}, blower {blowerBefore:F2} -> {engine.Blower:F2}");
        // Standing, he builds what the blower's draft can burn without smoking black.
        Assert.True(engine.Fire.CoalMass > coalBefore + 5f);
        Assert.True(black < 20f);
        Assert.True(engine.Fireman.GlassTarget >= AutoFireman.GlassBeforeWork);
        Assert.True(engine.Blower > blowerBefore);

        // Away, he fires hard for the pull at once; once the engine has
        // worked hard a minute, the call has done its job.
        engine.Standing = false;
        engine.EngineSteam = Climb;
        engine.Run(45f);
        output.WriteLine($"away: coal {engine.Fire.CoalMass:F1} kg, plan {engine.Fireman.PlannedSteam:F2} kg/s");
        Assert.True(engine.Fire.CoalMass >= 0.4f * engine.Fire.Parameters.MaxCoalCapacity);
        engine.Run(20f);
        Assert.Equal(FiremanCall.None, engine.Fireman.Call);
    }

    [Fact]
    public void AfterTheStartHeLetsTheGlassBackToItsUsualLevelOnLevelRoad()
    {
        // The S282 in game (2026-10-10): the start called for a high glass,
        // and the water he stored then counted as building, which kept the
        // glass high and the fire heavy on level road at a quarter's work,
        // the pressure at the safety valve and the fire short of air. A low glass
        // here stands for the S282's bigger boiler: more water to store.
        var engine = new Footplate(glass: 0.42f);
        engine.Standing = true;
        engine.StartComing = true;
        engine.Run(60f);
        Assert.True(engine.Fireman.GlassTarget >= AutoFireman.GlassBeforeWork);
        engine.Standing = false;
        engine.StartComing = false;
        engine.EngineSteam = 1.4f;
        engine.SteamAhead = 1.4f;
        engine.Run(180f);
        var high = 0f;
        var mostPlanned = 0f;
        var blown = engine.BlownOff;
        engine.Run(300f, e =>
        {
            high = Math.Max(high, e.Pressure);
            mostPlanned = Math.Max(mostPlanned, e.Fireman.PlannedSteam);
        });
        output.WriteLine($"glass target {engine.Fireman.GlassTarget:P0}, glass {engine.Glass:P0}, pressure high {high:F2}, " +
                         $"planned up to {mostPlanned:F2} kg/s for 1.4, blown off {engine.BlownOff - blown:F1} kg, fire {engine.Fire.CoalMass:F0} kg");
        Assert.Equal(AutoFireman.GlassGoal, engine.Fireman.GlassTarget);
        Assert.True(mostPlanned < 2f, $"planned {mostPlanned:F2} kg/s for 1.4");
        Assert.True(engine.BlownOff - blown < 1f, $"blew off {engine.BlownOff - blown:F1} kg");
    }

    [Fact]
    public void StandingOnAHeavyFireHeKeepsDraftToBurnTheSmoke()
    {
        // The S282 in game (2026-10-10): stopped with 75 kg on the grate and
        // the pressure over his mark, he shut the damper and it smoked black.
        var engine = new Footplate(coal: 40f, pressure: 14.9f);
        for (var i = 0; i < 12; i++)
            engine.Shovel.Deliver(engine.Fire, 3f, null);
        engine.Standing = true;
        var black = 0f;
        var leastDamper = 1f;
        var wasBlack = false;
        engine.Run(180f, e =>
        {
            // He sets the damper for the stack he saw before this tick.
            if (wasBlack)
                leastDamper = Math.Min(leastDamper, e.Last.Damper);
            wasBlack = e.Fire.SootDensity > AutoFireman.BlackSmoke;
            if (wasBlack)
                black += Footplate.Dt;
        });
        output.WriteLine($"black {black:F0} s, damper down to {leastDamper:F2} while black, pressure {engine.Pressure:F2}, fire {engine.Fire.CoalMass:F0} kg");
        Assert.True(black > 0f, "the fresh coal smokes");
        Assert.True(leastDamper >= 0.4f - 1e-3f, $"the damper shut to {leastDamper:F2} on a black stack");
    }

    [Fact]
    public void WithItsOwnSteamValveHeStartsTheInjectorWaterFirstAndHoldsTheGlass()
    {
        var engine = new Footplate(glass: 0.45f, injectorDelivery: 4.6f) { InjectorSteam = 0f };
        engine.EngineSteam = 2.5f;
        var low = 1f;
        var high = 0f;
        engine.Run(600f, e =>
        {
            if (e.Time > 180f)
            {
                low = Math.Min(low, e.Glass);
                high = Math.Max(high, e.Glass);
            }
        });
        output.WriteLine($"glass {low:F2}-{high:F2}, steam {engine.InjectorSteam:F2}, water {engine.InjectorLever:F2}, broke {engine.InjectorBroke}, overflowed {engine.Overflowed:F0} kg");
        Assert.False(engine.InjectorBroke);
        Assert.InRange(low, AutoFireman.GlassGoal - 0.1f, 1f);
        Assert.InRange(high, 0f, AutoFireman.GlassGoal + 0.15f);

        // Standing with the glass full he shuts it, steam first, then the water: none runs to waste.
        engine.EngineSteam = 0f;
        engine.Standing = true;
        engine.Run(300f);
        var overflowed = engine.Overflowed;
        engine.Run(120f);
        output.WriteLine($"standing: steam {engine.InjectorSteam:F2}, water {engine.InjectorLever:F2}, overflowed {engine.Overflowed - overflowed:F1} kg in two minutes");
        Assert.True(engine.Overflowed - overflowed < 20f, $"ran {engine.Overflowed - overflowed:F1} kg out of the overflow standing");
    }

    [Fact]
    public void EasingOffLetsTheFireBurnDownAndFillsTheBoiler()
    {
        var engine = new Footplate();
        engine.EngineSteam = Climb;
        engine.Run(300f);
        var coalBefore = engine.Fire.CoalMass;
        engine.Fireman.Call = FiremanCall.EasingOff;
        engine.Run(90f);

        output.WriteLine($"coal {coalBefore:F1} -> {engine.Fire.CoalMass:F1} kg, glass {engine.Glass:F2}, target {engine.Fireman.GlassTarget:F2}");
        Assert.True(engine.Fire.CoalMass < 0.7f * coalBefore);
        Assert.Equal(AutoFireman.GlassEasing, engine.Fireman.GlassTarget);
    }

    [Fact]
    public void WithTheTenderDryHeStopsFiringAndShutsTheInjector()
    {
        var engine = new Footplate(glass: 0.4f);
        engine.EngineSteam = Cruise;
        engine.Run(60f);
        engine.TenderWater = 0f;
        var throws = engine.Throws.Count;
        engine.Run(60f);

        Assert.Equal(throws, engine.Throws.Count);
        Assert.Equal(0f, engine.InjectorLever);
        Assert.True(engine.Last.Notices.HasFlag(FiremanNotice.TenderWaterOut));
    }

    [Fact]
    public void OffHeDoesNothing()
    {
        var engine = new Footplate(FiremanMode.Off, coal: 10f, glass: 0.3f);
        engine.EngineSteam = Cruise;
        engine.Run(120f);
        Assert.Empty(engine.Throws);
        Assert.Empty(engine.Shakes);
        Assert.Equal(0f, engine.InjectorLever);
    }

    [Fact]
    public void HeWigglesTheGrateAboutEveryTwelveMinutesOfHardWork()
    {
        var engine = new Footplate();
        engine.EngineSteam = Climb;
        engine.Run(1800f);

        output.WriteLine($"shakes: {string.Join(",", engine.Shakes)}");
        Assert.InRange(engine.Shakes.Count(s => s == ShakeStroke.Wiggle), 2, 4);
    }

    [Fact]
    public void HeSaysWhatHeCannotDo()
    {
        var engine = new Footplate();
        engine.TenderCoal = 0f;
        engine.Fire.Extinguish();
        engine.Run(1f);
        Assert.True(engine.Last.Notices.HasFlag(FiremanNotice.TenderCoalOut));
        Assert.True(engine.Last.Notices.HasFlag(FiremanNotice.FireOut));
        Assert.Empty(engine.Throws);
    }

    [Fact]
    public void TheCoalRateGivesTheHeatAsTheStockFireboxWould()
    {
        var p = Footplate.S282;
        foreach (var share in new[] { 0.1f, 0.5f, 0.9f })
        {
            var rate = share * p.MaxCombustionRate;
            var heat = (float)FireTestKit.StockHeat(p, rate);
            Assert.Equal(rate, AutoFireman.RateFor(p, heat), 3);
        }
        Assert.Equal(p.MaxCombustionRate, AutoFireman.RateFor(p, 2f * p.MaxHeatOutput));
        Assert.Equal(0f, AutoFireman.RateFor(p, 0f));
    }
}
