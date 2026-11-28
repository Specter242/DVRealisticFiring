using System.Collections.Generic;
using RealisticFiring.Core;

namespace RealisticFiring.Core.Tests;

/// <summary>
/// Fleet-wide vs per-locomotive career upgrade resolution, prerequisites,
/// multi-locomotive independence, save migration, and the kiosk's owned-locomotive list.
/// </summary>
public class UpgradeLedgerTests
{
    private const string LocoA = "11111111-1111-1111-1111-111111111111";
    private const string LocoB = "22222222-2222-2222-2222-222222222222";
    private const string LocoC = "33333333-3333-3333-3333-333333333333";

    [Fact]
    public void FreshCareerHasNothingFittedInEitherScopeAndSandboxHasEverything()
    {
        var ledger = new UpgradeLedger();
        Assert.False(ledger.HasAnyBought);
        Assert.Equal(UpgradeMask.None, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoA));
        Assert.Equal(UpgradeMask.None, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        // Sandbox: every improvement, and the engine as it came (no oil conversion).
        Assert.Equal(UpgradeMask.All & ~UpgradeKeys.Alternatives, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: false, LocoA));
        Assert.Equal(UpgradeMask.All & ~UpgradeKeys.Alternatives, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: false, LocoA));
    }

    [Fact]
    public void OilConversionIsAnAlternativeLeftOffInSandboxUntilBoughtOrAlwaysFitted()
    {
        var ledger = new UpgradeLedger();
        Assert.False(ledger.IsFitted(UpgradeMask.S282OilBurner, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: false, LocoA));
        Assert.False(ledger.IsFitted(UpgradeMask.S282OilBurner, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.S282OilBurner, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.IsFitted(UpgradeMask.S282OilBurner, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.False(ledger.IsFitted(UpgradeMask.S282OilBurner, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));
        var always = new UpgradeRulesSet(
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer, s282OilBurner: FittingRule.AlwaysFitted);
        Assert.True(ledger.IsFitted(UpgradeMask.S282OilBurner, always, UpgradeScope.FleetWide, inCareer: false, LocoB));
        Assert.Equal(UpgradeMask.S282OilBurner, UpgradeKeys.Alternatives);
        Assert.True(UpgradeKeys.Allows("LocoS282A", UpgradeMask.S282OilBurner));
        Assert.False(UpgradeKeys.Allows("LocoS060", UpgradeMask.S282OilBurner));
    }

    [Fact]
    public void FleetWidePurchaseFitsEveryLocomotive()
    {
        var ledger = new UpgradeLedger();
        Assert.True(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.FleetWide));
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.FleetWide));
        Assert.True(ledger.Buy(UpgradeMask.S282Stoker, UpgradeScope.FleetWide));

        Assert.True(ledger.IsFitted(UpgradeMask.BrickArch, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoA));
        Assert.True(ledger.IsFitted(UpgradeMask.BrickArch, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoB));
        Assert.True(ledger.IsFitted(UpgradeMask.S282Stoker, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, null));
        Assert.False(ledger.IsFitted(UpgradeMask.S282Booster, UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoA));
    }

    [Fact]
    public void PerLocomotivePurchasesStayIndependentAcrossEngines()
    {
        var ledger = new UpgradeLedger();
        Assert.True(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.S282Stoker, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.S282FeedPump, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.S282Booster, UpgradeScope.PerLocomotive, LocoA));

        Assert.True(ledger.Buy(UpgradeMask.AutomaticFireDoor, UpgradeScope.PerLocomotive, LocoB));
        Assert.True(ledger.Buy(UpgradeMask.SniftingValves, UpgradeScope.PerLocomotive, LocoB));

        Assert.True(ledger.Buy(UpgradeMask.DriftingValve, UpgradeScope.PerLocomotive, LocoC));
        Assert.True(ledger.Buy(UpgradeMask.ByPassValves, UpgradeScope.PerLocomotive, LocoC));

        var maskA = ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA);
        var maskB = ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB);
        var maskC = ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoC);
        var maskNone = ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, null);

        Assert.Equal(UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.S282Stoker | UpgradeMask.S282FeedPump | UpgradeMask.S282Booster, maskA);
        Assert.Equal(UpgradeMask.AutomaticFireDoor | UpgradeMask.SniftingValves, maskB);
        Assert.Equal(UpgradeMask.DriftingValve | UpgradeMask.ByPassValves, maskC);
        Assert.Equal(UpgradeMask.None, maskNone);
    }

    [Fact]
    public void OverFireJetsRequireBrickArchOnTheSameLocomotive()
    {
        var ledger = new UpgradeLedger();
        // Loco A has arch; Loco B has jets recorded without arch (e.g. edited save).
        ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA);
        ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.PerLocomotive, LocoB);

        Assert.True(ledger.IsFitted(UpgradeMask.BrickArch, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.False(ledger.IsFitted(UpgradeMask.OverFireJets, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.False(ledger.IsFitted(UpgradeMask.BrickArch, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));
        Assert.False(ledger.IsFitted(UpgradeMask.OverFireJets, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));

        // Even if jets are set AlwaysFitted while arch is BuyInCareer, Loco B still has no jets until it buys its own arch.
        var jetsAlways = new UpgradeRulesSet(
            FittingRule.BuyInCareer, FittingRule.AlwaysFitted, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer);
        Assert.True(ledger.IsFitted(UpgradeMask.OverFireJets, jetsAlways, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.False(ledger.IsFitted(UpgradeMask.OverFireJets, jetsAlways, UpgradeScope.PerLocomotive, inCareer: true, LocoB));

        // Buying the arch on Loco B immediately enables its jets.
        ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoB);
        Assert.True(ledger.IsFitted(UpgradeMask.OverFireJets, UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));
    }

    [Fact]
    public void AlwaysAndNeverRulesOverrideBoughtStateInBothScopes()
    {
        var ledger = new UpgradeLedger();
        ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA);
        ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.PerLocomotive, LocoA);
        ledger.Buy(UpgradeMask.S282Stoker, UpgradeScope.FleetWide);

        var rules = new UpgradeRulesSet(
            brickArch: FittingRule.NeverFitted,
            overFireJets: FittingRule.AlwaysFitted,
            automaticFireDoor: FittingRule.AlwaysFitted,
            sniftingValves: FittingRule.BuyInCareer,
            driftingValve: FittingRule.BuyInCareer,
            byPassValves: FittingRule.BuyInCareer,
            s282Stoker: FittingRule.NeverFitted,
            s282FeedPump: FittingRule.AlwaysFitted,
            s282Booster: FittingRule.BuyInCareer);

        // Arch is NeverFitted -> Jets cannot be fitted even when AlwaysFitted.
        var perLoco = ledger.Resolve(rules, UpgradeScope.PerLocomotive, inCareer: true, LocoA);
        Assert.Equal(UpgradeMask.AutomaticFireDoor | UpgradeMask.S282FeedPump, perLoco);

        // Fleet-wide stoker was bought, but NeverFitted disables it.
        var fleet = ledger.Resolve(rules, UpgradeScope.FleetWide, inCareer: true, LocoA);
        Assert.Equal(UpgradeMask.AutomaticFireDoor | UpgradeMask.S282FeedPump, fleet);
    }

    [Fact]
    public void SwitchingScopesPreservesBothFleetAndPerLocomotiveRecords()
    {
        var ledger = new UpgradeLedger();
        // Legacy career save had fleet-wide BrickArch and S282Booster.
        ledger.Load(UpgradeMask.BrickArch | UpgradeMask.S282Booster, null);
        Assert.True(ledger.IsBought(UpgradeMask.BrickArch, UpgradeScope.FleetWide));
        // Switching to PerLocomotive keeps prior fleet-wide purchases fitted on every engine.
        Assert.True(ledger.IsBought(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.IsBought(UpgradeMask.S282Booster, UpgradeScope.PerLocomotive, LocoB));
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA));

        // Player switches to PerLocomotive and upgrades LocoA with OverFireJets and Stoker.
        // OverFireJets works on LocoA because the prior fleet-wide BrickArch stays fitted.
        Assert.True(ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.PerLocomotive, LocoA));
        Assert.True(ledger.Buy(UpgradeMask.S282Stoker, UpgradeScope.PerLocomotive, LocoA));
        Assert.Equal(UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.S282Stoker | UpgradeMask.S282Booster,
            ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.Equal(UpgradeMask.BrickArch | UpgradeMask.S282Booster,
            ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));

        // Switching back to FleetWide keeps the original fleet-wide purchases intact and does not leak LocoA's upgrades.
        Assert.Equal(UpgradeMask.BrickArch | UpgradeMask.S282Booster,
            ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.FleetWide, inCareer: true, LocoB));

        // Reloading both sets together preserves everything without duplicating fleet flags per locomotive.
        var reloaded = new UpgradeLedger();
        reloaded.Load(ledger.FleetBought, ledger.LocomotiveBought);
        Assert.Equal(ledger.FleetBought, reloaded.FleetBought);
        Assert.Single(reloaded.LocomotiveBought);
        Assert.Equal(UpgradeMask.OverFireJets | UpgradeMask.S282Stoker, reloaded.LocomotiveBought[LocoA]);
    }

    [Fact]
    public void FleetToPerLocomotiveSwitchKeepsPriorFleetPurchasesAndScopesNewOnes()
    {
        var ledger = new UpgradeLedger();
        Assert.True(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.FleetWide));
        Assert.True(ledger.Buy(UpgradeMask.OverFireJets, UpgradeScope.FleetWide));
        Assert.True(ledger.Buy(UpgradeMask.S282Stoker, UpgradeScope.FleetWide));

        // Switching an existing career to PerLocomotive keeps all fleet-wide upgrades on every engine.
        var expectedFleet = UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.S282Stoker;
        Assert.Equal(expectedFleet, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.Equal(expectedFleet, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));
        Assert.Equal(expectedFleet, ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, null));

        // New purchases in PerLocomotive mode apply only to the selected locomotive.
        Assert.True(ledger.Buy(UpgradeMask.S282FeedPump, UpgradeScope.PerLocomotive, LocoA));
        Assert.Equal(expectedFleet | UpgradeMask.S282FeedPump,
            ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
        Assert.Equal(expectedFleet,
            ledger.Resolve(UpgradeRulesSet.Default, UpgradeScope.PerLocomotive, inCareer: true, LocoB));

        // NeverFitted rules still override prior fleet-wide purchases in PerLocomotive mode.
        var archNever = new UpgradeRulesSet(
            FittingRule.NeverFitted, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer);
        Assert.Equal(UpgradeMask.S282Stoker | UpgradeMask.S282FeedPump,
            ledger.Resolve(archNever, UpgradeScope.PerLocomotive, inCareer: true, LocoA));
    }

    [Fact]
    public void InvalidOrEmptyGuidsAreRejectedAndValidGuidsAreTrimmed()
    {
        var ledger = new UpgradeLedger();
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, null));
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, ""));
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, "   "));
        Assert.False(ledger.Buy(UpgradeMask.None, UpgradeScope.FleetWide));
        Assert.False(ledger.HasAnyBought);

        Assert.True(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, " " + LocoA + " "));
        Assert.True(ledger.IsBought(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA));
        Assert.False(ledger.Buy(UpgradeMask.BrickArch, UpgradeScope.PerLocomotive, LocoA));

        // Unknown high bits and blank GUIDs are dropped on Load.
        ledger.Load((UpgradeMask)0xFFFF, new[]
        {
            new KeyValuePair<string, UpgradeMask>("", UpgradeMask.BrickArch),
            new KeyValuePair<string, UpgradeMask>(LocoB, UpgradeMask.None),
            new KeyValuePair<string, UpgradeMask>(LocoC, (UpgradeMask)0xFFFF),
        });
        Assert.Equal(UpgradeMask.All, ledger.FleetBought);
        Assert.Single(ledger.LocomotiveBought);
        Assert.Equal(UpgradeMask.All, ledger.LocomotiveBought[LocoC]);
    }

    [Fact]
    public void LiveryEligibilityAndUpgradeCountsMatchStockEngines()
    {
        Assert.Equal(UpgradeMask.All, UpgradeKeys.AllowedForLivery("LocoS282A"));
        Assert.Equal(10, UpgradeKeys.Count(UpgradeKeys.AllowedForLivery("LocoS282A")));

        var s060 = UpgradeKeys.AllowedForLivery("LocoS060");
        Assert.Equal(6, UpgradeKeys.Count(s060));
        Assert.True((s060 & UpgradeMask.BrickArch) != 0);
        Assert.True((s060 & UpgradeMask.OverFireJets) != 0);
        Assert.True((s060 & UpgradeMask.AutomaticFireDoor) != 0);
        Assert.True((s060 & UpgradeMask.SniftingValves) != 0);
        Assert.True((s060 & UpgradeMask.DriftingValve) != 0);
        Assert.True((s060 & UpgradeMask.ByPassValves) != 0);
        Assert.Equal(UpgradeMask.None, s060 & (UpgradeMask.S282Stoker | UpgradeMask.S282FeedPump | UpgradeMask.S282Booster));

        Assert.Equal(UpgradeMask.None, UpgradeKeys.AllowedForLivery("LocoS282B"));
        Assert.Equal(UpgradeMask.None, UpgradeKeys.AllowedForLivery("Jk3A"));
        Assert.Equal(UpgradeMask.None, UpgradeKeys.AllowedForLivery("S464-730"));
        Assert.Equal(UpgradeMask.None, UpgradeKeys.AllowedForLivery(null));

        Assert.True(UpgradeKeys.IsEligibleLivery("LocoS282A"));
        Assert.True(UpgradeKeys.IsEligibleLivery("LocoS060"));
        Assert.False(UpgradeKeys.IsEligibleLivery("LocoS282B"));
        Assert.False(UpgradeKeys.IsEligibleLivery("S464-730"));
        Assert.False(UpgradeKeys.IsEligibleLivery(null));

        Assert.True(UpgradeKeys.Allows("LocoS282A", UpgradeMask.S282Stoker));
        Assert.False(UpgradeKeys.Allows("LocoS060", UpgradeMask.S282Stoker));
        Assert.True(UpgradeKeys.Allows("LocoS060", UpgradeMask.AutomaticFireDoor));
        Assert.False(UpgradeKeys.Allows("LocoS060", UpgradeMask.None));
    }

    [Fact]
    public void OwnedListKeepsEligibleEnginesOnceOrderedByCarId()
    {
        var list = OwnedLocomotivePicker.Order(new[]
        {
            new OwnedLocomotive("s282-b", "S282-310", "LocoS282A"),
            // Ineligible: a diesel, a tender, an engine without a GUID.
            new OwnedLocomotive("de2", "DE2-001", "LocoDE2"),
            new OwnedLocomotive("tender", "S282-310T", "LocoS282B"),
            new OwnedLocomotive(" ", "S282-999", "LocoS282A"),
            new OwnedLocomotive("s060", "S060-012", "LocoS060"),
            new OwnedLocomotive("s282-a", "S282-042", "LocoS282A"),
            // The same engine twice: listed once.
            new OwnedLocomotive("s060", "S060-012", "LocoS060"),
        });

        Assert.Equal(new[] { "S060-012", "S282-042", "S282-310" }, list.Select(e => e.Id));
        Assert.Empty(OwnedLocomotivePicker.Order(null));
    }

    [Fact]
    public void PickerStartsOnTheListAndGoesBackToTheEngineLeft()
    {
        var picker = new OwnedLocomotivePicker();
        Assert.True(picker.OnList);
        Assert.Null(picker.Picked);
        Assert.False(picker.Pick(0));

        picker.Refresh(new[]
        {
            new OwnedLocomotive("s282-a", "S282-042", "LocoS282A"),
            new OwnedLocomotive("s060", "S060-012", "LocoS060"),
        });
        Assert.True(picker.OnList);
        Assert.Equal(2, picker.Engines.Count);

        Assert.False(picker.Pick(2));
        Assert.True(picker.OnList);
        Assert.True(picker.Pick(1));
        Assert.False(picker.OnList);
        Assert.Equal("S282-042", picker.Picked?.Id);

        // A refresh that still owns the engine keeps it picked, wherever it now sorts.
        picker.Refresh(new[]
        {
            new OwnedLocomotive("s282-a", "S282-042", "LocoS282A"),
            new OwnedLocomotive("s060", "S060-012", "LocoS060"),
            new OwnedLocomotive("s282-z", "S282-999", "LocoS282A"),
        });
        Assert.Equal("s282-a", picker.PickedGuid);

        Assert.Equal(1, picker.Back());
        Assert.True(picker.OnList);
        Assert.Equal(-1, picker.Back());

        // An engine no longer owned while picked: back to the list.
        Assert.True(picker.Pick(2));
        picker.Refresh(new[] { new OwnedLocomotive("s060", "S060-012", "LocoS060") });
        Assert.True(picker.OnList);
        Assert.Null(picker.Picked);

        // None owned: an empty list, nothing to pick.
        picker.Refresh(new OwnedLocomotive[0]);
        Assert.Empty(picker.Engines);
        Assert.False(picker.Pick(0));
    }

    [Fact]
    public void WrappingVersionComparisonAcceptsNewerAndEqualAndRejectsOlder()
    {
        Assert.True(UpgradeLedger.VersionAtLeast(5, 5));
        Assert.True(UpgradeLedger.VersionAtLeast(6, 5));
        Assert.False(UpgradeLedger.VersionAtLeast(4, 5));
        Assert.True(UpgradeLedger.VersionAtLeast(0, 65535));
        Assert.True(UpgradeLedger.VersionAtLeast(2, 65534));
        Assert.False(UpgradeLedger.VersionAtLeast(65535, 0));
    }
}
