using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class SafetyValveFit
    {
        // Lift, absolute bar (DV's boiler pressure; gauge psi / 14.5 + 1), and
        // capacity, kg/s: 1.25 times the steam at the lift of the most heat
        // the mod's fire gives standing, door shut, blower wide open, its box
        // kept 30 to 100 % full (the 2901: its burner at any setting), rounded
        // up to half a kg/s; on the engines whose boilers are sized to their
        // prototypes' (BoilerFit, 2026-10-20), of that fire, and on the
        // boiler's efficiency curve (FireboxParameters.EfficiencyAt). Lifts within the
        // ICC's 6 psi of the prototype's pressure are the maker's: the S282
        // 203 psi (the USRA light Mikado 200), the S060 210 (the S100's), the
        // Hudson 280 (the J-3a's 275 as built), the 2901 297 (300).
        private static readonly Dictionary<string, (float Lift, float Capacity)> Fits = new Dictionary<string, (float, float)>
        {
            ["LocoS282A"] = (15.0f, 6f),
            ["LocoS060"] = (15.5f, 3.5f),
            ["Jk3A"] = (15.0f, 2.5f),
            ["S464-730"] = (20.3f, 9.5f),
            ["FEF-3"] = (21.7f, 10.5f),
            ["S484-840A"] = (21.7f, 10.5f),
            ["A.T.&S.F. 2901-A"] = (21.5f, 8f),
            // The 8F's 225 psi, which its maker set as 15.5 bar, gauge.
            ["em_lms_8f_loc_black"] = (16.5f, 4f),
            ["em_lms_8f_loc_grey"] = (16.5f, 4f),
        };

        /// <summary>
        /// How far under its lift a valve reseats, bar: 3 psi, the blow-back
        /// the 1912 Master Mechanics' standard rated valves at (1915
        /// Proceedings pp.540-541), and a Ross pop's "about 3 lbs".
        /// </summary>
        public const float Blowdown = 0.2f;

        private static readonly FieldInfo Opening = AccessTools.Field(typeof(Boiler), "safetyValveOpeningPressure");
        private static readonly FieldInfo Closing = AccessTools.Field(typeof(Boiler), "safetyValveClosingPressure");
        private static readonly FieldInfo VentRate = AccessTools.Field(typeof(Boiler), "maxSafetyValveVentRate");

        private static readonly AccessTools.FieldRef<Boiler, AnimationCurve> ExplosionThreshold =
            AccessTools.FieldRefAccess<Boiler, AnimationCurve>("explosionPressureThreshold");
        private static readonly AccessTools.FieldRef<SimComponent, SimGameParams> GameParams =
            AccessTools.FieldRefAccess<SimComponent, SimGameParams>("gameParams");

        /// <summary>Fits a fired engine's safety valves. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (flow == null || livery == null || !Fits.TryGetValue(livery, out var fit))
                return null;
            var boiler = flow.OrderedSimComps.OfType<Boiler>().FirstOrDefault();
            if (boiler == null)
                return null;
            var lift = (float)Opening.GetValue(boiler);
            var reseat = (float)Closing.GetValue(boiler);
            var capacity = (float)VentRate.GetValue(boiler);
            Fit(boiler, fit.Lift, fit.Capacity);
            return $"safety valves: lift {fit.Lift:F1} bar ({Psi(fit.Lift):F0} psi), reseat {fit.Lift - Blowdown:F1}, {fit.Capacity:F1} kg/s " +
                   $"(the mod's {lift:F1} bar, {Psi(lift):F0} psi, reseat {reseat:F1}, {capacity:F1} kg/s)";
        }

        /// <summary>Sets <paramref name="boiler"/>'s valves to lift at <paramref name="lift"/> absolute bar and pass <paramref name="capacity"/> kg/s.</summary>
        internal static void Fit(Boiler boiler, float lift, float capacity)
        {
            Opening.SetValue(boiler, lift);
            Closing.SetValue(boiler, lift - Blowdown);
            VentRate.SetValue(boiler, capacity);
        }

        /// <summary>Where <paramref name="boiler"/>'s safety valve lifts, absolute bar.</summary>
        public static float Lift(Boiler boiler) => (float)Opening.GetValue(boiler);

        /// <summary>Gauge psi of an absolute pressure in bar.</summary>
        public static float Psi(float bar) => (bar - 1.01325f) * 14.5038f;

        /// <summary>
        /// The pressure at which the game bursts <paramref name="boiler"/> now,
        /// absolute bar: its lift times its curve at the body's health (three
        /// times the lift whole, the lift at 20 %). Infinite with drivetrain
        /// failures off, when it never bursts.
        /// </summary>
        public static float BurstPressure(Boiler boiler)
        {
            var failures = GameParams(boiler);
            var curve = ExplosionThreshold(boiler);
            if (failures != null && !failures.DrivetrainFailuresAllowed || curve == null)
                return float.PositiveInfinity;
            return Lift(boiler) * curve.Evaluate(boiler.bodyHealthStateExtIn.Value);
        }
    }
}
