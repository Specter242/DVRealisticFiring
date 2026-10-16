using System.Collections.Generic;
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
        private static readonly Dictionary<string, (float MaxBlower, float Natural, AirPump Pump, int Pumps, float MainReservoir)> Fits =
            new Dictionary<string, (float, float, AirPump, int, float)>
        {
            ["LocoS282A"] = (0.5f, 0.6f, AirPump.NineAndHalf, 1, 0f),
            ["LocoS060"] = (0.4f, 0.3f, AirPump.NineAndHalf, 1, 0f),
            ["Jk3A"] = (0.2f, 0.15f, AirPump.NineAndHalf, 1, 0f),
            ["S464-730"] = (0.8f, 0.75f, AirPump.CrossCompound, 2, 0f),
            ["FEF-3"] = (0.85f, 0.9f, AirPump.CrossCompound, 2, FefMainReservoir),
            ["S484-840A"] = (0.85f, 0.9f, AirPump.CrossCompound, 2, FefMainReservoir),
            ["A.T.&S.F. 2901-A"] = (0.85f, 1.0f, AirPump.CrossCompound, 2, 0f),
            // LMS 8F: a 28.65 sq ft grate; its blower between the S060's and
            // the S282's (inferred), natural draft by grate area. Its pump
            // stands in for the vacuum ejector, whose exhaust also goes up
            // the chimney.
            ["em_lms_8f_loc_black"] = (0.4f, 0.26f, AirPump.NineAndHalf, 1, 0f),
            ["em_lms_8f_loc_grey"] = (0.4f, 0.26f, AirPump.NineAndHalf, 1, 0f),
        };

        // The FEF-1's 91,000 cu in, L.
        private const float FefMainReservoir = 1491f;

        private static readonly AccessTools.FieldRef<Boiler, float> SafetyValveOpeningPressure =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");

        private sealed class Fitted
        {
            public SteamDraft Draft;
            public SteamCompressor Pump;
        }

        private sealed class FittedPump
        {
            public float MaxSteam;
        }

        private static readonly ConditionalWeakTable<SteamExhaust, Fitted> ByExhaust = new ConditionalWeakTable<SteamExhaust, Fitted>();
        private static readonly ConditionalWeakTable<SteamCompressor, FittedPump> ByPump = new ConditionalWeakTable<SteamCompressor, FittedPump>();

        /// <summary>Fits the period blower to a fired engine's exhaust. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (flow == null || livery == null || !Fits.TryGetValue(livery, out var fit))
                return null;
            var exhaust = flow.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault();
            var boiler = flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault();
            if (exhaust == null || boiler == null)
                return null;
            var working = SafetyValveOpeningPressure(boiler);
            var pump = flow.OrderedSimComps.OfType<SteamCompressor>().FirstOrDefault();
            var maxPump = fit.Pump.Steam * fit.Pumps;
            Fit(exhaust, new SteamDraft(fit.MaxBlower, fit.Natural, exhaust.entrainmentRatio, working), pump, maxPump);
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
        internal static void Fit(SteamExhaust exhaust, SteamDraft draft, SteamCompressor pump = null, float maxPump = 0f)
        {
            ByExhaust.Remove(exhaust);
            ByExhaust.Add(exhaust, new Fitted { Draft = draft, Pump = pump });
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

        public static bool TryGet(SteamExhaust exhaust, out SteamDraft draft)
        {
            draft = default(SteamDraft);
            if (exhaust == null || !ByExhaust.TryGetValue(exhaust, out var fitted))
                return false;
            draft = fitted.Draft;
            return true;
        }

        /// <summary>The blower's steam now, kg/s, fitted or as the game works it out.</summary>
        public static float BlowerSteam(SteamExhaust exhaust)
        {
            if (exhaust == null)
                return 0f;
            if (TryGet(exhaust, out var draft))
                return draft.BlowerSteam(exhaust.boilerPressure.Value, exhaust.blowerControl.Value);
            return exhaust.maxBlowerFlow * Mathf.Min(Mathf.InverseLerp(1f, exhaust.pressureForMaxBlowerFlow, exhaust.boilerPressure.Value),
                exhaust.blowerControl.Value);
        }

        /// <summary>The draft with no steam, kg/s of air, fitted or the game's.</summary>
        public static float Natural(SteamExhaust exhaust) =>
            TryGet(exhaust, out var draft) ? draft.Natural : exhaust != null ? exhaust.passiveExhaust : 0f;
    }
}
