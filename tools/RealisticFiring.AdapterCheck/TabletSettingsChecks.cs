using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Serialization;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// The settings as a settings list reads them from their Draw attributes
    /// (the Comms Tablet's Settings app; Unity Mod Manager's window is
    /// <see cref="SettingsMenu"/>): the window's tabs as headings, in its
    /// order, with short labels and tooltips; no one-time actions, developer
    /// settings, or saved state; the chimney's six left out while Realistic
    /// Stack Effects draws the chimneys; the upgrade rules refitting the
    /// engines through OnChange; and a Settings.xml saved in the old order
    /// loading whole. With the Comms Tablet's assembly named, its own reader
    /// reads them.
    /// </summary>
    internal static class TabletSettingsChecks
    {
        private static readonly string[] Chimney = { "SmokeQuality", "SmokeSeason", "RealisticSmoke", "CustomSmokeEffects", "HeatShimmer", "EngineSteam" };

        private static readonly string[] UpgradeRules = { "BrickArch", "OverFireJets", "SniftingValves", "DriftingValve", "ByPassValves", "S282Stoker", "S282FeedPump", "S282Booster" };

        private static readonly string[] OneTime = { "GiveFireIron", "GiveFiringTablet", "GiveFiringManual", "GiveWoodCrate", "GiveRagTin", "RenewBoilerNow" };

        // The window's tabs (its Controls split into On screen and Keys) and their rows, in order.
        private static readonly (string Heading, string[] Fields)[] Layout =
        {
            ("Display", new[] { "ShowZonedCoalBed", "FireboxQuality", "AshpanGlow" }.Concat(Chimney).ToArray()),
            ("Firing", new[] { "ReplaceStockFireboxes", "LighterLightsCoal", "ShovelfulMass", "RainWetsCoal", "ClinkerTendency",
                "AimShovelThroughDoor", "Aim", "ShowAimLabel", "ShowAimKeypad", "GrateLeverSensitivity" }),
            ("Boiler", new[] { "RealisticInjectors", "InjectorTechniqueAndHeat", "SeparateInjectorSteamValve",
                "BoilerWaterImpurities", "BoilerWaterQuality", "FusiblePlugs" }),
            ("Engine", new[] { "RealisticCylinders", "UnlistedLocomotives", "RealisticOilBurners", "FitBoosters", "FitFeedPumps" }
                .Concat(UpgradeRules).Concat(new[] { "StokerJams", "ShowStokerJetValves" }).ToArray()),
            ("Sound", new[] { "OilBurnerVolume", "FeedPumpVolume", "LowWaterAlarmVolume", "SteamVentVolume" }),
            ("On screen", new[] { "ShowOverlay", "ShowFiremanStatus", "HostHidesHud" }),
            ("Keys", new[] { "OpenSettingsKey", "HandPumpKey", "ShakeGrateKey", "ToggleOverlayKey", "FiremanKey" }),
        };

        private const string HandedOffRule = "#ChimneyHandedOff|True";

        // A player's Settings.xml as 1.23.0 wrote it (its field order), set away from the defaults.
        private const string OldOrder = "<?xml version=\"1.0\" encoding=\"utf-8\"?><Settings>"
            + "<ReplaceStockFireboxes>false</ReplaceStockFireboxes><RealisticInjectors>false</RealisticInjectors>"
            + "<BoilerWaterQuality>Bad</BoilerWaterQuality><ClinkerTendency>2.5</ClinkerTendency><SteamVentVolume>0.5</SteamVentVolume>"
            + "<SmokeQuality>Low</SmokeQuality><FireboxQuality>Medium</FireboxQuality><SmokeSeason>Winter</SmokeSeason>"
            + "<ShovelfulMass>4.5</ShovelfulMass><Aim>Grate</Aim>"
            + "<OpenSettingsKey><keyCode>F9</keyCode><modifiers>0</modifiers></OpenSettingsKey>"
            + "<GrateLeverSensitivity>0.5</GrateLeverSensitivity>"
            + "<GiveFireIron>true</GiveFireIron><GiveFiringTablet>true</GiveFiringTablet><GiveFiringManual>true</GiveFiringManual>"
            + "<GiveWoodCrate>true</GiveWoodCrate><GiveRagTin>true</GiveRagTin>"
            + "<ShowOverlay>false</ShowOverlay><HostHidesHud>true</HostHidesHud><OverlayPage>2</OverlayPage>"
            + "<ShowZonedCoalBed>false</ShowZonedCoalBed><StokerJams>false</StokerJams>"
            + "<BrickArch>NeverFitted</BrickArch><OverFireJets>AlwaysFitted</OverFireJets><S282Booster>NeverFitted</S282Booster>"
            + "<RenewBoilerNow>true</RenewBoilerNow><LogCoalDeliveries>true</LogCoalDeliveries>"
            + "<KeysVersion>1</KeysVersion><DefaultsVersion>3</DefaultsVersion></Settings>";

        internal static void Run(Action<bool, string> check)
        {
            var drawn = Drawn().ToList();
            var expected = Layout.SelectMany(t => t.Fields).ToList();
            check(drawn.Select(d => d.Field.Name).SequenceEqual(expected),
                $"a settings list shows the window's rows in its order and nothing else ({drawn.Count} rows; no one-time action, developer setting, or saved state)"
                + (drawn.Select(d => d.Field.Name).SequenceEqual(expected) ? "" : ": " + string.Join(", ", drawn.Select(d => d.Field.Name))));
            var headings = drawn.Where(d => d.Heading != null).Select(d => (d.Heading, d.Field.Name)).ToList();
            check(headings.SequenceEqual(Layout.Select(t => (t.Heading, t.Fields[0]))),
                "each of the window's tabs heads its rows: " + string.Join(", ", headings.Select(h => h.Heading)));
            // Unity Mod Manager's tooltip box is one line per line of text: broken at about 80 characters.
            var tooLong = drawn.Where(d => string.IsNullOrEmpty(d.Draw.Label) || d.Draw.Label.Length > 60 || string.IsNullOrEmpty(d.Draw.Tooltip)
                                           || d.Draw.Tooltip.Split('\n').Any(line => line.Length > 80)).ToList();
            check(tooLong.Count == 0, $"every row has a label of at most 60 characters (longest {drawn.Max(d => d.Draw.Label?.Length ?? 0)}) and a tooltip in lines of at most 80"
                + (tooLong.Count > 0 ? ": not " + string.Join(", ", tooLong.Select(d => d.Field.Name)) : ""));
            check(Chimney.All(name => drawn.Any(d => d.Field.Name == name && d.Draw.InvisibleOn == HandedOffRule))
                  && drawn.Where(d => !Chimney.Contains(d.Field.Name)).All(d => d.Draw.VisibleOn == null && d.Draw.InvisibleOn == null),
                "the chimney's six, and only they, hide while Realistic Stack Effects draws the chimneys");
            check(UpgradeRules.All(name => drawn.Any(d => d.Field.Name == name && d.Draw.Type == DrawType.ToggleGroup)),
                "the five upgrade rules are on it, as choices");
            check(OneTime.All(name => typeof(Settings).GetField(name)?.GetCustomAttributes(typeof(XmlIgnoreAttribute), false).Length == 1),
                "the window's one-time give and renew switches are never saved");

            // The property the chimney's rule reads: an instance bool, as Unity Mod Manager's
            // #Property rule wants, following the hand-off, and not saved.
            var property = typeof(Settings).GetProperty(HandedOffRule.Split('|')[0].TrimStart('#'), BindingFlags.Instance | BindingFlags.Public);
            var handedOff = AccessTools.Property(typeof(StackEffectsLink), nameof(StackEffectsLink.HandedOff));
            var was = StackEffectsLink.HandedOff;
            try
            {
                var settings = new Settings();
                handedOff.SetValue(null, true);
                var on = property?.GetValue(settings) is bool a && a;
                handedOff.SetValue(null, false);
                var off = property?.GetValue(settings) is bool b && b;
                check(property != null && property.PropertyType == typeof(bool) && !property.CanWrite && on && !off,
                    "the chimney's rule reads an instance property that follows the hand-off");
            }
            finally
            {
                handedOff.SetValue(null, was);
            }

            // A change of upgrade rule refits the engines once, from the window or the tablet alike; other changes do not.
            var clinker = Core.FireTuning.Default.ClinkerTendency;
            try
            {
                var settings = new Settings();
                settings.OnChange();
                var version = RealisticFiring.Upgrades.Fittings.Version;
                settings.ClinkerTendency = 2f;
                settings.OnChange();
                var quiet = RealisticFiring.Upgrades.Fittings.Version == version;
                var refits = new List<int>();
                foreach (var name in UpgradeRules)
                {
                    var field = typeof(Settings).GetField(name);
                    field.SetValue(settings, RealisticFiring.Upgrades.UpgradeRule.NeverFitted);
                    settings.OnChange();
                    settings.OnChange();
                    refits.Add(RealisticFiring.Upgrades.Fittings.Version - version);
                }
                check(quiet && refits.SequenceEqual(Enumerable.Range(1, UpgradeRules.Length)),
                    $"OnChange refits the engines once for each changed upgrade rule and not for other settings ({string.Join(", ", refits)})");
                var loaded = new Settings { BrickArch = RealisticFiring.Upgrades.UpgradeRule.NeverFitted };
                version = RealisticFiring.Upgrades.Fittings.Version;
                loaded.OnChange();
                check(RealisticFiring.Upgrades.Fittings.Version == version, "the first OnChange, at load, only notes the rules");
            }
            finally
            {
                Core.FireTuning.Default.ClinkerTendency = clinker;
            }

            // An older Settings.xml loads whole into the new order; the one-time switches it kept are not read, nor written.
            var serializer = new XmlSerializer(typeof(Settings));
            Settings old;
            using (var reader = new StringReader(OldOrder))
                old = (Settings)serializer.Deserialize(reader);
            bool Kept(Settings s) => !s.ReplaceStockFireboxes && !s.RealisticInjectors && s.BoilerWaterQuality == Core.FeedWaterQuality.Bad
                && s.ClinkerTendency == 2.5f && s.SteamVentVolume == 0.5f && s.SmokeQuality.ToString() == "Low"
                && s.FireboxQuality == Core.FireboxQuality.Medium && s.SmokeSeason.ToString() == "Winter"
                && s.ShovelfulMass == 4.5f && s.Aim == ShovelAim.Grate && s.OpenSettingsKey.keyCode == KeyCode.F9
                && s.GrateLeverSensitivity == 0.5f && !s.ShowOverlay && s.HostHidesHud && s.OverlayPage == 2 && !s.ShowZonedCoalBed
                && !s.StokerJams && s.BrickArch == RealisticFiring.Upgrades.UpgradeRule.NeverFitted
                && s.OverFireJets == RealisticFiring.Upgrades.UpgradeRule.AlwaysFitted
                && s.S282Stoker == RealisticFiring.Upgrades.UpgradeRule.BuyInCareer
                && s.S282Booster == RealisticFiring.Upgrades.UpgradeRule.NeverFitted && s.LogCoalDeliveries
                && s.KeysVersion == 1 && s.DefaultsVersion == 3;
            check(Kept(old) && !old.GiveFireIron && !old.GiveFiringTablet && !old.GiveFiringManual && !old.GiveWoodCrate && !old.GiveRagTin && !old.RenewBoilerNow,
                "a Settings.xml in 1.23.0's order loads every value; the give and renew switches it kept on are not read");
            string saved;
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, old);
                saved = writer.ToString();
            }
            Settings again;
            using (var reader = new StringReader(saved))
                again = (Settings)serializer.Deserialize(reader);
            check(Kept(again) && OneTime.All(name => !saved.Contains("<" + name)) && !saved.Contains("ChimneyHandedOff") && !saved.Contains("appliedRules"),
                "saved again and loaded, it keeps every value, and writes no one-time switch or hand-off");
        }

        /// <summary>The Comms Tablet's own reader (its ModSettingsLink.Walk), from its assembly: the rows it shows, with and without the hand-off.</summary>
        internal static void CheckWithHost(Assembly host, Action<bool, string> check)
        {
            var link = host.GetType("CommsTablet.Tablet.ModSettingsLink", false);
            var rowType = host.GetType("CommsTablet.Tablet.SettingRow", false);
            var walk = link?.GetMethod("Walk", BindingFlags.NonPublic | BindingFlags.Static);
            if (walk == null || rowType == null || walk.GetParameters().Length != 7)
            {
                Console.WriteLine("skip the Comms Tablet named has no settings reader of the form checked (ModSettingsLink.Walk); name a build from its ninth slice on");
                return;
            }
            List<(string Kind, string Label, string Field)> Rows(bool chimneyHandedOff)
            {
                var handedOff = AccessTools.Property(typeof(StackEffectsLink), nameof(StackEffectsLink.HandedOff));
                var was = StackEffectsLink.HandedOff;
                try
                {
                    handedOff.SetValue(null, chimneyHandedOff);
                    var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType));
                    walk.Invoke(null, new object[] { new Settings(), typeof(Settings), DrawFieldMask.OnlyDrawAttr, null, rows, null, 0 });
                    return rows.Cast<object>().Select(r => (rowType.GetField("Kind", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(r).ToString(),
                        (string)rowType.GetField("Label", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(r),
                        (rowType.GetField("Field", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(r) as FieldInfo)?.Name)).ToList();
                }
                finally
                {
                    handedOff.SetValue(null, was);
                }
            }
            var alone = Rows(false);
            var beside = Rows(true);
            string Count(List<(string Kind, string Label, string Field)> rows) =>
                string.Join(", ", rows.GroupBy(r => r.Kind).Select(g => $"{g.Count()} {g.Key}"));
            var expected = Layout.SelectMany(t => new[] { t.Heading }.Concat(t.Fields)).ToList();
            check(alone.Select(r => r.Kind == "Heading" ? r.Label : r.Field).SequenceEqual(expected),
                $"the Comms Tablet reads the window's tabs and rows, in order ({Count(alone)})");
            check(beside.Select(r => r.Kind == "Heading" ? r.Label : r.Field).SequenceEqual(expected.Where(e => !Chimney.Contains(e))),
                $"with Realistic Stack Effects drawing the chimneys, it leaves out the chimney's six ({Count(beside)})");
            check(alone.Where(r => UpgradeRules.Contains(r.Field)).All(r => r.Kind == "Choice") && alone.Count(r => UpgradeRules.Contains(r.Field)) == UpgradeRules.Length,
                "and offers the five upgrade rules as choices");
        }

        private static IEnumerable<(string Heading, FieldInfo Field, DrawAttribute Draw)> Drawn()
        {
            foreach (var field in typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var draw = field.GetCustomAttributes(typeof(DrawAttribute), false).OfType<DrawAttribute>().LastOrDefault();
                if (draw == null || draw.Type == DrawType.Ignore)
                    continue;
                var header = field.GetCustomAttributes(typeof(HeaderAttribute), false).OfType<HeaderAttribute>().FirstOrDefault();
                yield return (header?.header, field, draw);
            }
        }
    }
}
