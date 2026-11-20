using System;
using DV.UserManagement;
using DV.Utils;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace RealisticFiring.Upgrades
{
 public static class Fittings
    {
        /// <summary>The save's key for what the player has bought.</summary>
        public const string SaveKey = "RealisticFiring_Upgrades";

        private const string ArchKey = "brickArch";
        private const string JetsKey = "overFireJets";
        private const string StokerKey = "s282Stoker";
        private const string FeedPumpKey = "s282FeedPump";
        private const string BoosterKey = "s282Booster";
        private const string SniftingKey = "sniftingValves";
        private const string DriftingValveKey = "driftingValve";
        private const string ByPassKey = "byPassValves";
        private const string AutoFireDoorKey = "automaticFireDoor";

        private static SaveGameData readFrom;
        private static bool archBought, jetsBought, stokerBought, feedPumpBought, boosterBought, sniftingBought, driftingValveBought, byPassBought, autoFireDoorBought;

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

        /// <summary>One more each time what is fitted may have changed, so fireboxes refit.</summary>
        public static int Version { get; private set; }

        /// <summary>Whether the engines have their brick arches now.</summary>
        public static bool Arch
        {
            get
            {
                if (FromHost.HasValue)
                    return FromHost.Value;
                return Allowed(Main.Settings?.BrickArch ?? UpgradeRule.BuyInCareer, ArchBought);
            }
        }

        /// <summary>Whether the engines have their over-fire jets now: the jets' rule allows them and the arch is fitted.</summary>
        public static bool Jets
        {
            get
            {
                if (JetsFromHost.HasValue)
                    return JetsFromHost.Value && Arch;
                return Arch && Allowed(Main.Settings?.OverFireJets ?? UpgradeRule.BuyInCareer, JetsBought);
            }
        }

        /// <summary>
        /// Whether the S282s have their stoker now. The stoker is built into
        /// every S282 unless the rule is never (<see cref="Session.RetrofitS282Stoker"/>,
        /// which takes a restart); this says whether it works and shows.
        /// </summary>
        public static bool Stoker
        {
            get
            {
                if (StokerFromHost.HasValue)
                    return StokerFromHost.Value;
                return Allowed(Main.Settings?.S282Stoker ?? UpgradeRule.BuyInCareer, StokerBought);
            }
        }

        /// <summary>
        /// Whether the S282s have their feedwater heater and pump now. Built
        /// into every S282 unless the rule is never (<see cref="Session.S282FeedPump"/>);
        /// this says whether it works and shows.
        /// </summary>
        public static bool FeedPump
        {
            get
            {
                if (FeedPumpFromHost.HasValue)
                    return FeedPumpFromHost.Value;
                return Allowed(Main.Settings?.S282FeedPump ?? UpgradeRule.BuyInCareer, FeedPumpBought);
            }
        }

        /// <summary>
        /// Whether the S282s have their booster now. Built into every S282
        /// unless the rule is never (<see cref="Session.S282Booster"/>); this
        /// says whether it works and shows.
        /// </summary>
        public static bool Booster
        {
            get
            {
                if (BoosterFromHost.HasValue)
                    return BoosterFromHost.Value;
                return Allowed(Main.Settings?.S282Booster ?? UpgradeRule.BuyInCareer, BoosterBought);
            }
        }

        /// <summary>Whether the S282s and S060s have their snifting valves now.</summary>
        public static bool Snifting
        {
            get
            {
                if (SniftingFromHost.HasValue)
                    return SniftingFromHost.Value;
                return Allowed(Main.Settings?.SniftingValves ?? UpgradeRule.BuyInCareer, SniftingBought);
            }
        }

        /// <summary>Whether the S282s and S060s have their drifting valve now.</summary>
        public static bool DriftingValve
        {
            get
            {
                if (DriftingValveFromHost.HasValue)
                    return DriftingValveFromHost.Value;
                return Allowed(Main.Settings?.DriftingValve ?? UpgradeRule.BuyInCareer, DriftingValveBought);
            }
        }

        /// <summary>Whether the S282s and S060s have their by-pass valves now.</summary>
        public static bool ByPass
        {
            get
            {
                if (ByPassFromHost.HasValue)
                    return ByPassFromHost.Value;
                return Allowed(Main.Settings?.ByPassValves ?? UpgradeRule.BuyInCareer, ByPassBought);
            }
        }

        /// <summary>Whether the S282s and S060s have their automatic fire door now (the car packs': <see cref="Sim.AutoFireDoorFit"/>).</summary>
        public static bool AutoFireDoor
        {
            get
            {
                if (AutoFireDoorFromHost.HasValue)
                    return AutoFireDoorFromHost.Value;
                return Allowed(Main.Settings?.AutomaticFireDoor ?? UpgradeRule.BuyInCareer, AutoFireDoorBought);
            }
        }

        private static bool Allowed(UpgradeRule rule, bool bought)
        {
            switch (rule)
            {
                case UpgradeRule.AlwaysFitted:
                    return true;
                case UpgradeRule.NeverFitted:
                    return false;
                default:
                    return !InCareer || bought;
            }
        }

        /// <summary>Whether this game is a career, where the arch is bought.</summary>
        public static bool InCareer
        {
            get
            {
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

        /// <summary>Whether the player of this save has bought the arch.</summary>
        public static bool ArchBought
        {
            get
            {
                Read();
                return archBought;
            }
        }

        /// <summary>Whether the player of this save has bought the jets.</summary>
        public static bool JetsBought
        {
            get
            {
                Read();
                return jetsBought;
            }
        }

        /// <summary>Whether the player of this save has bought the S282's stoker.</summary>
        public static bool StokerBought
        {
            get
            {
                Read();
                return stokerBought;
            }
        }

        /// <summary>Whether the player of this save has bought the S282's feed pump.</summary>
        public static bool FeedPumpBought
        {
            get
            {
                Read();
                return feedPumpBought;
            }
        }

        /// <summary>Whether the player of this save has bought the S282's booster.</summary>
        public static bool BoosterBought
        {
            get
            {
                Read();
                return boosterBought;
            }
        }

        /// <summary>Whether the player of this save has bought the snifting valves.</summary>
        public static bool SniftingBought
        {
            get
            {
                Read();
                return sniftingBought;
            }
        }

        /// <summary>Whether the player of this save has bought the drifting valve.</summary>
        public static bool DriftingValveBought
        {
            get
            {
                Read();
                return driftingValveBought;
            }
        }

        /// <summary>Whether the player of this save has bought the by-pass valves.</summary>
        public static bool ByPassBought
        {
            get
            {
                Read();
                return byPassBought;
            }
        }

        /// <summary>Whether the player of this save has bought the automatic fire door.</summary>
        public static bool AutoFireDoorBought
        {
            get
            {
                Read();
                return autoFireDoorBought;
            }
        }

        /// <summary>Buys the arch: every engine has it from now on, and the save keeps it.</summary>
        public static void BuyArch()
        {
            Read();
            archBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the jets: every engine with its arch has them from now on, and the save keeps it.</summary>
        public static void BuyJets()
        {
            Read();
            jetsBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the S282's stoker: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyStoker()
        {
            Read();
            stokerBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the S282's feed pump: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyFeedPump()
        {
            Read();
            feedPumpBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the S282's booster: every S282 has one from now on, and the save keeps it.</summary>
        public static void BuyBooster()
        {
            Read();
            boosterBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the snifting valves: every S282 and S060 has them from now on, and the save keeps it.</summary>
        public static void BuySnifting()
        {
            Read();
            sniftingBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the drifting valve: every S282 and S060 has one from now on, and the save keeps it.</summary>
        public static void BuyDriftingValve()
        {
            Read();
            driftingValveBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the by-pass valves: every S282 and S060 has them from now on, and the save keeps it.</summary>
        public static void BuyByPass()
        {
            Read();
            byPassBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Buys the automatic fire door: every coal-fired engine has one from now on, and the save keeps it.</summary>
        public static void BuyAutoFireDoor()
        {
            Read();
            autoFireDoorBought = true;
            Write(SingletonBehaviour<SaveGameManager>.Instance?.data);
            Changed();
        }

        /// <summary>Marks what is fitted as possibly changed (the override, the host's word).</summary>
        public static void Changed() => Version++;

        // The save's record, read again when another save is loaded.
        private static void Read()
        {
            var data = SingletonBehaviour<SaveGameManager>.Instance?.data;
            if (ReferenceEquals(data, readFrom))
                return;
            readFrom = data;
            var record = data?.GetJObject(SaveKey);
            archBought = record?[ArchKey]?.Value<bool>() ?? false;
            jetsBought = record?[JetsKey]?.Value<bool>() ?? false;
            stokerBought = record?[StokerKey]?.Value<bool>() ?? false;
            feedPumpBought = record?[FeedPumpKey]?.Value<bool>() ?? false;
            boosterBought = record?[BoosterKey]?.Value<bool>() ?? false;
            sniftingBought = record?[SniftingKey]?.Value<bool>() ?? false;
            driftingValveBought = record?[DriftingValveKey]?.Value<bool>() ?? false;
            byPassBought = record?[ByPassKey]?.Value<bool>() ?? false;
            autoFireDoorBought = record?[AutoFireDoorKey]?.Value<bool>() ?? false;
            Version++;
        }

        // The save's record, merged into what is there: keys this version
        // doesn't know are kept.
        private static void Write(SaveGameData data)
        {
            if (data == null || !archBought && !jetsBought && !stokerBought && !feedPumpBought && !boosterBought && !sniftingBought && !driftingValveBought && !byPassBought && !autoFireDoorBought)
                return;
            var record = data.GetJObject(SaveKey) ?? new JObject();
            if (archBought)
                record[ArchKey] = true;
            if (jetsBought)
                record[JetsKey] = true;
            if (stokerBought)
                record[StokerKey] = true;
            if (feedPumpBought)
                record[FeedPumpKey] = true;
            if (boosterBought)
                record[BoosterKey] = true;
            if (sniftingBought)
                record[SniftingKey] = true;
            if (driftingValveBought)
                record[DriftingValveKey] = true;
            if (byPassBought)
                record[ByPassKey] = true;
            if (autoFireDoorBought)
                record[AutoFireDoorKey] = true;
            data.SetJObject(SaveKey, record);
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
