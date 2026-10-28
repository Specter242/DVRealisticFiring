using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticStackEffects
{
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Header("Chimney smoke")]
        [Draw("Use the custom smoke effects when installed (restart required)")]
        public bool CustomSmokeEffects = true;

        [Draw("Heat shimmer over the stack (custom smoke effects; restart required)")]
        public bool HeatShimmer = true;

        [Draw("Smoke quality, for a slower graphics card: Medium draws the chimney's smoke at half resolution and drops its faintest smoke, Low with half its particles and more of the faint smoke besides (in VR or with forward rendering, half and a quarter of its particles instead)", DrawType.ToggleGroup)]
        public SmokeQuality SmokeQuality = SmokeQuality.High;

        // The game has no temperature or humidity; the exhaust's white
        // steam needs both.
        [Draw("Season for the chimney's steam (the game has none; with DV Seasons running, its season's temperature is used instead): a summer afternoon's dry air shows little white, a damp dawn or the rain plenty, and winter the most", DrawType.ToggleGroup)]
        public SmokeSeason SmokeSeason = SmokeSeason.Summer;

        // Not in the menu: set in Settings.xml for a bug hunt. Each plume's
        // setup, and every this many seconds the plume on the player's
        // engine (0 none).
        public bool LogSetup = true;
        public int StatusLogInterval;

        // The plume test bed, a development tool for tuning the chimney
        // smoke: shown and hidden by this key, unbound unless set here (not
        // in the menu). Its sliders set away from their defaults, as
        // name=value pairs, are put back only while the key is bound.
        public KeyBinding DevPlumeTestBedKey = new KeyBinding();

        public string PlumeTestBed = "";

        /// <summary>Whether the plume test bed's key is bound (a development setup).</summary>
        public bool PlumeTestBedEnabled => DevPlumeTestBedKey != null && DevPlumeTestBedKey.keyCode != KeyCode.None;

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void OnChange()
        {
        }
    }
}
