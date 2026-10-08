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

        [Draw("Hardest puff's launch speed, m/s", DrawType.Slider, Min = 5, Max = 40, Precision = 0)]
        public float SmokeLaunchSpeed = 18f;

        [Draw("Jet length on the hardest beat, m", DrawType.Slider, Min = 0.5, Max = 10, Precision = 1)]
        public float SmokeJetLength = 3.5f;

        [Draw("Jet width while fast, share of the spread puff", DrawType.Slider, Min = 0.05, Max = 1, Precision = 2)]
        public float SmokeJetWidth = 0.25f;

        [Draw("Rise speed of the spread smoke, m/s", DrawType.Slider, Min = 0, Max = 4, Precision = 1)]
        public float SmokeRiseSpeed = 1.2f;

        [Draw("Narrow-jet zone above the smoke emitter, m (smoke there stays thin and moves with the stack)", DrawType.Slider, Min = 0, Max = 3, Precision = 2)]
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

        [Draw("Fill the boiler from a hydrant (at a pit stop with water, boiler cold)")]
        public KeyBinding HydrantKey = Bind(KeyCode.H, ctrl: true, shift: true);

        [Draw("Shake grate")]
        public KeyBinding ShakeGrateKey = Bind(KeyCode.G, ctrl: true);

        [Draw("Toggle fire overlay")]
        public KeyBinding ToggleOverlayKey = Bind(KeyCode.F, ctrl: true);

        [Draw("Show fire overlay")]
        public bool ShowOverlay;

        [Draw("Show each grate zone's coal inside the firebox")]
        public bool ShowZonedCoalBed = true;

        [Header("Diagnostics")]
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

        public void OnChange()
        {
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
