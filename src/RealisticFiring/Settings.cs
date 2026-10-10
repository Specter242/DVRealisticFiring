using UnityEngine;
using UnityModManagerNet;

namespace RealisticFiring
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Header("Fire model")]
        [Draw("Replace the S282 and S060 fireboxes (restart required)")]
        public bool ReplaceStockFireboxes = true;

        [Draw("Realistic injectors on every steam loco (restart required)")]
        public bool RealisticInjectors = true;

        [Draw("Injector technique and heat: crack the lever until it primes before opening it; a hot injector won't take")]
        public bool InjectorTechniqueAndHeat = true;

        [Draw("Boiler water impurities: they build up as the water boils off until it foams (a lively, high glass, then water carried over to the cylinders); short blowdowns keep them down, a washout clears them")]
        public bool BoilerWaterImpurities = true;

        [Draw("Feed water quality: how much dissolved solids it brings", DrawType.ToggleGroup)]
        public Core.FeedWaterQuality BoilerWaterQuality = Core.FeedWaterQuality.Average;

        [Draw("Fusible plugs where the prototypes had them (the Hudson has a low-water alarm): a bared crown sheet melts the plug, which drowns the fire instead of the boiler failing (needs a restart)")]
        public bool FusiblePlugs = true;

        [Draw("Fit the NYC Hudson with its Franklin trailing-truck booster (needs a restart)")]
        public bool FitBoosters = true;

        [Draw("Fit feedwater heaters and pumps where the prototypes had them: the Hudson, 844, and S484-840 (needs a restart)")]
        public bool FitFeedPumps = true;

        [Draw("Coal's clinker tendency, × (1 typical coal, about two hours of hard work between cleanings; 0 never; raise it to see clinker sooner)", DrawType.Slider, Min = 0, Max = 20, Precision = 1)]
        public float ClinkerTendency = 1f;

        [Draw("Glow of the fire in the ashpan, under the engine, × (0 off): bright over clean grates, dimmed by dead ash, flaring when fire drops in", DrawType.Slider, Min = 0, Max = 3, Precision = 1)]
        public float AshpanGlow = 1f;

        [Draw("Realistic chimney smoke on the S282 and S060 (restart required)")]
        public bool RealisticSmoke = true;

        [Draw("Use the custom smoke effects when installed (restart required)")]
        public bool CustomSmokeEffects = true;

        [Header("Shovel")]
        [Draw("Coal per standard shovelful on the S282 and S060, kg (0 = stock 36 kg)", Min = 0, Precision = 1)]
        public float ShovelfulMass = 3f;

        [Draw("Aim shovelfuls by where you point in the firebox door")]
        public bool AimShovelThroughDoor = true;

        [Draw("Name the aimed zone under the crosshair")]
        public bool ShowAimLabel = true;

        [Header("Chimney smoke tuning (applies to new puffs as you move a slider)")]
        [Draw("Exhaust pressure for the hardest beat, bar", DrawType.Slider, Min = 2, Max = 12, Precision = 1)]
        public float SmokeMaxBeatPressure = 6f;

        [Draw("Exhaust pressure for the heaviest beat (a long cutoff at full steam, slogging up a grade), bar", DrawType.Slider, Min = 4, Max = 16, Precision = 1)]
        public float SmokeHeavyBeatPressure = 11f;

        [Draw("Heavy beats' extra volume, × (0 none; at 1 the heaviest puff is 2.5× the particles, 1.6× the size, and lasts 1.5× as long)", DrawType.Slider, Min = 0, Max = 3, Precision = 1)]
        public float SmokeHeavyVolume = 1f;

        [Draw("Hardest puff's launch speed, m/s", DrawType.Slider, Min = 5, Max = 40, Precision = 0)]
        public float SmokeLaunchSpeed = 18f;

        [Draw("Jet length on the hardest beat, m", DrawType.Slider, Min = 0.5, Max = 10, Precision = 1)]
        public float SmokeJetLength = 3.5f;

        [Draw("Jet width while fast, share of the spread puff", DrawType.Slider, Min = 0.05, Max = 1, Precision = 2)]
        public float SmokeJetWidth = 0.25f;

        [Draw("Rise speed of the spread smoke, m/s", DrawType.Slider, Min = 0, Max = 4, Precision = 1)]
        public float SmokeRiseSpeed = 1.2f;

        [Draw("Narrow-jet zone above the smoke emitter at most, m (smoke there stays thin and moves with the stack; it ends at the stack's lip where the mod finds it)", DrawType.Slider, Min = 0, Max = 3, Precision = 2)]
        public float SmokeJetZone = 1.5f;

        [Draw("Particles per puff, ×", DrawType.Slider, Min = 0.2, Max = 4, Precision = 1)]
        public float SmokePuffParticles = 1f;

        [Draw("Puff size, ×", DrawType.Slider, Min = 0.2, Max = 4, Precision = 1)]
        public float SmokePuffSize = 1f;

        [Draw("Steam opacity", DrawType.Slider, Min = 0, Max = 1, Precision = 2)]
        public float SmokeSteamOpacity = 0.7f;

        [Draw("Soot darkness, ×", DrawType.Slider, Min = 0, Max = 3, Precision = 1)]
        public float SmokeSootDarkness = 1f;

        [Draw("Haze opacity between beats", DrawType.Slider, Min = 0, Max = 1, Precision = 2)]
        public float SmokeHazeOpacity = 0.25f;

        [Draw("Sparks, ×", DrawType.Slider, Min = 0, Max = 2, Precision = 2)]
        public float SmokeSparks = 0.25f;

        [Header("Controls")]
        [Draw("Open these settings (Unity Mod Manager's Ctrl+F10 is taken by some desktops)")]
        public KeyBinding OpenSettingsKey = Bind(KeyCode.F, ctrl: true, shift: true);

        [Draw("Work the boiler hand pump (hold)")]
        public KeyBinding HandPumpKey = Bind(KeyCode.P, ctrl: true, shift: true);

        [Draw("Wash out the boiler and fill it from a hydrant (at a pit stop with water, boiler cold)")]
        public KeyBinding HydrantKey = Bind(KeyCode.H, ctrl: true, shift: true);

        [Draw("Shake grate")]
        public KeyBinding ShakeGrateKey = Bind(KeyCode.G, ctrl: true);

        [Draw("Grate shaker (fire-dump) lever's drag, × (lower for finer rocking: a short rock wiggles the grate, a half stroke shakes it)", DrawType.Slider, Min = 0.1f, Max = 1f, Precision = 2)]
        public float GrateLeverSensitivity = 0.35f;

        [Draw("Put a fire iron (slice bar) in front of me (unticks itself). Through the open door: click slices, the mouse wheel pushes and pulls, Shift with the wheel pushes sideways")]
        public bool GiveFireIron;

        [Draw("Toggle fire overlay")]
        public KeyBinding ToggleOverlayKey = Bind(KeyCode.F, ctrl: true);

        [Draw("Show fire overlay")]
        public bool ShowOverlay;

        [Draw("Show each grate zone's coal inside the firebox")]
        public bool ShowZonedCoalBed = true;

        [Draw("Put the stoker's jet valves in the cab (takes effect when the cab next loads)")]
        public bool ShowStokerJetValves = true;

        [Draw("Fit the stock S282 with a mechanical stoker (needs a restart)")]
        public bool RetrofitS282Stoker = true;

        [Draw("Stoker jams: lumps or tramp iron now and then; reverse the stoker engine to clear (needs a restart)")]
        public bool StokerJams = true;

        [Header("Stoker manifold placement (fine-tune for your eye; moves it as you slide)")]
        [Draw("Across, cm (+ toward the middle of the cab)", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float ManifoldNudgeAcross;

        [Draw("Up, cm", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float ManifoldNudgeUp;

        [Draw("Out, cm (+ into the panel)", DrawType.Slider, Min = -40, Max = 40, Precision = 0)]
        public float ManifoldNudgeOut;

        [Draw("Turn, degrees", DrawType.Slider, Min = -45, Max = 45, Precision = 0)]
        public float ManifoldNudgeTurn;

        [Draw("Tilt, degrees", DrawType.Slider, Min = -45, Max = 45, Precision = 0)]
        public float ManifoldNudgeTilt;

        [Draw("Roll, degrees", DrawType.Slider, Min = -45, Max = 45, Precision = 0)]
        public float ManifoldNudgeRoll;

        [Header("Booster controls placement, on the wall by the reverser (fine-tune for your eye; moves them as you slide)")]
        [Draw("Along the wall, cm (+ forward)", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float BoosterNudgeAcross;

        [Draw("Up, cm", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float BoosterNudgeUp;

        [Draw("Out, cm (+ into the wall)", DrawType.Slider, Min = -40, Max = 40, Precision = 0)]
        public float BoosterNudgeOut;

        [Draw("Turn, degrees", DrawType.Slider, Min = -90, Max = 90, Precision = 0)]
        public float BoosterNudgeTurn;

        [Header("Feed pump controls placement, beside the stoker manifold (fine-tune for your eye; moves them as you slide)")]
        [Draw("Across, cm (+ toward the middle of the cab)", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float FeedNudgeAcross;

        [Draw("Up, cm", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float FeedNudgeUp;

        [Header("Drop grate lever placement, from the fire-dump lever (fine-tune for your eye; moves it as you slide)")]
        [Draw("Across, cm (+ right)", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float DropGrateNudgeAcross;

        [Draw("Along, cm (+ forward)", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float DropGrateNudgeAlong;

        [Header("Ashpan handle placement, out from the fireman's side (fine-tune for your eye; moves it as you slide)")]
        [Draw("Along, cm (+ forward)", DrawType.Slider, Min = -150, Max = 150, Precision = 0)]
        public float AshpanLeverNudgeAlong;

        [Draw("Up, cm", DrawType.Slider, Min = -100, Max = 100, Precision = 0)]
        public float AshpanLeverNudgeUp;

        [Draw("Out, cm (+ away from the loco)", DrawType.Slider, Min = -150, Max = 40, Precision = 0)]
        public float AshpanLeverNudgeOut;

        [Header("Diagnostics")]
        [Draw("Jam the stoker now, on the loco you're on (testing; unticks itself)")]
        public bool JamStokerNow;

        [Draw("Foul the boiler water now, on the loco you're on, to where it foams when worked (testing; unticks itself)")]
        public bool FoulBoilerWaterNow;

        [Draw("Melt the fusible plug now, on the loco you're on (testing; unticks itself; a washout renews it)")]
        public bool MeltPlugNow;

        [Draw("Show the chimney smoke and cinders (untick to see what they cost the frame rate)")]
        public bool ShowChimneySmoke = true;

        [Draw("Log firebox creation")]
        public bool LogFireboxCreation = true;

        [Draw("Dump each steam loco's sim graph once per session")]
        public bool DumpSimGraphs;

        [Draw("Dump each supported loco's firebox geometry and chimney smoke setup once per session")]
        public bool DumpFireboxGeometry;

        [Draw("Log coal deliveries, door openings, grate shakes, and injector changes")]
        public bool LogCoalDeliveries;

        [Draw("Log the fire on your loco every N seconds (0 = off)", Min = 0)]
        public int FireStatusLogInterval;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        private Vector3 lastManifoldOffset;
        private Vector3 lastManifoldTurn;
        private Vector4 lastBooster;
        private Vector2 lastFeed;
        private Vector2 lastDropGrate;
        private Vector3 lastAshpanLever;

        public void OnChange()
        {
            Core.FireTuning.Default.ClinkerTendency = ClinkerTendency;
            var ashpanLever = new Vector3(AshpanLeverNudgeAlong, AshpanLeverNudgeUp, AshpanLeverNudgeOut);
            if (ashpanLever != lastAshpanLever)
            {
                lastAshpanLever = ashpanLever;
                Visuals.AshpanLever.RebuildLoaded();
            }
            var dropGrate = new Vector2(DropGrateNudgeAcross, DropGrateNudgeAlong);
            if (dropGrate != lastDropGrate)
            {
                lastDropGrate = dropGrate;
                Visuals.DropGrateCab.RebuildLoaded();
            }
            var booster = new Vector4(BoosterNudgeAcross, BoosterNudgeUp, BoosterNudgeOut, BoosterNudgeTurn);
            if (booster != lastBooster)
            {
                lastBooster = booster;
                Visuals.BoosterCab.RebuildLoaded();
            }
            var offset = new Vector3(ManifoldNudgeAcross, ManifoldNudgeUp, ManifoldNudgeOut);
            var turn = new Vector3(ManifoldNudgeTurn, ManifoldNudgeTilt, ManifoldNudgeRoll);
            var feed = new Vector2(FeedNudgeAcross, FeedNudgeUp);
            if (offset == lastManifoldOffset && turn == lastManifoldTurn && feed == lastFeed)
                return;
            lastManifoldOffset = offset;
            lastManifoldTurn = turn;
            lastFeed = feed;
            Visuals.StokerCab.RebuildLoaded();
        }

        // Change() also caches the key for UMM's new-input-system path.
        private static KeyBinding Bind(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
        {
            var binding = new KeyBinding();
            binding.Change(key, ctrl, shift, alt);
            return binding;
        }
    }
}
