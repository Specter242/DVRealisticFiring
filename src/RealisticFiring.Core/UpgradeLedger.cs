using System;
using System.Collections.Generic;
using System.Linq;

namespace RealisticFiring.Core
{
    /// <summary>
    /// Whether career firebox/engine upgrades unlock across the entire fleet or
    /// per individual locomotive (keyed by persistent car GUID).
    /// </summary>
    public enum UpgradeScope
    {
        /// <summary>Buying an upgrade fits every eligible locomotive in the career.</summary>
        FleetWide,

        /// <summary>Buying an upgrade fits only the selected locomotive, kept across saves by its car GUID.</summary>
        PerLocomotive,
    }

    /// <summary>How a fitting's rule behaves in career and sandbox.</summary>
    public enum FittingRule
    {
        /// <summary>Bought at a career manager in a career; fitted in sandbox.</summary>
        BuyInCareer,

        /// <summary>Every eligible locomotive has it.</summary>
        AlwaysFitted,

        /// <summary>No locomotive has it.</summary>
        NeverFitted,
    }

    /// <summary>Bitmask of career firebox and engine upgrades.</summary>
    [Flags]
    public enum UpgradeMask : ushort
    {
        None = 0,
        BrickArch = 1 << 0,
        OverFireJets = 1 << 1,
        AutomaticFireDoor = 1 << 2,
        SniftingValves = 1 << 3,
        DriftingValve = 1 << 4,
        ByPassValves = 1 << 5,
        S282Stoker = 1 << 6,
        S282FeedPump = 1 << 7,
        S282Booster = 1 << 8,
        All = BrickArch | OverFireJets | AutomaticFireDoor | SniftingValves | DriftingValve | ByPassValves | S282Stoker | S282FeedPump | S282Booster,
    }

    /// <summary>Canonical save keys and livery eligibility for career upgrades.</summary>
    public static class UpgradeKeys
    {
        public const string BrickArch = "brickArch";
        public const string OverFireJets = "overFireJets";
        public const string S282Stoker = "s282Stoker";
        public const string S282FeedPump = "s282FeedPump";
        public const string S282Booster = "s282Booster";
        public const string SniftingValves = "sniftingValves";
        public const string DriftingValve = "driftingValve";
        public const string ByPassValves = "byPassValves";
        public const string AutomaticFireDoor = "automaticFireDoor";
        public const string Locomotives = "locomotives";

        public static readonly (UpgradeMask Flag, string Key)[] All =
        {
            (UpgradeMask.BrickArch, BrickArch),
            (UpgradeMask.OverFireJets, OverFireJets),
            (UpgradeMask.S282Stoker, S282Stoker),
            (UpgradeMask.S282FeedPump, S282FeedPump),
            (UpgradeMask.S282Booster, S282Booster),
            (UpgradeMask.SniftingValves, SniftingValves),
            (UpgradeMask.DriftingValve, DriftingValve),
            (UpgradeMask.ByPassValves, ByPassValves),
            (UpgradeMask.AutomaticFireDoor, AutomaticFireDoor),
        };

        /// <summary>Upgrades shared by both the stock S282 and S060.</summary>
        public const UpgradeMask StockSteamShared =
            UpgradeMask.BrickArch | UpgradeMask.OverFireJets | UpgradeMask.AutomaticFireDoor
            | UpgradeMask.SniftingValves | UpgradeMask.DriftingValve | UpgradeMask.ByPassValves;

        /// <summary>Upgrades that can be fitted to <paramref name="liveryId"/>.</summary>
        public static UpgradeMask AllowedForLivery(string liveryId)
        {
            if (liveryId == "LocoS282A")
                return UpgradeMask.All;
            if (liveryId == "LocoS060")
                return StockSteamShared;
            return UpgradeMask.None;
        }

        /// <summary>Whether <paramref name="liveryId"/> is eligible for any career upgrade.</summary>
        public static bool IsEligibleLivery(string liveryId) => AllowedForLivery(liveryId) != UpgradeMask.None;

        /// <summary>Whether <paramref name="liveryId"/> can be fitted with <paramref name="upgrade"/>.</summary>
        public static bool Allows(string liveryId, UpgradeMask upgrade) =>
            upgrade != UpgradeMask.None && (AllowedForLivery(liveryId) & upgrade) == upgrade;

        /// <summary>Number of upgrade flags set in <paramref name="mask"/>.</summary>
        public static int Count(UpgradeMask mask)
        {
            var bits = (int)(mask & UpgradeMask.All);
            var count = 0;
            while (bits != 0)
            {
                bits &= bits - 1;
                count++;
            }
            return count;
        }
    }

    /// <summary>The nine per-upgrade rules from settings.</summary>
    public readonly struct UpgradeRulesSet
    {
        public static readonly UpgradeRulesSet Default = new UpgradeRulesSet(
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer,
            FittingRule.BuyInCareer, FittingRule.BuyInCareer, FittingRule.BuyInCareer);

        public UpgradeRulesSet(
            FittingRule brickArch,
            FittingRule overFireJets,
            FittingRule automaticFireDoor,
            FittingRule sniftingValves,
            FittingRule driftingValve,
            FittingRule byPassValves,
            FittingRule s282Stoker,
            FittingRule s282FeedPump,
            FittingRule s282Booster)
        {
            BrickArch = brickArch;
            OverFireJets = overFireJets;
            AutomaticFireDoor = automaticFireDoor;
            SniftingValves = sniftingValves;
            DriftingValve = driftingValve;
            ByPassValves = byPassValves;
            S282Stoker = s282Stoker;
            S282FeedPump = s282FeedPump;
            S282Booster = s282Booster;
        }

        public FittingRule BrickArch { get; }
        public FittingRule OverFireJets { get; }
        public FittingRule AutomaticFireDoor { get; }
        public FittingRule SniftingValves { get; }
        public FittingRule DriftingValve { get; }
        public FittingRule ByPassValves { get; }
        public FittingRule S282Stoker { get; }
        public FittingRule S282FeedPump { get; }
        public FittingRule S282Booster { get; }

        public FittingRule RuleFor(UpgradeMask upgrade)
        {
            switch (upgrade)
            {
                case UpgradeMask.BrickArch: return BrickArch;
                case UpgradeMask.OverFireJets: return OverFireJets;
                case UpgradeMask.AutomaticFireDoor: return AutomaticFireDoor;
                case UpgradeMask.SniftingValves: return SniftingValves;
                case UpgradeMask.DriftingValve: return DriftingValve;
                case UpgradeMask.ByPassValves: return ByPassValves;
                case UpgradeMask.S282Stoker: return S282Stoker;
                case UpgradeMask.S282FeedPump: return S282FeedPump;
                case UpgradeMask.S282Booster: return S282Booster;
                default: return FittingRule.BuyInCareer;
            }
        }
    }

    /// <summary>
    /// Tracks purchased upgrades in both fleet-wide and per-locomotive scopes
    /// side by side, so saves load older fleet-wide data cleanly and players
    /// can switch scopes without losing either set.
    /// </summary>
    public sealed class UpgradeLedger
    {
        private readonly Dictionary<string, UpgradeMask> locomotiveBought = new Dictionary<string, UpgradeMask>(StringComparer.Ordinal);

        /// <summary>Fleet-wide upgrades bought in this save.</summary>
        public UpgradeMask FleetBought { get; private set; }

        /// <summary>Per-locomotive upgrades bought in this save, keyed by persistent car GUID.</summary>
        public IReadOnlyDictionary<string, UpgradeMask> LocomotiveBought => locomotiveBought;

        /// <summary>Whether any upgrade (fleet-wide or per-locomotive) has been bought.</summary>
        public bool HasAnyBought => FleetBought != UpgradeMask.None || locomotiveBought.Count > 0;

        public void Clear()
        {
            FleetBought = UpgradeMask.None;
            locomotiveBought.Clear();
        }

        /// <summary>Replaces the ledger's contents from loaded save data.</summary>
        public void Load(UpgradeMask fleetBought, IEnumerable<KeyValuePair<string, UpgradeMask>> perLocomotive)
        {
            Clear();
            FleetBought = fleetBought & UpgradeMask.All;
            if (perLocomotive == null)
                return;
            foreach (var pair in perLocomotive)
            {
                var key = NormalizeGuid(pair.Key);
                var mask = pair.Value & UpgradeMask.All;
                if (key != null && mask != UpgradeMask.None)
                    locomotiveBought[key] = mask;
            }
        }

        /// <summary>
        /// The bought upgrade flags for the given <paramref name="scope"/> and <paramref name="carGuid"/>.
        /// In <see cref="UpgradeScope.PerLocomotive"/> mode, prior fleet-wide purchases stay included on every
        /// locomotive while new purchases apply only to the selected locomotive's GUID.
        /// </summary>
        public UpgradeMask BoughtMask(UpgradeScope scope, string carGuid = null)
        {
            if (scope == UpgradeScope.FleetWide)
                return FleetBought;
            var key = NormalizeGuid(carGuid);
            var perLoco = key != null && locomotiveBought.TryGetValue(key, out var mask) ? mask : UpgradeMask.None;
            return FleetBought | perLoco;
        }

        /// <summary>Whether <paramref name="upgrade"/> has been bought under <paramref name="scope"/> for <paramref name="carGuid"/>.</summary>
        public bool IsBought(UpgradeMask upgrade, UpgradeScope scope, string carGuid = null) =>
            upgrade != UpgradeMask.None && (BoughtMask(scope, carGuid) & upgrade) == upgrade;

        /// <summary>
        /// Records a purchase of <paramref name="upgrade"/> in <paramref name="scope"/>.
        /// In <see cref="UpgradeScope.PerLocomotive"/> mode, a non-empty <paramref name="carGuid"/> is required
        /// and upgrades already covered by <see cref="FleetBought"/> are not duplicated.
        /// Returns true if the ledger changed.
        /// </summary>
        public bool Buy(UpgradeMask upgrade, UpgradeScope scope, string carGuid = null)
        {
            var flags = upgrade & UpgradeMask.All;
            if (flags == UpgradeMask.None)
                return false;
            if (scope == UpgradeScope.FleetWide)
            {
                if ((FleetBought & flags) == flags)
                    return false;
                FleetBought |= flags;
                return true;
            }
            var key = NormalizeGuid(carGuid);
            if (key == null)
                return false;
            locomotiveBought.TryGetValue(key, out var current);
            var missing = flags & ~(FleetBought | current);
            if (missing == UpgradeMask.None)
                return false;
            locomotiveBought[key] = current | missing;
            return true;
        }

        /// <summary>Evaluates a single fitting rule without cross-upgrade prerequisites.</summary>
        public static bool Allowed(FittingRule rule, bool inCareer, bool bought)
        {
            switch (rule)
            {
                case FittingRule.AlwaysFitted:
                    return true;
                case FittingRule.NeverFitted:
                    return false;
                default:
                    return !inCareer || bought;
            }
        }

        /// <summary>Enforces fitting prerequisites: over-fire jets require the brick arch on the same locomotive.</summary>
        public static UpgradeMask EnforcePrerequisites(UpgradeMask mask)
        {
            mask &= UpgradeMask.All;
            if ((mask & UpgradeMask.BrickArch) == 0)
                mask &= ~UpgradeMask.OverFireJets;
            return mask;
        }

        /// <summary>Resolves every fitted upgrade for <paramref name="carGuid"/> under <paramref name="rules"/> and <paramref name="scope"/>.</summary>
        public UpgradeMask Resolve(UpgradeRulesSet rules, UpgradeScope scope, bool inCareer, string carGuid = null)
        {
            var bought = BoughtMask(scope, carGuid);
            var resolved = UpgradeMask.None;
            foreach (var (flag, _) in UpgradeKeys.All)
            {
                if (Allowed(rules.RuleFor(flag), inCareer, (bought & flag) == flag))
                    resolved |= flag;
            }
            return EnforcePrerequisites(resolved);
        }

        /// <summary>Whether <paramref name="upgrade"/> is fitted on <paramref name="carGuid"/> right now.</summary>
        public bool IsFitted(UpgradeMask upgrade, UpgradeRulesSet rules, UpgradeScope scope, bool inCareer, string carGuid = null) =>
            upgrade != UpgradeMask.None && (Resolve(rules, scope, inCareer, carGuid) & upgrade) == upgrade;

        /// <summary>
        /// True when <paramref name="incoming"/> is equal to or newer than <paramref name="current"/>
        /// in wrapping 16-bit sequence space (used to ignore stale unreliable mirror snapshots).
        /// </summary>
        public static bool VersionAtLeast(ushort incoming, ushort current) => (short)(incoming - current) >= 0;

        private static string NormalizeGuid(string carGuid)
        {
            if (string.IsNullOrWhiteSpace(carGuid))
                return null;
            return carGuid.Trim();
        }
    }

    /// <summary>An eligible locomotive the player owns, as the per-locomotive Upgrades list names it.</summary>
    public readonly struct OwnedLocomotive
    {
        public OwnedLocomotive(string guid, string id, string livery)
        {
            Guid = guid;
            Id = id;
            Livery = livery;
        }

        /// <summary>The car's persistent GUID, the per-locomotive ledger's key.</summary>
        public string Guid { get; }

        /// <summary>The car's ID, as the player reads it on the engine.</summary>
        public string Id { get; }

        public string Livery { get; }
    }

 public sealed class OwnedLocomotivePicker
    {
        private readonly List<OwnedLocomotive> engines = new List<OwnedLocomotive>();

        /// <summary>The list page: each owned eligible locomotive once, ordered by car ID.</summary>
        public IReadOnlyList<OwnedLocomotive> Engines => engines;

        /// <summary>The picked locomotive's GUID, or null on the list page.</summary>
        public string PickedGuid { get; private set; }

        /// <summary>Whether the screen shows the list rather than one locomotive's upgrades.</summary>
        public bool OnList => PickedGuid == null;

        /// <summary>The picked locomotive, or null on the list page.</summary>
        public OwnedLocomotive? Picked
        {
            get
            {
                var index = IndexOf(PickedGuid);
                return index >= 0 ? engines[index] : (OwnedLocomotive?)null;
            }
        }

        /// <summary>
        /// Replaces the list with <paramref name="owned"/>: eligible liveries with a GUID, each once,
        /// ordered by car ID. A picked locomotive no longer owned sends the screen back to the list.
        /// </summary>
        public void Refresh(IEnumerable<OwnedLocomotive> owned)
        {
            engines.Clear();
            engines.AddRange(Order(owned));
            if (IndexOf(PickedGuid) < 0)
                PickedGuid = null;
        }

        /// <summary>Opens the locomotive at <paramref name="index"/> on the list; false when there is none.</summary>
        public bool Pick(int index)
        {
            if (index < 0 || index >= engines.Count)
                return false;
            PickedGuid = engines[index].Guid;
            return true;
        }

        /// <summary>Back to the list. Returns the list index of the locomotive left, to highlight it, or -1.</summary>
        public int Back()
        {
            var left = IndexOf(PickedGuid);
            PickedGuid = null;
            return left;
        }

        /// <summary>Eligible owned locomotives with a GUID, each once, ordered by car ID.</summary>
        public static List<OwnedLocomotive> Order(IEnumerable<OwnedLocomotive> owned)
        {
            var list = new List<OwnedLocomotive>();
            if (owned == null)
                return list;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var engine in owned)
            {
                if (string.IsNullOrWhiteSpace(engine.Guid) || !UpgradeKeys.IsEligibleLivery(engine.Livery))
                    continue;
                if (seen.Add(engine.Guid))
                    list.Add(engine);
            }
            return list
                .OrderBy(e => e.Id ?? "", StringComparer.Ordinal)
                .ThenBy(e => e.Guid, StringComparer.Ordinal)
                .ToList();
        }

        private int IndexOf(string guid) =>
            guid == null ? -1 : engines.FindIndex(e => string.Equals(e.Guid, guid, StringComparison.Ordinal));
    }
}
