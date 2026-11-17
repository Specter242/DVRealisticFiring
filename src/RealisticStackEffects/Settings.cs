using RealisticStackEffects.Core;
using UnityEngine;
using UnityModManagerNet;

namespace RealisticStackEffects
{
    /// <summary>
    /// The player's settings, drawn by Unity Mod Manager's window (and read by
    /// other mods that list a mod's settings, the Comms Tablet's Settings
    /// app): the most used first, short labels ("(restart)" where it takes
    /// one) with the rest in their tooltips. The developer settings have no
    /// Draw: they are set in Settings.xml. Settings.xml keys on the field
    /// names, not their order.
    /// </summary>
    public class Settings : UnityModManager.ModSettings, IDrawable
    {
        [Header("Chimney smoke")]
        [Draw("Smoke quality", DrawType.ToggleGroup, Tooltip =
            "For a slower graphics card. Medium: half-resolution smoke without its faintest\n" +
            "parts. Low: half the particles and less faint smoke too. In VR and forward\n" +
            "rendering, both reduce particles instead of resolution (half and a quarter).\n" +
            "Auto: High, lowered while the frame rate is under 45 with smoke in the air and\n" +
            "the lower quality helps, and raised again when it runs well.")]
        public SmokeQuality SmokeQuality = SmokeQuality.High;

        // The game has no temperature or humidity; the exhaust's white
        // steam needs both.
        [Draw("Season for the chimney's steam", DrawType.ToggleGroup, Tooltip =
            "How much white steam condenses: a summer afternoon's dry air shows little, a\n" +
            "damp dawn or the rain plenty, and winter the most. When DV Seasons is running,\n" +
            "its weather is used instead.")]
        public SmokeSeason SmokeSeason = SmokeSeason.Summer;

        [Draw("Steam around the engine (restart)", Tooltip =
            "Draw the dynamo, cylinder cocks, safety valves, whistle, and leaks as the\n" +
            "chimney's steam, by the weather. Needs custom smoke effects; off, the game's own\n" +
            "puffs stay.")]
        public bool EngineSteam = true;

        [Draw("Heat shimmer (restart)", Tooltip = "Shimmer above the stack. Needs custom smoke effects.")]
        public bool HeatShimmer = true;

        [Draw("Custom smoke effects (restart)", Tooltip =
            "Use the mod's own chimney effects when their assets are installed. Off, the\n" +
            "plume drives the game's own smoke particles.")]
        public bool CustomSmokeEffects = true;

#if !PUBLIC_RELEASE
        // Developer builds only: the plume on the player's engine every this
        // many seconds (0 none), for the in-game checks.
        public int StatusLogInterval;
#endif

#if !PUBLIC_RELEASE
        public KeyBinding DevPlumeTestBedKey = new KeyBinding();

        public string PlumeTestBed = "";

        /// <summary>Whether the plume test bed's key is bound (a development setup).</summary>
        public bool PlumeTestBedEnabled => DevPlumeTestBedKey != null && DevPlumeTestBedKey.keyCode != KeyCode.None;
#endif

        public override void Save(UnityModManager.ModEntry modEntry) => Save(this, modEntry);

        public void OnChange()
        {
        }
    }
}
