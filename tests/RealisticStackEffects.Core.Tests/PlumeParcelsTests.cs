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
}
