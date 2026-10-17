using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Header("Fire model")]
        [Draw("Replace supported coal fireboxes, including the S282, S060, and JK3 (restart required)")]
        public bool ReplaceStockFireboxes = true;

        [Draw("Realistic injectors on every steam loco (restart required)")]
        public bool RealisticInjectors = true;

        [Draw("Injector technique and heat: prime it before opening it; a hot injector won't take")]
        public bool InjectorTechniqueAndHeat = true;

        [Draw("Separate injector steam valve: the injector lever becomes the water valve, with an Off / Prime / Feed steam lever beside it (restart required)")]
        public bool SeparateInjectorSteamValve = true;

        [Draw("Boiler water impurities: they build up as the water boils off until it foams (a lively, high glass, then water carried over to the cylinders); short blowdowns keep them down, a washout clears them")]
        public bool BoilerWaterImpurities = true;

        [Draw("Feed water quality: how much dissolved solids it brings", DrawType.ToggleGroup)]
        public Core.FeedWaterQuality BoilerWaterQuality = Core.FeedWaterQuality.Average;

        [Draw("Fusible plugs where the prototypes had them (the Hudson has a low-water alarm): a bared crown sheet melts the plug, which drowns the fire instead of the boiler failing (needs a restart)")]
        public bool FusiblePlugs = true;

        [Draw("Realistic oil burner on the Santa Fe 2901: firing valve, atomizer, oil heater, flarebacks, flue sanding (needs a restart)")]
        public bool RealisticOilBurners = true;

        [Draw("Realistic cylinders on every steam loco: valve events from the gear (lead, release, compression), clearance and cushion, steam through the opening port, and the blast pipe's back pressure")]
        public bool RealisticCylinders = true;

        [Draw("Fit the NYC Hudson with its Franklin trailing-truck booster (needs a restart)")]
        public bool FitBoosters = true;

        [Draw("Fit feedwater heaters and pumps where the prototypes had them: the Hudson, 844, S484-840, and Santa Fe 2901 (needs a restart)")]
        public bool FitFeedPumps = true;

        [Draw("Rain wets the coal in open tenders and bunkers: wet coal catches slower and takes heat to dry, and sticks on a stoker's table")]
        public bool RainWetsCoal = true;

        [Draw("Coal's clinker tendency, × (1 typical coal, about two hours of hard work between cleanings; 0 never; raise it to see clinker sooner)", DrawType.Slider, Min = 0, Max = 20, Precision = 1)]
        public float ClinkerTendency = 1f;

        [Draw("Glow of the fire in the ashpan, under the engine, × (0 off): bright over clean grates, dimmed by dead ash, flaring when fire drops in", DrawType.Slider, Min = 0, Max = 3, Precision = 1)]
        public float AshpanGlow = 1f;

        [Draw("Oil burner's roar and drumming, volume × (0 off)", DrawType.Slider, Min = 0, Max = 2, Precision = 1)]
        public float OilBurnerVolume = 1f;

        [Draw("Feed pump's sound, volume × (0 off)", DrawType.Slider, Min = 0, Max = 2, Precision = 1)]
        public float FeedPumpVolume = 1f;

        [Draw("Realistic chimney smoke on locomotives with a realistic firebox (restart required)")]
        public bool RealisticSmoke = true;

        [Draw("Use the custom smoke effects when installed (restart required)")]
        public bool CustomSmokeEffects = true;

        [Draw("Heat shimmer over the stack (custom smoke effects; restart required)")]
        public bool HeatShimmer = true;

        [Header("Shovel")]
        [Draw("Coal per standard shovelful on supported coal locomotives, kg (0 = the locomotive's stock shovel size)", Min = 0, Precision = 1)]
        public float ShovelfulMass = 3f;

        [Draw("Aim shovelfuls by where you point in the fire door")]
        public bool AimShovelThroughDoor = true;

        // A player's request (Rhasma2 on Nexus, 2026-10-16): the grate aim
        // back beside the keypad, which reaches the front of bigger boxes.
        [Draw("How to aim: Keypad, the firehole in thirds (the top third throws to the front of the grate); Grate, the spot on the fire you look at (on some engines the front is out of sight)", DrawType.ToggleGroup)]
        public ShovelAim Aim = ShovelAim.Keypad;

        [Draw("Name the aimed zone under the crosshair")]
        public bool ShowAimLabel = true;

        [Draw("Draw the fire door's aiming keypad while aiming with the keypad")]
        public bool ShowAimKeypad = true;

        [Header("Brakes")]
        [Draw("Click the independent brake lever to bail off: the engine's own brakes release from a train brake application, the train's stay on (every locomotive)")]
        public bool ClickToBailOff = true;

        [Header("Controls")]
        [Draw("Open these settings (Unity Mod Manager's Ctrl+F10 is taken by some desktops)")]
        public KeyBinding OpenSettingsKey = Bind(KeyCode.F8);

        [Draw("Work the boiler hand pump (hold)")]
        public KeyBinding HandPumpKey = Bind(KeyCode.C);

        [Draw("Wash out the boiler and fill it from a hydrant (on a service track: a service station with water, a loco parking track, or a roundhouse stall; boiler cold)")]
        public KeyBinding HydrantKey = Bind(KeyCode.F9);

        [Draw("Shake grate")]
        public KeyBinding ShakeGrateKey = Bind(KeyCode.Z);

        [Draw("Grate shaker (fire-dump) lever's drag, × (lower for finer rocking: a short rock wiggles the grate, a half stroke shakes it)", DrawType.Slider, Min = 0.1f, Max = 1f, Precision = 2)]
        public float GrateLeverSensitivity = 0.35f;

        [Draw("Put a fire iron (slice bar) in front of me (unticks itself). Through the open door: click slices, the mouse wheel pushes and pulls, Shift with the wheel pushes sideways")]
        public bool GiveFireIron;

        [Draw("Toggle fire overlay")]
        public KeyBinding ToggleOverlayKey = Bind(KeyCode.F6);

        [Draw("Show fire overlay")]
        public bool ShowOverlay;

        // The overlay's page: 0 the fire (or burner), 1 the water, 2 the engine.
        public int OverlayPage;

        [Draw("Fireman's orders: show or hide his window (hold Alt to free the mouse and click it)")]
        public KeyBinding FiremanKey = Bind(KeyCode.F7);

        [Draw("Show what the fireman is doing at the top of the screen")]
        public bool ShowFiremanStatus = true;

        [Draw("Show each grate zone's coal inside the firebox")]
        public bool ShowZonedCoalBed = true;

        [Draw("Put the stoker's jet valves in the cab (takes effect when the cab next loads)")]
        public bool ShowStokerJetValves = true;

        [Draw("Fit the stock S282 with a mechanical stoker (needs a restart)")]
        public bool RetrofitS282Stoker = true;

        [Draw("Stoker jams: lumps or tramp iron now and then; reverse the stoker engine to clear (needs a restart)")]
        public bool StokerJams = true;

        [Header("Recovery")]
        [Draw("Renew the boiler and its fusible plug now, on the loco you're on, as a service station's repair does (recovery for an engine stranded with a melted plug; unticks itself)")]
        public bool RenewBoilerNow;

        // Not in the menu: set in Settings.xml for a bug hunt.
        public bool LogFireboxCreation = true;

        // Dump each steam loco's sim graph once per session.
        public bool DumpSimGraphs;

        // Dump each supported loco's firebox geometry and chimney smoke setup once per session.
        public bool DumpFireboxGeometry;

        // Log the turntables, parking tracks, and service stations once a session, for the shed hoses.
        public bool SurveySheds;

        // Log coal deliveries, door openings, grate shakes, and injector changes.
        public bool LogCoalDeliveries;

        // Log the fire on your loco every N seconds (0 = off).
        public int FireStatusLogInterval;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        // Which keys the settings were saved with: 0 the Ctrl keys, 1 the keys
        // off the game's. Not in the menu.
        public int KeysVersion;

        private const int CurrentKeys = 1;

        // Which defaults the settings were saved with: 0 before 1.11.1, 1 the
        // aiming keypad drawn. Not in the menu.
        public int DefaultsVersion;

        private const int CurrentDefaults = 1;

 public bool ApplyNewDefaults()
        {
            if (DefaultsVersion >= CurrentDefaults)
                return false;
            DefaultsVersion = CurrentDefaults;
            ShowAimKeypad = true;
            return true;
        }

        /// <summary>
        /// Moves each key still on its old Ctrl default to the new one, once;
        /// a key the player bound stays. Returns whether anything changed.
        /// </summary>
        public bool MoveOldKeys()
        {
            if (KeysVersion >= CurrentKeys)
                return false;
            KeysVersion = CurrentKeys;
            const byte ctrl = 1, ctrlShift = 3;
            var fresh = new Settings();
            Move(OpenSettingsKey, KeyCode.F, ctrlShift, fresh.OpenSettingsKey);
            Move(HandPumpKey, KeyCode.P, ctrlShift, fresh.HandPumpKey);
            Move(HydrantKey, KeyCode.H, ctrlShift, fresh.HydrantKey);
            Move(ShakeGrateKey, KeyCode.G, ctrl, fresh.ShakeGrateKey);
            Move(ToggleOverlayKey, KeyCode.F, ctrl, fresh.ToggleOverlayKey);
            Move(FiremanKey, KeyCode.J, ctrl, fresh.FiremanKey);
            return true;
        }

        private static void Move(KeyBinding binding, KeyCode oldKey, byte oldModifiers, KeyBinding fresh)
        {
            if (binding != null && binding.keyCode == oldKey && binding.modifiers == oldModifiers)
                binding.Change(fresh.keyCode, fresh.modifiers);
        }

        public void OnChange()
        {
            Core.FireTuning.Default.ClinkerTendency = ClinkerTendency;
        }

        // Change() also caches the key for UMM's new-input-system path.
        private static KeyBinding Bind(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            var binding = new KeyBinding();
            binding.Change(key, ctrl, shift, alt);
            return binding;
        }
    }

    /// <summary>How the shovel, rake, and fire iron aim into a coal firebox.</summary>
    public enum ShovelAim
    {
        /// <summary>The firehole in thirds, as a keypad.</summary>
        Keypad,

        /// <summary>The spot on the fire the look meets.</summary>
        Grate,
    }
}
