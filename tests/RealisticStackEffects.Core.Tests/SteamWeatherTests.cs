using RealisticStackEffects.Core;

namespace RealisticStackEffects.Core.Tests;

 public class SteamWeatherTests
{
    private static readonly PlumeTuning Tuning = new PlumeTuning();

    private static SteamCloud Cloud(float temperature, float humidity, float wetness = 0f) =>
        SteamCloud.For(new PlumeAir(temperature, humidity), SteamCloud.ReferenceFlueGas, wetness);

    private static float Lasting(SteamCloud cloud, float airSpeed) => cloud.Lasting(airSpeed, Tuning.SteamMixing, 1f / MathF.E);

    [Fact]
    public void TheAirsWaterFollowsTheSteamTables()
    {
        Assert.InRange(Psychrometrics.SaturationPressure(20.0), 2330.0, 2345.0);
        Assert.InRange(Psychrometrics.SaturationPressure(100.0), 100000.0, 102500.0);
        Assert.InRange(Psychrometrics.DewPoint(20f, 0.5f), 9.0f, 9.6f);
        // Photo 18's hour: 14.9 °C at 31 %, 11.7 hPa short of saturation.
        Assert.InRange(Psychrometrics.SaturationDeficit(14.9f, 0.31f), 11.4f, 11.9f);
    }

    [Fact]
    public void TheBlowersJetShowsWhiteWhereTheMixedStackDoesNot()
    {
        const float airPerSteam = 6f;
        var mixed = SteamCloud.For(PlumeAir.Reference, Tuning.FlueGasMixing * airPerSteam);
        var jet = SteamCloud.For(PlumeAir.Reference, Tuning.BlowerFlueGasMixing * airPerSteam);
        Assert.True(mixed.Whiteness < 0.1f);
        Assert.True(jet.Whiteness > 0.5f);
        Assert.Equal(0f, SteamCloud.For(new PlumeAir(36.4f, 0.34f), Tuning.BlowerFlueGasMixing * airPerSteam).Whiteness);
    }

    [Fact]
    public void TheReferenceAirShowsTheTunedWhite()
    {
        Assert.Equal(1f, Cloud(10f, 0.8f).Whiteness, 3);
        Assert.Equal(1f, SteamCloud.Reference.Whiteness, 3);
    }

    [Fact]
    public void ARunningEngineShowsNoWhiteInHotDryAir()
    {
        // Photo 03 (36 °C, 34 %, working hard up a grade) and 01 (26 °C, 30 %): no white at all.
        Assert.True(Cloud(36.4f, 0.34f).Whiteness < 0.02f);
        Assert.True(Cloud(26f, 0.30f).Whiteness < 0.02f);
    }

    [Fact]
    public void DrynessNotColdDecidesTheWhite()
    {
        // Photos 18 (15 °C, 31 %: none) and 16 (14 °C, 82 %: 60 to 250 m of white).
        var dry = Cloud(14.9f, 0.31f);
        var damp = Cloud(14.3f, 0.82f);
        Assert.True(dry.Whiteness < 0.6f * damp.Whiteness, $"{dry.Whiteness:F2} against {damp.Whiteness:F2}");
        Assert.True(Lasting(dry, 7.6f) < 1.5f, $"{Lasting(dry, 7.6f):F1} s");
        Assert.True(Lasting(damp, 8.3f) > 2f * Lasting(dry, 7.6f));
    }

    [Fact]
    public void RawSaturatedAirKeepsTheWhiteForTensOfSeconds()
    {
        // Photo 31: a frosty, saturated dawn (2.5 °C, 92 %), the plume along a 250 m train.
        var raw = Cloud(2.5f, 0.92f);
        Assert.True(raw.Whiteness > 1.3f);
        Assert.True(Lasting(raw, 9f) > 8f, $"{Lasting(raw, 9f):F1} s");
        Assert.True(Lasting(raw, 9f) > 3f * Lasting(Cloud(14.3f, 0.82f), 9f));
        // In fog it never evaporates; only spreading thins it.
        Assert.True(Lasting(Cloud(5f, 1f), 9f) > 60f);
    }

    [Fact]
    public void StandingTheWhiteLastsAboutTwiceAsLong()
    {
        // A standing engine's puffs mix more slowly than a plume torn apart at speed.
        var cloud = Cloud(10f, 0.8f);
        var ratio = Lasting(cloud, 0f) / Lasting(cloud, 15f);
        Assert.InRange(ratio, 1.6f, 2.6f);
    }

    [Fact]
    public void WetExhaustShowsWhiteEvenInDryAir()
    {
        // Photo 07: a hard start after a multi-day stand, cylinder cocks open, 10 to 20 s of white at 23 °C and 22 %.
        Assert.True(Cloud(22.8f, 0.22f).Whiteness < 0.02f);
        Assert.True(Cloud(22.8f, 0.22f, wetness: 0.15f).Whiteness > 0.2f, $"{Cloud(22.8f, 0.22f, wetness: 0.15f).Whiteness:F2}");
    }

    [Fact]
    public void ASummerAfternoonsBeatsStillShowAShortWhite()
    {
        // 78youbaba (Nexus, 2026-10-28): a summer afternoon's engine showed no
        // white at all. The blast's core still shows some, briefly, however
        // much of the fire's gas is about.
        var afternoon = PlumeClimate.Air(SmokeSeason.Summer, 13f, 0f, 0f);
        Assert.Equal(0f, SteamCloud.For(afternoon, SteamCloud.ReferenceFlueGas).Whiteness);
        foreach (var flueGas in new[] { SteamCloud.ReferenceFlueGas, 6f })
        {
            var cloud = SteamCloud.ForBeats(afternoon, flueGas, 0f, out var white);
            Assert.InRange(white, 0.1f, 0.4f);
            Assert.InRange(Lasting(cloud, 15f), 0.3f, 3f);
        }
    }

    [Fact]
    public void TheBlastsCoreLeavesTheTunedAndTheHotDryAirAsTheyWere()
    {
        // The reference air and the damp photos (16, 31) are the mixed
        // steam's alone; the hot, dry photos (03, 01, 07) still show no
        // white, and photo 18's dry day only a wisp gone within 1.5 s.
        foreach (var air in new[] { PlumeAir.Reference, new PlumeAir(14.3f, 0.82f), new PlumeAir(2.5f, 0.92f) })
        {
            var mixed = SteamCloud.For(air, SteamCloud.ReferenceFlueGas);
            Assert.Same(mixed, SteamCloud.ForBeats(air, SteamCloud.ReferenceFlueGas, 0f, out var white));
            Assert.Equal(mixed.Whiteness, white);
        }
        foreach (var air in new[] { new PlumeAir(36.4f, 0.34f), new PlumeAir(26f, 0.30f), new PlumeAir(22.8f, 0.22f) })
        {
            SteamCloud.ForBeats(air, SteamCloud.ReferenceFlueGas, 0f, out var white);
            Assert.True(white < 0.02f, $"{air}: {white:F3}");
        }
        var dry = SteamCloud.ForBeats(new PlumeAir(14.9f, 0.31f), SteamCloud.ReferenceFlueGas, 0f, out _);
        Assert.True(Lasting(dry, 7.6f) < 1.5f, $"{Lasting(dry, 7.6f):F2} s");
    }

    [Fact]
    public void HotFlueGasInTheSteamKeepsItClear()
    {
        var air = new PlumeAir(10f, 0.8f);
        var little = SteamCloud.For(air, 0.6f).Whiteness;
        var some = SteamCloud.For(air, 1.2f).Whiteness;
        var much = SteamCloud.For(air, 2.4f).Whiteness;
        Assert.True(little > some && some > much, $"{little:F2} > {some:F2} > {much:F2}");
    }

    [Fact]
    public void TheWhiteFormsAFewFeetUpThenEvaporates()
    {
        var cloud = Cloud(5f, 0.85f);
        Assert.True(cloud.Left(0f, 10f, Tuning.SteamMixing) < 0.1f);
        var thickest = 0f;
        var last = 0f;
        var past = false;
        for (var age = 0f; age < 60f; age += 0.05f)
        {
            var left = cloud.Left(age, 10f, Tuning.SteamMixing);
            if (left < last - 1e-4f)
                past = true;
            else if (past)
                Assert.True(left <= last + 1e-4f, $"{left} after {last} at {age:F2} s: formed again");
            thickest = MathF.Max(thickest, left);
            last = left;
        }
        Assert.Equal(1f, thickest, 3);
        Assert.True(last < 0.05f);
        // Half formed within a second, by when a hard beat's puff is a few metres up.
        var half = cloud.Forming(10f, Tuning.SteamMixing, Tuning.SteamForming, 0.5f);
        Assert.InRange(half, 0.05f, 1f);
        Assert.False(cloud.PastThickest(half, 10f, Tuning.SteamMixing));
        // Faster forming forms it sooner; air that shows no white never forms any.
        Assert.True(cloud.Forming(10f, Tuning.SteamMixing, 3f, 0.5f) < half);
        Assert.True(float.IsPositiveInfinity(Cloud(32f, 0.25f).Forming(10f, Tuning.SteamMixing, 1f, 0.5f)));
    }

    [Fact]
    public void ColdDampAirShowsItsWhiteNearerTheStack()
    {
        // Seen as white once the steam's opacity passes 0.15: in raw air its
        // thick white shows within a few tenths of a second, a mild day's thin
        // white later.
        float Shows(SteamCloud cloud)
        {
            var parcel = PlumeRise.Parcel(Tuning.SteamOpacity, 0f);
            for (var age = 0f; age < 5f; age += 0.01f)
                if (PlumeRise.OpacityOf(PlumeRise.SteamLeft(parcel, cloud, age, 1f, Tuning.SteamMixing) * cloud.Whiteness, 0f) > 0.15f)
                    return age;
            return float.PositiveInfinity;
        }
        var raw = Shows(Cloud(-5f, 0.9f));
        var mild = Shows(Cloud(15f, 0.6f));
        Assert.True(raw < 0.3f, $"{raw:F2} s");
        Assert.True(mild > 1.3f * raw, $"{mild:F2} s against {raw:F2} s");
    }

    [Fact]
    public void ASummerDayIsDampAtDawnAndDryInTheAfternoon()
    {
        var dawn = PlumeClimate.Air(SmokeSeason.Summer, 5f, 0f, 0f);
        var afternoon = PlumeClimate.Air(SmokeSeason.Summer, 15f, 0f, 0f);
        Assert.InRange(afternoon.Temperature - dawn.Temperature, 10f, 14f);
        Assert.True(dawn.Humidity > 0.85f, $"{dawn}");
        Assert.True(afternoon.Humidity < 0.55f, $"{afternoon}");
        // So a summer afternoon's running engine shows almost no white, its dawn plenty.
        Assert.True(SteamCloud.For(afternoon, SteamCloud.ReferenceFlueGas).Whiteness < 0.1f);
        Assert.True(SteamCloud.For(dawn, SteamCloud.ReferenceFlueGas).Whiteness > 0.8f);
        // The day runs on without a jump at midnight or the turning hours.
        foreach (var hour in new[] { 0f, 5f, 15f })
        {
            var before = PlumeClimate.Air(SmokeSeason.Summer, hour - 0.01f, 0f, 0f).Temperature;
            var after = PlumeClimate.Air(SmokeSeason.Summer, hour + 0.01f, 0f, 0f).Temperature;
            Assert.True(MathF.Abs(after - before) < 0.05f, $"{before} -> {after} at {hour}");
        }
    }

    [Fact]
    public void DvSeasonsTemperatureGetsANightDampAndAnAfternoonDry()
    {
        // DV Seasons gives a temperature with a daily swing but no humidity:
        // the dew point sits a degree under the night's coolest.
        var night = PlumeClimate.WithTemperature(-2f, 3f, 0f, 0f);
        var afternoon = PlumeClimate.WithTemperature(20f, 15f, 0f, 0f);
        Assert.True(night.Humidity > 0.9f, $"{night}");
        Assert.InRange(afternoon.Humidity, 0.4f, 0.6f);
        Assert.Equal(20f, afternoon.Temperature);
        Assert.True(PlumeClimate.WithTemperature(20f, 15f, 1f, 0f).Humidity >= 0.95f);
        // A winter night shows far more white than a summer afternoon.
        Assert.True(SteamCloud.For(night, SteamCloud.ReferenceFlueGas).Whiteness > 1.3f);
        Assert.True(SteamCloud.For(afternoon, SteamCloud.ReferenceFlueGas).Whiteness < 0.2f);
    }

    [Fact]
    public void RainAndFogBringTheAirToSaturation()
    {
        var dry = PlumeClimate.Air(SmokeSeason.Summer, 14f, 0f, 0f);
        var rain = PlumeClimate.Air(SmokeSeason.Summer, 14f, 1f, 0f);
        Assert.True(rain.Humidity >= 0.95f && rain.Temperature < dry.Temperature);
        Assert.Equal(1f, PlumeClimate.Air(SmokeSeason.Summer, 14f, 0f, 1f).Humidity, 3);
        Assert.True(PlumeClimate.Air(SmokeSeason.Winter, 14f, 0f, 0f).Temperature < 5f);
    }

    private static PlumeInputs Working(float pressure, float whiteness, float smoke = 0f) => new PlumeInputs
    {
        ExhaustPressure = pressure,
        TotalFlowNormalized = 0.6f,
        Soot = smoke,
        FireOn = true,
        HeatShare = 0.5f,
        SteamWhiteness = whiteness,
        WaterWhiteness = whiteness,
    };

    [Fact]
    public void ThePuffsAreAsWhiteAsTheWeatherLetsThem()
    {
        var reference = PlumeModel.ForBeat(Tuning, Working(5f, 1f));
        var dry = PlumeModel.ForBeat(Tuning, Working(5f, 0f));
        var raw = PlumeModel.ForBeat(Tuning, Working(5f, 2f));
        Assert.Equal(Tuning.SteamOpacity, reference.Color.A, 3);
        Assert.True(dry.Color.A < 0.01f, $"{dry.Color.A:F2}");
        Assert.True(raw.Color.A > reference.Color.A + 0.15f, $"{raw.Color.A:F2}");
        // Smoke shows whatever the weather; with no white its puff is all smoke.
        var sooty = PlumeModel.ForBeat(Tuning, Working(5f, 0f, smoke: 0.3f));
        Assert.True(sooty.Color.A > 0.3f, $"{sooty.Color.A:F2}");
        Assert.Equal(1f, sooty.SmokeShare, 3);
        // Unset, the inputs are the reference air's.
        Assert.Equal(reference.Color.A, PlumeModel.ForBeat(Tuning, new PlumeInputs { ExhaustPressure = 5f, TotalFlowNormalized = 0.6f, FireOn = true }).Color.A, 3);
    }

    // A coal fire's beat: its soot and, unless clear, the haze its fine ash
    // and light smoke make, in air that lets its steam be so white.
    private static Puff CoalBeat(float soot, float whiteness, float clear = 0f)
    {
        var inputs = Working(4f, whiteness, smoke: soot);
        inputs.ClearStack = clear;
        inputs.PuffHaze = PlumeModel.WellFiredHaze(Tuning, clear);
        return PlumeModel.ForBeat(Tuning, inputs);
    }

    private static float Brightness(PlumeColor c) => (c.R + c.G + c.B) / 3f;

    [Fact]
    public void AWellFiredCoalStackIsLightGreyWhateverTheWeather()
    {
        var dry = CoalBeat(0.08f, 0f);
        Assert.InRange(dry.Color.A, 0.25f, 0.55f);
        Assert.InRange(Brightness(dry.Color), 0.5f, 0.75f);
        // In damp air the same fire's white steam is greyed by it.
        var damp = CoalBeat(0.08f, 1.5f);
        Assert.True(damp.Color.A > dry.Color.A + 0.2f);
        Assert.InRange(Brightness(damp.Color), 0.6f, 0.85f);
        // Overfed it goes dark; clean with air to spare, clear.
        Assert.True(Brightness(CoalBeat(0.5f, 0f).Color) < 0.3f);
        Assert.True(CoalBeat(0f, 0f, clear: 1f).Color.A < 0.02f);
    }

    [Fact]
    public void AWorkingEnginesClearStackShimmersInDryAir()
    {
        Assert.True(PlumeModel.Shimmer(Tuning, Working(5f, 0f)) > 0.5f);
        Assert.True(PlumeModel.Shimmer(Tuning, Working(5f, 1f)) < 0.3f);
    }
}
