using System;
using Xunit;

namespace RealisticFiring.Core.Tests;

public class CabooseStoveTests
{
    private static void StepFor(CabooseStoveModel stove, float seconds, float ambientC = 5f, float dt = 0.1f)
    {
        for (var t = 0f; t < seconds; t += dt)
            stove.Step(dt, ambientC);
    }

    [Fact]
    public void ColdStoveWithWoodNeverSelfIgnites()
    {
        var stove = new CabooseStoveModel(ambient: 10f);
        stove.SetDoorOpening(1f);
        var added = stove.AddWood(4 * CabooseStoveModel.PieceMass);
        Assert.Equal(4 * CabooseStoveModel.PieceMass, added, 4);

        StepFor(stove, 300f, ambientC: 10f);

        Assert.False(stove.Lit);
        Assert.Equal(0f, stove.Heat01, 4);
        Assert.Equal(0f, stove.Smoke01, 4);
        Assert.Equal(4 * CabooseStoveModel.PieceMass, stove.WoodKg, 4);
        Assert.Equal(10f, stove.WarmthC, 1);
    }

    [Fact]
    public void WoodAndBurningOiledRagIgnitesAndWarmsCaboose()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(1f);
        stove.AddWood(4 * CabooseStoveModel.PieceMass);
        Assert.True(stove.AddRag(RagFuel.OilCapacity, lit: true));
        Assert.True(stove.Lit);

        // Rag heats wood to piloted ignition over tens of seconds, then flames take over.
        StepFor(stove, 120f, ambientC: 0f);

        Assert.True(stove.Lit);
        Assert.True(stove.Flaming);
        Assert.True(stove.Heat01 > 0.35f, $"Expected warm fire heat, got {stove.Heat01:F3}");
        Assert.True(stove.Smoke01 > 0.10f, $"Expected chimney smoke, got {stove.Smoke01:F3}");
        Assert.True(stove.WarmthC > 12f, $"Expected caboose to warm up from 0 C, got {stove.WarmthC:F1} C");
    }

    [Fact]
    public void UnlitOiledRagStaysUnlitUntilLighterLightsIt()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(1f);
        stove.AddWood(3 * CabooseStoveModel.PieceMass);
        Assert.True(stove.AddRag(RagFuel.OilCapacity, lit: false));

        StepFor(stove, 60f, ambientC: 10f);
        Assert.False(stove.Lit);

        // Even with LighterLightsCoal off, a lighter lights an oiled rag already in the stove.
        Assert.True(stove.TryIgniteWithLighter(lighterShortcutEnabled: false));
        Assert.True(stove.RagLit);
        Assert.True(stove.Lit);

        StepFor(stove, 120f, ambientC: 10f);
        Assert.True(stove.Flaming);
    }

    [Fact]
    public void LighterShortcutLightsBareWoodOnlyWhenEnabled()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(1f);

        // Empty stove cannot be lit even with shortcut enabled.
        Assert.False(stove.TryIgniteWithLighter(lighterShortcutEnabled: true));
        Assert.False(stove.Lit);

        stove.AddWood(3 * CabooseStoveModel.PieceMass);

        // With shortcut disabled, bare wood refuses the lighter.
        Assert.False(stove.TryIgniteWithLighter(lighterShortcutEnabled: false));
        Assert.False(stove.Lit);

        // With shortcut enabled (LighterLightsCoal on), bare wood ignites cleanly.
        Assert.True(stove.TryIgniteWithLighter(lighterShortcutEnabled: true));
        Assert.True(stove.Lit);

        StepFor(stove, 90f, ambientC: 5f);
        Assert.True(stove.Flaming);
        Assert.True(stove.Heat01 > 0.3f);
        Assert.Equal(0, stove.RagCount);
    }

    [Fact]
    public void FireBurnsDownCleanlyToGlowingEmbersAndOut()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(0.5f);
        stove.AddWood(2 * CabooseStoveModel.PieceMass);
        Assert.True(stove.AddRag(RagFuel.OilCapacity, lit: true));

        // Let wood catch and burn.
        StepFor(stove, 150f, ambientC: 5f);
        Assert.True(stove.Flaming);

        // Run until wood is consumed and glowing embers remain.
        var sawEmbersOnly = false;
        for (var t = 0f; t < 2400f; t += 0.2f)
        {
            stove.Step(0.2f, 5f);
            if (!stove.Flaming && stove.GlowingEmbers)
            {
                sawEmbersOnly = true;
                Assert.True(stove.Lit);
                Assert.True(stove.Ember01 > 0f);
                break;
            }
        }
        Assert.True(sawEmbersOnly, "Expected wood fire to die down to glowing embers before going out");

        // Continue until embers finish burning out completely and caboose cools back to ambient.
        StepFor(stove, 1500f, ambientC: 5f, dt: 0.25f);
        Assert.False(stove.Lit);
        Assert.False(stove.Flaming);
        Assert.False(stove.GlowingEmbers);
        Assert.Equal(0f, stove.WoodKg, 4);
        Assert.Equal(0f, stove.CharKg, 4);
        Assert.True(stove.Heat01 < 0.02f);
        Assert.True(stove.Smoke01 < 0.02f);
        Assert.InRange(stove.WarmthC, 4.9f, 5.5f);
    }

    [Fact]
    public void RefuelingOnLiveFireOrGlowingEmbersCatchesWithoutANewRag()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(0.5f);
        stove.AddWood(2 * CabooseStoveModel.PieceMass);
        stove.AddRag(RagFuel.OilCapacity, lit: true);

        // Burn down until all wood is gone and only glowing embers remain.
        for (var t = 0f; t < 2400f && !(stove.WoodKg <= 0f && stove.GlowingEmbers); t += 0.2f)
            stove.Step(0.2f, 10f);

        Assert.Equal(0f, stove.WoodKg, 4);
        Assert.True(stove.GlowingEmbers);

        // Refuel with fresh firewood on the glowing embers — no rag needed.
        var added = stove.AddWood(3 * CabooseStoveModel.PieceMass);
        Assert.Equal(3 * CabooseStoveModel.PieceMass, added, 4);
        Assert.Equal(0f, stove.RagOil, 5);

        StepFor(stove, 120f, ambientC: 10f);
        Assert.True(stove.Flaming, "Fresh firewood laid on glowing embers should catch without a new rag");
        Assert.True(stove.Heat01 > 0.35f);

        // Capacity clamps cleanly at MaxWoodKg.
        stove.AddWood(100f);
        Assert.True(stove.FuelKg <= CabooseStoveModel.MaxWoodKg + 1e-4f);
        Assert.False(stove.CanAddWood());
    }

    [Fact]
    public void DoorOpeningIncreasesDraftAndBurnRateComparedToShutDoor()
    {
        CabooseStoveModel RunWithDoor(float door)
        {
            var stove = new CabooseStoveModel();
            stove.SetDoorOpening(door);
            stove.AddWood(6 * CabooseStoveModel.PieceMass);
            stove.AddRag(RagFuel.OilCapacity, lit: true);
            StepFor(stove, 300f, ambientC: 10f);
            return stove;
        }

        var shut = RunWithDoor(0f);
        var open = RunWithDoor(1f);

        Assert.False(shut.DoorOpen);
        Assert.True(open.DoorOpen);
        Assert.True(shut.Flaming, "Stove should still burn steadily with the door shut");
        Assert.True(open.Flaming);
        Assert.True(open.WoodKg < shut.WoodKg,
            $"Open door should burn faster than shut door (open {open.WoodKg:F2} kg left vs shut {shut.WoodKg:F2} kg)");
    }

    [Fact]
    public void SaveRestoreAndSnapshotMirrorRoundTripCleanly()
    {
        var stove = new CabooseStoveModel();
        stove.SetDoorOpening(0.8f);
        stove.AddWood(4 * CabooseStoveModel.PieceMass);
        stove.AddRag(RagFuel.OilCapacity, lit: true);
        StepFor(stove, 140f, ambientC: 2f);

        var saved = stove.Save();
        Assert.Equal(CabooseStoveModel.SaveStateLength, saved.Length);

        var restored = new CabooseStoveModel();
        restored.Restore(saved, ambient: 2f);

        Assert.Equal(stove.Lit, restored.Lit);
        Assert.Equal(stove.WoodKg, restored.WoodKg, 4);
        Assert.Equal(stove.CharKg, restored.CharKg, 4);
        Assert.Equal(stove.Heat01, restored.Heat01, 4);
        Assert.Equal(stove.Smoke01, restored.Smoke01, 4);
        Assert.Equal(stove.WarmthC, restored.WarmthC, 3);
        Assert.Equal(stove.DoorOpening, restored.DoorOpening, 4);
        Assert.Equal(stove.DoorOpen, restored.DoorOpen);

        // Multiplayer snapshot mirror round-trip.
        var snap = stove.Snapshot();
        var client = new CabooseStoveModel();
        client.Mirror(snap);

        Assert.Equal(stove.Lit, client.Lit);
        Assert.Equal(stove.Flaming, client.Flaming);
        Assert.Equal(stove.WoodKg, client.WoodKg, 4);
        Assert.Equal(stove.Heat01, client.Heat01, 4);
        Assert.Equal(stove.Smoke01, client.Smoke01, 4);
        Assert.Equal(stove.Flame01, client.Flame01, 4);
        Assert.Equal(stove.Ember01, client.Ember01, 4);
        Assert.Equal(stove.WarmthC, client.WarmthC, 3);
        Assert.Equal(stove.DoorOpening, client.DoorOpening, 4);

        // Corrupt / NaN / null save data resets safely to cold empty stove.
        restored.Restore(null, ambient: 12f);
        Assert.False(restored.Lit);
        Assert.Equal(0f, restored.WoodKg);
        Assert.Equal(12f, restored.WarmthC, 2);

        saved[0] = float.NaN;
        restored.Restore(saved, ambient: 12f);
        Assert.False(restored.Lit);
        Assert.Equal(0f, restored.WoodKg);

        // Named-field snapshot Restore round-trips and rejects NaN/Infinity.
        var fromSnap = new CabooseStoveModel();
        Assert.True(fromSnap.Restore(snap, ambient: 2f));
        Assert.Equal(stove.Lit, fromSnap.Lit);
        Assert.Equal(stove.WoodKg, fromSnap.WoodKg, 4);
        Assert.Equal(stove.CharKg, fromSnap.CharKg, 4);
        Assert.Equal(stove.Heat01, fromSnap.Heat01, 4);
        Assert.Equal(stove.Smoke01, fromSnap.Smoke01, 4);
        Assert.Equal(stove.WarmthC, fromSnap.WarmthC, 3);
        Assert.Equal(stove.DoorOpening, fromSnap.DoorOpening, 4);

        // CabooseStoveSnapshot explicitly maps every StarterFuel state slot.
        var viaArray = new CabooseStoveModel();
        viaArray.Restore(stove.Save(), ambient: 2f);
        var starterSaved = viaArray.Fuel.Save();
        Assert.Equal(StarterFuel.StateLength, starterSaved.Length);
        Assert.Equal(10, StarterFuel.StateLength);
        Assert.Equal(starterSaved, client.Fuel.Save());
        Assert.Equal(starterSaved, fromSnap.Fuel.Save());

        var badSnap = snap;
        badSnap.Heat01 = float.NaN;
        Assert.False(fromSnap.Restore(badSnap, ambient: 14f));
        Assert.False(fromSnap.Lit);
        Assert.Equal(0f, fromSnap.WoodKg);
        Assert.Equal(14f, fromSnap.WarmthC, 2);

        badSnap = snap;
        badSnap.Wood = float.PositiveInfinity;
        Assert.False(fromSnap.Restore(badSnap, ambient: 14f));
        Assert.False(fromSnap.Lit);
    }

    [Fact]
    public void StatusTextDescribesEachStoveStateClearly()
    {
        var stove = new CabooseStoveModel();
        Assert.Contains("cold", stove.StatusText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("door shut", stove.StatusText(), StringComparison.OrdinalIgnoreCase);

        stove.SetDoorOpening(1f);
        Assert.Contains("door open", stove.StatusText(), StringComparison.OrdinalIgnoreCase);

        stove.AddWood(2 * CabooseStoveModel.PieceMass);
        Assert.Contains("unlit", stove.StatusText(false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lighter", stove.StatusText(true), StringComparison.OrdinalIgnoreCase);

        stove.AddRag(RagFuel.OilCapacity, lit: true);
        Assert.Contains("catching", stove.StatusText(), StringComparison.OrdinalIgnoreCase);

        StepFor(stove, 120f, ambientC: 5f);
        Assert.Contains("burning", stove.StatusText(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("°C", stove.StatusText(), StringComparison.OrdinalIgnoreCase);
    }
}
