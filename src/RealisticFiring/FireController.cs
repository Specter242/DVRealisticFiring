using System.Linq;
using DV;
using LocoSim.Implementations;
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
        private readonly FiremanPanel firemanPanel = new FiremanPanel();
        private float lastShakeTime = float.NegativeInfinity;
        private float lastStatusTime;
        private GUIStyle aimLabel;
        private string notice;
        private float noticeTime = float.NegativeInfinity;

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
            if (settings.RenewBoilerNow)
            {
                settings.RenewBoilerNow = false;
                Notify(Net.IsClient ? "Only the host can renew the boiler"
                    : BoilerWaterRig.TryGetTended(PlayerManager.Car, out var renewed) && renewed.Renew("renewed from the settings")
                        ? "Boiler renewed with a new fusible plug: relight the fire"
                        : "No boiler on this loco to renew");
            }
            if (settings.ToggleOverlayKey.Down())
                FireOverlay.Step(settings);

            var withFireman = FiremanRig.TryGetTended(PlayerManager.Car, out _);
            if (settings.FiremanKey.Down())
            {
                if (withFireman)
                    firemanPanel.Toggle();
                else
                    Notify("No fireman here: board a coal-fired steam loco");
            }
            if (!withFireman)
                firemanPanel.SetOpen(false);

            if (settings.ShakeGrateKey.Down() && TimeUtil.IsFlowing
                && FireboxRegistry.TryGetTended(PlayerManager.Car, out var firebox)
                && Time.time - lastShakeTime >= ShakeCooldown)
            {
                lastShakeTime = Time.time;
                var ashBefore = firebox.Model.AshMass;
                firebox.ShakeGrate();
                if (settings.LogCoalDeliveries)
                    Main.Mod.Logger.Log($"{firebox.Car.ID}: grate shaken, ash {ashBefore:F2} -> {firebox.Model.AshMass:F2} kg");
            }

            if (InjectorRig.TryGetTended(PlayerManager.Car, out var rig))
                UpdateFilling(rig, settings);
            else
                LetGoOfPump(null);
            if (RealisticOilFirebox.TryGetTended(PlayerManager.Car, out var oil))
            {
                UpdateSteamLine(oil, settings);
                AnnounceFireUp(oil);
            }
        }

        private string lastFireUpStep;
        private static readonly InjectorReading[] NoInjectors = new InjectorReading[0];
        private float fireUpCheckTime;

        private void AnnounceFireUp(RealisticOilFirebox oil)
        {
            if (Time.time - fireUpCheckTime < 1f)
                return;
            fireUpCheckTime = Time.time;
            // The step, not its live figures, so a changing count does not
            // repeat it; lit with the lighter, the same step says so again.
            var advice = FiringAdvice.ForOil(ReadOil(oil));
            var step = advice.LightingStep > 0 ? $"{advice.LightingStep}{advice.Level}" : null;
            if (step != null && step != lastFireUpStep)
                Notify(advice.Text);
            lastFireUpStep = step;
        }

        private float steamLineCheckTime;

        // The shed's steam line toggles, and comes off once the train has left the pit stop.
        private void UpdateSteamLine(RealisticOilFirebox oil, Settings settings)
        {
            if (settings.SteamLineKey.Down())
            {
                if (Net.IsClient)
                    Net.TrySend(oil.Car, FireAction.SteamLine);
                else
                    Notify(ToggleSteamLine(oil));
                steamLineCheckTime = Time.time;
            }
            if (Net.IsClient || !oil.SteamLine || Time.time - steamLineCheckTime < HydrantCheckInterval)
                return;
            steamLineCheckTime = Time.time;
            if (!WaterPoints.Serves(oil.Car))
            {
                oil.SteamLine = false;
                Notify("Steam line off: the engine left the service track");
            }
        }

        /// <summary>
        /// Hooks a shed's steam line to the burner's valves where there is
        /// one, or takes it off. Returns the notice for whoever asked.
        /// </summary>
        internal static string ToggleSteamLine(RealisticOilFirebox oil)
        {
            if (oil.SteamLine)
            {
                oil.SteamLine = false;
                return "Steam line off";
            }
            if (!WaterPoints.Serves(oil.Car))
                return $"No steam line here: stand the engine on {WaterPoints.Where}";
            oil.SteamLine = true;
            return $"Steam line on: the shed's steam at {RealisticOilFirebox.SteamLinePressure - 1f:F1} bar for the atomizer and oil heater";
        }

        // The hand pump works while its key is held, on the loco being tended:
        // leaving it, or turning to another, lets go (audit 2026-10-10). The
        // hydrant hose toggles; the rig itself stops it once the glass is
        // full, the boiler has pressure, or the train has left the water point.
        private void UpdateFilling(InjectorRig rig, Settings settings)
        {
            var pumping = settings.HandPumpKey.Pressed() && TimeUtil.IsFlowing;
            if (Net.IsClient)
            {
                PumpRemotely(pumping ? rig.Car : null);
                if (settings.HydrantKey.Down())
                    Net.TrySend(rig.Car, FireAction.Hydrant);
                return;
            }
            LetGoOfPump(rig);
            if (pumping && !rig.HandPumping && !rig.CanHandPump)
                Notify($"The hand pump cannot force water in above {InjectorRig.HandPumpMaxPressure:F0} bar");
            rig.HandPumping = pumping;
            pumpedRig = pumping ? rig : null;

            if (settings.HydrantKey.Down())
                Notify(ToggleHydrant(rig));
            if (rig.TakeHoseStopped() is string reason)
                Notify("Hydrant hose off: " + reason);
        }

        private InjectorRig pumpedRig;
        private TrainCar remotePumpCar;

        // Lets go of the hand pump on any loco but this one.
        private void LetGoOfPump(InjectorRig keep)
        {
            if (Net.IsClient)
            {
                if (keep == null)
                    PumpRemotely(null);
                return;
            }
            if (pumpedRig != null && pumpedRig != keep)
            {
                pumpedRig.HandPumping = false;
                pumpedRig = null;
            }
        }

        // On a multiplayer client the host fills the boiler: the hand pump goes
        // to it as a request for the loco pumped, and stops there, wherever the
        // player has gone since.
        private void PumpRemotely(TrainCar car)
        {
            if (car == remotePumpCar)
                return;
            if (remotePumpCar != null)
                Net.TrySend(remotePumpCar, FireAction.HandPumpOff);
            if (car != null)
                Net.TrySend(car, FireAction.HandPumpOn);
            remotePumpCar = car;
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
                return $"No hydrant here: stand the engine on {WaterPoints.Where}";
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
                                        (InjectorRig.TryGetTended(PlayerManager.Car, out var oilRig) ? $"; {Describe(oilRig)}" : "") +
                                        (DescribeCylinders(oil.Car) is string oilCylinders ? $"; {oilCylinders}" : "") +
                                        (FiremanRig.TryGet(oil.Car, out var oilFireman) && oilFireman.Fireman.Mode != FiremanMode.Off
                                            ? $"; fireman {oilFireman.Fireman.Mode}: {string.Join("; ", FiremanPanel.Describe(oilFireman))}, trim {oilFireman.Fireman.Trim:F2}"
                                            : ""));
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
                (DescribeBooster(firebox.Car) is string booster ? $"; {booster}" : "") +
                (DescribeCylinders(firebox.Car) is string cylinders ? $"; {cylinders}" : "") +
                (FiremanRig.TryGet(firebox.Car, out var fireman) && fireman.Fireman.Mode != FiremanMode.Off
                    ? $"; fireman {fireman.Fireman.Mode}: {string.Join("; ", FiremanPanel.Describe(fireman))}, trim {fireman.Fireman.Trim:F2}"
                    : ""));
        }

        /// <summary>The stoker's engine, feed, jets, and losses, for the status log and overlay; null without one.</summary>
        internal static string DescribeStoker(RealisticFirebox firebox)
        {
            if (!firebox.LastStoker.HasValue)
                return null;
            var flow = firebox.LastStoker.Value;
            return $"stoker engine {(flow.Jammed ? "JAMMED (reverse it to clear) " : "")}{firebox.StokerEngineSpeed:P0} ({flow.EnginePressure:F1} bar), " +
                   $"jets {flow.ManifoldPressure:F1} bar (front {flow.LeftFrontPressure:F1}/{flow.RightFrontPressure:F1}, " +
                   $"back {flow.LeftBackPressure:F1}/{flow.RightBackPressure:F1}, fines {flow.FinesPressure:F1}), " +
                   $"cinders {flow.CinderRate:F2} kg/s, steam {flow.SteamUsed:F2} kg/s";
        }

        /// <summary>The booster, for the status log and overlay, or null without one.</summary>
        internal static string DescribeBooster(TrainCar car)
        {
            if (!RealisticBooster.TryGet(car, out var booster))
                return null;
            var flow = booster.Flow;
            return $"booster {flow.State} ({Visuals.BoosterCab.Describe(flow, flow.State != BoosterState.CutOut)}), " +
                   $"{flow.Force / 4.44822f:F0} lb, {flow.InletPressure:F1} bar, {flow.CrankRpm:F0} rpm, " +
                   $"steam {flow.SteamUsed:F2} kg/s, warm {booster.Model.Warmth:P0}" +
                   (booster.Clashes > 0 ? $", {booster.Clashes} clashes" : "");
        }

        /// <summary>The cylinders' indicator diagram this tick, for the status log and overlay, or null without the realistic cylinders.</summary>
        internal static string DescribeCylinders(TrainCar car)
        {
            if (!EngineCylinders.TryGet(car, out var engine, out var cylinders))
                return null;
            var cycle = cylinders.Cycle;
            var events = cycle.Events;
            return $"cylinders: cutoff {events.Cutoff:P0} (release {events.Release:P0}, compression {events.Compression:P0}), " +
                   $"chest {Gauge(engine.steamChestPressureReadOut.Value):F1} bar, admitted {Gauge(cycle.Admission):F1}, " +
                   $"release {Gauge(cycle.ReleasePressure):F1}, back {Gauge(cycle.Back):F1}, mean {cycle.MeanEffectivePressure:F1} bar, " +
                   $"steam {engine.exhaustFlowReadOut.Value:F2} kg/s";
        }

        // Gauge pressure, bar over the atmosphere, from absolute.
        private static float Gauge(float bar) => bar - 1f;

 internal static string OilLightingHint(RealisticOilFirebox oil)
        {
            var advice = FiringAdvice.ForOil(ReadOil(oil));
            return advice.LightingStep > 0 ? advice.Text : null;
        }

        /// <summary>An oil fire and its engine as the next step reads them, for the overlay and the fire-up notices.</summary>
        internal static OilReading ReadOil(RealisticOilFirebox oil)
        {
            var fire = oil.Model;
            InjectorRig.TryGetTended(oil.Car, out var rig);
            return new OilReading
            {
                FireOn = fire.FireOn,
                Vapor = fire.Vapor,
                PooledOil = fire.PooledOil,
                OnSteamLine = fire.OnSteamLine,
                SteamLineOpening = oil.SteamLineOpening,
                BoilerPressure = oil.BoilerPressure,
                OilTemperature = fire.OilTemperature,
                Torch = fire.Torch,
                BrickTemperature = fire.BrickTemperature,
                StackSmoke = fire.StackSmoke,
                FlueSoot = fire.FlueSoot,
                BlowerOn = Blower(oil.Car) > 0.05f,
                AtomizerOpen = oil.AtomizerValve > 0.01f,
                OnServiceTrack = oil.AtServiceTrack,
                ServiceTracks = WaterPoints.Where,
                SteamLineKey = Main.Settings.SteamLineKey.keyCode != KeyCode.None ? Main.Settings.SteamLineKey.ToString() : null,
                GlassLevel = rig != null ? Visuals.GaugeGlass.Read(rig) : 1f,
                Injectors = rig == null ? NoInjectors : rig.Circuits.Select(c => new InjectorReading(c.Flow.State, c.Feed.Value, c.SteamValve != null, c.TenderValveShut)).ToArray(),
            };
        }

        // The blower valve, 0 to 1, from the engine's exhaust.
        private static float Blower(TrainCar car)
        {
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            var exhaust = flow?.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            return exhaust != null ? exhaust.blowerControl.Value : 0f;
        }

        internal static string DescribeOil(RealisticOilFirebox oil)
        {
            var fire = oil.Model;
            var stack = !fire.FireOn ? (fire.Vapor > 0.05f ? "white vapor" : "clear")
                : fire.StackSmoke >= 0.6f ? "black" : fire.StackSmoke >= 0.3f ? "grey smoke"
                : fire.StackSmoke >= 0.08f ? "light haze" : "clear (too much air)";
            return $"Heat {fire.Heat / 1e6f:F2} MW ({fire.Heat / fire.Parameters.MaxHeatOutput:P0} of max), oil {fire.FeedRate:F2} kg/s " +
                   $"(firing valve {(oil.Feed != null ? oil.Feed.FiringValve : 0f) * 100f:0.#} %), burning {fire.BurnRate:F2} kg/s; " +
                   $"atomizer {oil.AtomizerValve * 100f:0.#} %: {fire.Atomizing:F1}× the oil's need" + (fire.Drumming > 0f ? $", drumming {fire.Drumming:P0}" : "") +
                   $", {fire.AtomizerSteam:F2} kg/s of steam; " +
                   $"draft {oil.AirFlowIn:F2} kg/s, {fire.ExcessAir:F1}× the air the oil needs, stack {stack}, soot {fire.SootDensity:P0}" +
                   (Visuals.ChimneyPlume.TryGet(oil.Car, out var plume) ? $", plume top {plume.PlumeTop:F0} m" : "") + "; " +
                   $"brick {fire.BrickTemperature:F0} C, firebox {fire.Temperature:F0} C, door {oil.DoorOpening:P0}; " +
                   $"oil {fire.OilTemperature:F0} C ({fire.OilTemperature * 9f / 5f + 32f:F0} F), heater {oil.HeaterValve:P0}" +
                   (oil.SteamLineOpening > 0f
                       ? (fire.OnSteamLine ? $", on the shed's steam line ({oil.SteamLineOpening:P0})" : ", shed's steam line open (the boiler's own steam is stronger)")
                       : oil.ShopSteamValve > 0f ? ", shop steam open but no line here (stand on a service track)" : "") + "; " +
                   $"flues {fire.FlueSoot:P0} sooted" + (fire.FlueSoot > 0.3f ? " (sand them)" : "") +
                   (fire.Vapor > 0.01f || fire.PooledOil > 0.01f ? $"; vapor {fire.Vapor:F2} kg, pooled oil {fire.PooledOil:F2} kg: clear it with the blower before lighting" : "") +
                   $"; flue sand on the shovel {Visuals.FlueSandBox.Loaded:F1} kg";
        }

        /// <summary>The injector and boiler filling, for the status log and overlay.</summary>
        internal static string Describe(InjectorRig rig)
        {
            var injector = string.Join("; ", rig.Circuits.Select(c =>
                (string.IsNullOrEmpty(c.Name) ? "" : c.Name + " ") + DescribeInjector(c, rig.BoilerPressure)));
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

        // Working spills this much or more, in kg/s, before it is worth saying.
        private const float WorkingSpill = 0.05f;

        private static string DescribeInjector(InjectorCircuit circuit, float pressure)
        {
            if (circuit.SteamValve == null)
                return DescribeLever(circuit, pressure);
            // Its own steam valve: say where both valves sit and what to do next.
            var flow = circuit.Flow;
            var valves = $"injector steam {InjectorValves.Position(circuit.SteamValve.Value)}, water {circuit.WaterValve.Value:P0}"
                         + (circuit.TenderValveShut ? ", tender valve shut" : "");
            switch (flow.State)
            {
                case InjectorState.Working:
                {
                    var working = $"{valves}: working, {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s";
                    if (circuit.SteamValve.Value < InjectorSpec.SteamFeedFrom)
                        return $"{working}: picked up, steam to Feed";
                    if (flow.Overflow < WorkingSpill)
                        return working;
                    var least = circuit.Injector.Spec.LeastShare(pressure);
                    return circuit.WaterValve.Value < least
                        ? $"{working}, spilling {flow.Overflow:F1} kg/s: open the water valve past {least:P0} to feed without waste"
                        : $"{working}, spilling {flow.Overflow:F1} kg/s: the boiler is full";
                }
                case InjectorState.Off:
                    return flow.Overflow > 0f ? $"{valves}: off, water running out of the overflow ({flow.Overflow:F1} kg/s): steam to Prime, or shut the water" : $"{valves}: off";
                case InjectorState.NoWater when circuit.TenderValveShut:
                    return $"{valves}: NO WATER, steam blowing at the overflow: open the tender water valve";
                case InjectorState.NoWater:
                    return circuit.WaterValve.Value <= InjectorSpec.OpenThreshold
                        ? $"{valves}: NO WATER, steam blowing at the overflow: open the water valve first"
                        : $"{valves}: NO WATER (tender dry)";
                default:
                    return $"{valves}: " + DescribeLever(circuit, pressure).Replace("injector ", "")
                        .Replace("hold it cracked to cool it", "hold the steam at Prime to cool it")
                        .Replace("shut it, then crack it until it primes", "steam Off, then Prime until it picks up");
            }
        }

        private static string DescribeLever(InjectorCircuit circuit, float pressure)
        {
            var flow = circuit.Flow;
            var spill = $"spilling {flow.Overflow:F1} kg/s water, {flow.OverflowSteam:F1} kg/s steam";
            var heat = $"{flow.InjectorTemperature:F0} C, takes below {flow.RestartLimit:F0} C";
            switch (flow.State)
            {
                case InjectorState.Working:
                {
                    var working = $"injector working, {flow.WaterIn:F1} kg/s at {flow.FeedTemperature:F0} C, steam {flow.SteamUsed:F2} kg/s";
                    if (flow.Overflow < WorkingSpill)
                        return working;
                    var least = circuit.Injector.Spec.LeastShare(pressure);
                    return circuit.Control.Value < least
                        ? $"{working}, {spill}: turned down too far, open it past {least:P0} to feed without waste"
                        : $"{working}, {spill}: the boiler is full";
                }
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
            if (FiremanRig.TryGetTended(PlayerManager.Car, out var fireman))
                firemanPanel.Draw(fireman);
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
