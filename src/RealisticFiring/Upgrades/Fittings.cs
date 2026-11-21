using System;
using System.Collections.Generic;
using DV.UserManagement;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using RealisticFiring.Core;

namespace RealisticFiring.Upgrades
{
    /// <summary>Each career upgrade the mod sells and fits.</summary>
    public enum UpgradeId
    {
        BrickArch,
        OverFireJets,
        S282Stoker,
        S282FeedPump,
        S282Booster,
        SniftingValves,
        DriftingValve,
        ByPassValves,
        AutomaticFireDoor,
    }

    /// <summary>Table entry describing one upgrade's save key, mask, settings rule, host override accessors, and optional prerequisite.</summary>
    public sealed class UpgradeSpec
    {
        public UpgradeSpec(
            UpgradeId id,
            UpgradeMask mask,
            string saveKey,
            Func<Settings, UpgradeRule> rule,
            Func<bool?> getHostOverride,
            Action<bool?> setHostOverride,
            UpgradeId? prerequisite = null)
        {
            Id = id;
            Mask = mask;
            SaveKey = saveKey;
            Rule = rule;
            GetHostOverride = getHostOverride;
            SetHostOverride = setHostOverride;
            Prerequisite = prerequisite;
        }

        public UpgradeId Id { get; }
        public UpgradeMask Mask { get; }
        public string SaveKey { get; }
        public Func<Settings, UpgradeRule> Rule { get; }
        public Func<bool?> GetHostOverride { get; }
        public Action<bool?> SetHostOverride { get; }
        public UpgradeId? Prerequisite { get; }
    }

    /// <summary>
    /// Whether the stock engines' fireboxes and cylinders carry their career
    /// upgrades: brick arch, over-fire jets (requiring the arch on the same
    /// locomotive), automatic fire door, drifting fittings (snifting, drifting
    /// valve, by-pass), and the S282's stoker, feed pump, and booster.
    ///
    /// Supports both fleet-wide career unlocks (default, legacy behavior) and
    /// optional per-locomotive unlocks keyed by each locomotive's persistent
    /// <c>logicCar.carGuid</c>. Both datasets are preserved side by side under
    /// <see cref="SaveKey"/> so older saves load cleanly and players can switch
    /// modes without losing purchases.
    ///
    /// In a Multiplayer session a client takes the host's word: fleet-wide flags
    /// (<see cref="FromHost"/> etc., also via <see cref="SetHostOverride"/> and
    /// <see cref="ClearHostOverrides"/>) in fleet-wide mode, and per-car effective
    /// masks (<see cref="SetCarFromHost"/> / <see cref="ApplyHostPerCar"/>) when
    /// the host runs per-locomotive mode (<see cref="HostPerLocomotive"/>).
    /// </summary>
    public static class Fittings
    {
        /// <summary>The save's key for what the player has bought.</summary>
        public const string SaveKey = "RealisticFiring_Upgrades";

        private static SaveGameData readFrom;
        private static bool skipSaveRead;
        private static readonly UpgradeLedger Ledger = new UpgradeLedger();

        // Keep the nine host-override fields as public static fields for binary
        // compatibility with pre-compiled RealisticFiring.Multiplayer.dll add-ons.
        /// <summary>The host's word on the arch, in a Multiplayer session where this game is a client; null otherwise.</summary>
        public static bool? FromHost;

        /// <summary>The host's word on the jets, as <see cref="FromHost"/>.</summary>
        public static bool? JetsFromHost;

        /// <summary>The host's word on the S282's stoker, as <see cref="FromHost"/>.</summary>
        public static bool? StokerFromHost;

        /// <summary>The host's word on the S282's feed pump, as <see cref="FromHost"/>.</summary>
        public static bool? FeedPumpFromHost;

        /// <summary>The host's word on the S282's booster, as <see cref="FromHost"/>.</summary>
        public static bool? BoosterFromHost;

        /// <summary>The host's word on the snifting valves, as <see cref="FromHost"/>.</summary>
        public static bool? SniftingFromHost;

        /// <summary>The host's word on the drifting valve, as <see cref="FromHost"/>.</summary>
        public static bool? DriftingValveFromHost;

        /// <summary>The host's word on the by-pass valves, as <see cref="FromHost"/>.</summary>
        public static bool? ByPassFromHost;

        /// <summary>The host's word on the automatic fire door, as <see cref="FromHost"/>.</summary>
        public static bool? AutoFireDoorFromHost;

        /// <summary>Whether the host is running in per-locomotive mode, on a Multiplayer client; null otherwise.</summary>
        public static bool? HostPerLocomotive;

        // Ordered identically to UpgradeId so (int)id indexes directly in O(1).
        private static readonly UpgradeSpec[] Specs =
        {
            new UpgradeSpec(UpgradeId.BrickArch, UpgradeMask.BrickArch, "brickArch", s => s.BrickArch, () => FromHost, v => FromHost = v),
            new UpgradeSpec(UpgradeId.OverFireJets, UpgradeMask.OverFireJets, "overFireJets", s => s.OverFireJets, () => JetsFromHost, v => JetsFromHost = v, prerequisite: UpgradeId.BrickArch),
            new UpgradeSpec(UpgradeId.S282Stoker, UpgradeMask.S282Stoker, "s282Stoker", s => s.S282Stoker, () => StokerFromHost, v => StokerFromHost = v),
            new UpgradeSpec(UpgradeId.S282FeedPump, UpgradeMask.S282FeedPump, "s282FeedPump", s => s.S282FeedPump, () => FeedPumpFromHost, v => FeedPumpFromHost = v),
            new UpgradeSpec(UpgradeId.S282Booster, UpgradeMask.S282Booster, "s282Booster", s => s.S282Booster, () => BoosterFromHost, v => BoosterFromHost = v),
            new UpgradeSpec(UpgradeId.SniftingValves, UpgradeMask.SniftingValves, "sniftingValves", s => s.SniftingValves, () => SniftingFromHost, v => SniftingFromHost = v),
            new UpgradeSpec(UpgradeId.DriftingValve, UpgradeMask.DriftingValve, "driftingValve", s => s.DriftingValve, () => DriftingValveFromHost, v => DriftingValveFromHost = v),
            new UpgradeSpec(UpgradeId.ByPassValves, UpgradeMask.ByPassValves, "byPassValves", s => s.ByPassValves, () => ByPassFromHost, v => ByPassFromHost = v),
            new UpgradeSpec(UpgradeId.AutomaticFireDoor, UpgradeMask.AutomaticFireDoor, "automaticFireDoor", s => s.AutomaticFireDoor, () => AutoFireDoorFromHost, v => AutoFireDoorFromHost = v),
        };

        /// <summary>All registered upgrades, in <see cref="UpgradeId"/> order.</summary>
        public static IReadOnlyList<UpgradeSpec> All => Specs;

        /// <summary>The table specification for <paramref name="id"/>.</summary>
        public static UpgradeSpec Spec(UpgradeId id) => Specs[(int)id];

        private readonly struct HostCarState
        {
            public HostCarState(UpgradeMask mask, ushort? version)
            {
                Mask = mask;
                Version = version;
            }

            public UpgradeMask Mask { get; }
            public ushort? Version { get; }
        }

        private static readonly Dictionary<TrainCar, HostCarState> CarMasksFromHost = new Dictionary<TrainCar, HostCarState>();
        private static readonly List<TrainCar> PruneBuffer = new List<TrainCar>();
        private static ushort? hostPerCarVersion;
        private static int hostCarUpdates;
        private const int PruneInterval = 64;

        /// <summary>One more each time what is fitted may have changed, so fireboxes and cabs refit.</summary>
        public static int Version { get; private set; }

        /// <summary>The active upgrade scope: the host's in multiplayer, else the local setting.</summary>
        public static UpgradeScope Scope =>
            HostPerLocomotive.HasValue
                ? (HostPerLocomotive.Value ? UpgradeScope.PerLocomotive : UpgradeScope.FleetWide)
                : (Main.Settings?.UpgradeScope ?? UpgradeScope.FleetWide);

        /// <summary>Whether upgrades are scoped per locomotive right now.</summary>
        public static bool IsPerLocomotive => Scope == UpgradeScope.PerLocomotive;

        /// <summary>The car's persistent GUID, or null before <c>logicCar</c> is assigned (avoids logging an error on spawn).</summary>
        public static string CarGuidOf(TrainCar car) =>
            car != null && car.logicCar != null && !string.IsNullOrEmpty(car.logicCar.carGuid)
                ? car.logicCar.carGuid
                : null;

        /// <summary>The host's word for <paramref name="id"/> in a Multiplayer client session; null otherwise.</summary>
        public static bool? GetHostOverride(UpgradeId id) => Specs[(int)id].GetHostOverride();

        /// <summary>Sets the host's word for <paramref name="id"/> in a Multiplayer client session.</summary>
        public static void SetHostOverride(UpgradeId id, bool? value) => Specs[(int)id].SetHostOverride(value);

        /// <summary>Clears all Multiplayer host overrides (fleet-wide and per-car) and marks fitted state changed.</summary>
        public static void ClearHostOverrides()
        {
            for (var i = 0; i < Specs.Length; i++)
                Specs[i].SetHostOverride(null);
            HostPerLocomotive = null;
            hostPerCarVersion = null;
            hostCarUpdates = 0;
            PruneBuffer.Clear();
            CarMasksFromHost.Clear();
            Changed();
        }

        /// <summary>Clears all host-override state when disconnecting from Multiplayer.</summary>
        public static void ClearHost() => ClearHostOverrides();

        /// <summary>Whether <paramref name="id"/> is fitted now (fleet-wide fallback), honoring prerequisites, host overrides, settings, and career purchases.</summary>
        public static bool IsFitted(UpgradeId id) => Has(Specs[(int)id].Mask, null);

        internal static bool EvaluateFitted(UpgradeId id, Settings settings, Func<bool> inCareer, Func<UpgradeId, bool> isBought)
        {
            var spec = Specs[(int)id];
            if (spec.GetHostOverride() is bool host)
            {
                if (!host)
                    return false;
                return !(spec.Prerequisite is UpgradeId req) || EvaluateFitted(req, settings, inCareer, isBought);
            }
            if (spec.Prerequisite is UpgradeId prerequisite && !EvaluateFitted(prerequisite, settings, inCareer, isBought))
                return false;
            var rule = settings != null ? spec.Rule(settings) : UpgradeRule.BuyInCareer;
            return Allowed(rule, inCareer, id, isBought);
        }

        private static bool Allowed(UpgradeRule rule, Func<bool> inCareer, UpgradeId id, Func<UpgradeId, bool> isBought)
        {
            switch (rule)
            {
                case UpgradeRule.AlwaysFitted:
                    return true;
                case UpgradeRule.NeverFitted:
                    return false;
                default:
                    return !inCareer() || isBought(id);
            }
        }

        /// <summary>Whether the engines have their brick arches now (fleet-wide fallback).</summary>
        public static bool Arch => Has(UpgradeMask.BrickArch, null);

        /// <summary>Whether the engines have their over-fire jets now (fleet-wide fallback).</summary>
        public static bool Jets => Has(UpgradeMask.OverFireJets, null);

        /// <summary>Whether the S282s have their stoker now (fleet-wide fallback).</summary>
        public static bool Stoker => Has(UpgradeMask.S282Stoker, null);

        /// <summary>Whether the S282s have their feedwater heater and pump now (fleet-wide fallback).</summary>
        public static bool FeedPump => Has(UpgradeMask.S282FeedPump, null);

        /// <summary>Whether the S282s have their booster now (fleet-wide fallback).</summary>
        public static bool Booster => Has(UpgradeMask.S282Booster, null);

        /// <summary>Whether the S282s and S060s have their snifting valves now (fleet-wide fallback).</summary>
        public static bool Snifting => Has(UpgradeMask.SniftingValves, null);

        /// <summary>Whether the S282s and S060s have their drifting valve now (fleet-wide fallback).</summary>
        public static bool DriftingValve => Has(UpgradeMask.DriftingValve, null);

        /// <summary>Whether the S282s and S060s have their by-pass valves now (fleet-wide fallback).</summary>
        public static bool ByPass => Has(UpgradeMask.ByPassValves, null);

        /// <summary>Whether the S282s and S060s have their automatic fire door now (fleet-wide fallback).</summary>
        public static bool AutoFireDoor => Has(UpgradeMask.AutomaticFireDoor, null);

        /// <summary>Per-locomotive fitting queries (fall back to fleet-wide when <see cref="Scope"/> is fleet-wide).</summary>
        public static bool HasArch(TrainCar car) => Has(UpgradeMask.BrickArch, car);
        public static bool HasJets(TrainCar car) => Has(UpgradeMask.OverFireJets, car);
        public static bool HasStoker(TrainCar car) => Has(UpgradeMask.S282Stoker, car);
        public static bool HasFeedPump(TrainCar car) => Has(UpgradeMask.S282FeedPump, car);
        public static bool HasBooster(TrainCar car) => Has(UpgradeMask.S282Booster, car);
        public static bool HasSnifting(TrainCar car) => Has(UpgradeMask.SniftingValves, car);
        public static bool HasDriftingValve(TrainCar car) => Has(UpgradeMask.DriftingValve, car);
        public static bool HasByPass(TrainCar car) => Has(UpgradeMask.ByPassValves, car);
        public static bool HasAutoFireDoor(TrainCar car) => Has(UpgradeMask.AutomaticFireDoor, car);

        /// <summary>The full effective upgrade mask for <paramref name="car"/> right now.</summary>
        public static UpgradeMask EffectiveMask(TrainCar car)
        {
            if (HostPerLocomotive == true)
            {
                if (car != null && CarMasksFromHost.TryGetValue(car, out var carState))
                    return UpgradeLedger.EnforcePrerequisites(carState.Mask);
                return HostFleetMask();
            }
            if (HasAnyHostFleetOverride)
                return HostFleetMask();
            Read();
            var scope = Main.Settings?.UpgradeScope ?? UpgradeScope.FleetWide;
            return Ledger.Resolve(CurrentRules(), scope, InCareer, CarGuidOf(car));
        }

        /// <summary>Whether <paramref name="upgrade"/> is fitted on <paramref name="car"/> right now.</summary>
        public static bool Has(UpgradeMask upgrade, TrainCar car)
        {
            if (upgrade == UpgradeMask.None)
                return false;
            if (HostPerLocomotive == true)
            {
                var mask = car != null && CarMasksFromHost.TryGetValue(car, out var carState)
                    ? UpgradeLedger.EnforcePrerequisites(carState.Mask)
                    : HostFleetMask();
                return (mask & upgrade) == upgrade;
            }
            if (TryHostFleetFlag(upgrade, out var hostValue))
            {
                if (upgrade == UpgradeMask.OverFireJets)
                    return hostValue && Has(UpgradeMask.BrickArch, car);
                return hostValue;
            }
            Read();
            var scope = Main.Settings?.UpgradeScope ?? UpgradeScope.FleetWide;
            return Ledger.IsFitted(upgrade, CurrentRules(), scope, InCareer, CarGuidOf(car));
        }

        private static bool HasAnyHostFleetOverride
        {
            get
            {
                for (var i = 0; i < Specs.Length; i++)
                    if (Specs[i].GetHostOverride().HasValue)
                        return true;
                return false;
            }
        }

        private static bool TryHostFleetFlag(UpgradeMask upgrade, out bool value)
        {
            for (var i = 0; i < Specs.Length; i++)
            {
                if (Specs[i].Mask != upgrade)
                    continue;
                if (Specs[i].GetHostOverride() is bool flag)
                {
                    value = flag;
                    return true;
                }
                break;
            }
            value = false;
            return false;
        }

        private static UpgradeMask HostFleetMask()
        {
            var mask = UpgradeMask.None;
            for (var i = 0; i < Specs.Length; i++)
                if (Specs[i].GetHostOverride() == true)
                    mask |= Specs[i].Mask;
            return UpgradeLedger.EnforcePrerequisites(mask);
        }

        /// <summary>Updates one car's host-authoritative upgrade mask on a Multiplayer client.</summary>
        internal static bool SetCarFromHost(TrainCar car, UpgradeMask mask, ushort? hostVersion = null)
        {
            if (car == null)
                return false;
            if (hostVersion.HasValue && hostPerCarVersion.HasValue && !UpgradeLedger.VersionAtLeast(hostVersion.Value, hostPerCarVersion.Value))
                return false;
            var hasExisting = CarMasksFromHost.TryGetValue(car, out var existing);
            if (hostVersion.HasValue && hasExisting && existing.Version.HasValue
                && !UpgradeLedger.VersionAtLeast(hostVersion.Value, existing.Version.Value))
                return false;
            PruneHostCars();
            var normalized = UpgradeLedger.EnforcePrerequisites(mask);
            var nextVersion = hostVersion ?? (hasExisting ? existing.Version : null);
            if (hasExisting && existing.Mask == normalized)
            {
                if (nextVersion != existing.Version)
                    CarMasksFromHost[car] = new HostCarState(normalized, nextVersion);
                return false;
            }
            CarMasksFromHost[car] = new HostCarState(normalized, nextVersion);
            Changed();
            return true;
        }

        /// <summary>Applies a reliable batch of per-car upgrade masks from the host on a Multiplayer client.</summary>
        internal static void ApplyHostPerCar(IEnumerable<(TrainCar Car, UpgradeMask Mask)> entries, ushort hostVersion, bool replaceAll)
        {
            if (hostPerCarVersion.HasValue && !UpgradeLedger.VersionAtLeast(hostVersion, hostPerCarVersion.Value))
                return;
            hostPerCarVersion = hostVersion;
            PruneHostCars(force: true);
            var changed = false;
            var incoming = new Dictionary<TrainCar, UpgradeMask>();
            if (entries != null)
            {
                foreach (var (car, mask) in entries)
                {
                    if (car != null)
                        incoming[car] = UpgradeLedger.EnforcePrerequisites(mask);
                }
            }
            if (replaceAll)
            {
                PruneBuffer.Clear();
                foreach (var pair in CarMasksFromHost)
                {
                    if (!incoming.ContainsKey(pair.Key)
                        && (!pair.Value.Version.HasValue || UpgradeLedger.VersionAtLeast(hostVersion, pair.Value.Version.Value)))
                    {
                        PruneBuffer.Add(pair.Key);
                    }
                }
                for (var i = 0; i < PruneBuffer.Count; i++)
                {
                    CarMasksFromHost.Remove(PruneBuffer[i]);
                    changed = true;
                }
                PruneBuffer.Clear();
            }
            foreach (var pair in incoming)
            {
                if (CarMasksFromHost.TryGetValue(pair.Key, out var existing))
                {
                    if (existing.Version.HasValue && !UpgradeLedger.VersionAtLeast(hostVersion, existing.Version.Value))
                        continue;
                    CarMasksFromHost[pair.Key] = new HostCarState(pair.Value, hostVersion);
                    if (existing.Mask != pair.Value)
                        changed = true;
                }
                else
                {
                    CarMasksFromHost[pair.Key] = new HostCarState(pair.Value, hostVersion);
                    changed = true;
                }
            }
            if (changed)
                Changed();
        }

        /// <summary>Clears per-car host masks (when the host switches back to fleet-wide mode).</summary>
        internal static void ClearHostPerCar()
        {
            hostPerCarVersion = null;
            hostCarUpdates = 0;
            PruneBuffer.Clear();
            if (CarMasksFromHost.Count == 0)
                return;
            CarMasksFromHost.Clear();
            Changed();
        }

        private static void PruneHostCars(bool force = false)
        {
            if (CarMasksFromHost.Count == 0)
                return;
            if (!force && ++hostCarUpdates < PruneInterval)
                return;
            hostCarUpdates = 0;
            PruneBuffer.Clear();
            foreach (var car in CarMasksFromHost.Keys)
            {
                if (car == null)
                    PruneBuffer.Add(car);
            }
            for (var i = 0; i < PruneBuffer.Count; i++)
                CarMasksFromHost.Remove(PruneBuffer[i]);
            PruneBuffer.Clear();
        }

        internal static UpgradeRulesSet CurrentRules(Settings settings = null)
        {
            var s = settings ?? Main.Settings;
            if (s == null)
                return UpgradeRulesSet.Default;
            return new UpgradeRulesSet(
                ToRule(s.BrickArch),
                ToRule(s.OverFireJets),
                ToRule(s.AutomaticFireDoor),
                ToRule(s.SniftingValves),
                ToRule(s.DriftingValve),
                ToRule(s.ByPassValves),
                ToRule(s.S282Stoker),
                ToRule(s.S282FeedPump),
                ToRule(s.S282Booster));
        }

        private static FittingRule ToRule(UpgradeRule rule)
        {
            switch (rule)
            {
                case UpgradeRule.AlwaysFitted: return FittingRule.AlwaysFitted;
                case UpgradeRule.NeverFitted: return FittingRule.NeverFitted;
                default: return FittingRule.BuyInCareer;
            }
        }

        /// <summary>Optional career-mode override for offline adapter checks.</summary>
        internal static bool? InCareerOverride;

        /// <summary>Whether this game is a career, where upgrades are bought.</summary>
        public static bool InCareer
        {
            get
            {
                if (InCareerOverride.HasValue)
                    return InCareerOverride.Value;
                try
                {
                    return SingletonBehaviour<UserManager>.Instance?.CurrentUser?.CurrentSession?.GameMode == "Career";
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Replaces the in-memory local ledger for offline adapter checks without touching save data.</summary>
        internal static void LoadLocalForTests(UpgradeMask fleet, IEnumerable<KeyValuePair<string, UpgradeMask>> perLoco = null)
        {
            skipSaveRead = true;
            readFrom = null;
            Ledger.Load(fleet, perLoco);
            Version++;
        }

        /// <summary>Restores normal save-backed ledger reading after offline adapter checks.</summary>
        internal static void ClearLocalForTests()
        {
            skipSaveRead = false;
            readFrom = null;
            Ledger.Load(UpgradeMask.None, null);
            Version++;
        }

        /// <summary>Whether the player of this save has bought <paramref name="id"/> fleet-wide.</summary>
        public static bool IsBought(UpgradeId id)
        {
            Read();
            return Ledger.IsBought(Specs[(int)id].Mask, UpgradeScope.FleetWide);
        }

        /// <summary>Whether <paramref name="upgrade"/> has been bought under the current scope (and for <paramref name="car"/> in per-loco mode).</summary>
        public static bool IsBought(UpgradeMask upgrade, TrainCar car = null) =>
            IsBoughtForGuid(upgrade, CarGuidOf(car));

        /// <summary>Whether <paramref name="upgrade"/> has been bought under the current scope for <paramref name="carGuid"/>.</summary>
        public static bool IsBoughtForGuid(UpgradeMask upgrade, string carGuid)
        {
            Read();
            var scope = Main.Settings?.UpgradeScope ?? UpgradeScope.FleetWide;
            return Ledger.IsBought(upgrade, scope, carGuid);
        }

        /// <summary>Whether the player of this save has bought the arch fleet-wide.</summary>
        public static bool ArchBought => IsBought(UpgradeId.BrickArch);

        /// <summary>Whether the player of this save has bought the jets fleet-wide.</summary>
        public static bool JetsBought => IsBought(UpgradeId.OverFireJets);

        /// <summary>Whether the player of this save has bought the S282's stoker fleet-wide.</summary>
        public static bool StokerBought => IsBought(UpgradeId.S282Stoker);

        /// <summary>Whether the player of this save has bought the S282's feed pump fleet-wide.</summary>
        public static bool FeedPumpBought => IsBought(UpgradeId.S282FeedPump);

        /// <summary>Whether the player of this save has bought the S282's booster fleet-wide.</summary>
        public static bool BoosterBought => IsBought(UpgradeId.S282Booster);

        /// <summary>Whether the player of this save has bought the snifting valves fleet-wide.</summary>
        public static bool SniftingBought => IsBought(UpgradeId.SniftingValves);

        /// <summary>Whether the player of this save has bought the drifting valve fleet-wide.</summary>
        public static bool DriftingValveBought => IsBought(UpgradeId.DriftingValve);

        /// <summary>Whether the player of this save has bought the by-pass valves fleet-wide.</summary>
        public static bool ByPassBought => IsBought(UpgradeId.ByPassValves);

        /// <summary>Whether the player of this save has bought the automatic fire door fleet-wide.</summary>
        public static bool AutoFireDoorBought => IsBought(UpgradeId.AutomaticFireDoor);

        /// <summary>Buys <paramref name="id"/> fleet-wide: every applicable engine has it from now on, and the save keeps it.</summary>
        public static void Buy(UpgradeId id) => BuyFleet(Specs[(int)id].Mask);

        /// <summary>Buys <paramref name="upgrade"/> under the current scope (for <paramref name="car"/> in per-locomotive mode).</summary>
        public static void Buy(UpgradeMask upgrade, TrainCar car = null) =>
            BuyForGuid(upgrade, CarGuidOf(car));

        /// <summary>Buys <paramref name="upgrade"/> under the current scope (for <paramref name="carGuid"/> in per-locomotive mode).</summary>
        public static void BuyForGuid(UpgradeMask upgrade, string carGuid)
        {
            Read();
            var scope = Main.Settings?.UpgradeScope ?? UpgradeScope.FleetWide;
            if (!Ledger.Buy(upgrade, scope, carGuid))
                return;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        private static void BuyFleet(UpgradeMask upgrade)
        {
            Read();
            Ledger.Buy(upgrade, UpgradeScope.FleetWide);
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the arch fleet-wide: every engine has it from now on, and the save keeps it.</summary>
        public static void BuyArch() => Buy(UpgradeId.BrickArch);

        /// <summary>Buys the jets fleet-wide: every engine with its arch has them from now on, and the save keeps it.</summary>
        public static void BuyJets() => Buy(UpgradeId.OverFireJets);

        /// <summary>Buys the S282's stoker fleet-wide: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyStoker() => Buy(UpgradeId.S282Stoker);

        /// <summary>Buys the S282's feed pump fleet-wide: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyFeedPump() => Buy(UpgradeId.S282FeedPump);

        /// <summary>Buys the S282's booster fleet-wide: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyBooster() => Buy(UpgradeId.S282Booster);

        /// <summary>Buys the snifting valves fleet-wide: every S282 and S060 has them from now on, and the save keeps it.</summary>
        public static void BuySnifting() => Buy(UpgradeId.SniftingValves);

        /// <summary>Buys the drifting valve fleet-wide: every S282 and S060 has one from now on, and the save keeps it.</summary>
        public static void BuyDriftingValve() => Buy(UpgradeId.DriftingValve);

        /// <summary>Buys the by-pass valves fleet-wide: every S282 and S060 has them from now on, and the save keeps it.</summary>
        public static void BuyByPass() => Buy(UpgradeId.ByPassValves);

        /// <summary>Buys the automatic fire door fleet-wide: every coal-fired engine has one from now on, and the save keeps it.</summary>
        public static void BuyAutoFireDoor() => Buy(UpgradeId.AutomaticFireDoor);

        /// <summary>Marks what is fitted as possibly changed (the override, a purchase, the host's word).</summary>
        public static void Changed() => Version++;

        internal static void ReadRecord(JObject record, bool[] target)
        {
            for (var i = 0; i < Specs.Length; i++)
                target[i] = ReadBool(record?[Specs[i].SaveKey]);
        }

        internal static bool WriteRecord(JObject record, bool[] source)
        {
            var any = false;
            for (var i = 0; i < Specs.Length; i++)
            {
                if (!source[i])
                    continue;
                record[Specs[i].SaveKey] = true;
                any = true;
            }
            return any;
        }

        /// <summary>Reads fleet-wide and per-locomotive upgrades from <paramref name="record"/> into <paramref name="target"/>.</summary>
        internal static void ReadJson(JObject record, UpgradeLedger target)
        {
            if (target == null)
                return;
            var fleet = ReadMask(record);
            var perLoco = new List<KeyValuePair<string, UpgradeMask>>();
            if (record?[UpgradeKeys.Locomotives] is JObject locos)
            {
                foreach (var prop in locos.Properties())
                {
                    if (prop.Value is JObject entry)
                    {
                        var mask = ReadMask(entry);
                        if (mask != UpgradeMask.None)
                            perLoco.Add(new KeyValuePair<string, UpgradeMask>(prop.Name, mask));
                    }
                }
            }
            target.Load(fleet, perLoco);
        }

        /// <summary>Merges fleet-wide and per-locomotive upgrades from <paramref name="source"/> into <paramref name="existing"/>, keeping unknown keys.</summary>
        internal static JObject WriteJson(JObject existing, UpgradeLedger source)
        {
            if (source == null || !source.HasAnyBought)
                return existing;
            var record = existing != null ? (JObject)existing.DeepClone() : new JObject();
            WriteMask(record, source.FleetBought);
            if (source.LocomotiveBought.Count > 0)
            {
                var locos = record[UpgradeKeys.Locomotives] as JObject ?? new JObject();
                foreach (var pair in source.LocomotiveBought)
                {
                    var entry = locos[pair.Key] as JObject ?? new JObject();
                    WriteMask(entry, pair.Value);
                    locos[pair.Key] = entry;
                }
                record[UpgradeKeys.Locomotives] = locos;
            }
            return record;
        }

        private static UpgradeMask ReadMask(JObject obj)
        {
            if (obj == null)
                return UpgradeMask.None;
            var mask = UpgradeMask.None;
            foreach (var (flag, key) in UpgradeKeys.All)
            {
                if (ReadBool(obj[key]))
                    mask |= flag;
            }
            return mask;
        }

        private static bool ReadBool(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return false;
            try
            {
                return token.Value<bool>();
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is ArgumentException || e is OverflowException)
            {
                return false;
            }
        }

        private static void WriteMask(JObject obj, UpgradeMask mask)
        {
            foreach (var (flag, key) in UpgradeKeys.All)
            {
                if ((mask & flag) == flag)
                    obj[key] = true;
            }
        }

        // The save's record, read again when another save is loaded.
        private static void Read()
        {
            if (skipSaveRead)
                return;
            var data = SingletonBehaviour<SaveGameManager>.Instance?.data;
            if (ReferenceEquals(data, readFrom))
                return;
            readFrom = data;
            ReadJson(data?.GetJObject(SaveKey), Ledger);
            Version++;
        }

        // The save's record, merged into what is there: keys this version
        // doesn't know are kept.
        private static void Write(SaveGameData data)
        {
            if (data == null || !Ledger.HasAnyBought)
                return;
            var written = WriteJson(data.GetJObject(SaveKey), Ledger);
            if (written != null)
                data.SetJObject(SaveKey, written);
        }

        /// <summary>A save writes the player's upgrades with the rest of the career.</summary>
        [HarmonyPatch(typeof(SaveGameManager), "UpdateInternalData")]
        private static class Saving
        {
            private static void Postfix(SaveGameManager __instance)
            {
                try
                {
                    if (ReferenceEquals(__instance.data, readFrom))
                        Write(__instance.data);
                }
                catch (Exception e)
                {
                    Main.Mod?.Logger.Error($"Saving the firebox upgrades failed: {e}");
                }
            }
        }
    }

    /// <summary>How the engines get a firebox upgrade.</summary>
    public enum UpgradeRule
    {
        /// <summary>Bought at a career manager in a career; fitted in sandbox.</summary>
        BuyInCareer,

        /// <summary>Every engine has it.</summary>
        AlwaysFitted,

        /// <summary>No engine has it.</summary>
        NeverFitted,
    }
}
