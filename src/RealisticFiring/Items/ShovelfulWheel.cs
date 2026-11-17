using DV;
using DV.CabControls;
using DV.Interaction;
using DV.Utils;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Items
{
 internal sealed class ShovelfulWheel : MonoBehaviour
    {
        // How long the size shows under the crosshair, in s.
        private const float ShowFor = 1.5f;

        // Saved this long after the last click, in s, so a run of clicks writes once.
        private const float SaveAfter = 1f;

        private static float saveAt = float.PositiveInfinity;

        private ItemScrolling scrolling;

        private void Start()
        {
            scrolling = GetComponent<ItemScrolling>();
            if (scrolling != null)
                scrolling.Scrolled += OnScrolled;
        }

        private void Update()
        {
            if (Time.unscaledTime >= saveAt)
                SaveNow();
        }

        // Stowed in the inventory or unloaded: saved now.
        private void OnDisable() => SaveNow();

        private void OnDestroy()
        {
            if (scrolling != null)
                scrolling.Scrolled -= OnScrolled;
            SaveNow();
        }

        private static void OnScrolled(ScrollAction action)
        {
            var up = action == ScrollAction.ScrollUp;
            if (!up && action != ScrollAction.ScrollDown)
                return;
            var settings = Main.Settings;
            var largest = settings.Shovelful;
            var before = settings.Scoop;
            var after = ShovelSize.Wheeled(before, up ? 1 : -1, Settings.MinShovelful, largest);
            var full = after >= largest;
            settings.ScoopMass = full ? 0f : after;
            if (after != before)
                saveAt = Time.unscaledTime + SaveAfter;
            FireController.Show(Notice(after, largest, full, atLimit: after == before), ShowFor);
        }

        private static string Notice(float scoop, float largest, bool full, bool atLimit)
        {
            if (!full)
                return atLimit ? $"Shovelful: {Kg(scoop)}, the smallest" : $"Shovelful: {scoop:0.#} of {Kg(largest)}";
            return atLimit
                ? $"Shovelful: {Kg(scoop)}, full (the largest is set in the settings, {Main.Settings.OpenSettingsKey.keyCode})"
                : $"Shovelful: {Kg(scoop)}, full";
        }

        private static void SaveNow()
        {
            if (float.IsPositiveInfinity(saveAt))
                return;
            saveAt = float.PositiveInfinity;
            var settings = Main.Settings;
            settings.Save(Main.Mod);
#if !PUBLIC_RELEASE
            Main.Mod.Logger.Log($"Shovelful set to {Kg(settings.Scoop)} of {Kg(settings.Shovelful)} with the mouse wheel");
#endif
        }

        internal static string Kg(float kg) => $"{kg:0.#} kg";
    }

    /// <summary>
    /// Gives each shovel the game's wheel for a held item (as the fire iron
    /// and the lantern have it) and <see cref="ShovelfulWheel"/>, with the
    /// mouse and with the realistic fireboxes on, where the shovelful counts.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "Start")]
    internal static class ShovelWheelPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            if (!Main.ReplacementEnabled || VRManager.IsVREnabled() || __instance == null
                || __instance.GetComponent<GrabHandlerItem>() == null || __instance.GetComponent<ItemBase>() == null
                || __instance.GetComponent<ShovelfulWheel>() != null)
                return;
            if (__instance.GetComponent<ItemScrolling>() == null)
                __instance.gameObject.AddComponent<ItemScrollingNonVR>();
            __instance.gameObject.AddComponent<ShovelfulWheel>();
        }
    }

    /// <summary>
    /// Pointing an empty shovel at a realistic firebox's coal names the
    /// shovelful it will take and that the wheel changes it.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), nameof(ShovelNonPhysicalCoal.HandleHover))]
    internal static class ShovelfulPromptPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance, ItemUseTarget target)
        {
            if (__instance.IsLoaded || target == null || __instance.GetComponent<ShovelfulWheel>() == null
                || !target.TryGetComponent<ShovelCoalPile>(out var pile) || !(pile.CoalAvailable() > 0f)
                || !FireboxRegistry.TryGetTended(TrainCar.Resolve(pile.gameObject), out _))
                return;
            var texts = SingletonBehaviour<InteractionText>.Instance;
            var prompt = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            var load = texts != null ? texts.GetText(InteractionInfoType.ShovelLoadCoal) : null;
            if (prompt != null && !string.IsNullOrEmpty(load))
                prompt.DisplayText($"{load} ({ShovelfulWheel.Kg(Main.Settings.Scoop)}; mouse wheel for less)");
        }
    }
}
