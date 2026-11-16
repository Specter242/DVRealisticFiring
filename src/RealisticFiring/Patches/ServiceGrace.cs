using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DV.Damage;
using DV.ServicePenalty;
using DV.ThingTypes;
using HarmonyLib;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
 internal static class ServiceGrace
    {
        /// <summary>How long a paid engine with its fire in stands unbilled, s.</summary>
        internal const float Seconds = 180f;

        /// <summary>The speed above which the engine is no longer standing, m/s (about 2 km/h).</summary>
        internal const float StandingSpeed = 0.5f;

        private sealed class Grace
        {
            public float Until;
        }

        private static readonly ConditionalWeakTable<TrainCar, Grace> Cars = new ConditionalWeakTable<TrainCar, Grace>();

        /// <summary>Whether a grace that runs until <paramref name="until"/> still holds at <paramref name="now"/> for a car moving at <paramref name="speed"/> m/s.</summary>
        internal static bool Holds(float now, float until, float speed) => now < until && Mathf.Abs(speed) <= StandingSpeed;

        /// <summary>Whether <paramref name="car"/>'s standing costs are held now; a car that has moved off loses its grace.</summary>
        internal static bool Holds(TrainCar car)
        {
            if (car == null || !Cars.TryGetValue(car, out var grace))
                return false;
            if (Holds(Time.time, grace.Until, car.GetForwardSpeed()))
                return true;
            Cars.Remove(car);
            return false;
        }

        /// <summary>
        /// Starts the grace on <paramref name="car"/>, just paid, if it or a
        /// car its payment puts in neutral (<see cref="Paid"/>: an engine and
        /// its tender) has a realistic fire burning. Only the paid car is
        /// held: each car's fees are paid on their own, and a tender's coal
        /// drawn before it was paid is still owed (job 2415, 2026-11-15: held
        /// with its engine, an unpaid tender's $554 of coal was wiped).
        /// </summary>
        internal static bool Start(TrainCar car)
        {
            if (car == null || Net.IsClient)
                return false;
            var lit = Paid(car).Any(c => FireboxRegistry.TryGet(c, out var coal) && coal.Model.FireOn
                                         || RealisticOilFirebox.TryGet(c, out var oil) && oil.Model.FireOn);
            if (!lit)
                return false;
            Cars.Remove(car);
            Cars.Add(car, new Grace { Until = Time.time + Seconds });
            return true;
        }

        /// <summary>
        /// The cars a payment for <paramref name="car"/> puts in neutral,
        /// found as the game's <c>BaseControlsOverrider.SetNeutralState</c>
        /// passes it on: across a coupling only where both cars pass it that
        /// way (an engine and its tender), never to the rest of the train.
        /// </summary>
        internal static List<TrainCar> Paid(TrainCar car)
        {
            var reached = new List<TrainCar> { car };
            for (var i = 0; i < reached.Count; i++)
            {
                var overrider = reached[i].SimController != null ? reached[i].SimController.controlsOverrider : null;
                if (overrider == null)
                    continue;
                if (overrider.propagateNeutralStateToFront)
                    Pass(reached[i].frontCoupler, reached);
                if (overrider.propagateNeutralStateToRear)
                    Pass(reached[i].rearCoupler, reached);
            }
            return reached;
        }

        private static void Pass(Coupler from, List<TrainCar> reached)
        {
            var to = from != null ? from.coupledTo : null;
            var next = to != null ? to.train : null;
            if (next == null || reached.Contains(next))
                return;
            var overrider = next.SimController != null ? next.SimController.controlsOverrider : null;
            if (overrider != null && (to.isFrontCoupler ? overrider.propagateNeutralStateToFront : overrider.propagateNeutralStateToRear))
                reached.Add(next);
        }

        /// <summary>The tender resources a held car's debt starts again from: coal, water, and an oil burner's fuel.</summary>
        internal static bool Held(ResourceType type) => type == ResourceType.Coal || type == ResourceType.Water || type == ResourceType.Fuel;
    }

    /// <summary>
    /// Paying a loco's fees (<c>SimulatedCarDebtTracker.ResetState</c>) starts
    /// the grace when the fire was kept in (<see cref="ServiceGrace"/>).
    /// </summary>
    [HarmonyPatch(typeof(SimulatedCarDebtTracker), nameof(SimulatedCarDebtTracker.ResetState))]
    internal static class ServiceGracePatch
    {
        private static readonly AccessTools.FieldRef<SimulatedCarDebtTracker, DamageController> Damage =
            AccessTools.FieldRefAccess<SimulatedCarDebtTracker, DamageController>("dmgController");

        private static void Postfix(SimulatedCarDebtTracker __instance)
        {
            try
            {
                var car = Damage(__instance) != null ? Damage(__instance).GetComponent<TrainCar>() : null;
                if (ServiceGrace.Start(car))
                    Main.Mod.Logger.Log($"{car.LogName()}: paid with its fire kept in; standing costs held for {ServiceGrace.Seconds:F0} s while it stands");
            }
            catch (System.Exception e)
            {
                Faults.Report("holding a paid engine's standing costs", null, e);
            }
        }
    }

    /// <summary>
    /// While its grace holds, a car's tender coal and water drawn since the
    /// last look are not owed (<see cref="ServiceGrace"/>): their debt starts
    /// again from what is in the tender now, which also takes in the
    /// payment's own refill, made a tick after the debt was reset.
    /// </summary>
    [HarmonyPatch(typeof(SimulatedCarDebtTracker), nameof(SimulatedCarDebtTracker.UpdateDebtValues))]
    internal static class ServiceGraceDebtPatch
    {
        private static readonly AccessTools.FieldRef<SimulatedCarDebtTracker, DamageController> Damage =
            AccessTools.FieldRefAccess<SimulatedCarDebtTracker, DamageController>("dmgController");

        private static void Postfix(SimulatedCarDebtTracker __instance)
        {
            try
            {
                var car = Damage(__instance) != null ? Damage(__instance).GetComponent<TrainCar>() : null;
                if (!ServiceGrace.Holds(car))
                    return;
                foreach (var debt in __instance.GetDebtData().GetTrackedDebts())
                    if (ServiceGrace.Held(debt.Type) && debt.EndValue != debt.StartValue)
                        debt.UpdateStartValueToEndValue();
            }
            catch (System.Exception e)
            {
                Faults.Report("holding a paid engine's tender use", null, e);
            }
        }
    }
}
