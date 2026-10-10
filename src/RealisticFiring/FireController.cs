using System.Linq;
using DV;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
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

        private static FireController instance;

        private void Awake() => instance = this;

        /// <summary>Shows a notice under the crosshair, and logs it.</summary>
        internal static void Announce(string text)
        {
            if (instance != null)
                instance.Notify(text);
            else
                Main.Mod?.Logger.Log(text);
        }

        private void FixedUpdate() => PerfMeter.PhysicsStep();

        private void Update()
        {
            PerfMeter.Frame();
            var start = PerfMeter.Start();
            HandleKeys();
            Visuals.StokerCab.EnsureBuilt();
            Visuals.OilBurnerCab.EnsureBuilt();
            Visuals.OilFlareback.Update();
            PerfMeter.Stop(PerfSection.Controller, start);
        }

        private void HandleKeys()
        {
            var settings = Main.Settings;
            if (settings.GiveFireIron)
            {
                settings.GiveFireIron = false;
                // In multiplayer only the host makes items; the shops sell the iron.
                Notify(Net.IsClient ? "In multiplayer, buy the fire iron at a shop, beside the shovel" : Items.FireIron.GiveToPlayer());
            }
            if (settings.JamStokerNow)
            {
                settings.JamStokerNow = false;
                if (FireboxRegistry.TryGetTended(PlayerManager.Car, out var jammed) && jammed.StokerModel != null)
                {
                    jammed.StokerModel.Jam();
                    Notify("Stoker jammed: the engine stalls; reverse it a few turns to clear it");
                }
                else
                {
                    Notify("No stoker on this loco to jam");
                }
            }
            if (settings.FoulBoilerWaterNow)
            {
                settings.FoulBoilerWaterNow = false;
                if (BoilerWaterRig.TryGetTended(PlayerManager.Car, out var water))
                {
                    water.Foul(BoilerWater.FoamOnsetPpm + 0.75f * (BoilerWater.FoamFullPpm - BoilerWater.FoamOnsetPpm));
                    Notify("Boiler water fouled: work the engine and watch the glass");
                }
                else
                {
                    Notify("No boiler on this loco to foul");
                }
            }
            if (settings.MeltPlugNow)
            {
                settings.MeltPlugNow = false;
                Notify(BoilerWaterRig.TryGetTended(PlayerManager.Car, out var plugged) && plugged.MeltPlug()
                    ? "Fusible plug melted: steam is blowing into the firebox"
                    : "No fusible plug on this loco");
            }
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
            if (Net.IsClient)
            {
                UpdateFillingRemotely(rig, settings);
                return;
            }
            var pumping = settings.HandPumpKey.Pressed() && TimeUtil.IsFlowing;
            if (pumping && !rig.HandPumping && !rig.CanHandPump)
                Notify($"The hand pump cannot force water in above {InjectorRig.HandPumpMaxPressure:F0} bar");
            rig.HandPumping = pumping;

            if (settings.HydrantKey.Down())
            {
                Notify(ToggleHydrant(rig));
                hydrantCheckTime = Time.time;
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

        private bool remotePumping;

        // On a multiplayer client the host fills the boiler: the hand pump and
        // hose go to it as requests, and it answers with a notice.
        private void UpdateFillingRemotely(InjectorRig rig, Settings settings)
        {
            var pumping = settings.HandPumpKey.Pressed() && TimeUtil.IsFlowing;
            if (pumping != remotePumping)
            {
                remotePumping = pumping;
                Net.TrySend(rig.Car, pumping ? FireAction.HandPumpOn : FireAction.HandPumpOff);
            }
            if (settings.HydrantKey.Down())
                Net.TrySend(rig.Car, FireAction.Hydrant);
        }

        /// <summary>
        /// Turns the hydrant hose off, or on where it can fill the boiler,
        /// washing a cold boiler out first. Returns the notice for whoever asked.
        /// </summary>
        internal static string ToggleHydrant(InjectorRig rig)
        {
            if (rig.HydrantFilling)
            {
                rig.HydrantFilling = false;
                return "Hydrant hose off";
            }
            if (rig.BoilerPressure > InjectorRig.HydrantMaxPressure)
                return "The boiler is under pressure: a hose can only fill a cold boiler";
            if (!WaterPoints.Serves(rig.Car))
                return "No hydrant here: stand at a pit stop with water";
            // With the boiler cold at the shed, the hose washes it out first.
            var washed = BoilerWaterRig.TryGet(rig.Car, out var water) && (Main.Settings.BoilerWaterImpurities || water.Plug != null);
            if (washed)
                water.Washout();
            if (rig.CanHydrantFill)
            {
                rig.HydrantFilling = true;
                return washed ? "Hydrant hose on: boiler washed out, filling it with fresh water" : "Hydrant hose on: filling the boiler";
            }
            return washed ? "Boiler washed out and filled with fresh water" : "The boiler is already full to the top of the glass";
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
            if (interval <= 0 || Time.time - lastStatusTime < interval)
                return;
            lastStatusTime = Time.time;
            Main.Mod.Logger.Log($"Performance: {PerfMeter.Report()}");
            if (!FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox))
            {
                if (RealisticOilFirebox.TryGetTended(PlayerManager.Car, out var oil))
                    Main.Mod.Logger.Log($"{oil.Car.ID} oil status: fire {(oil.Model.FireOn ? "on" : "out")}; {DescribeOil(oil)}" +
                                        (InjectorRig.TryGetTended(PlayerManager.Car, out var oilRig) ? $"; {Describe(oilRig)}" : ""));
                return;
            }

            var fire = firebox.Model;
            var zones = string.Join(" ", fire.Zones.Select(z => z.Coal.ToString("F0")));
            Main.Mod.Logger.Log(
                $"{firebox.Car.ID} status: fire {(fire.FireOn ? "on" : "out")}, heat {fire.Heat / 1e6f:F2} MW, " +
                $"{fire.HeatPerCoal / 1e6f:F1} MJ/kg, burning {fire.ConsumptionRate * 3600f:F0} kg/h, " +
                $"temp {fire.Temperature:F0} C, smoke {fire.SmokeDensity:P0}, bed air {fire.OxygenAvailability:P0}, " +
                $"draft {firebox.AirFlowIn:F2} kg/s (grate {fire.PrimaryAirFlow:F2}, door {fire.DoorAirFlow:F2} at {firebox.DoorOpening:P0}, " +
                $"excess {fire.ExcessAirFlow:F2}), coal {fire.CoalMass:F1} kg, ash {fire.AshMass:F2}, clinker {fire.ClinkerMass:F2}, " +
                $"ashpan {fire.Ashpan:F0} kg ({fire.AshpanFill:P0}), " +
                $"startup x{firebox.StartupMultiplier:F1}, stoked {firebox.TakeContinuousFeed():F1} kg, zones back-to-front [{zones}]" +
                (DescribeStoker(firebox) is string stoker ? $"; {stoker}" : "") +
                (InjectorRig.TryGetTended(PlayerManager.Car, out var rig) ? $"; {Describe(rig)}" : "") +
                (DescribeBooster(firebox.Car) is string booster ? $"; {booster}" : ""));
        }

        private const float PsiPerBar = 14.5038f;

        /// <summary>The stoker's engine, feed, jets, and losses, for the status log and overlay; null without one.</summary>
        internal static string DescribeStoker(RealisticFirebox firebox)
        {
            if (!firebox.LastStoker.HasValue)
                return null;
            var flow = firebox.LastStoker.Value;
            return $"stoker engine {(flow.Jammed ? "JAMMED (reverse it to clear) " : "")}{firebox.StokerEngineSpeed:P0} ({flow.EnginePressure * PsiPerBar:F0} psi), " +
                   $"jets {flow.ManifoldPressure * PsiPerBar:F0} psi (front {flow.LeftFrontPressure * PsiPerBar:F0}/{flow.RightFrontPressure * PsiPerBar:F0}, " +
                   $"back {flow.LeftBackPressure * PsiPerBar:F0}/{flow.RightBackPressure * PsiPerBar:F0}, fines {flow.FinesPressure * PsiPerBar:F0}), " +
                   $"cinders {flow.CinderRate:F2} kg/s, steam {flow.SteamUsed:F2} kg/s";
        }

        /// <summary>The booster, for the status log and overlay, or null without one.</summary>
        internal static string DescribeBooster(TrainCar car)
        {
            if (!RealisticBooster.TryGet(car, out var booster))
                return null;
            var flow = booster.Flow;
            return $"booster {flow.State} ({Visuals.BoosterCab.Describe(flow, flow.State != BoosterState.CutOut)}), " +
                   $"{flow.Force / 4.44822f:F0} lb, {flow.InletPressure * PsiPerBar:F0} psi, {flow.CrankRpm:F0} rpm, " +
                   $"steam {flow.SteamUsed:F2} kg/s, warm {booster.Model.Warmth:P0}" +
                   (booster.Clashes > 0 ? $", {booster.Clashes} clashes" : "");
        }

        /// <summary>The oil fire, for the status log and overlay: lines separated by semicolons.</summary>
        internal static string DescribeOil(RealisticOilFirebox oil)
        {
            var fire = oil.Model;
            var stack = !fire.FireOn ? (fire.Vapor > 0.05f ? "white vapor" : "clear")
                : fire.SootDensity > 0.3f ? "black" : fire.ExcessAir > 2f ? "clear (too much air)" : "light haze";
            return $"Heat {fire.Heat / 1e6f:F2} MW ({fire.Heat / fire.Parameters.MaxHeatOutput:P0} of max), oil {fire.FeedRate:F2} kg/s " +
                   $"(firing valve {(oil.Feed != null ? oil.Feed.FiringValve : 0f):P0}), burning {fire.BurnRate:F2} kg/s; " +
                   $"atomizer {oil.AtomizerValve:P0}: {fire.Atomizing:F1}× the oil's need" + (fire.Drumming > 0f ? $", drumming {fire.Drumming:P0}" : "") +
                   $", {fire.AtomizerSteam:F2} kg/s of steam; " +
                   $"draft {oil.AirFlowIn:F2} kg/s, {fire.ExcessAir:F1}× the air the oil needs, stack {stack}, soot {fire.SootDensity:P0}; " +
                   $"brick {fire.BrickTemperature:F0} C, firebox {fire.Temperature:F0} C, door {oil.DoorOpening:P0}; " +
                   $"oil {fire.OilTemperature:F0} C ({fire.OilTemperature * 9f / 5f + 32f:F0} F), heater {oil.HeaterValve:P0}; " +
                   $"flues {fire.FlueSoot:P0} sooted" + (fire.FlueSoot > 0.3f ? " (sand them)" : "") +
                   (fire.Vapor > 0.01f || fire.PooledOil > 0.01f ? $"; vapor {fire.Vapor:F2} kg, pooled oil {fire.PooledOil:F2} kg: clear it with the blower before lighting" : "") +
                   $"; flue sand on the shovel {Visuals.FlueSandBox.Loaded:F1} kg";
        }

        /// <summary>The injector and boiler filling, for the status log and overlay.</summary>
        internal static string Describe(InjectorRig rig)
        {
            var flow = rig.Flow;
            var injector = DescribeInjector(flow);
            var filling = rig.HydrantFilling ? $", hydrant {rig.FillRate:F1} kg/s" : rig.HandPumping ? $", hand pump {rig.FillRate:F1} kg/s" : "";
            var pump = rig.Pump != null ? $"; {Visuals.FeedPumpCab.Hover(rig)}" : "";
            var water = BoilerWaterRig.TryGet(rig.Car, out var waterRig) ? $"; {DescribeWater(waterRig)}" : "";
            return $"boiler {rig.BoilerPressure:F1} bar, water {rig.WaterLevel:P0}, {injector}{filling}{pump}{water}";
        }

        /// <summary>The boiler water and its guard, for the status log and overlay.</summary>
        internal static string DescribeWater(BoilerWaterRig rig)
        {
            var water = rig.Water;
            var quality = Main.Settings.BoilerWaterImpurities
                ? $"water {water.FoamingConcentration:F0} ppm ({water.Concentration:F0} dissolved, sludge {water.SettledSludge:F2} kg settled), " +
                  $"foam {water.Foam:P0}, glass {rig.GlassLevel:F2} over {rig.TrueLevel:F2} true, carryover {water.Carryover:P0}"
                : "water impurities off";
            var guard = rig.Plug != null
                ? rig.Plug.Melted ? $", fusible plug MELTED, blowing {rig.PlugVent:F2} kg/s" : $", fusible plug at {rig.Plug.CoreTemperature:F0} C"
                : rig.Alarm != null ? rig.Alarm.Sounding ? ", LOW-WATER ALARM" : ", low-water alarm quiet" : "";
            return quality + guard;
        }

        private static string DescribeInjector(InjectorFlow flow)
        {
            var spill = $"spilling {flow.Overflow:F1} kg/s water, {flow.OverflowSteam:F1} kg/s steam";
            var heat = $"{flow.InjectorTemperature:F0} C, takes below {flow.RestartLimit:F0} C";
            switch (flow.State)
            {
                case InjectorState.Working:
                    return $"injector working, {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s";
                case InjectorState.Priming:
                    return $"injector priming, {spill}";
                case InjectorState.Hot:
                    return $"injector HOT ({heat}; hold it cracked to cool it), {spill}";
                case InjectorState.LowPressure:
                    return $"injector waiting for pressure, {spill}";
                case InjectorState.NoWater:
                    return $"injector NO WATER (tender dry), {spill}";
                case InjectorState.Broken:
                    return $"injector BROKEN (shut it, then crack it until it primes; {heat}), {spill}";
                default:
                    return flow.InjectorTemperature > flow.RestartLimit ? $"injector off, hot ({heat})" : "injector off";
            }
        }

        private void OnGUI()
        {
            var start = PerfMeter.Start();
            DrawGui();
            PerfMeter.Stop(PerfSection.Gui, start);
        }

        private void DrawGui()
        {
            if (notice != null && Time.time - noticeTime <= NoticeTime)
                DrawLabel(notice, NoticeOffset, new Color(1f, 0.85f, 0.5f));
            if (!FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox))
            {
                if (Main.Settings.ShowOverlay && RealisticOilFirebox.TryGetTended(PlayerManager.Car, out var oil))
                    overlay.DrawOil(oil);
                return;
            }
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
