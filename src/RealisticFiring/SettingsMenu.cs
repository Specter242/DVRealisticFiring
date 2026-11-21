using System;
using RealisticFiring.Visuals;
using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    /// <summary>The player's settings, grouped without changing their saved fields.</summary>
    internal sealed class SettingsMenu
    {
        internal static readonly string[] Pages = { "DISPLAY", "FIRING", "BOILER", "ENGINE", "UPGRADES", "SOUND", "CONTROLS" };
        private static readonly string[] Quality = { "High", "Medium", "Low" };
        private static readonly string[] UpgradeScopeChoices = { "Fleet-wide", "Per locomotive" };
        private static readonly string[] UpgradeChoices = { "Buy in career", "Always fitted", "Never fitted" };
        private static readonly string[] SmokeChoices = { "High", "Medium", "Low", "Auto" };
        private static readonly string[] Seasons = { "Summer", "Spring / autumn", "Winter" };
        private static readonly string[] Water = { "Good", "Average", "Bad" };
        private static readonly string[] Aim = { "Keypad", "Grate" };

        private int page, nextPage, columns = Pages.Length;
        private float width = 700f;
        private GUISkin skin;
        private float uiScale;
        private GUIStyle title, subtitle, heading, label, help, value, badge, card, button, selected, slider, thumb;

        public void Draw(Settings settings, bool stackHandedOff)
        {
            Styles();
            // Keep one hierarchy from Layout through Repaint. Switching it
            // while painting a resized window makes GUILayout read stale groups.
            if (Event.current.type == EventType.Layout)
            {
                page = nextPage;
                columns = width < Pixels(680f) ? 4 : Pages.Length;
            }
            var measure = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint && measure.width > 0f)
                width = measure.width;

            GUILayout.Space(Pixels(8f));
            GUILayout.Label("REALISTIC FIRING", title);
            GUILayout.Label("Fire, water, and steam", subtitle);
            GUILayout.Space(Pixels(14f));
            for (var start = 0; start < Pages.Length; start += columns)
            {
                GUILayout.BeginHorizontal();
                for (var i = start; i < Math.Min(start + columns, Pages.Length); i++)
                    if (GUILayout.Button(Pages[i], page == i ? selected : button, GUILayout.MinWidth(0f), GUILayout.Height(Pixels(32f))))
                    {
                        nextPage = i;
                        GUI.FocusControl(null);
                    }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(Pixels(12f));

            switch (page)
            {
                case 0: Display(settings, stackHandedOff); break;
                case 1: Firing(settings); break;
                case 2: Boiler(settings); break;
                case 3: Engine(settings); break;
                case 4: Upgrades(settings); break;
                case 5: Sound(settings); break;
                case 6: Controls(settings); break;
            }
            GUILayout.Space(Pixels(4f));
            GUILayout.Label("Restart = applies after restarting the game. Use Unity Mod Manager's Save button to keep your changes.", help);
            GUILayout.Space(Pixels(8f));
            if (GUI.changed)
                settings.OnChange();
        }

        private void Display(Settings s, bool stackHandedOff)
        {
            Begin("DIFFICULTY", s.IsRelaxed ? "Now: Relaxed. One click sets several options; each can still be changed on its own page."
                : s.IsStandard ? "Now: Standard. One click sets several options; each can still be changed on its own page."
                : "Now: your own mix. One click sets several options; each can still be changed on its own page.");
            if (Action("Relaxed", "For learning. Injectors take without priming and never overheat, the boiler water stays clean, thermal shock and superheater overheat care are off, rain leaves the coal dry, clinker is slow, the stoker does not jam (after a restart), and the fire overlay is shown."))
                s.ApplyRelaxed();
            if (Action("Standard", "The mod as it comes: all of those back on but thermal shock, which is off unless you switch it on."))
                s.ApplyStandard();
            End();

            Begin("FIREBOX", "Balance the detail inside the firebox against performance.");
            s.ShowZonedCoalBed = Toggle("Detailed firebox", s.ShowZonedCoalBed,
                "Coal heaps, flames, smoke, and firelight. Turn off to use the stock visuals. Applies immediately.");
            s.FireboxQuality = Choice("Firebox quality", s.FireboxQuality, Quality,
                "High: full detail. Medium: flames at half resolution, half the coal lumps, 20 surface updates/s; much cheaper leaning into the firebox. Low: fewer flame samples too, a quarter of the lumps, 10 updates/s. Firelight stays the same.");
            s.AshpanGlow = Slider("Ashpan glow", s.AshpanGlow, 0f, 3f, 1, "×",
                "Light beneath the engine, dimmed by ash. 0 turns it off.");
            End();

            Begin("CHIMNEY", stackHandedOff
                ? "Managed by Realistic Stack Effects. Use that mod's settings in Unity Mod Manager."
                : "Smoke, steam, and heat above the stack.");
            var enabled = GUI.enabled;
            GUI.enabled = enabled && !stackHandedOff;
            s.SmokeQuality = Choice("Smoke quality", s.SmokeQuality, SmokeChoices,
                "Medium: half-resolution smoke without its faintest parts. Low: fewer particles and less faint smoke too. In VR and forward rendering, both reduce particles instead of resolution. Auto: High, lowered while the frame rate is under 45 with smoke in the air and the lower quality helps, and raised again when it runs well.");
            s.SmokeSeason = Choice("Season", s.SmokeSeason, Seasons,
                "Controls how much white steam condenses. When DV Seasons is running, its weather is used instead.");
            s.SteamVisibility = Slider("Steam visibility", PlumeSettings.ClampSteamVisibility(s.SteamVisibility),
                PlumeSettings.MinSteamVisibility, PlumeSettings.MaxSteamVisibility, 1, "×",
                "Scales how strongly exhaust steam shows in the air. 1 is realistic for the weather; raise it for a fuller plume in dry summer air, or lower it for a subtler one.");
            s.WindStrength = Slider("Plume wind", PlumeSettings.ClampWindStrength(s.WindStrength),
                PlumeSettings.MinWindStrength, PlumeSettings.MaxWindStrength, 1, "×",
                "Scales how strongly the weather's wind drifts the plume. 1 is normal; 0 is still air.");
            s.HeatShimmer = Toggle("Heat shimmer", s.HeatShimmer,
                "Shimmer above the stack with custom smoke effects.");
            s.RealisticSmoke = Toggle("Realistic chimney smoke", s.RealisticSmoke,
                "Use smoke driven by the realistic fire on supported locomotives.", restart: true);
            s.CustomSmokeEffects = Toggle("Custom smoke effects", s.CustomSmokeEffects,
                "Use the mod's chimney effects when their assets are available.", restart: true);
            s.EngineSteam = Toggle("Steam around the engine", s.EngineSteam,
                "Draw the dynamo, cylinder cocks, safety valves, whistle, and leaks as the chimney's steam, by the weather. Needs custom smoke effects.", restart: true);
            GUI.enabled = enabled;
            End();
        }

        private void Firing(Settings s)
        {
            Begin("COAL FIRE", "How you build and maintain the fire.");
            s.ReplaceStockFireboxes = Toggle("Realistic coal fireboxes", s.ReplaceStockFireboxes,
                "Replace the fire model on supported coal locomotives, including the S282, S060, and JK3.", restart: true);
            s.LighterLightsCoal = Toggle("Light the coal from the lighter", s.LighterLightsCoal,
                "Light the coal straight from the lighter, as in the stock game, skipping the wood and rag. They still work. In multiplayer the host's setting applies.");
            var largest = Slider("Largest shovelful", s.Shovelful, Settings.MinShovelful, Settings.MaxShovelful, 1, "kg",
                "A full shovel on supported coal locomotives, and the fireman's (never under 3 kg for him). With the shovel in hand, the mouse wheel (or trigger + stick in VR) takes less, half a kilogram a click down to 1 kg. 6 kg by default.");
            // A new largest: the wheel starts again from a full shovel.
            if (largest != s.Shovelful)
                s.ScoopMass = 0f;
            s.ShovelfulMass = largest;
            s.StartupShovelBoost = Toggle("Bigger shovelfuls during the accelerated startup", s.StartupShovelBoost,
                "While the game's Steam Startup speeds up a cold engine's fire, each shovelful you scoop is bigger by as much, up to 12 kg (less in a small firebox): fewer throws. None at Steam Startup 2,500 %; a stoker's own feed is unchanged. In multiplayer the host's setting applies.");
            s.RainWetsCoal = Toggle("Rain wets the coal", s.RainWetsCoal,
                "Coal in open tenders and bunkers gets wet, catches slower, and can stick on a stoker table.");
            s.ClinkerTendency = Slider("Clinker tendency", s.ClinkerTendency, 0f, 20f, 1, "×",
                "1 is typical coal: about two hours of hard work between cleanings. 0 prevents clinker.");
            End();

            Begin("SHOVEL & GRATE", "Choose your aim and how finely the grate lever responds.");
            s.AimShovelThroughDoor = Toggle("Aim through the fire door", s.AimShovelThroughDoor,
                "Place shovelfuls where you point in the open fire door.");
            s.Aim = Choice("Aiming method", s.Aim, Aim,
                "Keypad: the door in thirds; the top throws to the front. Grate: the coal you look at, where visible.");
            var aimEnabled = GUI.enabled;
            GUI.enabled = aimEnabled && !HudPolicy.Hidden;
            s.ShowAimLabel = Toggle("Aimed zone label", s.ShowAimLabel, "Name the targeted grate zone under the crosshair.");
            s.ShowAimKeypad = Toggle("Aiming keypad", s.ShowAimKeypad, "Draw the grid while aiming with the keypad.");
            GUI.enabled = aimEnabled;
            s.GrateLeverSensitivity = Slider("Grate lever sensitivity", s.GrateLeverSensitivity, 0.1f, 1f, 2, "×",
                "Lower for finer rocking. A short rock wiggles the grate; a half stroke shakes it.");
            End();

            Begin("CABOOSE STOVE", "Warm the caboose with the mod's firewood, oiled rags, and lighter.");
            s.CabooseStove = Toggle("Caboose stove", s.CabooseStove,
                "Make the caboose's stove work with firewood, oiled rags, and the lighter, with firelight, interior warmth, and chimney smoke. Applies immediately.");
            End();
        }

        private void Boiler(Settings s)
        {
            Begin("INJECTORS", "Live-steam feed and its cab controls.");
            s.RealisticInjectors = Toggle("Realistic injectors", s.RealisticInjectors,
                "Use live-steam injectors on the mod's steam locomotives.", restart: true);
            s.InjectorTechniqueAndHeat = Toggle("Priming & heat", s.InjectorTechniqueAndHeat,
                "Prime before opening. A hot injector must cool before it will take.");
            s.SeparateInjectorSteamValve = Toggle("Separate steam valve", s.SeparateInjectorSteamValve,
                "The injector lever controls water, with a separate Off / Prime / Feed steam valve.", restart: true);
            End();

            Begin("WATER & PROTECTION", "Keep the boiler clean and the crown sheet covered.");
            s.BoilerWaterImpurities = Toggle("Water impurities", s.BoilerWaterImpurities,
                "Dissolved solids build up and cause foaming, carryover, and a choking gauge glass. Blow down regularly, blow through the glass, check the try cocks, or wash out to clear them.");
            s.BoilerWaterQuality = Choice("Feed water quality", s.BoilerWaterQuality, Water,
                "Better water brings fewer dissolved solids into the boiler.");
            s.FusiblePlugs = Toggle("Fusible plugs & low-water alarm", s.FusiblePlugs,
                "Fit the prototype's protection. A melted plug drowns the fire; the Hudson uses an alarm.", restart: true);
            s.BoilerThermalShock = Toggle("Boiler thermal shock", s.BoilerThermalShock,
                "Forcing a cold boiler hard stresses tubes and stays, causing slow leaks and efficiency loss until repaired at a service station. Off by default.");
            s.SuperheaterCare = Toggle("Superheater element care", s.SuperheaterCare,
                "Shutting off on a hot fire without steam flow overheats and scales the superheater elements. Crack the throttle, open the drifting valve, or ease the fire.");
            End();
        }

        private void Engine(Settings s)
        {
            Begin("STEAM & FITTINGS", "Equipment fitted to the locomotives that support it.");
            s.RealisticCylinders = Toggle("Realistic cylinders", s.RealisticCylinders,
                "Valve events, clearance, cushioning, port flow, and blast-pipe back pressure.");
            s.UnlistedLocomotives = Toggle("Unlisted steam locomotives", s.UnlistedLocomotives,
                "Also put the injectors, boiler water, superheater, and cylinders on steam locomotives this mod does not list. Unchecked on them: they may steam or feed badly.", restart: true);
            s.RealisticOilBurners = Toggle("Santa Fe 2901 oil burner", s.RealisticOilBurners,
                "Firing valve, atomizer, oil heater, flarebacks, and flue sanding.", restart: true);
            s.FitBoosters = Toggle("Hudson booster", s.FitBoosters,
                "Fit the NYC Hudson's Franklin trailing-truck booster.", restart: true);
            s.FitFeedPumps = Toggle("Feedwater heaters & pumps", s.FitFeedPumps,
                "Fit the Hudson, 844, S484-840, QJ, and Santa Fe 2901 with their prototype equipment.", restart: true);
            End();

            Begin("MECHANICAL STOKER", "Feed, distribution, and cab controls.");
            s.StokerJams = Toggle("Stoker jams", s.StokerJams,
                "Lumps, foreign objects, or wet-coal packing can jam the screw. Reverse its engine to clear it.", restart: true);
            s.ShowStokerJetValves = Toggle("Cab jet valves", s.ShowStokerJetValves,
                "Show the stoker's distribution valves. Applies when the cab next loads.");
            End();
        }

        private void Upgrades(Settings s)
        {
            // A changed rule refits the engines through Settings.OnChange, run in Draw on any change.
            Begin("SCOPE", "Choose whether career upgrades fit every eligible locomotive or one locomotive at a time.");
            s.UpgradeScope = Choice("Career upgrade scope", s.UpgradeScope, UpgradeScopeChoices,
                "Fleet-wide: buying an upgrade fits every eligible locomotive. " +
                "Per locomotive: Upgrades lists the S282s and S060s you own (Owned Vehicles, a restored engine); pick one by its ID and buy its upgrades for it alone. " +
                "Prior fleet-wide purchases stay fitted; new purchases apply only to the picked locomotive and are kept across saves. " +
                "Switching back to Fleet-wide hides per-locomotive purchases until you return to Per locomotive; nothing is lost.");
            End();

            Begin("FIREBOX & DOOR", "Fittings the stock engines start without in a career.");
            s.BrickArch = Choice("Brick arch", s.BrickArch, UpgradeChoices,
                "Without it the engines burn about 7 to 13 % more coal for the same work, steam about 13 % less at their most, and smoke more after firing. " +
                "Buy it in a career at a career manager's Upgrades screen; sandbox has it unless set to Never fitted.");
            s.OverFireJets = Choice("Over-fire jets", s.OverFireJets, UpgradeChoices,
                "Steam jets through the firebox sides, worked by a valve in the cab, that blow air over the fire and burn its smoke. " +
                "Fitted only with the brick arch. Buy them in a career at a career manager's Upgrades screen after the arch.");
            s.AutomaticFireDoor = Choice("Automatic fire door", s.AutomaticFireDoor, UpgradeChoices,
                "With the shovel in hand at the fire door, holding the jump key (or the shovel-hand trigger in VR) opens the door with air, and it shuts as soon as you let go. " +
                "Buy it for the S282 and S060 in a career at a career manager's Upgrades screen. The Hudson, 844, S484-840 and QJ have it as built, unless set to Never fitted.");
            End();

            Begin("DRIFTING FITTINGS", "Cylinder relief and lubrication when drifting with the throttle shut.");
            s.SniftingValves = Choice("Snifting valves", s.SniftingValves, UpgradeChoices,
                "Vacuum relief valves on the steam chests that let air in when drifting with the throttle shut, so the cylinders draw less smokebox gas: " +
                "drifting hooked up wears the engine a third as much. Buy them in a career at a career manager's Upgrades screen.");
            s.DriftingValve = Choice("Drifting valve", s.DriftingValve, UpgradeChoices,
                "A DRIFTING valve in the cab that feeds a little steam to the steam chests while drifting with the throttle shut: hooked up, " +
                "it spares most of the wear, for a little steam, and with by-pass valves ends the cylinders' braking. Buy it in a career at a career manager's Upgrades screen.");
            s.ByPassValves = Choice("By-pass valves", s.ByPassValves, UpgradeChoices,
                "Valves on the cylinders that join their ends when drifting with the throttle shut, so the engine drifts freely in any notch until it runs fast. " +
                "Buy them in a career at a career manager's Upgrades screen.");
            End();

            Begin("S282 RETROFITS", "Heavy fittings bought for the stock S282. Changing to or from Never fitted takes a restart.");
            s.S282Stoker = Choice("S282 stoker", s.S282Stoker, UpgradeChoices,
                "A mechanical stoker for the S282, worked from the cab, that feeds the fire from the tender. " +
                "Buy it in a career at a career manager's Upgrades screen. Changing to or from Never fitted takes a restart.");
            s.S282FeedPump = Choice("S282 feed pump", s.S282FeedPump, UpgradeChoices,
                "A Worthington feedwater heater and pump for the S282, worked from the cab, that feeds the boiler hot water heated by the exhaust. " +
                "Needs the realistic injectors. Buy it in a career at a career manager's Upgrades screen. Changing to or from Never fitted takes a restart.");
            s.S282Booster = Choice("S282 booster", s.S282Booster, UpgradeChoices,
                "A Franklin trailing-truck booster for the S282, worked from the cab by the reverser, for starting and slow climbing. " +
                "Buy it in a career at a career manager's Upgrades screen. Changing to or from Never fitted takes a restart.");
            End();
        }

        private void Sound(Settings s)
        {
#if !PUBLIC_RELEASE
            Begin("CHUFFS", "How the chimney's beats sound.");
            s.RealisticChuffs = Toggle("Realistic chuffs", s.RealisticChuffs,
                "Each beat as loud as the steam it lets go, the soft and hard sounds blending, each engine's own slightly uneven beat, " +
                "a drifting engine's panting, and echoes off walls, cuttings and tunnels. Off, the game's own chuffs.");
            s.ChuffVolume = Slider("Chuff volume", s.ChuffVolume, 0f, 200f, 0, "%", "How loud the realistic chuffs are. 100 is normal; 0 is silent.");
            End();
#endif

            Begin("VOLUME", "Adjust individual effects. 1 is normal volume; 0 is silent.");
            s.OilBurnerVolume = Slider("Oil burner", s.OilBurnerVolume, 0f, 2f, 1, "×", "The roar and drumming of the burner.");
            s.FeedPumpVolume = Slider("Feed pump", s.FeedPumpVolume, 0f, 2f, 1, "×", "The locomotive's feed pump.");
            s.LowWaterAlarmVolume = Slider("Low-water alarm", s.LowWaterAlarmVolume, 0f, 2f, 1, "×",
                "The warning whistle. Silent without steam pressure. Muting it leaves the low-water indication active.");
            s.SteamVentVolume = Slider("Steam blowing off", s.SteamVentVolume, 0f, 2f, 1, "×",
                "Injector overflow and spilled water, open shed steam hoses, and a melted fusible plug.");
            End();
        }

        private void Controls(Settings s)
        {
            Begin("KEY BINDINGS", "Click a key to rebind it. The hand pump works while its key is held.");
            s.OpenSettingsKey = Key("Open settings", s.OpenSettingsKey);
            s.HandPumpKey = Key("Hand pump", s.HandPumpKey);
            s.ShakeGrateKey = Key("Shake grate", s.ShakeGrateKey);
            s.ToggleOverlayKey = Key("Fire overlay", s.ToggleOverlayKey);
            s.FiremanKey = Key("Fireman's orders", s.FiremanKey);
            s.ShowControlsKey = Key("Show added controls", s.ShowControlsKey);
            End();

            Begin("FIREMAN'S ORDERS BY KEY", "Unbound until you bind them. Ctrl, Shift, or Alt may go with a key (Alt+1, say). They order the fireman on your engine.");
            s.FiremanOffKey = Key("Fireman: off", s.FiremanOffKey);
            s.FiremanKeepAliveKey = Key("Fireman: keep it alive", s.FiremanKeepAliveKey);
            s.FiremanShuntingKey = Key("Fireman: shunting", s.FiremanShuntingKey);
            s.FiremanRoadKey = Key("Fireman: road", s.FiremanRoadKey);
            s.FiremanNextKey = Key("Fireman: next order", s.FiremanNextKey);
            s.FiremanHeavyAheadKey = Key("Call: heavy work ahead", s.FiremanHeavyAheadKey);
            s.FiremanEasingOffKey = Key("Call: easing off", s.FiremanEasingOffKey);
            End();

            Begin("ON-SCREEN DISPLAYS", "The firing tablet works independently of the optional overlay.");
            var displayEnabled = GUI.enabled;
            GUI.enabled = displayEnabled && !HudPolicy.Hidden;
            s.ShowOverlay = Toggle("Fire overlay", s.ShowOverlay, "Show the Fire, Water, and Engine pages on screen.");
            s.ShowFiremanStatus = Toggle("Fireman status", s.ShowFiremanStatus, "Show what the fireman is doing at the top of the screen.");
            if (Visuals.CabControlSpotlight.Supported)
            {
                if (Action("Show added controls", "Briefly highlights every cab control added or reworked by Realistic Firing on the current locomotive (~10 s; press again to hide)."))
                    s.ShowAddedControls = true;
            }
            else
                GUILayout.Label("Show added controls: desktop only (floating screen labels are not visible in VR).", help);
            GUI.enabled = displayEnabled;
            if (HudPolicy.Hidden)
                GUILayout.Label("This host requires cab instruments. RF's on-screen displays and aiming helpers are hidden; your own preferences are kept.", help);
            GUI.enabled = displayEnabled && !Net.IsClient;
            s.HostHidesHud = Toggle("Host: require cab instruments", s.HostHidesHud,
                "In multiplayer, hide RF's overlay, fireman's orders and status, and aiming helpers for everyone. Changes apply live. The physical tablet, manual, and safety alarms remain available.");
            GUI.enabled = displayEnabled;
            End();

            Begin("EQUIPMENT", "Place an item in front of you in a loaded game.");
            // Merged into the Comms Tablet, the firing tablet is retired and not given.
            if (CommsTabletLink.MergedLately)
            {
                GUILayout.Label(CommsTabletLink.RetiredNotice + ".", help);
                GUILayout.Space(Pixels(10f));
            }
            else if (Action("Give firing tablet", "Hold it to read the engine, or fit it to a large gadget mount."))
                s.GiveFiringTablet = true;
            if (Action(EquipmentActions.Labels[EquipmentActions.Manual], EquipmentActions.Help[EquipmentActions.Manual]))
                s.GiveFiringManual = true;
            if (Action(EquipmentActions.Labels[EquipmentActions.Checklists], EquipmentActions.Help[EquipmentActions.Checklists]))
                s.GiveFiringChecklists = true;
            if (Action(EquipmentActions.Labels[EquipmentActions.FireIron], EquipmentActions.Help[EquipmentActions.FireIron]))
                s.GiveFireIron = true;
            if (Action(EquipmentActions.Labels[EquipmentActions.WoodCrate], EquipmentActions.Help[EquipmentActions.WoodCrate]))
                s.GiveWoodCrate = true;
            if (Action(EquipmentActions.Labels[EquipmentActions.RagTin], EquipmentActions.Help[EquipmentActions.RagTin]))
                s.GiveRagTin = true;
            End();

            Begin("RECOVERY", "For a locomotive stranded with a melted fusible plug.");
            if (Action("Renew current boiler", "Repair the boiler and renew its plug, as a service station's repair does."))
                s.RenewBoilerNow = true;
            End();
        }

        private void Begin(string name, string description)
        {
            GUILayout.BeginVertical(card);
            GUILayout.Label(name, heading);
            GUILayout.Label(description, help);
            GUILayout.Space(Pixels(8f));
        }

        private static void End()
        {
            GUILayout.EndVertical();
            GUILayout.Space(Pixels(10f));
        }

        private bool Toggle(string name, bool current, string description, bool restart = false)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            if (restart)
                GUILayout.Label("Restart", badge, GUILayout.Width(Pixels(70f)));
            var next = GUILayout.Toggle(current, current ? "ON" : "OFF", current ? selected : button, GUILayout.Width(Pixels(68f)), GUILayout.Height(Pixels(28f)));
            GUILayout.EndHorizontal();
            GUILayout.Label(description, help);
            GUILayout.Space(Pixels(12f));
            return next;
        }

        private T Choice<T>(string name, T current, string[] choices, string description) where T : struct, Enum
        {
            GUILayout.Label(name, label);
            var index = Convert.ToInt32(current);
            GUILayout.BeginHorizontal();
            for (var i = 0; i < choices.Length; i++)
                if (GUILayout.Toggle(index == i, choices[i], index == i ? selected : button, GUILayout.Height(Pixels(28f))))
                    index = i;
            GUILayout.EndHorizontal();
            GUILayout.Label(description, help);
            GUILayout.Space(Pixels(12f));
            return (T)Enum.ToObject(typeof(T), index);
        }

        private float Slider(string name, float current, float min, float max, int precision, string unit, string description)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            GUILayout.Label(current.ToString("F" + precision, System.Globalization.CultureInfo.InvariantCulture) + " " + unit, value, GUILayout.Width(Pixels(84f)));
            GUILayout.EndHorizontal();
            var next = GUILayout.HorizontalSlider(current, min, max, slider, thumb, GUILayout.Height(Pixels(22f)));
            GUILayout.Label(description, help);
            GUILayout.Space(Pixels(12f));
            return (float)Math.Round(next, precision);
        }

        private KeyBinding Key(string name, KeyBinding binding)
        {
            binding = binding ?? new KeyBinding();
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, label, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            UnityModManager.UI.DrawKeybindingSmart(binding, name, button, GUILayout.Width(Pixels(140f)), GUILayout.Height(Pixels(30f)));
            GUILayout.EndHorizontal();
            GUILayout.Space(Pixels(6f));
            return binding;
        }

        private bool Action(string name, string description)
        {
            var pressed = GUILayout.Button(name, button, GUILayout.Height(Pixels(32f)));
            GUILayout.Label(description, help);
            GUILayout.Space(Pixels(10f));
            return pressed;
        }

        private void Styles()
        {
            if (skin == GUI.skin && Mathf.Approximately(uiScale, Pixels(1f)) && card != null && card.normal.background != null)
                return;
            skin = GUI.skin;
            uiScale = Pixels(1f);
            title = Text(24, OverlayArt.Cream, FontStyle.Bold);
            subtitle = Text(13, OverlayArt.Soft);
            heading = Text(14, OverlayArt.Brass, FontStyle.Bold);
            label = Text(14, OverlayArt.Cream);
            help = Text(12, OverlayArt.Soft);
            value = Text(14, OverlayArt.Brass, FontStyle.Bold);
            value.alignment = TextAnchor.MiddleRight;
            badge = Text(11, OverlayArt.Soft);
            badge.alignment = TextAnchor.MiddleCenter;
            card = new GUIStyle { normal = { background = OverlayArt.Panel }, border = Insets(10, 10, 10, 10), padding = Insets(18, 18, 16, 8) };
            button = new GUIStyle(skin.button)
            {
                fontSize = Pixels(12), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                padding = Insets(8, 8, 4, 4), margin = Insets(2, 2, 2, 2), border = Insets(5, 5, 5, 5),
                normal = { background = OverlayArt.PlateOff, textColor = OverlayArt.Soft },
                hover = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                active = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                onNormal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                onHover = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
                onActive = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream },
            };
            selected = new GUIStyle(button) { normal = { background = OverlayArt.PlateOn, textColor = OverlayArt.Cream } };
            slider = new GUIStyle(skin.horizontalSlider) { normal = { background = OverlayArt.PlateOff }, border = Insets(3, 3, 3, 3), fixedHeight = Pixels(8f), margin = Insets(4, 4, 8, 8) };
            thumb = new GUIStyle(skin.horizontalSliderThumb)
            {
                normal = { background = OverlayArt.PlateOn }, hover = { background = OverlayArt.PlateOn }, active = { background = OverlayArt.PlateOn },
                border = Insets(4, 4, 4, 4), fixedWidth = Pixels(16f), fixedHeight = Pixels(22f),
            };
        }

        private static float Pixels(float pixels) => UnityModManager.UI.Scale(pixels);
        private static int Pixels(int pixels) => UnityModManager.UI.Scale(pixels);
        private static RectOffset Insets(int left, int right, int top, int bottom) => new RectOffset(Pixels(left), Pixels(right), Pixels(top), Pixels(bottom));

        private GUIStyle Text(int size, Color color, FontStyle weight = FontStyle.Normal) => new GUIStyle(skin.label)
        {
            fontSize = Pixels(size), fontStyle = weight, wordWrap = true, alignment = TextAnchor.MiddleLeft,
            margin = Insets(2, 2, 2, 2), padding = Insets(0, 0, 1, 1), normal = { textColor = color },
        };
    }
}
