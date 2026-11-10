using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RealisticFiring.Patches;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// Failure containment (audit 2026-11-09): the game's private fields the
    /// mod binds are all there; a class that binds one that is gone is found
    /// before anything is patched and its patch left off; and a step that
    /// throws is ended by its guard, logged once, and counted.
    /// </summary>
    internal static class ContainmentChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var types = AccessTools.GetTypesFromAssembly(typeof(Main).Assembly);
            var unbound = GameMembers.Unbound(types);
            check(unbound.Count == 0, "every private game field the mod binds is in the installed game" +
                                      (unbound.Count > 0 ? $" (not: {string.Join("; ", unbound.Select(p => $"{p.Key.Name}: {p.Value}"))})" : ""));

            var probes = GameMembers.Unbound(new[] { typeof(UnboundProbe), typeof(BoundProbe), typeof(UnboundProbe.Inner) });
            check(probes.Count == 1 && probes.ContainsKey(typeof(UnboundProbe)),
                "a class that binds a field the game no longer has is found at load, and one whose fields are there is not");
            check(GameMembers.Within(typeof(UnboundProbe.Inner), probes.Keys) && !GameMembers.Within(typeof(BoundProbe), probes.Keys)
                  && GameMembers.Essential(typeof(Sim.InjectorRig)) && !GameMembers.Essential(typeof(LeverFeelPatch)) && !GameMembers.Essential(typeof(Visuals.FlueSandBox)),
                "a missing field under the simulation keeps the mod off whole; under a control's feel or a cab fitting, only that");

            // Unlisted steam locos stay stock unless the player asks (audit 2026-11-09).
            var listed = new[] { "LocoS282A", "LocoS060", "Jk3A", "S464-730", "S484-840A", "FEF-3", "em_lms_8f_loc_black", "S2102-870", "A.T.&S.F. 2901-A", "GWI40xx", "GWI_Manor" };
            check(listed.All(l => !Sim.LeftStock.Is(l, null, false)) && Sim.LeftStock.Is("SomeNewEngine", "SomeNewType", false)
                  && !Sim.LeftStock.Is("SomeNewEngine", "SomeNewType", true)
                  && Sim.LeftStock.Is("S484-841A", null, true) && Sim.LeftStock.Is("AnyPaint", "S040F", true),
                "a steam loco the mod does not list is left stock unless the player's setting asks; the listed ones are not, and the two left out stay out either way");

            // A coupling in the hand is told where it goes, from where the
            // connection sits on the engine (the S282's blower fitting is low
            // on the left of the smokebox; a water connection sits by the cab).
            check(Shed.ShedConnections.Describe(new UnityEngine.Vector3(-1.2f, 1.1f, 8f), 11f) == "low on the left side, toward the front"
                  && Shed.ShedConnections.Describe(new UnityEngine.Vector3(1.3f, 2f, -7f), 11f) == "on the right side, toward the rear"
                  && Shed.ShedConnections.Describe(new UnityEngine.Vector3(0f, 3f, 1f), 11f) == "high on the centerline, about the middle",
                "a hose's connection is described by its side, height, and end of the engine, for a player holding the coupling");

            const string id = "RealisticFiring.AdapterCheck.Containment";
            var harmony = new Harmony(id);
            try
            {
                var errors = new List<string>();
                var failed = Main.PatchEachClass(harmony, new[] { typeof(UnboundProbe.Inner), typeof(GoodProbePatch) }, errors.Add, probes.Keys);
                check(failed.Count == 1 && failed[0] == typeof(UnboundProbe.Inner).FullName && PatchProbe.Target() == 2,
                    "a patch inside a class with a missing game field is left off and named, and the next still applies");
                harmony.UnpatchAll(id);

                errors.Clear();
                var guarded = TickGuard.Guarded(types).ToList();
                var unguarded = TickGuard.Install(harmony, types, errors.Add);
                check(unguarded.Count == 0 && guarded.Count >= 25 && guarded.Any(g => g.Type == typeof(Sim.RealisticFirebox) && g.Method == "Tick")
                      && guarded.Any(g => g.Type == typeof(Sim.RealisticStoker) && g.Method == "SetSaveStateData"),
                    $"the guard goes on all {guarded.Count} sim steps and save hooks, stock and the mod's own" +
                    (unguarded.Count > 0 ? $" (not on: {string.Join(", ", unguarded)}; {string.Join(" | ", errors)})" : ""));
                harmony.UnpatchAll(id);

                Faults.Reset();
                TickGuard.Install(harmony, new[] { (typeof(ThrowingStep), nameof(ThrowingStep.Tick)), (typeof(ThrowingStep), nameof(ThrowingStep.GetSaveStateData)) }, errors.Add);
                var escaped = 0;
                object saved = "unset";
                for (var i = 0; i < 5; i++)
                {
                    try { ThrowingStep.Tick(0.02f); }
                    catch (Exception) { escaped++; }
                }
                try { saved = ThrowingStep.GetSaveStateData(); }
                catch (Exception) { escaped++; }
                check(escaped == 0 && ThrowingStep.Ran == 6 && Faults.CountAt("ThrowingStep.Tick") == 5 && Faults.CountAt("ThrowingStep.GetSaveStateData") == 1 && saved == null && Faults.Places == 2,
                    $"a step that throws every tick is ended by its guard and counted ({Faults.CountAt("ThrowingStep.Tick")} of 5), and a save hook that throws returns nothing");
            }
            finally
            {
                harmony.UnpatchAll(id);
                Faults.Reset();
            }
        }

        private static class BoundProbe
        {
            internal static readonly AccessTools.FieldRef<ThrowingState, int> There = AccessTools.FieldRefAccess<ThrowingState, int>("there");
        }

        private static class UnboundProbe
        {
            internal static readonly AccessTools.FieldRef<ThrowingState, int> Gone = AccessTools.FieldRefAccess<ThrowingState, int>("renamedInAnUpdate");

            [HarmonyPatch(typeof(PatchProbe), nameof(PatchProbe.Target))]
            internal static class Inner
            {
                private static void Postfix(ref int __result) => __result = 3;
            }
        }

        private sealed class ThrowingState
        {
#pragma warning disable CS0169, CS0414
            private int there = 1;
#pragma warning restore CS0169, CS0414
        }

        private static class ThrowingStep
        {
            internal static int Ran;

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static void Tick(float delta)
            {
                Ran++;
                throw new InvalidOperationException("a step that breaks");
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            internal static object GetSaveStateData()
            {
                Ran++;
                throw new InvalidOperationException("a save hook that breaks");
            }
        }
    }
}
