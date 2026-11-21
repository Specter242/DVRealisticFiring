using System.Collections.Generic;
using RealisticStackEffects.Core;
using Xunit;

namespace RealisticStackEffects.Core.Tests;

 public class CarPlumesTests
{
    private sealed class FakePlume
    {
        public string Name { get; }
        public bool Active { get; set; } = true;
        public bool HasVents { get; set; }
        public bool HasSorting { get; set; }
        public bool CanHostVents { get; set; } = true;
        public bool Alive { get; set; } = true;
        public float PlumeTop { get; set; }
        public List<string> ActiveRenderers { get; } = new List<string>();
        public string? EffectsRoot { get; set; }

        public FakePlume(string name) => Name = name;
    }

    private sealed class FakeReader
    {
        public string? SmokeParent { get; set; }
        public string? EmberParent { get; set; }
    }

    private static bool IsActive(FakePlume p) => p.Active;
    private static bool HasVents(FakePlume p) => p.HasVents;
    private static bool HasSorting(FakePlume p) => p.HasSorting;
    private static bool CanHost(FakePlume p) => p.Active && p.CanHostVents;

    [Fact]
    public void RegistersMultipleStacksOnOneCarWithoutOverwritingAndIgnoresDuplicates()
    {
        var car = new CarPlumes<FakePlume>();
        var front = new FakePlume("front");
        var rear = new FakePlume("rear");

        Assert.True(car.Register(front));
        Assert.True(car.Register(rear));
        Assert.False(car.Register(front));
        Assert.False(car.Register(null!));

        Assert.Equal(2, car.Count);
        Assert.Same(front, car.All[0]);
        Assert.Same(rear, car.All[1]);
    }

    [Fact]
    public void AllowsOnlyOneActivePlumePerCarToOwnVents()
    {
        var car = new CarPlumes<FakePlume>();
        var front = new FakePlume("front");
        var rear = new FakePlume("rear");
        var third = new FakePlume("third");
        car.Register(front);
        car.Register(rear);
        car.Register(third);

        Assert.True(car.CanOwnVents(front, IsActive, HasVents));
        front.HasVents = true;

        // Secondary stacks cannot create duplicate SteamVents while the primary owns them.
        Assert.False(car.CanOwnVents(rear, IsActive, HasVents));
        Assert.False(car.CanOwnVents(third, IsActive, HasVents));

        Assert.True(car.CanOwnVents(front, IsActive, HasVents));
    }

    [Fact]
    public void InactivePlumeCannotClaimOrBlockVentsOwnership()
    {
        var car = new CarPlumes<FakePlume>();
        var first = new FakePlume("first") { Active = false, HasVents = true };
        var second = new FakePlume("second") { Active = true };
        car.Register(first);
        car.Register(second);

        Assert.False(car.CanOwnVents(first, IsActive, HasVents));
        Assert.True(car.CanOwnVents(second, IsActive, HasVents));
    }

    [Fact]
    public void PrimaryPrefersActiveVentsOwnerRegardlessOfRegistrationOrder()
    {
        var car = new CarPlumes<FakePlume>();
        var first = new FakePlume("first") { Active = true, HasVents = false };
        var second = new FakePlume("second") { Active = true, HasVents = true };
        var third = new FakePlume("third") { Active = true, HasVents = false };
        car.Register(first);
        car.Register(second);
        car.Register(third);

        Assert.Same(second, car.Primary(IsActive, HasVents));
        Assert.Same(second, CarPlumes.SelectPrimary(new[] { first, second, third }, IsActive, HasVents));

        // Falls back to the first active plume when none owns vents.
        second.HasVents = false;
        Assert.Same(first, car.Primary(IsActive, HasVents));

        // Skips inactive plumes when falling back.
        first.Active = false;
        Assert.Same(second, car.Primary(IsActive, HasVents));

        // Returns null when no active plume remains.
        second.Active = false;
        third.Active = false;
        Assert.Null(car.Primary(IsActive, HasVents));
    }

    [Fact]
    public void ReleaseOfPrimaryVentsOwnerTriggersOneShotHandoffToSurvivingStack()
    {
        var car = new CarPlumes<FakePlume>();
        var front = new FakePlume("front") { HasVents = true };
        var rear = new FakePlume("rear");
        var aux = new FakePlume("aux");
        car.Register(front);
        car.Register(rear);
        car.Register(aux);

        // Removing a non-owning secondary stack does not disturb ownership or trigger handoff.
        Assert.True(car.Remove(aux, ownedVents: false));
        Assert.False(car.NeedsVentsHandoff);
        Assert.Same(front, car.Primary(IsActive, HasVents));

        car.Register(aux);

        front.Active = false;
        front.HasVents = false;
        Assert.True(car.Remove(front, ownedVents: true));
        Assert.True(car.NeedsVentsHandoff);
        Assert.Equal(2, car.Count);

        // Duplicate removal (e.g., OnDestroy after manual Release) is idempotent.
        Assert.False(car.Remove(front, ownedVents: true));

        // First active survivor consumes the handoff once; the second does not duplicate it.
        Assert.True(car.TryConsumeVentsHandoff(rear, IsActive, HasVents, CanHost));
        Assert.False(car.NeedsVentsHandoff);
        rear.HasVents = true;

        Assert.False(car.TryConsumeVentsHandoff(aux, IsActive, HasVents, CanHost));
        Assert.False(car.CanOwnVents(aux, IsActive, HasVents));
        Assert.Same(rear, car.Primary(IsActive, HasVents));
    }

    [Fact]
    public void ReleaseOfLastPlumeClearsPendingHandoff()
    {
        var car = new CarPlumes<FakePlume>();
        var front = new FakePlume("front") { HasVents = true };
        var rear = new FakePlume("rear");
        car.Register(front);
        car.Register(rear);

        // Whole car teardown: primary releases first, then secondary releases before any update runs.
        Assert.True(car.Remove(front, ownedVents: true));
        Assert.True(car.NeedsVentsHandoff);

        Assert.True(car.Remove(rear, ownedVents: false));
        Assert.Equal(0, car.Count);
        Assert.False(car.NeedsVentsHandoff);
        Assert.Null(car.Primary(IsActive, HasVents));
    }

    [Fact]
    public void HandoffYieldsFromIncapableStackToVentsCapableStack()
    {
        var car = new CarPlumes<FakePlume>();
        var primary = new FakePlume("primary") { HasVents = true };
        var stockOnly = new FakePlume("stockOnly") { CanHostVents = false };
        var custom = new FakePlume("custom") { CanHostVents = true };
        car.Register(primary);
        car.Register(stockOnly);
        car.Register(custom);

        Assert.True(car.Remove(primary, ownedVents: true));
        Assert.True(car.NeedsVentsHandoff);

        // Stock-only stack yields so the custom stack can take over SteamVents.
        Assert.False(car.TryConsumeVentsHandoff(stockOnly, IsActive, HasVents, CanHost));
        Assert.True(car.NeedsVentsHandoff);

        // Custom stack takes over cleanly.
        Assert.True(car.TryConsumeVentsHandoff(custom, IsActive, HasVents, CanHost));
        Assert.False(car.NeedsVentsHandoff);
    }

    [Fact]
    public void HandoffStillCompletesWhenNoSurvivorCanHostVents()
    {
        // When EngineSteam is off, no stack can host SteamVents, yet a survivor
        // still consumes the handoff once (e.g., to take over stock steam sorting)
        // and clears the flag so it never loops.
        var car = new CarPlumes<FakePlume>();
        var primary = new FakePlume("primary") { CanHostVents = false };
        var secondary = new FakePlume("secondary") { CanHostVents = false };
        car.Register(primary);
        car.Register(secondary);

        Assert.True(car.Remove(primary, ownedVents: true));
        Assert.True(car.NeedsVentsHandoff);

        Assert.True(car.TryConsumeVentsHandoff(secondary, IsActive, HasVents, CanHost));
        Assert.False(car.NeedsVentsHandoff);
        Assert.False(car.TryConsumeVentsHandoff(secondary, IsActive, HasVents, CanHost));
    }

    [Fact]
    public void HandoffCancelsIfAnotherStackAlreadyAcquiredVents()
    {
        var car = new CarPlumes<FakePlume>();
        var front = new FakePlume("front") { HasVents = true };
        var rear = new FakePlume("rear");
        var third = new FakePlume("third");
        car.Register(front);
        car.Register(rear);
        car.Register(third);

        car.Remove(front, ownedVents: true);
        Assert.True(car.NeedsVentsHandoff);

        // Suppose rear rebound via Attach before third's Update ran.
        rear.HasVents = true;
        Assert.False(car.TryConsumeVentsHandoff(third, IsActive, HasVents, CanHost));
        Assert.False(car.NeedsVentsHandoff);
    }

    [Fact]
    public void TryGetMaxReturnsTallestActivePlumeTopAcrossStacks()
    {
        var car = new CarPlumes<FakePlume>();
        Assert.False(car.TryGetMax(IsActive, p => p.PlumeTop, out _));

        var front = new FakePlume("front") { PlumeTop = 8f };
        var rear = new FakePlume("rear") { PlumeTop = 19.5f };
        var inactive = new FakePlume("inactive") { Active = false, PlumeTop = 40f };
        var nanStack = new FakePlume("nan") { PlumeTop = float.NaN };
        car.Register(front);
        car.Register(rear);
        car.Register(inactive);
        car.Register(nanStack);

        Assert.True(car.TryGetMax(IsActive, p => p.PlumeTop, out var max));
        Assert.Equal(19.5f, max, 3);
    }

    [Fact]
    public void PruneDropsDeadPlumesAndClearsHandoffWhenEmpty()
    {
        var car = new CarPlumes<FakePlume>();
        var a = new FakePlume("a") { Alive = false };
        var b = new FakePlume("b") { Alive = true };
        car.Register(a);
        car.Register(b);

        Assert.Equal(1, car.Prune(p => p.Alive));
        Assert.Equal(1, car.Count);
        Assert.Same(b, car.All[0]);

        b.Alive = false;
        Assert.Equal(1, car.Prune(p => p.Alive));
        Assert.Equal(0, car.Count);
        Assert.False(car.NeedsVentsHandoff);
    }

    [Fact]
    public void SelectPrimaryHandlesNullOrEmptyCandidatesCleanly()
    {
        Assert.Null(CarPlumes.SelectPrimary<FakePlume>(null!, IsActive, HasVents));
        Assert.Null(CarPlumes.SelectPrimary(new List<FakePlume>(), IsActive, HasVents));
        Assert.Null(CarPlumes.SelectPrimary(new FakePlume[] { null! }, IsActive, HasVents));
    }

    [Fact]
    public void SplitOwnershipSortingHandoffPreservesExistingVents()
    {
        // Stack A acquired stock steam sorting while EngineSteam was off; later
        // EngineSteam was enabled and Stack B created SteamVents while A kept
        // sorting. When A releases, B must consume the sorting handoff without
        // triggering a vents handoff or losing its existing SteamVents.
        var car = new CarPlumes<FakePlume>();
        var stackA = new FakePlume("A") { HasSorting = true, HasVents = false };
        var stackB = new FakePlume("B") { HasSorting = false, HasVents = true };
        car.Register(stackA);
        car.Register(stackB);

        Assert.False(car.CanOwnSorting(stackB, IsActive, HasSorting));
        Assert.False(car.CanOwnVents(stackA, IsActive, HasVents));

        stackA.Active = false;
        stackA.HasSorting = false;
        Assert.True(car.Remove(stackA, ownedVents: false, ownedSorting: true));
        Assert.False(car.NeedsVentsHandoff);
        Assert.True(car.NeedsSortingHandoff);
        Assert.True(car.NeedsAnyHandoff);

        Assert.False(car.TryConsumeVentsHandoff(stackB, IsActive, HasVents, CanHost));
        Assert.True(car.TryConsumeSortingHandoff(stackB, IsActive, HasSorting, HasVents));
        Assert.False(car.NeedsSortingHandoff);
        Assert.False(car.NeedsAnyHandoff);

        Assert.True(car.CanOwnSorting(stackB, IsActive, HasSorting));
        stackB.HasSorting = true;

        // Stack B keeps its existing vents and now owns sorting too.
        Assert.True(stackB.HasVents);
        Assert.True(stackB.HasSorting);
        Assert.Same(stackB, car.Primary(IsActive, HasVents));
    }

    [Fact]
    public void SplitOwnershipSortingHandoffYieldsToExistingVentsOwner()
    {
        var car = new CarPlumes<FakePlume>();
        var stackA = new FakePlume("A") { HasSorting = true, HasVents = false };
        var stackB = new FakePlume("B") { HasSorting = false, HasVents = true };
        var stackC = new FakePlume("C") { HasSorting = false, HasVents = false };
        car.Register(stackA);
        car.Register(stackB);
        car.Register(stackC);

        stackA.Active = false;
        stackA.HasSorting = false;
        Assert.True(car.Remove(stackA, ownedVents: false, ownedSorting: true));
        Assert.True(car.NeedsSortingHandoff);

        Assert.False(car.TryConsumeSortingHandoff(stackC, IsActive, HasSorting, HasVents));
        Assert.True(car.NeedsSortingHandoff);

        // Stack B takes sorting while keeping its existing vents.
        Assert.True(car.TryConsumeSortingHandoff(stackB, IsActive, HasSorting, HasVents));
        Assert.False(car.NeedsSortingHandoff);
        Assert.True(stackB.HasVents);
    }

    [Fact]
    public void ReverseSplitOwnershipVentsHandoffPreservesSortingOwner()
    {
        var car = new CarPlumes<FakePlume>();
        var stackA = new FakePlume("A") { HasVents = true, HasSorting = false };
        var stackB = new FakePlume("B") { HasVents = false, HasSorting = true };
        car.Register(stackA);
        car.Register(stackB);

        stackA.Active = false;
        stackA.HasVents = false;
        Assert.True(car.Remove(stackA, ownedVents: true, ownedSorting: false));
        Assert.True(car.NeedsVentsHandoff);
        Assert.False(car.NeedsSortingHandoff);

        Assert.True(car.TryConsumeVentsHandoff(stackB, IsActive, HasVents, CanHost));
        Assert.False(car.TryConsumeSortingHandoff(stackB, IsActive, HasSorting, HasVents));
        stackB.HasVents = true;

        Assert.False(car.NeedsAnyHandoff);
        Assert.True(stackB.HasVents);
        Assert.True(stackB.HasSorting);
    }

    [Fact]
    public void SingleOwnerHandoffTransfersBothVentsAndSortingToCapableSurvivor()
    {
        var car = new CarPlumes<FakePlume>();
        var primary = new FakePlume("primary") { HasVents = true, HasSorting = true };
        var stockOnly = new FakePlume("stockOnly") { CanHostVents = false };
        var custom = new FakePlume("custom") { CanHostVents = true };
        car.Register(primary);
        car.Register(stockOnly);
        car.Register(custom);

        primary.Active = false;
        primary.HasVents = false;
        primary.HasSorting = false;
        Assert.True(car.Remove(primary, ownedVents: true, ownedSorting: true));
        Assert.True(car.NeedsVentsHandoff);
        Assert.True(car.NeedsSortingHandoff);

        // Stock-only survivor updates first: yields vents to custom, and while vents
        // handoff is still pending yields sorting to the vents-capable survivor too.
        Assert.False(car.TryConsumeVentsHandoff(stockOnly, IsActive, HasVents, CanHost));
        Assert.True(car.NeedsVentsHandoff);
        Assert.False(car.TryConsumeSortingHandoff(stockOnly, IsActive, HasSorting, CanHost));
        Assert.True(car.NeedsSortingHandoff);

        // Custom survivor claims both handoffs in a single takeover.
        Assert.True(car.TryConsumeVentsHandoff(custom, IsActive, HasVents, CanHost));
        Assert.True(car.TryConsumeSortingHandoff(custom, IsActive, HasSorting));
        Assert.False(car.NeedsAnyHandoff);
        custom.HasVents = true;
        custom.HasSorting = true;

        // Clearing the final survivors resets both flags.
        Assert.True(car.Remove(custom, ownedVents: true, ownedSorting: true));
        Assert.True(car.NeedsVentsHandoff);
        Assert.True(car.NeedsSortingHandoff);
        Assert.True(car.Remove(stockOnly, ownedVents: false, ownedSorting: false));
        Assert.False(car.NeedsAnyHandoff);
    }

    [Fact]
    public void ClearSortingHandoffAndPruneResetSortingState()
    {
        var car = new CarPlumes<FakePlume>();
        var a = new FakePlume("a") { HasSorting = true };
        var b = new FakePlume("b");
        car.Register(a);
        car.Register(b);

        Assert.True(car.Remove(a, ownedVents: false, ownedSorting: true));
        Assert.True(car.NeedsSortingHandoff);

        car.ClearSortingHandoff();
        Assert.False(car.NeedsSortingHandoff);

        // Re-trigger and verify Prune clears pending handoffs when roster empties.
        car.Register(a);
        Assert.True(car.Remove(a, ownedVents: true, ownedSorting: true));
        Assert.True(car.NeedsVentsHandoff);
        Assert.True(car.NeedsSortingHandoff);

        b.Alive = false;
        Assert.Equal(1, car.Prune(p => p.Alive));
        Assert.False(car.NeedsVentsHandoff);
        Assert.False(car.NeedsSortingHandoff);
    }

    [Fact]
    public void CollectStackRenderersExcludesSiblingReadersAndInactiveFxWhileKeepingEngineSteam()
    {
        var stackA = new FakePlume("A") { EffectsRoot = "fxA" };
        stackA.ActiveRenderers.Add("cloudA");

        var stackB = new FakePlume("B") { EffectsRoot = "fxB" };
        stackB.ActiveRenderers.Add("cloudB");

        var readerA = new FakeReader { SmokeParent = "smokeA", EmberParent = "emberA" };
        var readerB = new FakeReader { SmokeParent = "smokeB", EmberParent = "emberB" };
        var unregisteredC = new FakeReader { SmokeParent = "smokeC", EmberParent = "emberC" };

        var tree = new Dictionary<string, string[]>
        {
            ["fxA"] = new[] { "cloudA", "inactiveHeatA" },
            ["fxB"] = new[] { "cloudB", "inactiveHeatB" },
            ["smokeA"] = new[] { "stockSmokeA", "cloudA", "inactiveHeatA" },
            ["emberA"] = new[] { "stockEmberA" },
            ["smokeB"] = new[] { "stockSmokeB" },
            ["emberB"] = new[] { "stockEmberB" },
            ["smokeC"] = new[] { "unregisteredStockSmokeC" },
            ["emberC"] = new[] { "unregisteredStockEmberC" },
        };

        var excluded = CarPlumes.CollectStackRenderers(
            stackA.ActiveRenderers,
            new[] { stackA, stackB },
            new[] { readerA, readerB, unregisteredC },
            p => p.ActiveRenderers,
            p => p.EffectsRoot,
            r => r.SmokeParent,
            r => r.EmberParent,
            key => key != null && tree.TryGetValue(key, out var items) ? items : System.Array.Empty<string>());

        Assert.Contains("cloudA", excluded);
        Assert.Contains("inactiveHeatA", excluded);
        Assert.Contains("cloudB", excluded);
        Assert.Contains("inactiveHeatB", excluded);
        Assert.Contains("unregisteredStockSmokeC", excluded);
        Assert.Contains("unregisteredStockEmberC", excluded);
        Assert.Contains("stockSmokeA", excluded);
        Assert.Contains("stockEmberB", excluded);

        Assert.DoesNotContain("DynamoSteam", excluded);
        Assert.DoesNotContain("CylCockSteam", excluded);
        Assert.DoesNotContain("SteamSafetyRelease", excluded);
    }
}
