using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class PlumeParcelsTests
{
    // The plume's optical depth across, from its particles: those in an
    // envelope's length, each its depth over its disc, over the envelope's
    // face.
    private static float AcrossDepth(float envelope, float speed, float rate, float lump, float across, float depth, float area)
    {
        var kept = PlumeParcels.Kept(envelope, speed, rate, across);
        var size = PlumeParcels.Size(envelope, lump);
        var each = PlumeParcels.Depth(depth, area, size, kept);
        var shown = PlumeParcels.ShownShare * size;
        var inLength = rate * kept * envelope / speed;
        return inLength * each * (MathF.PI / 4f * shown * shown) / (envelope * envelope);
    }

    [Fact]
    public void ThePlumeIsAsThickHoweverManyParticlesCarryIt()
    {
        var full = AcrossDepth(envelope: 10f, speed: 4f, rate: 135f, lump: 0.4f, across: 12f, depth: 0.5f, area: 0.1f);
        var fewerKept = AcrossDepth(10f, 4f, 135f, 0.4f, 6f, 0.5f, 0.1f);
        var moreKept = AcrossDepth(10f, 4f, 135f, 0.4f, 24f, 0.5f, 0.1f);

        Assert.Equal(full, fewerKept, 4);
        Assert.Equal(full, moreKept, 4);
    }

    [Fact]
    public void ThePlumeIsAsThickHoweverLargeItsParticlesAreDrawn()
    {
        var small = AcrossDepth(10f, 4f, 135f, 0.3f, 12f, 0.5f, 0.1f);
        var large = AcrossDepth(10f, 4f, 135f, 0.6f, 12f, 0.5f, 0.1f);

        Assert.Equal(small, large, 4);
    }

    [Fact]
    public void ThePlumeThinsAsItWidensAndAsItMovesOnFaster()
    {
        var near = AcrossDepth(5f, 4f, 135f, 0.4f, 12f, 0.5f, 0.1f);

        Assert.Equal(near / 2f, AcrossDepth(10f, 4f, 135f, 0.4f, 12f, 0.5f, 0.1f), 4);
        Assert.Equal(near / 2f, AcrossDepth(5f, 8f, 135f, 0.4f, 12f, 0.5f, 0.1f), 4);
    }

    [Fact]
    public void TwiceTheExhaustIsTwiceAsThick()
    {
        var one = AcrossDepth(10f, 4f, 60f, 0.4f, 12f, 0.5f, 0.1f);
        var two = AcrossDepth(10f, 4f, 120f, 0.4f, 12f, 0.5f, 0.1f);

        Assert.Equal(2f * one, two, 4);
    }

    [Fact]
    public void OnlyAsManyAreKeptAsFillThePlume()
    {
        // 135 a second through a 10 m envelope at 4 m/s is 337 in its length: 12 are wanted.
        Assert.Equal(12f / 337.5f, PlumeParcels.Kept(10f, 4f, 135f, 12f), 4);
        // Fewer left the stack than would fill it: all are kept.
        Assert.Equal(1f, PlumeParcels.Kept(1f, 10f, 15f, 12f));
        Assert.Equal(1f, PlumeParcels.Kept(10f, 4f, 0f, 12f));
    }

    [Fact]
    public void AParticleIsALumpOfThePlumeSetAnywhereAcrossIt()
    {
        var envelope = PlumeParcels.Envelope(plumeWidth: 8f, leastShown: 3f);
        Assert.Equal(10f, envelope, 3);
        // No narrower than the exhaust spreading from the lip.
        Assert.Equal(3f, PlumeParcels.Envelope(1f, 3f), 3);

        var size = PlumeParcels.Size(envelope, 0.4f);
        Assert.Equal(4f, PlumeParcels.ShownShare * size, 3);
        // The outermost's edge reaches the envelope's; the middle one sits on the axis.
        Assert.Equal(5f, PlumeParcels.Offset(envelope, 0.4f, 1f) + 0.5f * PlumeParcels.ShownShare * size, 3);
        Assert.Equal(0f, PlumeParcels.Offset(envelope, 0.4f, 0f), 3);
    }

    [Fact]
    public void DenseSteamsLumpsAreOpaqueAndALightHazesFaint()
    {
        // Slogging, 5 m up: a 3 m envelope moving on at 6 m/s, 135 particles a second.
        var kept = PlumeParcels.Kept(3f, 6f, 135f, 12f);
        var size = PlumeParcels.Size(3f, 0.4f);
        var steam = PlumeParcels.Opacity(PlumeParcels.Depth(0.75f, 0.8f, size, kept));
        var haze = PlumeParcels.Opacity(PlumeParcels.Depth(0.16f, 0.8f * PlumeParcels.SmokeAmount(0f, 0.13f), size, kept));

        Assert.True(steam > 0.9f, $"steam {steam}");
        Assert.InRange(haze, 0.02f, 0.2f);
    }

    [Fact]
    public void BlackSmokeCarriesAsMuchAsSteamAndALightHazeAShare()
    {
        Assert.Equal(1f, PlumeParcels.SmokeAmount(1f, 0.13f), 4);
        Assert.Equal(0.13f, PlumeParcels.SmokeAmount(0f, 0.13f), 4);
        Assert.InRange(PlumeParcels.SmokeAmount(0.5f, 0.13f), 0.5f, 0.6f);
        // Black smoke's amount may be set under steam's, never under a light haze's.
        Assert.Equal(0.4f, PlumeParcels.SmokeAmount(1f, 0.13f, 0.4f), 4);
        Assert.Equal(0.13f, PlumeParcels.SmokeAmount(1f, 0.13f, 0.05f), 4);
    }

    [Fact]
    public void ALumpRollsUpThePlumesMiddleAndDownItsSides()
    {
        PlumeParcels.Roll(axial: 0f, radial: 0f, out var middle, out _);
        PlumeParcels.Roll(0f, 0.95f, out var side, out _);
        PlumeParcels.Roll(0.6f, 0.5f, out _, out var overTop);
        PlumeParcels.Roll(-0.6f, 0.5f, out _, out var underBottom);

        Assert.True(middle > 0f);
        Assert.True(side < 0f);
        Assert.True(overTop > 0f);
        Assert.True(underBottom < 0f);
    }

    [Fact]
    public void ALumpNeverRollsOutOfThePlume()
    {
        // On the ball's surface its motion has no part outward.
        foreach (var angle in new[] { 0.2f, 0.9f, 1.6f, 2.4f, 3f })
        {
            var axial = MathF.Cos(angle);
            var radial = MathF.Sin(angle);
            PlumeParcels.Roll(axial, radial, out var axialRate, out var radialRate);
            Assert.Equal(0f, axial * axialRate + radial * radialRate, 4);
        }

        // Carried round for a minute (by midpoint steps), it stays inside.
        float z = 0.1f, r = 0.3f;
        for (var i = 0; i < 6000; i++)
        {
            PlumeParcels.Roll(z, r, out var dz, out var dr);
            PlumeParcels.Roll(z + 0.005f * dz, r + 0.005f * dr, out dz, out dr);
            z += dz * 0.01f;
            r += dr * 0.01f;
        }
        Assert.True(z * z + r * r < 1.02f, $"at {z}, {r}");
    }

    [Fact]
    public void ThePlumesDepthAcrossFollowsFromWhatLeftTheStack()
    {
        // The particles' own depths over their discs add up to it.
        var amount = 0.5f * 0.1f;
        var fromParticles = AcrossDepth(envelope: 10f, speed: 4f, rate: 135f, lump: 0.4f, across: 12f, depth: 0.5f, area: 0.1f);

        Assert.Equal(fromParticles, PlumeParcels.AcrossDepth(amount, 135f, 4f, 10f), 4);
    }

    [Fact]
    public void LumpsAreLargerInThePlumesMiddleAndMixIntoAnEvenHaze()
    {
        var body = PlumeParcels.Lump(0.4f, 0f, 0f);
        var rim = PlumeParcels.Lump(0.4f, 1f, 0f);

        Assert.True(body > 0.7f && body < 0.8f, $"body {body}");
        Assert.True(rim > 0.25f && rim < 0.4f, $"rim {rim}");
        // Mixed, none is less than most of the plume across, wherever it sits.
        Assert.Equal(0.6f, PlumeParcels.Lump(0.4f, 1f, 1f), 3);
        Assert.Equal(body, PlumeParcels.Lump(0.4f, 0f, 1f), 3);
        Assert.InRange(PlumeParcels.Lump(0.4f, 1f, 0.5f), rim, 0.6f);
    }

    [Fact]
    public void ADenseColumnShowsNoSkyThroughItsMiddle()
    {
        // The usual lumps of an envelope's length, each at its place by an
        // even draw: how many deep they cover the column's face on average.
        const int lumps = 20;
        var covered = 0f;
        for (var i = 0; i < lumps; i++)
        {
            var across = PlumeParcels.Lump(0.4f, PlumeParcels.Out((i + 0.5f) / lumps), 0f);
            covered += MathF.PI / 4f * across * across;
        }

        // Three deep leaves about a twentieth of it clear, set at random.
        Assert.True(covered > 3f, $"covered {covered} deep");
        // More sit toward the middle than even through a ball would put there.
        Assert.True(PlumeParcels.Out(0.5f) < MathF.Cbrt(0.5f));
    }

    [Fact]
    public void NoParticleIsDrawnDeeperThanTheMost()
    {
        Assert.Equal(PlumeParcels.MostDepth, PlumeParcels.Depth(3f, 0.8f, 0.5f, 0.01f));
        Assert.True(PlumeParcels.Opacity(PlumeParcels.MostDepth) > 0.99f);
    }

    [Fact]
    public void ParcelSootVariationIsDeterministicBoundedAndPreservesMean()
    {
        const int count = 10000;
        double depthSum = 0.0, shadeSum = 0.0;
        float minDepth = float.MaxValue, maxDepth = float.MinValue;
        float minShade = float.MaxValue, maxShade = float.MinValue;

        for (uint seed = 1; seed <= count; seed++)
        {
            var depth = PlumeParcels.SootDepthFactor(seed, smokiness: 1f);
            var shade = PlumeParcels.SootShadeFactor(seed, smokiness: 1f);

            // Deterministic on repeat lookup.
            Assert.Equal(depth, PlumeParcels.SootDepthFactor(seed, 1f));
            Assert.Equal(shade, PlumeParcels.SootShadeFactor(seed, 1f));

            // Strictly bounded within +/- MaxSootVariation.
            Assert.InRange(depth, 1f - PlumeParcels.MaxSootVariation - 1e-5f, 1f + PlumeParcels.MaxSootVariation + 1e-5f);
            Assert.InRange(shade, 1f - PlumeParcels.MaxSootVariation - 1e-5f, 1f + PlumeParcels.MaxSootVariation + 1e-5f);

            // Sootier parcels are darker and cleaner parcels lighter.
            Assert.Equal(2f, depth + shade, 4);

            depthSum += depth;
            shadeSum += shade;
            minDepth = MathF.Min(minDepth, depth);
            maxDepth = MathF.Max(maxDepth, depth);
            minShade = MathF.Min(minShade, shade);
            maxShade = MathF.Max(maxShade, shade);
        }

        // Mean darkness and shade are preserved across seeds.
        Assert.InRange(depthSum / count, 0.995, 1.005);
        Assert.InRange(shadeSum / count, 0.995, 1.005);
        // Full contrast range is spanned.
        Assert.True(maxDepth - minDepth > 1.8f * PlumeParcels.MaxSootVariation);
        Assert.True(maxShade - minShade > 1.8f * PlumeParcels.MaxSootVariation);
    }

    [Fact]
    public void ParcelSootVariationIsStrongOnHeavySmokeAndAbsentOnCleanSteam()
    {
        var cleanSteam = PlumeRise.Parcel(opacity: 0.7f, smokeShare: 0f);
        var lightHaze = PlumeRise.Parcel(opacity: 0.15f, smokeShare: 0.35f);
        var heavyCoal = PlumeRise.Parcel(opacity: 0.97f, smokeShare: 0.95f);

        Assert.Equal(0f, PlumeParcels.Smokiness(cleanSteam));
        Assert.InRange(PlumeParcels.Smokiness(lightHaze), 0.1f, 0.5f);
        Assert.True(PlumeParcels.Smokiness(heavyCoal) > 0.9f);

        // Find a seed with strong variation.
        uint activeSeed = 1;
        for (uint s = 1; s < 100; s++)
            if (MathF.Abs(PlumeParcels.SootVariation(s)) > 0.7f) { activeSeed = s; break; }

        var variedSteam = PlumeParcels.VarySoot(cleanSteam, activeSeed);
        Assert.Equal(cleanSteam.SteamDepth, variedSteam.SteamDepth);
        Assert.Equal(0f, variedSteam.SmokeDepth);

        var variedHaze = PlumeParcels.VarySoot(lightHaze, activeSeed);
        var variedCoal = PlumeParcels.VarySoot(heavyCoal, activeSeed);
        var hazeDelta = MathF.Abs(variedHaze.SmokeDepth / lightHaze.SmokeDepth - 1f);
        var coalDelta = MathF.Abs(variedCoal.SmokeDepth / heavyCoal.SmokeDepth - 1f);
        Assert.Equal(heavyCoal.SteamDepth, variedCoal.SteamDepth);
        Assert.True(coalDelta > hazeDelta * 2f, $"coal delta {coalDelta:F3} should exceed haze delta {hazeDelta:F3}");
    }

    [Fact]
    public void CalmDawnInversionExpandsParcelHorizontalTargetOffsetAndFlattensVerticalWithoutAcceleratingDilution()
    {
        var dawnStrength = PlumeWind.InversionStrength(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var dawnCeiling = PlumeWind.InversionCeiling(hour: 5.5f, windSpeed: 0.3f, rain: 0f);
        var noonStrength = PlumeWind.InversionStrength(hour: 13f, windSpeed: 0.3f, rain: 0f);
        var noonCeiling = PlumeWind.InversionCeiling(hour: 13f, windSpeed: 0.3f, rain: 0f);

        var flux = PlumeRise.StackFlux(2e6f, 1f);
        const float height = 35f;
        const float airSpeed = 0.3f;
        const float lump = 0.4f;
        const float dt = 0.25f;

        var spreadWithInversion = 0f;
        var spreadWithoutInversion = 0f;
        var inversionSpreadWidth = 0f;
        var dawnDamping = 1f;
        var noonDamping = 1f;

        for (var step = 0; step < 80; step++)
        {
            var undampedRise = PlumeRise.RiseSpeed(flux, height, airSpeed, calmShare: 0.7f);
            dawnDamping = PlumeRise.InversionDamping(height, dawnCeiling, dawnStrength);
            noonDamping = PlumeRise.InversionDamping(height, noonCeiling, noonStrength);
            var inversionSpreadRate = PlumeRise.InversionSpread(undampedRise, dawnDamping);

            spreadWithInversion = PlumeRise.SpreadOn(spreadWithInversion, dt, airSpeed);
            spreadWithoutInversion = PlumeRise.SpreadOn(spreadWithoutInversion, dt, airSpeed);
            inversionSpreadWidth = PlumeRise.InversionSpreadOn(inversionSpreadWidth, dt, inversionSpreadRate);
        }

        // 3. parcel.Spread (and therefore PlumeWidth, Dilution, and SurvivalAt) stays identical to no-inversion.
        Assert.Equal(spreadWithoutInversion, spreadWithInversion, 5);
        var widthWith = PlumeRise.PlumeWidth(height, spreadWithInversion);
        var widthWithout = PlumeRise.PlumeWidth(height, spreadWithoutInversion);
        Assert.Equal(widthWithout, widthWith, 5);
        Assert.Equal(PlumeRise.Dilution(height, spreadWithoutInversion), PlumeRise.Dilution(height, spreadWithInversion), 5);
        Assert.Equal(PlumeRise.SurvivalAt(widthWithout), PlumeRise.SurvivalAt(widthWith), 5);

        var envelope = PlumeParcels.Envelope(widthWith, leastShown: 0.5f);
        PlumeParcels.TargetOffsets(envelope, lump, dawnDamping, inversionSpreadWidth,
            out var dawnHorizontal, out var dawnVertical);
        PlumeParcels.TargetOffsets(envelope, lump, noonDamping, 0f,
            out var noonHorizontal, out var noonVertical);

        var baseOffset = PlumeParcels.Offset(envelope, lump, 1f);
        Assert.Equal(baseOffset, noonHorizontal, 5);
        Assert.Equal(baseOffset, noonVertical, 5);

        // 1. Under calm dawn inversion, parcel mode expands horizontally MORE than without inversion.
        Assert.True(inversionSpreadWidth > 5f, $"accumulated inversion spread {inversionSpreadWidth:F2} m");
        Assert.True(dawnHorizontal > noonHorizontal + 2.5f,
            $"dawn horizontal {dawnHorizontal:F2} m should exceed noon {noonHorizontal:F2} m");

        // 2. Vertical offset is compressed by flattening.
        Assert.True(dawnVertical < noonVertical * 0.7f,
            $"dawn vertical {dawnVertical:F2} m should be flattened well below noon {noonVertical:F2} m");
        Assert.True(dawnVertical >= baseOffset * PlumeParcels.MinInversionVerticalScale - 1e-4f);
    }

    [Fact]
    public void ParcelTargetOffsetsHandleBoundaryAndInvalidInputsSafely()
    {
        var baseOffset = PlumeParcels.Offset(envelope: 10f, lump: 0.4f, @out: 1f);

        // Full inversion damping flattens vertical offset to MinInversionVerticalScale.
        PlumeParcels.TargetOffsets(10f, 0.4f, inversionDamping: 0f, inversionSpreadWidth: 6f,
            out var horizFull, out var vertFull);
        Assert.Equal(baseOffset + 3f, horizFull, 4);
        Assert.Equal(baseOffset * PlumeParcels.MinInversionVerticalScale, vertFull, 4);

        // Even when lump fills the base envelope (baseOffset == 0), inversion spread still widens horizontally.
        PlumeParcels.TargetOffsets(10f, 1f, inversionDamping: 0.2f, inversionSpreadWidth: 4f,
            out var horizUnitLump, out var vertUnitLump);
        Assert.Equal(2f, horizUnitLump, 4);
        Assert.Equal(0f, vertUnitLump, 4);

        // Negative or NaN inputs never produce NaN or shrink horizontal spread below baseOffset.
        PlumeParcels.TargetOffsets(10f, 0.4f, float.NaN, float.NaN, out var horizNan, out var vertNan);
        Assert.Equal(baseOffset, horizNan, 4);
        Assert.Equal(baseOffset, vertNan, 4);

        // InversionSpreadOn accumulates monotonically and ignores negative/NaN terms.
        var accumulated = PlumeRise.InversionSpreadOn(2f, 0.5f, 1.2f);
        Assert.Equal(2.6f, accumulated, 4);
        Assert.Equal(accumulated, PlumeRise.InversionSpreadOn(accumulated, -1f, 2f), 4);
        Assert.Equal(accumulated, PlumeRise.InversionSpreadOn(accumulated, 1f, -2f), 4);
        Assert.Equal(0f, PlumeRise.InversionSpreadOn(float.NaN, float.NaN, float.NaN), 4);
    }

    [Fact]
    public void ParcelTargetOffsetsGuardNonFiniteEnvelopeLumpAndOverflowInputs()
    {
        // NaN envelope alone falls back to 0 base offset while preserving inversion lateral spread.
        PlumeParcels.TargetOffsets(float.NaN, 0.4f, 0.5f, 6f, out var horizNanEnv, out var vertNanEnv);
        Assert.True(float.IsFinite(horizNanEnv) && float.IsFinite(vertNanEnv));
        Assert.Equal(3f, horizNanEnv, 4);
        Assert.Equal(0f, vertNanEnv, 4);

        // NaN lump alone treats lump as 0 (baseOffset = 0.5 * envelope).
        PlumeParcels.TargetOffsets(10f, float.NaN, 1f, 0f, out var horizNanLump, out var vertNanLump);
        Assert.True(float.IsFinite(horizNanLump) && float.IsFinite(vertNanLump));
        Assert.Equal(5f, horizNanLump, 4);
        Assert.Equal(5f, vertNanLump, 4);

        // +/-Infinity envelope alone treats envelope as 0.
        foreach (var infEnvelope in new[] { float.PositiveInfinity, float.NegativeInfinity })
        {
            PlumeParcels.TargetOffsets(infEnvelope, 0.4f, 0.5f, 4f, out var horiz, out var vert);
            Assert.True(float.IsFinite(horiz) && float.IsFinite(vert));
            Assert.Equal(2f, horiz, 4);
            Assert.Equal(0f, vert, 4);
        }

        // +/-Infinity lump alone treats lump as 0.
        foreach (var infLump in new[] { float.PositiveInfinity, float.NegativeInfinity })
        {
            PlumeParcels.TargetOffsets(10f, infLump, 1f, 0f, out var horiz, out var vert);
            Assert.True(float.IsFinite(horiz) && float.IsFinite(vert));
            Assert.Equal(5f, horiz, 4);
            Assert.Equal(5f, vert, 4);
        }

        // Finite overflow saturates cleanly to float.MaxValue rather than +Infinity.
        PlumeParcels.TargetOffsets(float.MaxValue, -2f, 1f, float.MaxValue, out var horizOverflow, out var vertOverflow);
        Assert.True(float.IsFinite(horizOverflow) && float.IsFinite(vertOverflow));
        Assert.Equal(float.MaxValue, horizOverflow);
        Assert.Equal(float.MaxValue, vertOverflow);

        // No combination of non-finite, negative, zero, or extreme finite inputs ever returns NaN or Infinity.
        var samples = new[] { float.NaN, float.NegativeInfinity, -10f, 0f, 10f, float.MaxValue, float.PositiveInfinity };
        foreach (var env in samples)
        foreach (var lump in samples)
        foreach (var damping in samples)
        foreach (var spread in samples)
        {
            PlumeParcels.TargetOffsets(env, lump, damping, spread, out var h, out var v);
            Assert.True(float.IsFinite(h) && h >= 0f, $"non-finite/negative horizontal {h} for ({env}, {lump}, {damping}, {spread})");
            Assert.True(float.IsFinite(v) && v >= 0f, $"non-finite/negative vertical {v} for ({env}, {lump}, {damping}, {spread})");
        }
    }
}
