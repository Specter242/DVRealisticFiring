using System;
using System.Reflection;
using DV;
using DV.CabControls;
using DV.Interaction;
using DV.Utils;
using DV.VRTK_Extensions;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using TMPro;
using UnityEngine;
using VRTK;

namespace RealisticFiring.Items
{
 internal sealed class ShovelfulWheel : MonoBehaviour
    {
        // How long the size shows under the crosshair (or on the VR hand tooltip), in s.
        private const float ShowFor = 1.5f;
        private const float VrShowFor = 1.8f;

        // Saved this long after the last click, in s, so a run of clicks writes once.
        private const float SaveAfter = 1f;

        private static readonly FieldInfo TooltipTextField = AccessTools.Field(typeof(ControllerTooltip), "tooltipText");

        private static float saveAt = float.PositiveInfinity;

        private ItemScrolling scrolling;
        private ShovelScrollingVR vrScrolling;
        private ControllerTooltip activeVrTooltip;
        private string activeVrTooltipText;
        private float hideVrTooltipAt = float.PositiveInfinity;

        private void Start()
        {
            scrolling = GetComponent<ItemScrolling>();
            vrScrolling = scrolling as ShovelScrollingVR;
            if (scrolling != null)
                scrolling.Scrolled += OnScrolled;
        }

        private void Update()
        {
            if (Time.unscaledTime >= saveAt)
                SaveNow();
            if (activeVrTooltip != null && Time.unscaledTime >= hideVrTooltipAt)
                HideVrTooltip();
        }

        // Stowed in the inventory or unloaded: saved now.
        private void OnDisable()
        {
            HideVrTooltip();
            SaveNow();
        }

        private void OnDestroy()
        {
            if (scrolling != null)
                scrolling.Scrolled -= OnScrolled;
            HideVrTooltip();
            SaveNow();
        }

        private void OnScrolled(ScrollAction action)
        {
            if (action == ScrollAction.Release)
                return;
            var up = action.IsPositive();
            var settings = Main.Settings;
            var largest = settings.Shovelful;
            var before = settings.Scoop;
            var after = ShovelSize.Wheeled(before, up ? 1 : -1, Settings.MinShovelful, largest);
            var full = after >= largest;
            var atLimit = after == before;
            settings.ScoopMass = full ? 0f : after;
            if (!atLimit)
                saveAt = Time.unscaledTime + SaveAfter;
            FireController.Show(Notice(after, largest, full, atLimit), ShowFor);
            if (vrScrolling != null)
                ShowVrFeedback(after, largest, full, atLimit, vrScrolling.LastScrollController);
        }

        private void ShowVrFeedback(float scoop, float largest, bool full, bool atLimit, VRTK_ControllerReference ctrlRef)
        {
            if (ctrlRef != null && ctrlRef.IsValid())
                HapticUtils.DoHapticPulse(ctrlRef, atLimit ? HapticIntensityType.Weak : HapticIntensityType.Normal);

            var tooltip = TooltipFor(ctrlRef);
            if (tooltip == null)
                return;
            var text = VrNotice(scoop, largest, full, atLimit);
            if (activeVrTooltip != null && activeVrTooltip != tooltip)
                HideVrTooltip();
            activeVrTooltip = tooltip;
            activeVrTooltipText = text;
            hideVrTooltipAt = Time.unscaledTime + VrShowFor;
            tooltip.ShowTooltip(text, showBackground: true);
        }

        private void HideVrTooltip()
        {
            if (activeVrTooltip != null && ShouldHideTooltip(ReadTooltipText(activeVrTooltip), activeVrTooltipText))
                activeVrTooltip.HideTooltip();
            activeVrTooltip = null;
            activeVrTooltipText = null;
            hideVrTooltipAt = float.PositiveInfinity;
        }

        private static string ReadTooltipText(ControllerTooltip tooltip)
        {
            var tmp = TooltipTextField != null ? TooltipTextField.GetValue(tooltip) as TMP_Text : null;
            if (tmp == null)
                tmp = tooltip.GetComponentInChildren<TMP_Text>(true);
            return tmp != null ? tmp.text : null;
        }

        internal static bool ShouldHideTooltip(string currentText, string shownText) =>
            !string.IsNullOrEmpty(shownText) && string.Equals(currentText, shownText, StringComparison.Ordinal);

        private static ControllerTooltip TooltipFor(VRTK_ControllerReference ctrlRef)
        {
            if (ctrlRef != null && ctrlRef.hand == SDK_BaseController.ControllerHand.Left)
                return VRTK_ControllerUtils_DV.ControllerTooltipLeft ?? VRTK_ControllerUtils_DV.ControllerTooltipRight;
            if (ctrlRef != null && ctrlRef.hand == SDK_BaseController.ControllerHand.Right)
                return VRTK_ControllerUtils_DV.ControllerTooltipRight ?? VRTK_ControllerUtils_DV.ControllerTooltipLeft;
            return VRTK_ControllerUtils_DV.ControllerTooltipRight ?? VRTK_ControllerUtils_DV.ControllerTooltipLeft;
        }

        private static string Notice(float scoop, float largest, bool full, bool atLimit)
        {
            if (!full)
                return atLimit ? $"Shovelful: {Kg(scoop)}, the smallest" : $"Shovelful: {scoop:0.#} of {Kg(largest)}";
            return atLimit
                ? $"Shovelful: {Kg(scoop)}, full (the largest is set in the settings, {Main.Settings.OpenSettingsKey.keyCode})"
                : $"Shovelful: {Kg(scoop)}, full";
        }

        internal static string VrNotice(float scoop, float largest, bool full, bool atLimit)
        {
            if (!full)
                return atLimit ? $"Shovelful: {Kg(scoop)} (min)" : $"Shovelful: {scoop:0.#} / {Kg(largest)}";
            return $"Shovelful: {Kg(scoop)} (full)";
        }

        private static void SaveNow()
        {
            if (float.IsPositiveInfinity(saveAt))
                return;
            saveAt = float.PositiveInfinity;
            var settings = Main.Settings;
            settings.Save(Main.Mod);
#if !PUBLIC_RELEASE
            Main.Mod.Logger.Log($"Shovelful set to {Kg(settings.Scoop)} of {Kg(settings.Shovelful)}");
#endif
        }

        internal static string Kg(float kg) => $"{kg:0.#} kg";
    }

    /// <summary>
    /// Gives each shovel held-item scrolling (mouse wheel on desktop, Trigger +
    /// Thumbstick/Touchpad in VR) and <see cref="ShovelfulWheel"/> when the
    /// realistic fireboxes are on, where the shovelful counts.
    /// </summary>
    [HarmonyPatch(typeof(ShovelNonPhysicalCoal), "Start")]
    internal static class ShovelWheelPatch
    {
        private static void Postfix(ShovelNonPhysicalCoal __instance)
        {
            if (!Main.ReplacementEnabled || __instance == null
                || __instance.GetComponent<ItemBase>() == null
                || __instance.GetComponent<ShovelfulWheel>() != null)
                return;
            if (VRManager.IsVREnabled())
            {
                if (__instance.GetComponent<ItemScrolling>() == null)
                    __instance.gameObject.AddComponent<ShovelScrollingVR>();
                __instance.gameObject.AddComponent<ShovelfulWheel>();
                return;
            }
            if (__instance.GetComponent<GrabHandlerItem>() == null)
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
                || !FireboxRegistry.TryGetTended(TrainCar.Resolve(pile.gameObject), out var firebox))
                return;
            var texts = SingletonBehaviour<InteractionText>.Instance;
            var prompt = SingletonBehaviour<InteractionTextControllerNonVr>.Instance;
            var load = texts != null ? texts.GetText(InteractionInfoType.ShovelLoadCoal) : null;
            // During the accelerated startup the shovelful it will take is bigger: "12 kg (startup ×4)".
            var scoop = Main.Settings.Scoop;
            var kg = firebox.StartupScoop(scoop);
            if (prompt != null && !string.IsNullOrEmpty(load))
                prompt.DisplayText($"{load} ({ShovelfulWheel.Kg(kg)}{ShovelSize.BoostNote(scoop > 0f ? kg / scoop : 1f)}; mouse wheel for less)");
        }
    }
}
