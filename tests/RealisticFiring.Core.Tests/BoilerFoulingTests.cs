using System;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Smoky, slow firing soots the tubes and hard work scours them; hard water
/// scales the heating surface as it boils off; drifting hooked up with the
/// throttle shut wears the engine.
/// </summary>
public class BoilerFoulingTests
{
    private const float Dt = 1f;
    private const float Surface = 280f;

    private static float SootAfter(float hours, float soot, float flow, BoilerFouling? fouling = null)
    {
        fouling ??= new BoilerFouling();
        for (var t = 0f; t < hours * 3600f; t += Dt)
            fouling.Step(Dt, new BoilerFoulingInputs { Soot = soot, GasFlow = flow });
        return fouling.SootLoss;
    }

    [Fact]
    public void Black_smoke_at_half_flow_soots_the_tubes_in_an_hour_and_a_clean_fire_hardly()
    {
        Assert.InRange(SootAfter(1f, 0.5f, 0.5f), 0.03f, 0.06f);
        Assert.True(SootAfter(1f, 0.05f, 0.7f) < 0.01f);
    }

    [Fact]
    public void Hard_clean_work_scours_sooted_tubes()
    {
        var fouling = new BoilerFouling();
        fouling.Restore(0f, 0.08f);
        Assert.True(SootAfter(1f, 0.05f, 1f, fouling) < 0.02f);
    }

    [Fact]
    public void Hard_water_scales_the_surface_slowly_and_a_washout_loosens_half()
    {
        var fouling = new BoilerFouling();
        // A hundred hours of hard work on bad water: 6 kg/s boiled at 800 ppm.
        fouling.Step(Dt, new BoilerFoulingInputs { Boiled = 6f * 3600f * 100f, FeedSolids = 800f, HeatingSurface = Surface });
        var loss = fouling.Loss(Surface);
        Assert.InRange(loss, 0.02f, 0.06f);
        fouling.Washout();
        Assert.Equal(loss / 2f, fouling.Loss(Surface), 3);
        fouling.Clean(1f);
        Assert.Equal(0f, fouling.Loss(Surface));
    }

    [Fact]
    public void Drifting_hooked_up_with_the_throttle_shut_wears_the_engine_but_not_in_full_gear_or_with_steam()
    {
        Assert.True(Drifting.Wear(0f, 0.2f, 20f) > 0f);
        Assert.Equal(0f, Drifting.Wear(0f, 0.9f, 20f));
        Assert.Equal(0f, Drifting.Wear(0.05f, 0.2f, 20f));
        Assert.Equal(0f, Drifting.Wear(0f, 0.2f, 3f));
        Assert.InRange(Drifting.Wear(0f, 0f, 20f) * 3600f, 0.049f, 0.051f);
    }

    private static DriftState Drift(float throttle, float reverser, float speed, float draw = 0.5f, float steam = 0f,
        bool snifting = false, bool byPass = false) =>
        Drifting.State(throttle, reverser, speed, draw, steam, new DriftFittings(snifting, byPass, steam > 0f ? 1f : 0f));

    [Fact]
    public void Snifting_valves_hold_the_chests_at_the_atmosphere_and_leave_a_third_of_the_drifting_wear()
    {
        var bare = Drift(0f, 0.2f, 20f);
        var snifting = Drift(0f, 0.2f, 20f, snifting: true);
        Assert.Equal(Drifting.ChestVacuum, bare.Chest);
        Assert.False(bare.SniftingOpen);
        Assert.Equal(1f, snifting.Chest);
        Assert.True(snifting.SniftingOpen);
        Assert.Equal(Drifting.Wear(0f, 0.2f, 20f, bare) / 3f, Drifting.Wear(0f, 0.2f, 20f, snifting), 6);
        Assert.Equal(Drifting.Wear(0f, -0.2f, -20f), Drifting.Wear(0f, -0.2f, -20f, Drift(0f, -0.2f, -20f, snifting: true)) * 3f, 6);
        // In full gear or with steam on there is none to spare.
        Assert.Equal(0f, Drifting.Wear(0f, 0.9f, 20f, Drift(0f, 0.9f, 20f, snifting: true)));
        Assert.Equal(0f, Drifting.Wear(0.05f, 0.2f, 20f, Drift(0.05f, 0.2f, 20f, snifting: true)));
    }

    [Fact]
    public void Against_the_motion_or_with_steam_on_nothing_drifts_and_snifting_valves_stay_shut()
    {
        Assert.False(Drift(0f, -0.5f, 10f, snifting: true).Drifting);
        Assert.False(Drift(0f, -0.5f, 10f, snifting: true).SniftingOpen);
        Assert.False(Drift(0.1f, 0.5f, 10f, snifting: true).Drifting);
        Assert.False(Drift(0f, 0.5f, 1f, snifting: true).Drifting);
        Assert.True(Drift(0f, 0f, 10f, snifting: true).SniftingOpen);
        Assert.True(Drift(0f, 1f, 10f, snifting: true).SniftingOpen);
        Assert.True(Drift(0f, -0.5f, -10f, snifting: true).SniftingOpen);
        // The hooked-up wear against the motion is the bare engine's, the valves shut.
        Assert.Equal(Drifting.Wear(0f, -0.2f, 20f), Drifting.Wear(0f, -0.2f, 20f, Drift(0f, -0.2f, 20f, snifting: true)));
    }

    [Fact]
    public void A_drifting_valve_fills_the_chests_as_its_steam_meets_the_draw_and_spares_the_wear_with_them()
    {
        var short_ = Drift(0f, 0.2f, 20f, draw: 0.2f, steam: 0.26f, snifting: true);
        Assert.Equal(1f, short_.SteamShare);
        Assert.False(short_.SniftingOpen);
        Assert.InRange(short_.Chest, 1.29f, 1.31f);
        Assert.Equal(0f, Drifting.Wear(0f, 0.2f, 20f, short_));
        var half = Drift(0f, 0.2f, 20f, draw: 0.52f, steam: 0.26f);
        Assert.Equal(0.5f, half.SteamShare, 3);
        Assert.Equal(Drifting.ChestVacuum, half.Chest, 3);
        Assert.Equal(Drifting.Wear(0f, 0.2f, 20f) / 2f, Drifting.Wear(0f, 0.2f, 20f, half), 6);
        Assert.Equal(Drifting.MaxChest, Drift(0f, 0.2f, 20f, draw: 0.01f, steam: 0.26f).Chest);
        // Wide open at 14 bar it passes about 0.12 kg/s (950 lb/h).
        Assert.InRange(Drifting.ValveSteam(1f, 14f), 0.11f, 0.13f);
        Assert.Equal(0f, Drifting.ValveSteam(0f, 14f));
        // The S282's cylinders (533 by 711 mm) draw about 1.5 kg/s at the atmosphere in full gear at four turns a second.
        var volume = (float)Math.PI / 4f * 0.533f * 0.533f * 0.711f * 1000f;
        Assert.InRange(Drifting.Draw(0.9f, 0.1f, volume, 2, 4f), 1.4f, 1.6f);
    }

    [Fact]
    public void By_pass_valves_leave_the_cylinders_drawing_little_so_drifting_steam_fills_them()
    {
        var byPass = Drift(0f, 0.2f, 20f, byPass: true);
        Assert.True(byPass.ByPassOpen);
        Assert.Equal(Drifting.Wear(0f, 0.2f, 20f) / 3f, Drifting.Wear(0f, 0.2f, 20f, byPass), 6);
        // Steam a fifth of the draw fills what the joined cylinders still draw.
        var both = Drift(0f, 0.2f, 20f, draw: 0.5f, steam: 0.1f, byPass: true);
        Assert.Equal(1f, both.SteamShare, 3);
        Assert.Equal(0f, Drifting.Wear(0f, 0.2f, 20f, both));
    }

    [Fact]
    public void Drifting_brakes_as_newell_measured_most_hooked_up_rising_with_the_piston_speed()
    {
        var bare = Drift(0f, 0.2f, 20f);
        const float psi = 1f / 14.5038f;
        // Newell's Mikado at 40 mph: 5.2 psi at 50 % cut-off, 1.1 in full gear.
        Assert.Equal(-5.2f * psi, Drifting.Mep(bare, 0.5f, Drifting.ReferencePistonSpeed), 4);
        Assert.Equal(-1.1f * psi, Drifting.Mep(bare, 0.9f, Drifting.ReferencePistonSpeed), 4);
        Assert.Equal(-6f * psi, Drifting.Mep(bare, 0.1f, Drifting.ReferencePistonSpeed), 4);
        // A quarter of it at 10 mph; none standing; at most half again at speed.
        Assert.Equal(-5.2f * psi / 4f, Drifting.Mep(bare, 0.5f, Drifting.ReferencePistonSpeed / 4f), 4);
        Assert.Equal(0f, Drifting.Mep(bare, 0.5f, 0f));
        Assert.Equal(-5.2f * psi * Drifting.MostSpeedShare, Drifting.Mep(bare, 0.5f, 20f), 4);
        // Snifting valves and drifting steam alone leave it; not drifting, none.
        Assert.Equal(Drifting.Mep(bare, 0.5f, 5f), Drifting.Mep(Drift(0f, 0.2f, 20f, snifting: true), 0.5f, 5f), 5);
        Assert.Equal(Drifting.Mep(bare, 0.5f, 5f), Drifting.Mep(Drift(0f, 0.2f, 20f, draw: 0.5f, steam: 0.25f), 0.5f, 5f), 5);
        Assert.Equal(0f, Drifting.Mep(Drift(0.5f, 0.2f, 20f), 0.5f, 5f));
        // By-pass valves: none until 20 mph's piston speed, half at 40; with drifting steam, none.
        var byPass = Drift(0f, 0.2f, 20f, byPass: true);
        Assert.Equal(0f, Drifting.Mep(byPass, 0.5f, 2.5f));
        Assert.Equal(-5.2f * psi / 2f, Drifting.Mep(byPass, 0.5f, Drifting.ReferencePistonSpeed), 2);
        Assert.Equal(0f, Drifting.Mep(Drift(0f, 0.2f, 20f, draw: 0.5f, steam: 0.1f, byPass: true), 0.5f, Drifting.ReferencePistonSpeed), 5);
        Assert.Equal(1.422f, Drifting.PistonSpeed(0.711f, 1f), 3);
    }
}
