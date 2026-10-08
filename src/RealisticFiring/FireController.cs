using System.Linq;
using DV;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring
{
    /// <summary>
    /// Unity-side controls for the loco the player is in: the grate-shake,
    /// hand-pump, and hydrant keybinds, the status log, the aim label and
    /// notices under the crosshair, and the debug overlay.
    /// </summary>
    internal sealed class FireController : MonoBehaviour
    {
        // Just below the crosshair, where the game's own prompts appear.
        private const float AimLabelOffset = 36f;

        // Notices sit a line below the aim label.
        private const float NoticeOffset = 66f;
        private const float NoticeTime = 4f;

        // How often a running hose checks that the train is still at the water point.
        private const float HydrantCheckInterval = 1f;

        // The rocking lever takes a moment to work back and forth.
        private const float ShakeCooldown = 1.5f;

        private readonly FireOverlay overlay = new FireOverlay();
        private float lastShakeTime = float.NegativeInfinity;
        private float lastStatusTime;
        private GUIStyle aimLabel;
        private string notice;
        private float noticeTime = float.NegativeInfinity;
        private float hydrantCheckTime;

        private void Update()
        {
            var settings = Main.Settings;
            if (settings.ToggleOverlayKey.Down())
                settings.ShowOverlay = !settings.ShowOverlay;

            if (settings.ShakeGrateKey.Down() && TimeUtil.IsFlowing
                && FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox)
                && Time.time - lastShakeTime >= ShakeCooldown)
            {
                lastShakeTime = Time.time;
                var ashBefore = firebox.Model.AshMass;
                firebox.ShakeGrate();
                overlay.NoteShake(Time.time);
                if (settings.LogCoalDeliveries)
                    Main.Mod.Logger.Log($"{firebox.Car.ID}: grate shaken, ash {ashBefore:F2} -> {firebox.Model.AshMass:F2} kg");
            }

            if (InjectorRig.TryGetTended(PlayerManager.Car, out var rig))
                UpdateFilling(rig, settings);
        }

        // The hand pump works while its key is held; the hydrant hose toggles,
        // and stops itself once the glass is full, the boiler has pressure, or
        // the train has left the water point.
        private void UpdateFilling(InjectorRig rig, Settings settings)
        {
            var pumping = settings.HandPumpKey.Pressed() && TimeUtil.IsFlowing;
            if (pumping && !rig.HandPumping && !rig.CanHandPump)
                Notify($"The hand pump cannot force water in above {InjectorRig.HandPumpMaxPressure:F0} bar");
            rig.HandPumping = pumping;

            if (settings.HydrantKey.Down())
            {
                if (rig.HydrantFilling)
                {
                    rig.HydrantFilling = false;
                    Notify("Hydrant hose off");
                }
                else if (rig.BoilerPressure > InjectorRig.HydrantMaxPressure)
                    Notify("The boiler is under pressure: a hose can only fill a cold boiler");
                else if (!rig.CanHydrantFill)
                    Notify("The boiler is already full to the top of the glass");
                else if (!WaterPoints.Serves(rig.Car))
                    Notify("No hydrant here: stand at a pit stop with water");
                else
                {
                    rig.HydrantFilling = true;
                    hydrantCheckTime = Time.time;
                    Notify("Hydrant hose on: filling the boiler");
                }
            }

            if (rig.HydrantFilling)
            {
                if (!rig.CanHydrantFill)
                {
                    rig.HydrantFilling = false;
                    Notify(rig.BoilerPressure > InjectorRig.HydrantMaxPressure ? "Hydrant hose off: the boiler has pressure" : "Hydrant hose off: the boiler is full");
                }
                else if (Time.time - hydrantCheckTime >= HydrantCheckInterval)
                {
                    hydrantCheckTime = Time.time;
                    if (!WaterPoints.Serves(rig.Car))
                    {
                        rig.HydrantFilling = false;
                        Notify("Hydrant hose off: the train left the water point");
                    }
                }
            }
        }

        private void Notify(string text)
        {
            notice = text;
            noticeTime = Time.time;
            Main.Mod.Logger.Log(text);
        }

        private void LateUpdate()
        {
            var interval = Main.Settings.FireStatusLogInterval;
            if (interval <= 0 || Time.time - lastStatusTime < interval
                || !FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox))
                return;

            lastStatusTime = Time.time;
            var fire = firebox.Model;
            var zones = string.Join(" ", fire.Zones.Select(z => z.Coal.ToString("F0")));
            Main.Mod.Logger.Log(
                $"{firebox.Car.ID} status: fire {(fire.FireOn ? "on" : "out")}, heat {fire.Heat / 1e6f:F2} MW, " +
                $"{fire.HeatPerCoal / 1e6f:F1} MJ/kg, burning {fire.ConsumptionRate * 3600f:F0} kg/h, " +
                $"temp {fire.Temperature:F0} C, smoke {fire.SmokeDensity:P0}, bed air {fire.OxygenAvailability:P0}, " +
                $"draft {firebox.AirFlowIn:F2} kg/s (grate {fire.PrimaryAirFlow:F2}, door {fire.DoorAirFlow:F2} at {firebox.DoorOpening:P0}, " +
                $"excess {fire.ExcessAirFlow:F2}), coal {fire.CoalMass:F1} kg, ash {fire.AshMass:F2}, clinker {fire.ClinkerMass:F2}, " +
                $"startup x{firebox.StartupMultiplier:F1}, zones back-to-front [{zones}]" +
                (InjectorRig.TryGetTended(PlayerManager.Car, out var rig) ? $"; {Describe(rig)}" : ""));
        }

        /// <summary>The injector and boiler filling, for the status log and overlay.</summary>
        internal static string Describe(InjectorRig rig)
        {
            var flow = rig.Flow;
            var injector = flow.State == InjectorState.Working
                ? $"injector working, {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s"
                : flow.State == InjectorState.PickingUp
                    ? $"injector picking up, spilling {flow.Overflow:F1} kg/s"
                    : flow.State == InjectorState.KnockedOff
                        ? $"injector KNOCKED OFF (close and reopen), spilling {flow.Overflow:F1} kg/s"
                        : "injector off";
            var filling = rig.HydrantFilling ? $", hydrant {rig.FillRate:F1} kg/s" : rig.HandPumping ? $", hand pump {rig.FillRate:F1} kg/s" : "";
            return $"boiler {rig.BoilerPressure:F1} bar, water {rig.WaterLevel:P0}, {injector}{filling}";
        }

        private void OnGUI()
        {
            if (notice != null && Time.time - noticeTime <= NoticeTime)
                DrawLabel(notice, NoticeOffset, new Color(1f, 0.85f, 0.5f));
            if (!FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox))
                return;
            if (Main.Settings.ShowOverlay)
                overlay.Draw(firebox, Time.time);
            if (Main.Settings.ShowAimLabel && firebox.HoverAim is DoorAimSample aim && aim.IsLiveAt(Time.time)
                && (!aim.Raking || firebox.DoorOpening >= ShovelNonPhysicalCoal.REQUIRED_DOOR_OPENED_PERCENTAGE_NON_VR))
                DrawLabel((aim.Raking ? "Rake → " : "Shovel → ") + GrateLayout.Describe(aim.Zone), AimLabelOffset, new Color(0.55f, 0.8f, 1f));
        }

        private void DrawLabel(string text, float offset, Color color)
        {
            aimLabel = aimLabel ?? new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 20,
                fontStyle = FontStyle.Bold,
            };
            var area = new Rect(0f, Screen.height / 2f + offset, Screen.width, 30f);
            var shadow = new Rect(area.x + 1f, area.y + 1f, area.width, area.height);
            aimLabel.normal.textColor = Color.black;
            GUI.Label(shadow, text, aimLabel);
            aimLabel.normal.textColor = color;
            GUI.Label(area, text, aimLabel);
        }
    }
}
