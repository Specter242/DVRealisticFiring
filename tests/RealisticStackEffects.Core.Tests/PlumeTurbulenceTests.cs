using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

/// <summary>
/// Divergence-free 3D curl turbulence and slow horizontal meander for the
/// chimney plume: solenoidal eddies do not pinch or clump particles, and
/// slow crosswind meander weaves plumes naturally aloft.
/// </summary>
public class PlumeTurbulenceTests
{
    [Theory]
    [InlineData(0f, 5f, 0f, 0f)]
    [InlineData(12.3f, 27.8f, -41.2f, 14.5f)]
    [InlineData(-85.4f, 110.0f, 63.7f, 123.4f)]
    [InlineData(310.5f, 18.2f, -220.9f, 540.1f)]
    [InlineData(1000f, 30f, 1000f, 25f)]
    [InlineData(1000f, 2f, 1000f, 25f)]
    [InlineData(1000f, 6f, 1000f, 25f)]
    public void CurlFieldIsNumericallyDivergenceFreeIncludingLipRampAndFarFromOrigin(float x, float y, float z, float time)
    {
        const float eps = 1e-2f;

        PlumeTurbulence.Curl(x + eps, y, z, time, out var vxp, out _, out _);
        PlumeTurbulence.Curl(x - eps, y, z, time, out var vxm, out _, out _);

        PlumeTurbulence.Curl(x, y + eps, z, time, out _, out var vyp, out _);
        PlumeTurbulence.Curl(x, y - eps, z, time, out _, out var vym, out _);

        PlumeTurbulence.Curl(x, y, z + eps, time, out _, out _, out var vzp);
        PlumeTurbulence.Curl(x, y, z - eps, time, out _, out _, out var vzm);

        var dx = (x + eps) - (x - eps);
        var dy = (y + eps) - (y - eps);
        var dz = (z + eps) - (z - eps);
        var divergence = (vxp - vxm) / dx + (vyp - vym) / dy + (vzp - vzm) / dz;
        Assert.InRange(MathF.Abs(divergence), 0f, 1e-3f);
    }

    [Fact]
    public void CurlFieldIsZeroAtAndBelowTheLipAndRampsInSmoothly()
    {
        foreach (var height in new[] { 0f, -0.01f, -2f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            PlumeTurbulence.Curl(1000f, height, 1000f, 15f, out var vx, out var vy, out var vz);
            Assert.Equal(0f, vx);
            Assert.Equal(0f, vy);
            Assert.Equal(0f, vz);
        }

        // Smooth C² envelope: zero with zero slope at the lip, growing smoothly across the ramp,
        // and decaying gently aloft.
        PlumeTurbulence.Envelope(0f, out var a0, out var d0);
        PlumeTurbulence.Envelope(0.25f, out var aNear, out _);
        PlumeTurbulence.Envelope(2f, out var aMid, out _);
        PlumeTurbulence.Envelope(PlumeTurbulence.EddyLipRamp, out var aFull, out _);
        PlumeTurbulence.Envelope(50f, out var aHigh, out _);

        Assert.Equal(0.0, a0);
        Assert.Equal(0.0, d0);
        Assert.InRange(aNear, 0.0, 0.01);
        Assert.True(aMid > aNear && aFull > aMid);
        Assert.True(aHigh > 0.0 && aHigh < aFull);
    }

    [Fact]
    public void CurlFieldHasNoCoordinateDependentVerticalPhaseJitterFarFromOrigin()
    {
        const float dy = 0.1f;
        var maxJumpNear = 0f;
        var maxJumpFar = 0f;
        for (var i = 0; i < 40; i++)
        {
            var y0 = 10f + i * dy;
            var y1 = y0 + dy;

            PlumeTurbulence.Curl(0f, y0, 0f, 12f, out var ax0, out var ay0, out var az0);
            PlumeTurbulence.Curl(0f, y1, 0f, 12f, out var ax1, out var ay1, out var az1);
            var nearDiff = MathF.Sqrt((ax1 - ax0) * (ax1 - ax0) + (ay1 - ay0) * (ay1 - ay0) + (az1 - az0) * (az1 - az0)) / dy;
            maxJumpNear = MathF.Max(maxJumpNear, nearDiff);

            PlumeTurbulence.Curl(10000f, y0, 10000f, 12f, out var bx0, out var by0, out var bz0);
            PlumeTurbulence.Curl(10000f, y1, 10000f, 12f, out var bx1, out var by1, out var bz1);
            var farDiff = MathF.Sqrt((bx1 - bx0) * (bx1 - bx0) + (by1 - by0) * (by1 - by0) + (bz1 - bz0) * (bz1 - bz0)) / dy;
            maxJumpFar = MathF.Max(maxJumpFar, farDiff);
        }

        // Fixed wavenumbers keep vertical variation gentle everywhere regardless of map coordinates.
        Assert.InRange(maxJumpNear, 0.01f, 0.5f);
        Assert.InRange(maxJumpFar, 0.01f, 0.5f);
    }

    [Fact]
    public void CurlFieldIsBoundedDeterministicHorizontalDominantAndNonTrivial()
    {
        var maxMag = 0f;
        var maxAbsY = 0f;
        var sumMag = 0f;
        var samples = 0;

        for (var ix = -3; ix <= 3; ix++)
        for (var iy = 1; iy <= 6; iy++)
        for (var iz = -3; iz <= 3; iz++)
        {
            var x = ix * 6.7f;
            var y = iy * 5.3f;
            var z = iz * 7.1f;
            PlumeTurbulence.Curl(x, y, z, 25f, out var vx, out var vy, out var vz);
            var mag = MathF.Sqrt(vx * vx + vy * vy + vz * vz);
            Assert.True(mag <= 1.0001f, $"magnitude {mag} exceeded unit bound");
            maxMag = MathF.Max(maxMag, mag);
            maxAbsY = MathF.Max(maxAbsY, MathF.Abs(vy));
            sumMag += mag;
            samples++;
        }

        Assert.True(sumMag / samples > 0.12f, "curl field should have meaningful eddy energy");
        Assert.True(maxMag > 0.35f, "curl field should reach substantial eddy peaks");
        Assert.True(maxAbsY < 0.25f * maxMag, $"vertical eddy component {maxAbsY:F3} should stay restrained vs peak {maxMag:F3}");

        // Deterministic for identical inputs.
        PlumeTurbulence.Curl(7.5f, 14.2f, -3.1f, 42f, out var ax, out var ay, out var az);
        PlumeTurbulence.Curl(7.5f, 14.2f, -3.1f, 42f, out var bx, out var by, out var bz);
        Assert.Equal(ax, bx);
        Assert.Equal(ay, by);
        Assert.Equal(az, bz);
    }

    [Fact]
    public void SlowMeanderIsZeroAtTheLipCrosswindInABreezeAndBounded()
    {
        // At or below stack lip: zero meander so smoke stays attached to the stack.
        PlumeTurbulence.Meander(time: 10f, height: 0f, windX: 4f, windZ: 0f, out var lipX, out var lipZ);
        Assert.Equal(0f, lipX);
        Assert.Equal(0f, lipZ);
        PlumeTurbulence.Meander(time: 10f, height: -1f, windX: 4f, windZ: 0f, out lipX, out lipZ);
        Assert.Equal(0f, lipX);
        Assert.Equal(0f, lipZ);

        // In a steady east wind (windX = 3, windZ = 0), meander acts purely crosswind (along Z) and weaves both ways over time.
        float minZ = float.MaxValue, maxZ = float.MinValue, maxAbsX = 0f;
        for (var t = 0f; t < 180f; t += 1f)
        {
            PlumeTurbulence.Meander(t, height: 25f, windX: 3f, windZ: 0f, out var mx, out var mz);
            maxAbsX = MathF.Max(maxAbsX, MathF.Abs(mx));
            minZ = MathF.Min(minZ, mz);
            maxZ = MathF.Max(maxZ, mz);
        }
        Assert.True(maxAbsX < 1e-4f, $"along-wind leak {maxAbsX}");
        Assert.True(minZ < -0.3f && maxZ > 0.3f, $"crosswind weave [{minZ:F2}, {maxZ:F2}]");
        Assert.True(MathF.Max(-minZ, maxZ) <= PlumeTurbulence.CalmMeander + PlumeTurbulence.WindMeanderShare * PlumeTurbulence.MaxMeanderWind + 1e-3f);

        // In calm air, meander still provides a gentle bounded 2D wander aloft.
        PlumeTurbulence.Meander(time: 15f, height: 25f, windX: 0f, windZ: 0f, out var calmX, out var calmZ);
        var calmMag = MathF.Sqrt(calmX * calmX + calmZ * calmZ);
        Assert.InRange(calmMag, 0.01f, PlumeTurbulence.CalmMeander * 1.5f);
    }
}
