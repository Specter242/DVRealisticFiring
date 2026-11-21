using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class PlumeRiseTests
{
    [Theory]
    [InlineData(30f)]
    [InlineData(60f)]
    [InlineData(100f)]
    public void MovingTrainFinishesItsPuffsWithinTenMetres(float kilometresPerHour)
    {
        var speed = kilometresPerHour / 3.6f;
        var scale = PlumeRise.PuffAgeScale(speed, 8f, 0.45f, 10f);
        Assert.InRange(speed * 8f * 0.45f * scale, 0f, 10.001f);
        Assert.InRange(scale, 0f, 1f);
    }

    [Fact]
    public void StationaryAndWalkingSpeedKeepTheAcceptedPuffTiming()
    {
        Assert.Equal(1f, PlumeRise.PuffAgeScale(0f, 8f, 0.45f, 10f));
        Assert.Equal(1f, PlumeRise.PuffAgeScale(1f, 8f, 0.45f, 10f));
        Assert.Equal(1f, PlumeRise.PuffAgeScale(20f, 8f, 0.45f, 0f));
    }

    [Fact]
    public void HigherSpeedShortensTheHandoffWithoutExtendingTheLightBeat()
    {
        var slow = PlumeRise.PuffAgeScale(10f, 8f, 0.45f, 10f);
        var fast = PlumeRise.PuffAgeScale(20f, 8f, 0.45f, 10f);
        Assert.True(fast < slow);
        Assert.InRange(20f * 8f * 0.3f * fast, 0f, 10f);
    }

    // A hard-working engine (its fire in W, its exhaust steam in kg/s) and
    // an idling one, its blower on.
    private static readonly (float Fire, float Steam) Working = (30e6f, 10f);
    private static readonly (float Fire, float Steam) Idling = (2e6f, 1f);

    private static float Flux((float Fire, float Steam) engine) => PlumeRise.StackFlux(engine.Fire, engine.Steam);

    // Climbs a parcel for so many seconds in still air or past moving air.
    private static float HeightAfter(float seconds, (float Fire, float Steam) engine, float airSpeed = 0f)
    {
        var flux = Flux(engine);
        var height = 0f;
        for (var t = 0f; t < seconds; t += 0.05f)
            height += PlumeRise.RiseSpeed(flux, height, airSpeed) * 0.05f;
        return height;
    }

    [Fact]
    public void AWorkingEnginesPlumeTowersHundredsOfFeetInStillAir()
    {
        // 100 ft is 30 m: within half a minute, and on past 100 m in a minute.
        Assert.True(HeightAfter(30f, Working) > 100f, $"{HeightAfter(30f, Working):F0} m after 30 s");
        Assert.True(HeightAfter(60f, Working) > 150f, $"{HeightAfter(60f, Working):F0} m after a minute");
    }

    [Fact]
    public void ThePlumeNeverStopsRisingButSlowsWithHeight()
    {
        var flux = Flux(Working);
        var low = PlumeRise.RiseSpeed(flux, 10f, 0f);
        var high = PlumeRise.RiseSpeed(flux, 300f, 0f);
        Assert.True(high < low);
        Assert.True(high > 1f, $"{high:F2} m/s at 300 m");
        Assert.True(PlumeRise.RiseSpeed(flux, 3000f, 0f) > 0.5f);
    }

    [Fact]
    public void ShowingTheStillAirRiseSlowerLeavesTheBentPlume()
    {
        var flux = Flux(Working);
        Assert.Equal(0.7f * PlumeRise.RiseSpeed(flux, 10f, 0f), PlumeRise.RiseSpeed(flux, 10f, 0f, 0.7f), 4);
        // At speed the bent-over plume is the slower, its trajectory the air's and its rise's.
        Assert.Equal(PlumeRise.RiseSpeed(flux, 10f, 20f), PlumeRise.RiseSpeed(flux, 10f, 20f, 0.7f), 4);
    }

    [Fact]
    public void ABiggerFireRisesFaster()
    {
        Assert.True(HeightAfter(30f, Working) > HeightAfter(30f, Idling));
        Assert.Equal(0f, PlumeRise.RiseSpeed(PlumeRise.StackFlux(0f, 0f), 10f, 0f));
        // Steam alone, the fire out, still rises.
        Assert.True(PlumeRise.RiseSpeed(PlumeRise.StackFlux(0f, 1f), 10f, 0f) > 0.5f);
    }

    [Fact]
    public void AtSpeedThePlumeLiesBack()
    {
        // At 20 m/s it rises a fraction as high in the same time; slow, nearly as in still air.
        var still = HeightAfter(30f, Working);
        Assert.True(HeightAfter(30f, Working, 20f) < 0.5f * still);
        Assert.True(HeightAfter(30f, Working, 1f) > HeightAfter(30f, Working, 20f));
    }

    [Fact]
    public void ALightBreezeDoesNotBendAStrongPlume()
    {
        // Standing in a 1.1 m/s breeze the plume was bent over from the stack.
        Assert.True(HeightAfter(30f, Idling, 1f) > 0.95f * HeightAfter(30f, Idling));
        Assert.True(HeightAfter(30f, Idling, 3f) < HeightAfter(30f, Idling, 1f));
    }

    [Fact]
    public void TheTrainsWakeCarriesThePlumesBaseOnly()
    {
        Assert.Equal(0.3f, PlumeRise.Wake(0f), 3);
        Assert.True(PlumeRise.Wake(3f) < 0.05f, $"{PlumeRise.Wake(3f):F2} of the train's speed 3 m up");
        Assert.True(PlumeRise.Wake(20f) < 0.001f);
    }

    [Fact]
    public void CondensingExhaustSteamLiftsThePlume()
    {
        // A working engine's steam is buoyant beyond being light and hot: its latent heat warms the cloud.
        Assert.True(PlumeRise.StackFlux(0f, 10f) > PlumeRise.StackFlux(30e6f, 0f));
    }

    [Fact]
    public void ThePlumeWidensWithHeight()
    {
        Assert.True(PlumeRise.Width(100f) > 20f);
        Assert.True(PlumeRise.Width(0f) < 1f);
    }

    [Fact]
    public void BlackSmokeStaysVisibleHighUpWhileSteamEvaporates()
    {
        var black = PlumeRise.Parcel(0.8f, 1f);
        var steam = PlumeRise.Parcel(0.7f, 0f);
        var mild = SteamCloud.For(new PlumeAir(10f, 0.8f), SteamCloud.ReferenceFlueGas);
        var raw = SteamCloud.For(new PlumeAir(1f, 0.95f), SteamCloud.ReferenceFlueGas);
        // A minute on, 300 m up: the smoke still shows, the steam is gone.
        Assert.True(PlumeRise.Opacity(black, mild, 60f, 300f, 5f) > 0.08f);
        Assert.True(PlumeRise.Opacity(steam, mild, 60f, 300f, 5f) < PlumeRise.Invisible);
        // At the lip the smoke shows as it left; the steam's white forms a
        // little way up, and shows as it left once formed.
        Assert.Equal(0.8f, PlumeRise.Opacity(black, mild, 0f, 0f, 5f), 2);
        Assert.True(PlumeRise.Opacity(steam, mild, 0f, 0f, 5f) < 0.1f);
        Assert.InRange(PlumeRise.Opacity(steam, mild, 1f, 2f, 5f), 0.55f, 0.7f);
        // Cold, damp air keeps the steam longer.
        Assert.True(PlumeRise.Opacity(steam, raw, 10f, 20f, 5f) > PlumeRise.Opacity(steam, mild, 10f, 20f, 5f) + 0.1f);
    }

    [Fact]
    public void AtSpeedATrailOfBlackSmokeThinsOutWithinAMileAndAHalf()
    {
        var black = PlumeRise.Parcel(0.8f, 1f);
        float At(float seconds) => PlumeRise.OpacityOf(black.SmokeDepth, 15f, PlumeRise.SpreadWidth(seconds, 20f));
        Assert.True(At(10f) > 0.15f, $"{At(10f):F2} at 200 m");
        Assert.True(At(40f) < 0.06f, $"{At(40f):F2} at 800 m");
        // Standing in still air the towering plume barely spreads by age.
        Assert.True(PlumeRise.SpreadWidth(60f, 0f) < PlumeRise.Width(200f));
    }

    [Fact]
    public void ATrailStaysSpreadWhenTheTrainStops()
    {
        // loady740 (Nexus, 2026-10-27): a line of puffs hung where the train had run.
        var running = 0f;
        for (var turn = 0; turn < 1200; turn++)
            running = PlumeRise.SpreadOn(running, 0.025f, 20f);
        Assert.Equal(PlumeRise.SpreadWidth(30f, 20f), running, 1);
        var stopped = PlumeRise.SpreadOn(running, 10f, 0f);
        Assert.True(stopped > running);
        float Shown(float spread) => PlumeRise.OpacityOf(PlumeRise.Parcel(0.8f, 1f).SmokeDepth, 5f, spread);
        Assert.True(Shown(stopped) < Shown(running));
        // Worked out from its age at the air's speed now, the stopped trail drew back in over three times as thick.
        Assert.True(Shown(PlumeRise.SpreadWidth(40f, 0f)) > 3f * Shown(stopped));
    }

    [Fact]
    public void AnAgingPlumeKeepsWidening()
    {
        Assert.True(PlumeRise.PlumeWidth(100f, PlumeRise.SpreadWidth(60f, 5f)) > PlumeRise.PlumeWidth(100f, PlumeRise.SpreadWidth(20f, 5f)));
        Assert.True(PlumeRise.PlumeWidth(100f, 0f) > 30f);
    }

    [Fact]
    public void FewerParticlesCarryTheWiderPlumeAbove()
    {
        Assert.Equal(1f, PlumeRise.Survival(0f));
        Assert.True(PlumeRise.Survival(100f) < 0.1f);
        Assert.True(PlumeRise.Survival(300f) > 0f);
    }

    [Fact]
    public void CalmDawnInversionCapsRiseGentlyAndSpreadsLaterallyWhileMiddayTowersFreely()
    {
        var dawnStrength = PlumeWind.InversionStrength(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var dawnCeiling = PlumeWind.InversionCeiling(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var noonStrength = PlumeWind.InversionStrength(hour: 13f, windSpeed: 0.3f, rain: 0f);
        var noonCeiling = PlumeWind.InversionCeiling(hour: 13f, windSpeed: 0.3f, rain: 0f);

        // Below the inversion layer rise is unaffected; above it slows toward zero.
        Assert.Equal(1f, PlumeRise.InversionDamping(10f, dawnCeiling, dawnStrength), 4);
        var dampingHigh = PlumeRise.InversionDamping(55f, dawnCeiling, dawnStrength);
        Assert.InRange(dampingHigh, 0f, 0.1f);
        Assert.True(PlumeRise.InversionSpread(4f, dampingHigh) > 1f);

        // Midday leaves rise completely unconstrained and adds no flattening spread.
        Assert.Equal(1f, PlumeRise.InversionDamping(55f, noonCeiling, noonStrength), 4);
        Assert.Equal(0f, PlumeRise.InversionSpread(4f, 1f), 4);

        // Climb an idling plume for 60 s: calm dawn levels off gently near the ceiling, midday climbs freely.
        var flux = Flux(Idling);
        float dawnHeight = 0f, noonHeight = 0f;
        for (var t = 0f; t < 60f; t += 0.05f)
        {
            dawnHeight += PlumeRise.RiseSpeed(flux, dawnHeight, 0.3f, 1f, dawnCeiling, dawnStrength) * 0.05f;
            noonHeight += PlumeRise.RiseSpeed(flux, noonHeight, 0.3f, 1f, noonCeiling, noonStrength) * 0.05f;
        }
        Assert.InRange(dawnHeight, 25f, 55f);
        Assert.True(noonHeight > 100f, $"midday height {noonHeight:F1} m");
    }

    [Fact]
    public void SlipstreamDownwashPullsSoftExhaustDownBehindAFastTrain()
    {
        const float driftingExit = 1.2f;
        var driftingFlux = PlumeRise.StackFlux(1e6f, 0f);
        var downwash = PlumeRise.Downwash(height: 0.2f, trainSpeed: 20f, exitSpeed: driftingExit, flux: driftingFlux);

        // Gentle downward velocity strong enough to beat down soft drifting smoke, within cap.
        Assert.InRange(downwash, 1f, PlumeRise.MaxDownwash);
        Assert.True(downwash > PlumeRise.RiseSpeed(driftingFlux, 0.2f, 20f),
            $"downwash {downwash:F2} m/s should exceed weak bent-over rise");
    }

    [Fact]
    public void SlipstreamDownwashVanishesWhenStationaryUnderHardExhaustAloftOrAtBoilerFloor()
    {
        var softFlux = PlumeRise.StackFlux(1e6f, 0f);
        var hardFlux = Flux(Working);

        // Stationary and slow yard movement: zero downwash.
        Assert.Equal(0f, PlumeRise.Downwash(height: 0f, trainSpeed: 0f, exitSpeed: 1.2f, flux: softFlux));
        Assert.Equal(0f, PlumeRise.Downwash(height: 0f, trainSpeed: 2f, exitSpeed: 1.2f, flux: softFlux));

        // Hard working exhaust punches cleanly above the wake even at high speed.
        Assert.Equal(0f, PlumeRise.Downwash(height: 0f, trainSpeed: 25f, exitSpeed: 18f, flux: hardFlux));
        Assert.Equal(0f, PlumeRise.Downwash(height: 0.5f, trainSpeed: 20f, exitSpeed: 14f, flux: hardFlux));

        // Decays rapidly with height above the stack lip.
        var atLip = PlumeRise.Downwash(height: 0f, trainSpeed: 20f, exitSpeed: 1.2f, flux: softFlux);
        var twoUp = PlumeRise.Downwash(height: 2f, trainSpeed: 20f, exitSpeed: 1.2f, flux: softFlux);
        var tenUp = PlumeRise.Downwash(height: 10f, trainSpeed: 20f, exitSpeed: 1.2f, flux: softFlux);
        Assert.True(twoUp < 0.45f * atLip, $"2 m up {twoUp:F2} vs lip {atLip:F2}");
        Assert.True(tenUp < 0.02f * atLip, $"10 m up {tenUp:F3} vs lip {atLip:F2}");

        // Tapers to zero at the boiler-top floor so smoke never dives through the boiler or ground.
        Assert.Equal(0f, PlumeRise.Downwash(height: PlumeRise.DownwashFloor, trainSpeed: 25f, exitSpeed: 1.2f, flux: softFlux));
        Assert.Equal(0f, PlumeRise.Downwash(height: PlumeRise.DownwashFloor - 2f, trainSpeed: 25f, exitSpeed: 1.2f, flux: softFlux));
        var midBoiler = PlumeRise.Downwash(height: 0.5f * PlumeRise.DownwashFloor, trainSpeed: 20f, exitSpeed: 1.2f, flux: softFlux);
        Assert.InRange(midBoiler, 0.01f, atLip);
    }

    [Fact]
    public void SlipstreamDownwashIsZeroForDistantParcelsInBothTravelDirections()
    {
        var softFlux = PlumeRise.StackFlux(1e6f, 0f);
        const float trainSpeed = 20f;
        const float exitSpeed = 1.2f;

        // Horizontal distance from the stack lip is symmetric for forward and reverse travel.
        foreach (var signedOffset in new[] { -30f, 30f, -PlumeRise.DownwashWakeLength, PlumeRise.DownwashWakeLength, -120f, 120f })
        {
            var horizontalDistance = MathF.Abs(signedOffset);
            var downwash = PlumeRise.Downwash(height: 0f, trainSpeed: trainSpeed, exitSpeed: exitSpeed, flux: softFlux,
                horizontalDistance: horizontalDistance, age: 0f);
            Assert.Equal(0f, downwash);
        }

        // Near the stack lip it acts; mid-wake it weakens smoothly before vanishing at DownwashWakeLength.
        var atStack = PlumeRise.Downwash(height: 0f, trainSpeed: trainSpeed, exitSpeed: exitSpeed, flux: softFlux, horizontalDistance: 0f, age: 0f);
        var midWake = PlumeRise.Downwash(height: 0f, trainSpeed: trainSpeed, exitSpeed: exitSpeed, flux: softFlux, horizontalDistance: 0.5f * PlumeRise.DownwashWakeLength, age: 0f);
        Assert.True(midWake > 0f && midWake < atStack);
    }

    [Fact]
    public void DriftingParcelRecoversUpwardAfterLeavingLocomotiveWake()
    {
        var softFlux = PlumeRise.StackFlux(1e6f, 0f);
        const float trainSpeed = 20f;
        const float exitSpeed = 1.2f;
        const float drag = 2f;
        const float dt = 0.05f;
        var strength = PlumeRise.DownwashStrength(trainSpeed, exitSpeed, softFlux);

        var height = 0.2f;
        var horizontal = 0f;
        var verticalVelocity = 0.2f;
        var minHeight = height;

        for (var t = 0f; t < 30f; t += dt)
        {
            var wake = PlumeRise.Wake(height);
            var airSpeed = trainSpeed * (1f - wake);
            var rise = PlumeRise.RiseSpeed(softFlux, height, airSpeed);
            var inWake = horizontal < PlumeRise.DownwashWakeLength;
            var downwash = inWake ? PlumeRise.Downwash(strength, height, horizontal, age: t, trainSpeed: trainSpeed, stepDt: dt) : 0f;
            var targetUp = rise - downwash;
            var keep = MathF.Exp(-drag * dt);
            verticalVelocity = targetUp + (verticalVelocity - targetUp) * keep;
            if (inWake)
                verticalVelocity = PlumeRise.ClampDownwashVertical(verticalVelocity, height, dt);
            height += verticalVelocity * dt;
            horizontal += airSpeed * dt;
            minHeight = MathF.Min(minHeight, height);
        }

        // Initial downwash beats the smoke below the lip, never below DownwashFloor,
        // and once past the wake buoyancy lifts it cleanly well above the lip.
        Assert.True(minHeight < 0.2f, $"parcel should dip in wake (minHeight {minHeight:F2} m)");
        Assert.True(minHeight >= PlumeRise.DownwashFloor - 1e-4f, $"parcel overshot DownwashFloor ({minHeight:F2} m)");
        Assert.True(height > 3f, $"parcel should recover upward after leaving wake, ended at {height:F2} m");
    }

    [Fact]
    public void DelayedDownwashUpdateNeverOvershootsDownwashFloor()
    {
        var softFlux = PlumeRise.StackFlux(1e6f, 0f);
        const float trainSpeed = 25f;
        const float exitSpeed = 1.0f;
        const float drag = 2f;
        const float delayedDt = 0.5f;
        var strength = PlumeRise.DownwashStrength(trainSpeed, exitSpeed, softFlux);

        // Start halfway to DownwashFloor already moving downward fast on a delayed 0.5 s step.
        var height = -0.5f;
        var velocityUp = -1.8f;
        var downwash = PlumeRise.Downwash(strength, height, horizontalDistance: 4f, age: 0.2f, trainSpeed: trainSpeed, stepDt: delayedDt);
        var targetUp = -downwash;
        var keep = MathF.Exp(-drag * delayedDt);
        velocityUp = targetUp + (velocityUp - targetUp) * keep;
        velocityUp = PlumeRise.ClampDownwashVertical(velocityUp, height, delayedDt);

        var nextHeight = height + velocityUp * delayedDt;
        Assert.True(nextHeight >= PlumeRise.DownwashFloor - 1e-5f, $"delayed step landed at {nextHeight:F3} m below floor {PlumeRise.DownwashFloor}");

        // Already at or below the floor: downward velocity is clamped to >= 0.
        Assert.Equal(0f, PlumeRise.ClampDownwashVertical(-1.5f, PlumeRise.DownwashFloor, delayedDt));
        Assert.Equal(0f, PlumeRise.ClampDownwashVertical(-1.5f, PlumeRise.DownwashFloor - 0.5f, delayedDt));
    }

    [Fact]
    public void InversionSpreadWidensLaterallyWithoutAcceleratingDilutionOrRetirement()
    {
        var dawnStrength = PlumeWind.InversionStrength(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var dawnCeiling = PlumeWind.InversionCeiling(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var flux = Flux(Idling);
        const float height = 35f;
        const float airSpeed = 0.3f;

        var undampedRise = PlumeRise.RiseSpeed(flux, height, airSpeed);
        var damping = PlumeRise.InversionDamping(height, dawnCeiling, dawnStrength);
        var rise = undampedRise * damping;
        var inversionSpread = PlumeRise.InversionSpread(undampedRise, damping);
        Assert.True(inversionSpread > 0.5f);

        // Lateral widening velocity increases under the inversion.
        var wideningWithout = PlumeRise.WideningRate(rise, airSpeed, spreadScale: 1f, inversionSpread: 0f);
        var wideningWith = PlumeRise.WideningRate(rise, airSpeed, spreadScale: 1f, inversionSpread: inversionSpread);
        Assert.True(wideningWith > wideningWithout + 0.5f);

        // SpreadOn does NOT absorb inversionSpread, so dilution and particle survival stay intact.
        var spread = PlumeRise.SpreadOn(0f, seconds: 20f, airSpeed: airSpeed, scale: 1f);
        var dilution = PlumeRise.Dilution(height, spread);
        var survival = PlumeRise.Survival(height, spread);
        Assert.Equal(PlumeRise.Dilution(height, PlumeRise.SpreadWidth(20f, airSpeed)), dilution, 5);
        Assert.Equal(PlumeRise.Survival(height, PlumeRise.SpreadWidth(20f, airSpeed)), survival, 5);
    }

    [Fact]
    public void InversionSpreadOnSaturatesFiniteMaxValueAndPreservesMonotonicityOnOverflow()
    {
        var saturated = PlumeRise.InversionSpreadOn(0f, 2f, float.MaxValue);
        Assert.True(float.IsFinite(saturated), $"expected finite saturation, got {saturated}");
        Assert.Equal(float.MaxValue, saturated);

        // Subsequent updates stay finite and monotonic rather than resetting to 0.
        var followUp = PlumeRise.InversionSpreadOn(saturated, 1f, 1f);
        Assert.True(float.IsFinite(followUp), $"expected finite follow-up, got {followUp}");
        Assert.True(followUp >= saturated, $"follow-up {followUp} fell below {saturated}");
        Assert.Equal(float.MaxValue, followUp);

        // Overflow from an already large finite width also saturates cleanly.
        var additiveOverflow = PlumeRise.InversionSpreadOn(float.MaxValue, 1f, float.MaxValue);
        Assert.True(float.IsFinite(additiveOverflow));
        Assert.Equal(float.MaxValue, additiveOverflow);
    }
}
