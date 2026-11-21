using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using UnityEngine;

namespace RealisticFiring.Sim
{
 internal static class SafetyValveFit
    {
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

        /// <summary>A livery's fitted lift, absolute bar, and capacity, kg/s, if it has a fit.</summary>
        internal static bool TryGetFit(string livery, out (float Lift, float Capacity) fit)
        {
            fit = default;
            if (LocomotiveProfiles.TryGet(livery, out var profile) && profile.SafetyValve is SafetyValveSpec spec)
            {
                fit = (spec.Lift, spec.Capacity);
                return true;
            }
            return MakerFitRegistry.TryGetSafetyValveFit(livery, out fit);
        }

        /// <summary>Fits a fired engine's safety valves. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, SimulationFlow flow)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (flow == null || !TryGetFit(livery, out var fit))
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
        public static float Lift(Boiler boiler) => OpeningNow(boiler);

        // Read every fireman tick: unboxed, where FieldInfo.GetValue boxed it each time.
        private static readonly AccessTools.FieldRef<Boiler, float> OpeningNow =
            AccessTools.FieldRefAccess<Boiler, float>("safetyValveOpeningPressure");

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
