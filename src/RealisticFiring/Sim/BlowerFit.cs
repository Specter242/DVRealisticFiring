using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class BlowerFit
    {
        private static readonly AccessTools.FieldRef<Boiler, float> SafetyValveOpeningPressure =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");

        private sealed class Fitted
        {
            public SteamDraft Draft;
            public SteamCompressor Pump;
            public RealisticFirebox Firebox;
        }

        private sealed class FittedPump
        {
            public float MaxSteam;
        }

        private static readonly ConditionalWeakTable<SteamExhaust, Fitted> ByExhaust = new ConditionalWeakTable<SteamExhaust, Fitted>();
        private static readonly ConditionalWeakTable<SteamCompressor, FittedPump> ByPump = new ConditionalWeakTable<SteamCompressor, FittedPump>();

        private static bool TryGetFit(string livery, out BlowerSpec fit)
        {
            if (LocomotiveProfiles.TryGet(livery, out var profile) && profile.Blower is BlowerSpec builtIn)
            {
                fit = builtIn;
                return true;
            }
            if (MakerFitRegistry.TryGetBlowerFit(livery, out var maker))
            {
                fit = new BlowerSpec(maker.MaxBlower, maker.Natural, maker.Pump, maker.Pumps, maker.MainReservoir);
                return true;
            }
            fit = default;
            return false;
        }

        /// <summary>A livery's fitted blower, kg/s of steam wide open at working pressure, and natural draft, kg/s of air, if it has a fit.</summary>
        internal static bool TryGetDraft(string livery, out (float MaxBlower, float Natural) draft)
        {
            draft = default;
            if (!TryGetFit(livery, out var fit))
                return false;
            draft = (fit.MaxBlower, fit.Natural);
            return true;
        }

        /// <summary>Fits the period blower to a fired engine's exhaust. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (flow == null || !TryGetFit(livery, out var fit))
                return null;
            var exhaust = flow.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var boiler = flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault();
            if (exhaust == null || boiler == null)
                return null;
            var working = SafetyValveOpeningPressure(boiler);
            var pump = flow.OrderedSimComps.OfType<SteamCompressor>().FirstOrDefault();
            var maxPump = fit.Pump.Steam * fit.Pumps;
            Fit(exhaust, new SteamDraft(fit.MaxBlower, fit.Natural, exhaust.entrainmentRatio, working), pump, maxPump,
                flow.OrderedSimComps.OfType<RealisticFirebox>().FirstOrDefault());
            var note = $"period blower: {fit.MaxBlower:F2} kg/s at {working:F1} bar (the mod's {exhaust.maxBlowerFlow:F2} at {exhaust.pressureForMaxBlowerFlow:F1}), " +
                       $"{SteamDraft.BlowerEntrainment:F0} kg of air a kg, natural draft {fit.Natural:F2} kg/s (the mod's {exhaust.passiveExhaust:F2}); ";
            if (pump == null)
                return note + "no steam air pump";
            var modAir = pump.maxProductionRate;
            var modFullAt = pump.pressureForMaxProduction;
            var modSteam = pump.maxSteamConsumption;
            var modReservoir = pump.mainResVolumeReadOut.Value;
            var reservoir = fit.MainReservoir > 0f ? fit.MainReservoir : modReservoir;
            FitAirPump(pump, fit.Pump, fit.Pumps, reservoir);
            // The brake system took the reservoir's size as the sim started, before this fit.
            if (car.brakeSystem != null)
                car.brakeSystem.mainResVolume = reservoir;
            return note + $"{fit.Pumps} {fit.Pump.Name} air pump{(fit.Pumps > 1 ? "s" : "")}: {fit.Pump.Air * fit.Pumps:F0} L/s of free air " +
                   $"from {fit.Pump.FullSpeedPressure:F1} bar (the mod's {modAir:F0} from {modFullAt:F1}), {maxPump:F2} kg/s of steam flat out " +
                   $"(the mod's {modSteam:F2}), into the blast pipe; main reservoir {reservoir:F0} L (the mod's {modReservoir:F0})";
        }

        // The compressor's own figures, read-only fields the stock tick reads.
        private static readonly FieldInfo MaxProductionRate = AccessTools.Field(typeof(SteamCompressor), nameof(SteamCompressor.maxProductionRate));
        private static readonly FieldInfo PressureForMaxProduction = AccessTools.Field(typeof(SteamCompressor), nameof(SteamCompressor.pressureForMaxProduction));

        /// <summary>
        /// Fits <paramref name="count"/> period pumps' free air to the game's
        /// <paramref name="pump"/>, and its main reservoir, L. The game
        /// multiplies the air by its main reservoir fill time setting (by 1 at
        /// 1000 %, the period rate), as it does every pump's.
        /// </summary>
        internal static void FitAirPump(SteamCompressor pump, AirPump period, int count, float mainReservoir)
        {
            MaxProductionRate.SetValue(pump, period.Air * count);
            PressureForMaxProduction.SetValue(pump, period.FullSpeedPressure);
            pump.mainResVolumeReadOut.Value = mainReservoir;
        }

        /// <summary>Fits a draft to <paramref name="exhaust"/> and, with <paramref name="pump"/>, the pump's steam and its exhaust into the blast pipe.</summary>
        internal static void Fit(SteamExhaust exhaust, SteamDraft draft, SteamCompressor pump = null, float maxPump = 0f, RealisticFirebox firebox = null)
        {
            ByExhaust.Remove(exhaust);
            ByExhaust.Add(exhaust, new Fitted { Draft = draft, Pump = pump, Firebox = firebox });
            if (pump == null)
                return;
            ByPump.Remove(pump);
            ByPump.Add(pump, new FittedPump { MaxSteam = maxPump });
        }

        /// <summary>A fitted air pump's steam flat out, kg/s.</summary>
        public static bool TryGetPump(SteamCompressor pump, out float maxSteam)
        {
            maxSteam = 0f;
            if (pump == null || !ByPump.TryGetValue(pump, out var fitted))
                return false;
            maxSteam = fitted.MaxSteam;
            return true;
        }

        /// <summary>The steam a fitted exhaust's air pump sends into its blast pipe now, kg/s.</summary>
        public static float PumpSteam(SteamExhaust exhaust) =>
            exhaust != null && ByExhaust.TryGetValue(exhaust, out var fitted) && fitted.Pump != null
                ? Mathf.Max(0f, fitted.Pump.steamConsumptionReadOut.Value)
                : 0f;

        /// <summary>
        /// The steam the engine's over-fire jets take from the boiler now, kg/s:
        /// from the turret like the blower's, so drawn with it.
        /// </summary>
        public static float JetSteam(SteamExhaust exhaust) =>
            exhaust != null && ByExhaust.TryGetValue(exhaust, out var fitted) && fitted.Firebox != null
                ? Mathf.Max(0f, fitted.Firebox.Model.JetSteamFlow)
                : 0f;

        /// <summary>
        /// The steam the engine's drifting valve takes from the boiler now,
        /// kg/s: from the turret like the blower's, so drawn with it
        /// (<see cref="EngineDrift.SteamUsed"/>).
        /// </summary>
        public static float DriftingSteam(SteamExhaust exhaust) =>
            exhaust != null && ByExhaust.TryGetValue(exhaust, out var fitted) && fitted.Firebox != null
                ? EngineDrift.SteamUsed(fitted.Firebox.Car)
                : 0f;

        public static bool TryGet(SteamExhaust exhaust, out SteamDraft draft)
        {
            draft = default(SteamDraft);
            if (exhaust == null || !ByExhaust.TryGetValue(exhaust, out var fitted))
                return false;
            draft = fitted.Draft;
            return true;
        }

 public static float NozzleSteam(SteamExhaust exhaust, SteamDraft draft, out float fromBoiler)
        {
            var cab = draft.BlowerSteam(exhaust.boilerPressure.Value, exhaust.blowerControl.Value);
            var line = Shed.ShedConnections.BlowerLine(exhaust);
            var house = line > 0f ? draft.BlowerSteam(Shed.ShedConnections.HousePressure, line) : 0f;
            fromBoiler = house > cab ? 0f : cab;
            return Mathf.Max(cab, house);
        }

        /// <summary>The blower's steam now, kg/s, fitted or as the game works it out.</summary>
        public static float BlowerSteam(SteamExhaust exhaust)
        {
            if (exhaust == null)
                return 0f;
            if (TryGet(exhaust, out var draft))
                return NozzleSteam(exhaust, draft, out _);
            return exhaust.maxBlowerFlow * Mathf.Min(Mathf.InverseLerp(1f, exhaust.pressureForMaxBlowerFlow, exhaust.boilerPressure.Value),
                exhaust.blowerControl.Value);
        }

        /// <summary>The draft with no steam, kg/s of air, fitted or the game's.</summary>
        public static float Natural(SteamExhaust exhaust) =>
            TryGet(exhaust, out var draft) ? draft.Natural : exhaust != null ? exhaust.passiveExhaust : 0f;
    }
}
