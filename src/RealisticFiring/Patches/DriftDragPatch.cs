using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Patches
{
 [HarmonyPatch]
    internal static class DriftDragPatch
    {
        private static readonly AccessTools.FieldRef<ReciprocatingSteamEngine, float> PistonArea =
            AccessTools.FieldRefAccess<ReciprocatingSteamEngine, float>("pistonArea");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ReciprocatingSteamEngine), "SimulateMeanTorque");
            yield return AccessTools.Method(typeof(ReciprocatingSteamEngine), "SimulateInstantaneousTorque");
        }

        private static void Postfix(ReciprocatingSteamEngine __instance, ref float __result)
        {
            if (!EngineCylinders.Works(__instance))
                return;
            __result += Torque(__instance);
        }

        /// <summary>The adapter check's drifting, standing in for the car it can't build; null in the game.</summary>
        internal static DriftState? Assumed;

        /// <summary>The drifting torque on <paramref name="engine"/>'s crank now, N·m, signed with its turning.</summary>
        internal static float Torque(ReciprocatingSteamEngine engine)
        {
            if (!EngineCylinders.TryGet(engine, out _))
                return 0f;
            var car = EngineDrift.CarOf(engine);
            var state = Assumed ?? (car != null ? EngineDrift.Read(car, engine) : DriftState.NotDrifting);
            if (!state.Drifting)
                return 0f;
            var turns = engine.crankRpm.Value / 60f;
            var mep = Drifting.Mep(state, EngineDrift.Cutoff(engine), Drifting.PistonSpeed(engine.pistonStroke, turns));
            var perBar = 100000f * PistonArea(engine) * (2f / Mathf.PI) * engine.pistonStroke / 2f * engine.numCylinders;
            return mep * perBar * Mathf.Sign(turns);
        }
    }
}
