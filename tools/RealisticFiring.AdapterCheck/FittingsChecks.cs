using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RealisticFiring.Upgrades;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Verifies the enum/table-driven <see cref="Fittings"/> upgrade registry:
    /// exact save keys, JSON round-trip preserving unknown keys, rule and
    /// prerequisite evaluation, lazy <see cref="Fittings.InCareer"/> evaluation,
    /// host overrides, and binary-compatible public static host-override fields.
    /// </summary>
    internal static class FittingsChecks
    {
        private static readonly (UpgradeId Id, string Key, string HostField)[] ExpectedSpecs =
        {
            (UpgradeId.BrickArch, "brickArch", "FromHost"),
            (UpgradeId.OverFireJets, "overFireJets", "JetsFromHost"),
            (UpgradeId.S282Stoker, "s282Stoker", "StokerFromHost"),
            (UpgradeId.S282FeedPump, "s282FeedPump", "FeedPumpFromHost"),
            (UpgradeId.S282Booster, "s282Booster", "BoosterFromHost"),
            (UpgradeId.SniftingValves, "sniftingValves", "SniftingFromHost"),
            (UpgradeId.DriftingValve, "driftingValve", "DriftingValveFromHost"),
            (UpgradeId.ByPassValves, "byPassValves", "ByPassFromHost"),
            (UpgradeId.AutomaticFireDoor, "automaticFireDoor", "AutoFireDoorFromHost"),
            (UpgradeId.S282OilBurner, "s282OilBurner", "OilBurnerFromHost"),
        };

        internal static void Run(Action<bool, string> check)
        {
            var enumValues = (UpgradeId[])Enum.GetValues(typeof(UpgradeId));
            check(enumValues.Length == ExpectedSpecs.Length
                  && Fittings.All.Count == ExpectedSpecs.Length
                  && Fittings.SaveKey == "RealisticFiring_Upgrades",
                $"Fittings registry has all {ExpectedSpecs.Length} UpgradeId entries under save key '{Fittings.SaveKey}'");

            check(ExpectedSpecs.Select((e, i) => Fittings.All[i].Id == e.Id && (int)Fittings.All[i].Id == i
                                                 && Fittings.All[i].SaveKey == e.Key
                                                 && ReferenceEquals(Fittings.Spec(e.Id), Fittings.All[i])).All(ok => ok),
                "every UpgradeSpec matches its UpgradeId index and exact JSON save key");

            check(Fittings.Spec(UpgradeId.OverFireJets).Prerequisite == UpgradeId.BrickArch
                  && Fittings.All.Where(s => s.Id != UpgradeId.OverFireJets).All(s => s.Prerequisite == null),
                "OverFireJets declares BrickArch as its sole prerequisite");

            // Binary compatibility with pre-compiled RealisticFiring.Multiplayer.dll:
            // all 9 host-override members must remain public static bool? FieldInfo
            // members (never properties), and table-driven accessors must route directly
            // through those exact static fields.
            var fittingsType = typeof(Fittings);
            const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;
            var allHostFieldsValid = true;
            foreach (var spec in ExpectedSpecs)
            {
                var field = fittingsType.GetField(spec.HostField, PublicStatic);
                var property = fittingsType.GetProperty(spec.HostField, PublicStatic);
                if (field == null || field.FieldType != typeof(bool?) || property != null)
                    allHostFieldsValid = false;
            }
            check(allHostFieldsValid,
                "all 9 Fittings *FromHost overrides are public static bool? fields (not properties) for binary compatibility");

            // Save record round-trip preserves exact keys and unknown future keys.
            var bought = new bool[Fittings.All.Count];
            bought[(int)UpgradeId.BrickArch] = true;
            bought[(int)UpgradeId.S282Booster] = true;
            bought[(int)UpgradeId.AutomaticFireDoor] = true;
            var json = new JObject { ["futureKey"] = true };
            var wrote = Fittings.WriteRecord(json, bought);
            check(wrote && json["brickArch"]?.Value<bool>() == true
                  && json["s282Booster"]?.Value<bool>() == true
                  && json["automaticFireDoor"]?.Value<bool>() == true
                  && json["overFireJets"] == null
                  && json["futureKey"]?.Value<bool>() == true,
                "Fittings.WriteRecord writes only bought keys and preserves unknown keys");

            var readBack = new bool[Fittings.All.Count];
            Fittings.ReadRecord(json, readBack);
            check(readBack.SequenceEqual(bought), "Fittings.ReadRecord restores exact bought flags from JSON");

            Fittings.ReadRecord(null, readBack);
            check(readBack.All(b => !b), "Fittings.ReadRecord clears flags on a null save record");

            // Rule, prerequisite, lazy InCareer, and host-override evaluation.
            var settings = new Settings();
            var noneBought = new HashSet<UpgradeId>();
            var archOnly = new HashSet<UpgradeId> { UpgradeId.BrickArch };
            var archAndJets = new HashSet<UpgradeId> { UpgradeId.BrickArch, UpgradeId.OverFireJets };

            Fittings.ClearHostOverrides();
            try
            {
                // Verify each UpgradeSpec get/set/clear routes directly through its public static FieldInfo.
                var allFieldRouted = true;
                foreach (var spec in ExpectedSpecs)
                {
                    var field = fittingsType.GetField(spec.HostField, PublicStatic);
                    Fittings.SetHostOverride(spec.Id, true);
                    if (!Equals(field.GetValue(null), true))
                        allFieldRouted = false;
                    field.SetValue(null, false);
                    if (Fittings.GetHostOverride(spec.Id) != false)
                        allFieldRouted = false;
                    Fittings.SetHostOverride(spec.Id, null);
                    if (field.GetValue(null) != null)
                        allFieldRouted = false;
                }
                check(allFieldRouted,
                    $"Fittings.GetHostOverride/SetHostOverride route directly through the {ExpectedSpecs.Length} public static *FromHost fields");

                // Career BuyInCareer requires purchase; sandbox fits unless NeverFitted.
                check(!Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, () => true, noneBought.Contains)
                      && Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, () => true, archOnly.Contains)
                      && Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, () => false, noneBought.Contains),
                    "BuyInCareer requires purchase in career and fits automatically in sandbox");

                // The oil conversion changes what an engine is: Buy in career leaves sandbox S282s burning coal.
                var oilBought = new HashSet<UpgradeId> { UpgradeId.S282OilBurner };
                check(!Fittings.EvaluateFitted(UpgradeId.S282OilBurner, settings, () => false, noneBought.Contains)
                      && !Fittings.EvaluateFitted(UpgradeId.S282OilBurner, settings, () => true, noneBought.Contains)
                      && Fittings.EvaluateFitted(UpgradeId.S282OilBurner, settings, () => true, oilBought.Contains),
                    "the S282's oil conversion under Buy in career is fitted only once bought, in sandbox too");
                settings.S282OilBurner = UpgradeRule.AlwaysFitted;
                check(Fittings.EvaluateFitted(UpgradeId.S282OilBurner, settings, () => false, noneBought.Contains),
                    "Always fitted converts every S282, in sandbox too");
                settings.S282OilBurner = UpgradeRule.BuyInCareer;

                // InCareer is evaluated lazily: only in the BuyInCareer branch with no host override.
                var careerCalls = 0;
                Func<bool> countingCareer = () =>
                {
                    careerCalls++;
                    return true;
                };

                settings.BrickArch = UpgradeRule.AlwaysFitted;
                Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, countingCareer, noneBought.Contains);
                settings.BrickArch = UpgradeRule.NeverFitted;
                Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, countingCareer, noneBought.Contains);
                settings.BrickArch = UpgradeRule.BuyInCareer;
                Fittings.FromHost = true;
                Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, countingCareer, noneBought.Contains);
                Fittings.FromHost = null;
                Fittings.JetsFromHost = false;
                Fittings.EvaluateFitted(UpgradeId.OverFireJets, settings, countingCareer, noneBought.Contains);
                var callsWhenSkipped = careerCalls;

                Fittings.JetsFromHost = null;
                Fittings.EvaluateFitted(UpgradeId.BrickArch, settings, countingCareer, noneBought.Contains);
                check(callsWhenSkipped == 0 && careerCalls == 1,
                    "EvaluateFitted evaluates InCareer lazily only for BuyInCareer without a host override");

                // OverFireJets requires BrickArch even when OverFireJets is AlwaysFitted.
                settings.BrickArch = UpgradeRule.NeverFitted;
                settings.OverFireJets = UpgradeRule.AlwaysFitted;
                check(!Fittings.EvaluateFitted(UpgradeId.OverFireJets, settings, () => false, archAndJets.Contains),
                    "OverFireJets is never fitted when its BrickArch prerequisite is not fitted");

                settings.BrickArch = UpgradeRule.AlwaysFitted;
                check(Fittings.EvaluateFitted(UpgradeId.OverFireJets, settings, () => true, noneBought.Contains),
                    "OverFireJets fits when BrickArch is fitted and its own rule allows it");

                // Host override wins over local rules, while still honoring prerequisites.
                settings.BrickArch = UpgradeRule.BuyInCareer;
                settings.OverFireJets = UpgradeRule.NeverFitted;
                Fittings.SetHostOverride(UpgradeId.BrickArch, true);
                Fittings.SetHostOverride(UpgradeId.OverFireJets, true);
                check(Fittings.FromHost == true && Fittings.JetsFromHost == true
                      && Fittings.GetHostOverride(UpgradeId.OverFireJets) == true
                      && Fittings.EvaluateFitted(UpgradeId.OverFireJets, settings, () => true, noneBought.Contains),
                    "host overrides enable upgrades regardless of local settings and forward through static fields");

                Fittings.FromHost = false;
                check(!Fittings.EvaluateFitted(UpgradeId.OverFireJets, settings, () => false, archAndJets.Contains),
                    "host override disabling BrickArch also suppresses OverFireJets");

                var versionBefore = Fittings.Version;
                Fittings.ClearHostOverrides();
                check(Fittings.Version == versionBefore + 1
                      && Fittings.All.All(s => Fittings.GetHostOverride(s.Id) == null)
                      && ExpectedSpecs.All(s => fittingsType.GetField(s.HostField, PublicStatic).GetValue(null) == null),
                    "ClearHostOverrides resets all host override fields and increments Fittings.Version");
            }
            finally
            {
                Fittings.ClearHostOverrides();
            }
        }
    }
}
