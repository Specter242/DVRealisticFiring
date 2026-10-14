using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class BlowerFit
    {
        // Blower steam wide open at working pressure, kg/s, and the draft with
        // no steam, kg/s of air. Inferred from period tests and fittings:
        // a B&O Mikado's blower passed 0.31 kg/s at 85 psi (RME July 1925
        // p.460), the USRA light Mikado had a 1 1/4-in. blower fitting (RME
        // Aug 1918 p.438), a DR class 50's blower about 0.15 kg/s; natural
        // draft scaled by grate area from the S282's.
        private static readonly Dictionary<string, (float MaxBlower, float Natural)> Fits = new Dictionary<string, (float, float)>
        {
            ["LocoS282A"] = (0.5f, 0.6f),
            ["LocoS060"] = (0.4f, 0.3f),
            ["Jk3A"] = (0.2f, 0.15f),
            ["S464-730"] = (0.8f, 0.75f),
            ["FEF-3"] = (0.85f, 0.9f),
            ["S484-840A"] = (0.85f, 0.9f),
            ["A.T.&S.F. 2901-A"] = (0.85f, 1.0f),
            // LMS 8F: a 28.65 sq ft grate; its blower between the S060's and
            // the S282's (inferred), natural draft by grate area.
            ["em_lms_8f_loc_black"] = (0.4f, 0.26f),
            ["em_lms_8f_loc_grey"] = (0.4f, 0.26f),
        };

        private static readonly AccessTools.FieldRef<Boiler, float> SafetyValveOpeningPressure =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");

        private sealed class Fitted
        {
            public SteamDraft Draft;
        }

        private static readonly ConditionalWeakTable<SteamExhaust, Fitted> ByExhaust = new ConditionalWeakTable<SteamExhaust, Fitted>();

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
            Fit(exhaust, new SteamDraft(fit.MaxBlower, fit.Natural, exhaust.entrainmentRatio, working));
            return $"period blower: {fit.MaxBlower:F2} kg/s at {working:F1} bar (the mod's {exhaust.maxBlowerFlow:F2} at {exhaust.pressureForMaxBlowerFlow:F1}), " +
                   $"{SteamDraft.BlowerEntrainment:F0} kg of air a kg, natural draft {fit.Natural:F2} kg/s (the mod's {exhaust.passiveExhaust:F2})";
        }

        internal static void Fit(SteamExhaust exhaust, SteamDraft draft)
        {
            ByExhaust.Remove(exhaust);
            ByExhaust.Add(exhaust, new Fitted { Draft = draft });
        }

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
