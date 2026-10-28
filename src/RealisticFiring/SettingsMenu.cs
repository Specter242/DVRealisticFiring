using System;
using RealisticFiring.Visuals;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    /// <summary>The player's settings, grouped without changing their saved fields.</summary>
    internal sealed class SettingsMenu
    {
        private static readonly string[] Pages = { "DISPLAY", "FIRING", "BOILER", "ENGINE", "SOUND", "CONTROLS" };
        private static readonly string[] Quality = { "High", "Medium", "Low" };
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
                columns = width < Pixels(620f) ? 3 : Pages.Length;
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
                case 4: Sound(settings); break;
                case 5: Controls(settings); break;
            }
            GUILayout.Space(Pixels(4f));
            GUILayout.Label("Restart = applies after restarting the game. Use Unity Mod Manager's Save button to keep your changes.", help);
            GUILayout.Space(Pixels(8f));
            if (GUI.changed)
                settings.OnChange();
        }

        private void Display(Settings s, bool stackHandedOff)
        {
            Begin("FIREBOX", "Balance the detail inside the firebox against performance.");
            s.ShowZonedCoalBed = Toggle("Detailed firebox", s.ShowZonedCoalBed,
                "Coal heaps, flames, smoke, and firelight. Turn off to use the stock visuals. Applies immediately.");
            s.FireboxQuality = Choice("Firebox quality", s.FireboxQuality, Quality,
                "High: full detail. Medium: half the open-door samples. Low: a quarter. Firelight stays the same.");
            s.AshpanGlow = Slider("Ashpan glow", s.AshpanGlow, 0f, 3f, 1, "×",
                "Light beneath the engine, dimmed by ash. 0 turns it off.");
            End();

            Begin("CHIMNEY", stackHandedOff
                ? "Managed by Realistic Stack Effects. Use that mod's settings in Unity Mod Manager."
                : "Smoke, steam, and heat above the stack.");
            var enabled = GUI.enabled;
            GUI.enabled = enabled && !stackHandedOff;
            s.SmokeQuality = Choice("Smoke quality", s.SmokeQuality, Quality,
                "Medium: half-resolution smoke without its faintest parts. Low: fewer particles and less faint smoke too. In VR and forward rendering, both reduce particles instead of resolution.");
            s.SmokeSeason = Choice("Season", s.SmokeSeason, Seasons,
                "Controls how much white steam condenses. When DV Seasons is running, its weather is used instead.");
            s.RealisticSmoke = Toggle("Realistic chimney smoke", s.RealisticSmoke,
                "Use smoke driven by the realistic fire on supported locomotives.", restart: true);
            s.CustomSmokeEffects = Toggle("Custom smoke effects", s.CustomSmokeEffects,
                "Use the mod's chimney effects when their assets are available.", restart: true);
            s.HeatShimmer = Toggle("Heat shimmer", s.HeatShimmer,
                "Shimmer above the stack with custom smoke effects.", restart: true);
            GUI.enabled = enabled;
            End();
        }

        private void Firing(Settings s)
        {
            Begin("COAL FIRE", "How you build and maintain the fire.");
            s.ReplaceStockFireboxes = Toggle("Realistic coal fireboxes", s.ReplaceStockFireboxes,
                "Replace the fire model on supported coal locomotives, including the S282, S060, and JK3.", restart: true);
            s.ShovelfulMass = Slider("Coal per shovelful", s.Shovelful, Settings.MinShovelful, Settings.MaxShovelful, 1, "kg",
                "Affects supported coal locomotives. A normal shovelful is 3 kg.");
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
            s.ShowAimLabel = Toggle("Aimed zone label", s.ShowAimLabel, "Name the targeted grate zone under the crosshair.");
            s.ShowAimKeypad = Toggle("Aiming keypad", s.ShowAimKeypad, "Draw the grid while aiming with the keypad.");
            s.GrateLeverSensitivity = Slider("Grate lever sensitivity", s.GrateLeverSensitivity, 0.1f, 1f, 2, "×",
                "Lower for finer rocking. A short rock wiggles the grate; a half stroke shakes it.");
            End();
        }

        private void Boiler(Settings s)
        {
            Begin("INJECTORS", "Live-steam feed and its cab controls.");
            s.RealisticInjectors = Toggle("Realistic injectors", s.RealisticInjectors,
                "Use live-steam injectors on every steam locomotive.", restart: true);
            s.InjectorTechniqueAndHeat = Toggle("Priming & heat", s.InjectorTechniqueAndHeat,
                "Prime before opening. A hot injector must cool before it will take.");
            s.SeparateInjectorSteamValve = Toggle("Separate steam valve", s.SeparateInjectorSteamValve,
                "The injector lever controls water, with a separate Off / Prime / Feed steam valve.", restart: true);
            End();

            Begin("WATER & PROTECTION", "Keep the boiler clean and the crown sheet covered.");
            s.BoilerWaterImpurities = Toggle("Water impurities", s.BoilerWaterImpurities,
                "Dissolved solids build up and cause foaming and carryover. Blow down regularly; wash out to clear them.");
            s.BoilerWaterQuality = Choice("Feed water quality", s.BoilerWaterQuality, Water,
                "Better water brings fewer dissolved solids into the boiler.");
            s.FusiblePlugs = Toggle("Fusible plugs & low-water alarm", s.FusiblePlugs,
                "Fit the prototype's protection. A melted plug drowns the fire; the Hudson uses an alarm.", restart: true);
            End();
        }

        private void Engine(Settings s)
        {
            Begin("STEAM & FITTINGS", "Equipment fitted to the locomotives that support it.");
            s.RealisticCylinders = Toggle("Realistic cylinders", s.RealisticCylinders,
                "Valve events, clearance, cushioning, port flow, and blast-pipe back pressure on every steam locomotive.");
            s.RealisticOilBurners = Toggle("Santa Fe 2901 oil burner", s.RealisticOilBurners,
                "Firing valve, atomizer, oil heater, flarebacks, and flue sanding.", restart: true);
            s.FitBoosters = Toggle("Hudson booster", s.FitBoosters,
                "Fit the NYC Hudson's Franklin trailing-truck booster.", restart: true);
            s.FitFeedPumps = Toggle("Feedwater heaters & pumps", s.FitFeedPumps,
                "Fit the Hudson, 844, S484-840, and Santa Fe 2901 with their prototype equipment.", restart: true);
            End();

            Begin("MECHANICAL STOKER", "Feed, distribution, and cab controls.");
            s.RetrofitS282Stoker = Toggle("Fit the S282 with a stoker", s.RetrofitS282Stoker,
                "Add a mechanical stoker to the stock S282.", restart: true);
            s.StokerJams = Toggle("Stoker jams", s.StokerJams,
                "Lumps, foreign objects, or wet-coal packing can jam the screw. Reverse its engine to clear it.", restart: true);
            s.ShowStokerJetValves = Toggle("Cab jet valves", s.ShowStokerJetValves,
                "Show the stoker's distribution valves. Applies when the cab next loads.");
            End();
        }

        private void Sound(Settings s)
        {
            Begin("VOLUME", "Adjust individual effects. 1 is normal volume; 0 is silent.");
            s.OilBurnerVolume = Slider("Oil burner", s.OilBurnerVolume, 0f, 2f, 1, "×", "The roar and drumming of the burner.");
            s.FeedPumpVolume = Slider("Feed pump", s.FeedPumpVolume, 0f, 2f, 1, "×", "The locomotive's feed pump.");
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
            End();

            Begin("ON-SCREEN DISPLAYS", "The firing tablet works independently of the optional overlay.");
            s.ShowOverlay = Toggle("Fire overlay", s.ShowOverlay, "Show the Fire, Water, and Engine pages on screen.");
            s.ShowFiremanStatus = Toggle("Fireman status", s.ShowFiremanStatus, "Show what the fireman is doing at the top of the screen.");
            End();

            Begin("EQUIPMENT", "Place an item in front of you in a loaded game.");
            if (Action("Give firing tablet", "Hold it to read the engine, or fit it to a large gadget mount."))
                s.GiveFiringTablet = true;
            if (Action("Give fire iron", "Through the open door: click to slice, scroll to push or pull, Shift + scroll to move sideways."))
                s.GiveFireIron = true;
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
