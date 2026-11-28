using System.Linq;
using System.Xml.Serialization;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    /// <summary>
    /// The player's settings. Unity Mod Manager's window is the mod's own
    /// tabbed <see cref="SettingsMenu"/>, so the Draw attributes here are read
    /// only by other mods that show a mod's settings (the Comms Tablet's
    /// Settings app). They follow the window: its tabs as headings, in its
    /// order, with its short labels ("(restart)" where it shows Restart) and
    /// its descriptions as tooltips. The window's buttons (difficulty, items,
    /// recovery), developer settings, and saved state have no Draw.
    /// Settings.xml keys on the field names, not their order.
    /// </summary>
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Header("Display")]
        [Draw("Detailed firebox", Tooltip =
            "Coal heaps, flames, smoke, and firelight inside the firebox. Turn off to use the\n" +
            "stock visuals. Applies immediately.")]
        public bool ShowZonedCoalBed = true;

        [Draw("Firebox quality", DrawType.ToggleGroup, Tooltip =
            "High: full detail. Medium: flames at half resolution, half the coal lumps, 20\n" +
            "surface updates/s; much cheaper leaning into the firebox. Low: fewer flame\n" +
            "samples too, a quarter of the lumps, 10 updates/s. Firelight stays the same.")]
        public Core.FireboxQuality FireboxQuality = Core.FireboxQuality.High;

        [Draw("Ashpan glow, ×", DrawType.Slider, Min = 0, Max = 3, Precision = 1, Tooltip =
            "Light from the ashpan beneath the engine: bright over clean grates, dimmed by\n" +
            "dead ash, flaring when fire drops in. 0 turns it off.")]
        public float AshpanGlow = 1f;

        // The chimney's eight do nothing while Realistic Stack Effects draws the
        // chimneys (its own settings apply): the window greys them, and a
        // settings list leaves them out (ChimneyHandedOff).

        // Two players' frame rate fell from 60 to 15 with the plume
        // (Nexus, 2026-10-25); Core.SmokeDetail.
        [Draw("Smoke quality", DrawType.ToggleGroup, InvisibleOn = "#ChimneyHandedOff|True", Tooltip =
            "Medium: half-resolution smoke without its faintest parts. Low: fewer particles\n" +
            "and less faint smoke too. In VR and forward rendering, both reduce particles\n" +
            "instead of resolution. Auto: High, lowered while the frame rate is under 45 with\n" +
            "smoke in the air and the lower quality helps, and raised again when it runs\n" +
            "well.")]
        public RealisticStackEffects.Core.SmokeQuality SmokeQuality = RealisticStackEffects.Core.SmokeQuality.High;

        [Draw("Season for the chimney's steam", DrawType.ToggleGroup, InvisibleOn = "#ChimneyHandedOff|True", Tooltip =
            "How much white steam condenses: a summer afternoon's dry air shows little, a\n" +
            "damp dawn or the rain plenty, and winter the most. When DV Seasons is running,\n" +
            "its weather is used instead.")]
        public RealisticStackEffects.Core.SmokeSeason SmokeSeason = RealisticStackEffects.Core.SmokeSeason.Summer;

        [Draw("Steam visibility, ×", DrawType.Slider, Min = RealisticStackEffects.Core.PlumeSettings.MinSteamVisibility, Max = RealisticStackEffects.Core.PlumeSettings.MaxSteamVisibility, Precision = 1, InvisibleOn = "#ChimneyHandedOff|True", Tooltip =
            "Scales how strongly exhaust steam shows in the air. 1 is realistic for the\n" +
            "weather; raise it for a fuller plume in dry summer air, or lower it for a\n" +
            "subtler one.")]
        public float SteamVisibility = RealisticStackEffects.Core.PlumeSettings.DefaultSteamVisibility;

        [Draw("Plume wind, ×", DrawType.Slider, Min = RealisticStackEffects.Core.PlumeSettings.MinWindStrength, Max = RealisticStackEffects.Core.PlumeSettings.MaxWindStrength, Precision = 1, InvisibleOn = "#ChimneyHandedOff|True", Tooltip =
            "Scales how strongly the weather's wind drifts the plume. 1 is normal; 0 is\n" +
            "still air.")]
        public float WindStrength = RealisticStackEffects.Core.PlumeSettings.DefaultWindStrength;

        [Draw("Heat shimmer", InvisibleOn = "#ChimneyHandedOff|True", Tooltip = "Shimmer above the stack. Needs custom smoke effects.")]
        public bool HeatShimmer = true;

        [Draw("Realistic chimney smoke (restart)", InvisibleOn = "#ChimneyHandedOff|True", Tooltip = "Use smoke driven by the realistic fire on supported locomotives.")]
        public bool RealisticSmoke = true;

        [Draw("Custom smoke effects (restart)", InvisibleOn = "#ChimneyHandedOff|True", Tooltip = "Use the mod's chimney effects when their assets are available.")]
        public bool CustomSmokeEffects = true;

        [Draw("Steam around the engine (restart)", InvisibleOn = "#ChimneyHandedOff|True", Tooltip =
            "Draw the dynamo, cylinder cocks, safety valves, whistle, and leaks as the\n" +
            "chimney's steam, by the weather. Needs custom smoke effects.")]
        public bool EngineSteam = true;

        /// <summary>
        /// Whether Realistic Stack Effects draws the chimneys this session, so
        /// the chimney settings above do nothing; their InvisibleOn reads it.
        /// Get-only, so never saved.
        /// </summary>
        public bool ChimneyHandedOff => StackEffectsLink.HandedOff;

        [Header("Firing")]
        [Draw("Realistic coal fireboxes (restart)", Tooltip =
            "Replace the fire model on supported coal locomotives, including the S282, S060,\n" +
            "and JK3.")]
        public bool ReplaceStockFireboxes = true;

        [Draw("Light the coal from the lighter", Tooltip =
            "Light the coal straight from the lighter, as in the stock game, skipping the\n" +
            "wood and rag. They still work. In multiplayer the host's setting applies.")]
        public bool LighterLightsCoal;

        [Draw("Largest shovelful, kg", Min = MinShovelful, Max = MaxShovelful, Precision = 1, Tooltip =
            "A full shovel on supported coal locomotives, and the fireman's (never under 3\n" +
            "kg for him). With the shovel in hand, the mouse wheel (or trigger + stick in\n" +
            "VR) takes less, half a kilogram a click down to 1 kg. 6 kg by default.")]
        public float ShovelfulMass = DefaultShovelful;

        [Draw("Bigger shovelfuls during the accelerated startup", Tooltip =
            "While the game's Steam Startup speeds up a cold engine's fire, each shovelful\n" +
            "you scoop is bigger by as much, up to 12 kg (less in a small firebox): fewer\n" +
            "throws. None at Steam Startup 2,500 %; a stoker's own feed is unchanged.")]
        public bool StartupShovelBoost = true;

        // The shovelful the mouse wheel has picked, kg; 0, the default, is a
        // full one, which follows the largest. Not drawn: the wheel sets it,
        // and a new largest in the menu puts it back to 0.
        public float ScoopMass;

        internal const float MinShovelful = 1f;
        internal const float MaxShovelful = 8f;
        internal const float DefaultShovelful = 6f;

        /// <summary>The largest shovelful, kg (a full shovel, and the fireman's, never under 3 kg for him): the setting, or <see cref="DefaultShovelful"/> where it is out of range.</summary>
        internal float Shovelful => ShovelfulMass >= MinShovelful && ShovelfulMass <= MaxShovelful ? ShovelfulMass : DefaultShovelful;

        /// <summary>The player's own shovelful, kg: the wheel's pick, 1 kg up to <see cref="Shovelful"/>, or a full one.</summary>
        internal float Scoop => Core.ShovelSize.Scoop(ScoopMass, Shovelful, MinShovelful);

        [Draw("Rain wets the coal", Tooltip =
            "Coal in open tenders and bunkers gets wet, catches slower, and can stick on a\n" +
            "stoker table.")]
        public bool RainWetsCoal = true;

        [Draw("Clinker tendency, ×", DrawType.Slider, Min = 0, Max = 20, Precision = 1, Tooltip =
            "1 is typical coal: about two hours of hard work between cleanings. 0 prevents\n" +
            "clinker; raise it to see clinker sooner.")]
        public float ClinkerTendency = 1f;

        [Draw("Aim through the fire door", Tooltip = "Place shovelfuls where you point in the open fire door.")]
        public bool AimShovelThroughDoor = true;

        // A player's request (Rhasma2 on Nexus, 2026-10-16): the grate aim
        // back beside the keypad, which reaches the front of bigger boxes.
        [Draw("Aiming method", DrawType.ToggleGroup, Tooltip =
            "Keypad: the door in thirds; the top throws to the front. Grate: the coal you\n" +
            "look at, where visible.")]
        public ShovelAim Aim = ShovelAim.Keypad;

        [Draw("Aimed zone label", Tooltip = "Name the targeted grate zone under the crosshair.")]
        public bool ShowAimLabel = true;

        [Draw("Aiming keypad", Tooltip = "Draw the grid while aiming with the keypad.")]
        public bool ShowAimKeypad = true;

        [Draw("Grate lever sensitivity, ×", DrawType.Slider, Min = 0.1f, Max = 1f, Precision = 2, Tooltip =
            "Lower for finer rocking. A short rock wiggles the grate; a half stroke shakes\n" +
            "it.")]
        public float GrateLeverSensitivity = 0.35f;

        [Draw("Caboose stove", Tooltip =
            "Make the caboose's stove work with firewood, oiled rags, and the lighter, with\n" +
            "firelight, interior warmth, and chimney smoke. Applies immediately.")]
        public bool CabooseStove = true;

        [Header("Boiler")]
        [Draw("Realistic injectors (restart)", Tooltip = "Use live-steam injectors on the mod's steam locomotives.")]
        public bool RealisticInjectors = true;

        [Draw("Injector priming and heat", Tooltip = "Prime before opening. A hot injector must cool before it will take.")]
        public bool InjectorTechniqueAndHeat = true;

        [Draw("Separate injector steam valve (restart)", Tooltip =
            "The injector lever controls water, with a separate Off / Prime / Feed steam\n" +
            "valve.")]
        public bool SeparateInjectorSteamValve = true;

        [Draw("Boiler water impurities", Tooltip =
            "Dissolved solids build up, causing foaming, carryover, and a choking gauge\n" +
            "glass. Blow down regularly, blow through the glass, check try cocks, or wash\n" +
            "out to clear them.")]
        public bool BoilerWaterImpurities = true;

        [Draw("Feed water quality", DrawType.ToggleGroup, Tooltip = "Better water brings fewer dissolved solids into the boiler.")]
        public Core.FeedWaterQuality BoilerWaterQuality = Core.FeedWaterQuality.Average;

        [Draw("Fusible plugs and low-water alarm (restart)", Tooltip =
            "Fit the prototype's protection. A melted plug drowns the fire; the Hudson uses\n" +
            "an alarm.")]
        public bool FusiblePlugs = true;

        [Draw("Boiler thermal shock", Tooltip =
            "Forcing a cold boiler hard stresses tubes and stays, causing slow leaks and\n" +
            "efficiency loss until repaired at a service station. Off by default.")]
        public bool BoilerThermalShock;

        [Draw("Superheater element care", Tooltip =
            "Shutting off on a hot fire without steam flow overheats and scales the\n" +
            "superheater elements. Crack the throttle, open the drifting valve, or ease the\n" +
            "fire.")]
        public bool SuperheaterCare = true;

        [Header("Engine")]
        [Draw("Realistic cylinders", Tooltip = "Valve events, clearance, cushioning, port flow, and blast-pipe back pressure.")]
        public bool RealisticCylinders = true;

        [Draw("Unlisted steam locomotives (restart)", Tooltip =
            "Also put the injectors, boiler water, superheater, and cylinders on steam\n" +
            "locomotives this mod does not list. Unchecked on them: they may steam or feed\n" +
            "badly.")]
        public bool UnlistedLocomotives;

        [Draw("Santa Fe 2901 oil burner (restart)", Tooltip = "Firing valve, atomizer, oil heater, flarebacks, and flue sanding.")]
        public bool RealisticOilBurners = true;

        [Draw("Hudson booster (restart)", Tooltip = "Fit the NYC Hudson's Franklin trailing-truck booster.")]
        public bool FitBoosters = true;

        [Draw("Feedwater heaters and pumps (restart)", Tooltip =
            "Fit the Hudson, 844, S484-840, QJ, and Santa Fe 2901 with their prototype\n" +
            "equipment.")]
        public bool FitFeedPumps = true;

        [Draw("Stoker jams (restart)", Tooltip =
            "Lumps, foreign objects, or wet-coal packing can jam the screw. Reverse its\n" +
            "engine to clear it.")]
        public bool StokerJams = true;

        [Draw("Stoker jet valves in the cab", Tooltip = "Show the stoker's distribution valves. Applies when the cab next loads.")]
        public bool ShowStokerJetValves = true;

        // The upgrade rules: each fitting bought in a career (Upgrades on a
        // career manager), or always or never fitted. A change refits the
        // engines through OnChange, from the window or a settings list alike.

        [Header("Upgrades")]
        [Draw("Career upgrade scope", DrawType.ToggleGroup, Tooltip =
            "Fleet-wide: buying an upgrade fits every eligible locomotive.\n" +
            "Per locomotive: Upgrades lists the S282s and S060s you own (Owned Vehicles,\n" +
            "a restored engine); pick one by its ID and buy its upgrades for it alone.\n" +
            "Prior fleet-wide purchases stay fitted; new purchases apply only to the\n" +
            "picked locomotive and are kept across saves.\n" +
            "Switching back to Fleet-wide hides per-locomotive purchases until you return\n" +
            "to Per locomotive; nothing is lost.")]
        public Core.UpgradeScope UpgradeScope = Core.UpgradeScope.FleetWide;
        // The stock engines' brick arch.
        [Draw("Brick arch", DrawType.ToggleGroup, Tooltip =
            "Without it the engines burn about 7 to 13 % more coal for the same work, steam\n" +
            "about 13 % less at their most, and smoke more after firing. Buy it in a career\n" +
            "at a career manager's Upgrades screen; sandbox has it unless set to Never\n" +
            "fitted.")]
        public Upgrades.UpgradeRule BrickArch = Upgrades.UpgradeRule.BuyInCareer;

        // The stock engines' over-fire jets, the same way; fitted only where
        // the arch is.
        [Draw("Over-fire jets", DrawType.ToggleGroup, Tooltip =
            "Steam jets through the firebox sides, worked by a valve in the cab, that blow\n" +
            "air over the fire and burn its smoke. Fitted only with the brick arch. Buy them\n" +
            "in a career at a career manager's Upgrades screen after the arch.")]
        public Upgrades.UpgradeRule OverFireJets = Upgrades.UpgradeRule.BuyInCareer;

        // The S282's and S060's automatic fire door, the same way. Never
        // fitted also turns off the one the car packs' engines have as built.
        [Draw("Automatic fire door", DrawType.ToggleGroup, Tooltip =
            "With the shovel in hand at the fire door, holding jump (or trigger in VR)\n" +
            "opens the door with air, and it shuts when you let go. Buy it for the S282 and\n" +
            "S060 in a career at a career manager's Upgrades screen. The Hudson, 844,\n" +
            "S484-840 and QJ have it as built, unless this is Never fitted.")]
        public Upgrades.UpgradeRule AutomaticFireDoor = Upgrades.UpgradeRule.BuyInCareer;

        // The stock engines' snifting valves on their steam chests, the same
        // way: drifting hooked up wears the engine a third as much.
        [Draw("Snifting valves", DrawType.ToggleGroup, Tooltip =
            "Vacuum relief valves on the steam chests that let air in when drifting with the\n" +
            "throttle shut, so the cylinders draw less smokebox gas: drifting hooked up wears\n" +
            "the engine a third as much. Buy them in a career at a career manager's Upgrades\n" +
            "screen.")]
        public Upgrades.UpgradeRule SniftingValves = Upgrades.UpgradeRule.BuyInCareer;

        // The stock engines' drifting valve, worked from the cab, the same way.
        [Draw("Drifting valve", DrawType.ToggleGroup, Tooltip =
            "A DRIFTING valve in the cab that feeds a little steam to the steam chests while\n" +
            "drifting with the throttle shut: hooked up, it spares most of the wear, for a\n" +
            "little steam, and with by-pass valves ends the cylinders' braking. Buy it in a\n" +
            "career at a career manager's Upgrades screen.")]
        public Upgrades.UpgradeRule DriftingValve = Upgrades.UpgradeRule.BuyInCareer;

        // The stock engines' by-pass valves on their cylinders, the same way.
        [Draw("By-pass valves", DrawType.ToggleGroup, Tooltip =
            "Valves on the cylinders that join their ends when drifting with the throttle\n" +
            "shut, so the engine drifts freely in any notch until it runs fast. Buy them in a\n" +
            "career at a career manager's Upgrades screen.")]
        public Upgrades.UpgradeRule ByPassValves = Upgrades.UpgradeRule.BuyInCareer;

        // The stock S282's mechanical stoker, the same way. Never fitted
        // leaves it out of the engine altogether, and so takes a restart.
        [Draw("S282 stoker", DrawType.ToggleGroup, Tooltip =
            "A mechanical stoker for the S282, worked from the cab, that feeds the fire from\n" +
            "the tender. Buy it in a career at a career manager's Upgrades screen. Changing\n" +
            "to or from Never fitted takes a restart.")]
        public Upgrades.UpgradeRule S282Stoker = Upgrades.UpgradeRule.BuyInCareer;

        // The stock S282's feedwater heater and pump, the same way; it needs
        // the realistic injectors. Never fitted takes a restart.
        [Draw("S282 feed pump", DrawType.ToggleGroup, Tooltip =
            "A Worthington feedwater heater and pump for the S282, worked from the cab, that\n" +
            "feeds the boiler hot water heated by the exhaust. Needs the realistic injectors.\n" +
            "Buy it in a career at a career manager's Upgrades screen. Changing to or from\n" +
            "Never fitted takes a restart.")]
        public Upgrades.UpgradeRule S282FeedPump = Upgrades.UpgradeRule.BuyInCareer;

        // The stock S282's trailing-truck booster, the same way. Never fitted
        // takes a restart.
        [Draw("S282 booster", DrawType.ToggleGroup, Tooltip =
            "A Franklin trailing-truck booster for the S282, worked from the cab by the\n" +
            "reverser, for starting and slow climbing. Buy it in a career at a career\n" +
            "manager's Upgrades screen. Changing to or from Never fitted takes a restart.")]
        public Upgrades.UpgradeRule S282Booster = Upgrades.UpgradeRule.BuyInCareer;

        [Draw("S282 oil burner", DrawType.ToggleGroup, Tooltip =
            "The S282 converted to burn oil: an oil tank in the tender's coal space and an\n" +
            "oil burner fired from the cab, in place of the coal fire and the stoker. Buy it\n" +
            "in a career at a career manager's Upgrades screen; in sandbox, Always fitted\n" +
            "converts every S282. It takes effect when the game next loads.")]
        public Upgrades.UpgradeRule S282OilBurner = Upgrades.UpgradeRule.BuyInCareer;

        [Header("Sound")]
#if !PUBLIC_RELEASE
        [Draw("Realistic chuffs", Tooltip =
            "Each beat as loud as the steam it lets go, the soft and hard sounds\n" +
            "blending, each engine's own slightly uneven beat, a drifting engine's\n" +
            "panting, and echoes off walls, cuttings and tunnels. Off, the game's own.")]
        public bool RealisticChuffs = true;

        [Draw("Chuff volume, %", DrawType.Slider, Min = 0, Max = 200, Precision = 0, Tooltip =
            "How loud the realistic chuffs are. 100 is normal; 0 is silent.")]
        public float ChuffVolume = 100f;
#endif

        [Draw("Oil burner volume, ×", DrawType.Slider, Min = 0, Max = 2, Precision = 1, Tooltip = "The roar and drumming of the burner. 1 is normal volume; 0 is silent.")]
        public float OilBurnerVolume = 1f;

        [Draw("Feed pump volume, ×", DrawType.Slider, Min = 0, Max = 2, Precision = 1, Tooltip = "The locomotive's feed pump. 1 is normal volume; 0 is silent.")]
        public float FeedPumpVolume = 1f;

        [Draw("Low-water alarm volume, ×", DrawType.Slider, Min = 0, Max = 2, Precision = 1, Tooltip =
            "The warning whistle. Silent without steam pressure. Muting it leaves the\n" +
            "low-water indication active.")]
        public float LowWaterAlarmVolume = 1f;

        [Draw("Steam blowing off, volume ×", DrawType.Slider, Min = 0, Max = 2, Precision = 1, Tooltip =
            "Injector overflow and spilled water, open shed steam hoses, and a melted fusible\n" +
            "plug. 1 is normal volume; 0 is silent.")]
        public float SteamVentVolume = 1f;

        // On for a new install (audit 2026-11-09): its next-step line is what
        // tells a new player why the fire is dying. Saved settings keep
        // their own choice.
        [Header("On screen")]
        [Draw("Fire overlay", Tooltip = "Show the Fire, Water, and Engine pages on screen.")]
        public bool ShowOverlay = true;

        [Draw("Fireman status", Tooltip = "Show what the fireman is doing at the top of the screen.")]
        public bool ShowFiremanStatus = true;

        [Draw("Host: require cab instruments", Tooltip =
            "In multiplayer, hide RF's overlay, fireman's orders and status, and aiming\n" +
            "helpers for everyone. Changes apply live. The physical tablet, manual, and\n" +
            "safety alarms remain available.")]
        public bool HostHidesHud;

        [Header("Keys")]
        [Draw("Open settings", Tooltip =
            "This mod's settings window. Unity Mod Manager's Ctrl+F10 is taken by some\n" +
            "desktops.")]
        public KeyBinding OpenSettingsKey = Bind(KeyCode.F8);

        [Draw("Hand pump (hold)", Tooltip = "Works the boiler hand pump while held.")]
        public KeyBinding HandPumpKey = Bind(KeyCode.C);

        [Draw("Shake grate", Tooltip = "Shakes the grate, as rocking the fire-dump lever does.")]
        public KeyBinding ShakeGrateKey = Bind(KeyCode.Z);

        [Draw("Fire overlay", Tooltip = "Shows the fire overlay, turns its pages, and hides it.")]
        public KeyBinding ToggleOverlayKey = Bind(KeyCode.F6);

        [Draw("Fireman's orders", Tooltip = "Shows or hides his window. Hold Alt to free the mouse and click it.")]
        public KeyBinding FiremanKey = Bind(KeyCode.F7);

        [Draw("Show added controls", Tooltip = "Briefly highlights every cab control added or reworked by Realistic Firing on\nthe current locomotive (~10 s). Unbound until you bind it.")]
        public KeyBinding ShowControlsKey = new KeyBinding();

        [Draw("Fireman: off", Tooltip = "Orders the fireman on your engine off. Unbound until you bind it; Ctrl,\nShift, or Alt may go with the key.")]
        public KeyBinding FiremanOffKey = new KeyBinding();

        [Draw("Fireman: keep it alive", Tooltip = "Orders him to keep a thin fire and the water up, as for standing a while.\nUnbound until you bind it.")]
        public KeyBinding FiremanKeepAliveKey = new KeyBinding();

        [Draw("Fireman: shunting", Tooltip = "Orders him to fire for stop-start work. Unbound until you bind it.")]
        public KeyBinding FiremanShuntingKey = new KeyBinding();

        [Draw("Fireman: road", Tooltip = "Orders him to fire for the road, just under the safety valve. Unbound until\nyou bind it.")]
        public KeyBinding FiremanRoadKey = new KeyBinding();

        [Draw("Fireman: next order", Tooltip = "Steps him to his next order: off, keep it alive, shunting, road, and round\nagain. Unbound until you bind it.")]
        public KeyBinding FiremanNextKey = new KeyBinding();

        [Draw("Call: heavy work ahead", Tooltip = "Tells him heavy work is coming, or takes the call back. Unbound until you\nbind it.")]
        public KeyBinding FiremanHeavyAheadKey = new KeyBinding();

        [Draw("Call: easing off", Tooltip = "Tells him you're shutting off or stopping soon, or takes the call back.\nUnbound until you bind it.")]
        public KeyBinding FiremanEasingOffKey = new KeyBinding();

        // One-time actions, not settings: the window's Equipment and Recovery
        // buttons set them, and FireController does each once and clears it.
        // Never saved, so a Settings.xml that kept one set (it is no longer
        // read) cannot give the item or renew the boiler again at a later
        // start. Not drawn: a settings list has no buttons.
        [XmlIgnore]
        public bool GiveFireIron;

        // Merged into the Comms Tablet, its give says where the pages are instead.
        [XmlIgnore]
        public bool GiveFiringTablet;

        [XmlIgnore]
        public bool GiveFiringManual;

        [XmlIgnore]
        public bool GiveFiringChecklists;

        [XmlIgnore]
        public bool GiveWoodCrate;

        [XmlIgnore]
        public bool GiveRagTin;

        [XmlIgnore]
        public bool ShowAddedControls;

        // Recovery for an engine stranded with a melted plug, on the loco
        // you're on, as a service station's repair does.
        [XmlIgnore]
        public bool RenewBoilerNow;

        // Whether the one-time card naming the mod's keys has been shown.
        public bool ToldFirstSteps;

        // The overlay's page: 0 the fire (or burner), 1 the water, 2 the engine.
        public int OverlayPage;

        // Before the stoker was an upgrade: whether the S282 had one. Read
        // once into S282Stoker (ApplyNewDefaults); not in the menu.
        public bool RetrofitS282Stoker = true;


#if !PUBLIC_RELEASE

        // Dump each steam loco's sim graph once per session.
        public bool DumpSimGraphs;

        // Dump each supported loco's firebox geometry and chimney smoke setup once per session.
        public bool DumpFireboxGeometry;

        // Log the turntables, parking tracks, and service stations once a session, for the shed hoses.
        public bool SurveySheds;

        // Log what is drawn under the firebox of the engine nearest the camera, as it changes (Diagnostics.UnderEngineDump).
        public bool DumpUnderEngine;

        // With DumpUnderEngine: hide each of those drawings in turn, named on the screen, to find which draws a fault.
        public bool ProbeUnderEngine;

        // Once a game is loaded, write the inventory icons, the mod's and some stock ones, to icon-dump/ in the mod's
        // folder, then turn off (Diagnostics.ItemIconDump).
        public bool DumpItemIcons;
#endif

#if !PUBLIC_RELEASE
        // Developer builds only, as the tools are: a line for each coal
        // delivery, door opening, grate shake, and injector change, and the
        // fire's status every N seconds (0 = off), for the in-game checks.
        public bool LogCoalDeliveries;

        public int FireStatusLogInterval;
#endif

#if !PUBLIC_RELEASE
        // The plume test bed, a development tool for tuning the chimney
        // smoke: shown and hidden by this key, unbound unless set here (not
        // in the menu; it shipped bound to F9 in 1.13.3 by mistake, and that
        // setting's name is no longer read). Its sliders set away from their
        // defaults, as name=value pairs, are put back only while the key is bound.
        public KeyBinding DevPlumeTestBedKey = new KeyBinding();

        public string PlumeTestBed = "";

        /// <summary>Whether the plume test bed's key is bound (a development setup).</summary>
        public bool PlumeTestBedEnabled => DevPlumeTestBedKey != null && DevPlumeTestBedKey.keyCode != KeyCode.None;

        // The seating bench, a development tool for seating a new engine's
        // added cab parts and shed connections in game: shown and hidden by
        // this key, unbound unless set here (not in the menu). Nothing it
        // sets is saved; it writes the seats to the log to be built in.
        public KeyBinding DevSeatingKey = new KeyBinding();

        /// <summary>Whether the seating bench's key is bound (a development setup).</summary>
        public bool SeatingBenchEnabled => DevSeatingKey != null && DevSeatingKey.keyCode != KeyCode.None;

        // The firebox test bed, a development tool for the look of the fire
        // inside the box: its capture runs (and the flames' draft and beats
        // set by hand) only with this key bound, unbound unless set here (not
        // in the menu).
        public KeyBinding DevFireboxTestBedKey = new KeyBinding();

        /// <summary>Whether the firebox test bed's key is bound (a development setup).</summary>
        public bool FireboxTestBedEnabled => DevFireboxTestBedKey != null && DevFireboxTestBedKey.keyCode != KeyCode.None;
#endif

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        // Which keys the settings were saved with: 0 the Ctrl keys, 1 the keys
        // off the game's. Not in the menu.
        public int KeysVersion;

        private const int CurrentKeys = 1;

        // Which defaults the settings were saved with: 0 before 1.11.1, 1 the
        // aiming keypad drawn, 2 the shovelful always a real one, 3 the S282's
        // stoker an upgrade, 4 boiler thermal shock and superheater care kept
        // off for Relaxed players. Not in the menu.
        public int DefaultsVersion;

        private const int CurrentDefaults = 4;

 public bool ApplyNewDefaults()
        {
            if (DefaultsVersion >= CurrentDefaults)
                return false;
            if (DefaultsVersion < 1)
                ShowAimKeypad = true;
            // The stock shovelful at 0 is gone: a saved 0 is a real shovelful again.
            if (DefaultsVersion < 2 && !(ShovelfulMass >= MinShovelful && ShovelfulMass <= MaxShovelful))
                ShovelfulMass = DefaultShovelful;
            // The S282's stoker switched off before it was an upgrade stays off.
            if (DefaultsVersion < 3 && !RetrofitS282Stoker)
                S282Stoker = Upgrades.UpgradeRule.NeverFitted;
            RetrofitS282Stoker = true;
            // Existing Relaxed players keep the new care systems off instead of
            // having them switched on silently and breaking IsRelaxed.
            if (DefaultsVersion < 4 && WasOldRelaxed)
            {
                BoilerThermalShock = false;
                SuperheaterCare = false;
            }
            DefaultsVersion = CurrentDefaults;
            return true;
        }

        private bool WasOldRelaxed => !InjectorTechniqueAndHeat && !BoilerWaterImpurities
                                      && !RainWetsCoal && !StokerJams && ClinkerTendency <= RelaxedClinker;

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


        /// <summary>
        /// For learning (audit 2026-11-09: a new player met every demand at
        /// once, with the help switched off): the unforgiving parts off, and
        /// the overlay on. Each stays a setting of its own afterward.
        /// </summary>
        internal void ApplyRelaxed()
        {
            InjectorTechniqueAndHeat = false;
            BoilerWaterImpurities = false;
            BoilerThermalShock = false;
            SuperheaterCare = false;
            RainWetsCoal = false;
            StokerJams = false;
            ClinkerTendency = RelaxedClinker;
            ShowOverlay = true;
            ShowFiremanStatus = true;
            OnChange();
        }

        /// <summary>The mod as it comes: what <see cref="ApplyRelaxed"/> turned off, back on, but thermal shock, which is off as it comes.</summary>
        internal void ApplyStandard()
        {
            InjectorTechniqueAndHeat = true;
            BoilerWaterImpurities = true;
            BoilerThermalShock = false;
            SuperheaterCare = true;
            RainWetsCoal = true;
            StokerJams = true;
            ClinkerTendency = 1f;
            OnChange();
        }

        internal const float RelaxedClinker = 0.3f;

        /// <summary>Whether the difficulty options stand as <see cref="ApplyRelaxed"/> leaves them.</summary>
        internal bool IsRelaxed => !InjectorTechniqueAndHeat && !BoilerWaterImpurities && !BoilerThermalShock && !SuperheaterCare
                                   && !RainWetsCoal && !StokerJams && ClinkerTendency <= RelaxedClinker;

        /// <summary>Whether they stand as the mod comes.</summary>
        internal bool IsStandard => InjectorTechniqueAndHeat && BoilerWaterImpurities && !BoilerThermalShock && SuperheaterCare
                                    && RainWetsCoal && StokerJams && Mathf.Approximately(ClinkerTendency, 1f);

        /// <summary>
        /// After any change, from the window, a settings list (the Comms
        /// Tablet's), or a difficulty button; once at load, to note the upgrade
        /// rules as they stand. A changed upgrade rule refits the engines.
        /// </summary>
        public void OnChange()
        {
            Core.FireTuning.Default.ClinkerTendency = ClinkerTendency;
            var rules = UpgradeRules;
            if (appliedRules != null && rules != appliedRules)
                Upgrades.Fittings.Changed();
            appliedRules = rules;
        }

        // The upgrade rules as OnChange last saw them; null before its first call.
        private string appliedRules;

        private string UpgradeRules => $"{UpgradeScope} " + string.Join(" ", Upgrades.Fittings.All.Select(spec => spec.Rule(this)));

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
