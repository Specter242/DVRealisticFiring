using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RealisticFiring.Core;
using RealisticFiring.Items;
using RealisticFiring.Patches;
using RealisticFiring.Sim;

namespace RealisticFiring.AdapterCheck
{
    /// <summary>
    /// The starting supplies' refill, their texts, and the wood and rag aim,
    /// wired to the Core rules that decide them (<see cref="SupplyPlan.Refills"/>,
    /// <see cref="DoorAimRules"/>). The game's hover and the sites' stocking
    /// need Unity's native side, so each caller is read for its call.
    /// </summary>
    internal static class StartingSupplyChecks
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        internal static void Run(Action<bool, string> check)
        {
            foreach (var kind in new[] { StartingFuelKind.WoodCrate, StartingFuelKind.RagBox })
            {
                var empty = FireStartingItems.EmptyNotice(kind);
                check(empty.EndsWith(FireStartingItems.RefillHint, StringComparison.Ordinal) && !empty.Contains("service-track"),
                    $"an empty {FireStartingItems.Name(kind).ToLowerInvariant()} says to set it down by any supply place: \"{empty}\"");
            }
            var desktop = FireStartingItems.ContainerDescription(7, vr: false);
            var vr = FireStartingItems.ContainerDescription(7, vr: true);
            check(desktop.Contains("7 left") && desktop.Contains("Reach in") && desktop.EndsWith(FireStartingItems.RefillHint, StringComparison.Ordinal),
                $"a crate's description off VR says to reach in, and how it refills: \"{desktop}\"");
            check(vr.Contains("7 left") && vr.Contains("pull the trigger") && !vr.Contains("Reach in") && vr.EndsWith(FireStartingItems.RefillHint, StringComparison.Ordinal),
                $"a crate's description in VR says to hold it and pull the trigger, not to reach in: \"{vr}\"");
            check(Calls(typeof(StartingFuelItem), "GetCustomDescription", typeof(VRManager), nameof(VRManager.IsVREnabled))
                  && Calls(typeof(StartingFuelItem), "GetCustomDescription", typeof(FireStartingItems), nameof(FireStartingItems.ContainerDescription)),
                "a crate's description asks the game whether it runs in VR");
            check(Calls(typeof(StartingFuelItem), nameof(StartingFuelItem.TakeOut), typeof(FireStartingItems), nameof(FireStartingItems.EmptyNotice)),
                "an empty crate's notice is the corrected one");

            var site = typeof(StartingSupplySite);
            check(Calls(site, "Supply", site, "SetDownHere") && IteratorCalls(site, "SetDownHere", typeof(SupplyPlan), nameof(SupplyPlan.Refills)),
                "a supply place fills any crate or tin set down by it, whichever place it came from (SupplyPlan.Refills)");
            check(Calls(site, "Update", site, "WantsRefill") && Calls(site, "WantsRefill", site, "SetDownHere"),
                "a guest's game asks the host to fill a crate or tin set down by a supply place");

            check(Calls(typeof(StartingFuelPlace), "HandleHover", typeof(FuelFireboxIntake), nameof(FuelFireboxIntake.TryAim))
                  && Calls(typeof(StartingFuelPlace), "HandleHover", typeof(RealisticFirebox), nameof(RealisticFirebox.NoteAim)),
                "wood or a rag hovered at the door records its aim, for the keypad, the label, and the bed");
            check(Calls(typeof(FuelFireboxIntake), nameof(FuelFireboxIntake.AimedZone), typeof(DoorAimRules), nameof(DoorAimRules.AimsLay)),
                "a lay goes where its own hover aim showed");
            check(Calls(typeof(RealisticFirebox), "TakeFreshAim", typeof(DoorAimRules), nameof(DoorAimRules.AimsCoal)),
                "only a shovel's aim steers coal: a lay's never steers a keyboard shovelful or a stoker's coal");
            check(Calls(typeof(ShovelRakePatch), "Prefix", typeof(DoorAimRules), nameof(DoorAimRules.AimsRake)),
                "only a rake's aim steers the empty shovel's rake");
            check(Calls(typeof(FireController), "DrawGui", typeof(DoorAimRules), nameof(DoorAimRules.Label)),
                "the aim label names what is aimed (Shovel, Rake, Wood, Rag)");
            var laid = new DoorAimSample(GrateZone.Front, 0.5f, 0.9f, DoorAimKind.LayWood, 10f);
            check(laid.Kind == DoorAimKind.LayWood && Math.Abs(laid.AgeAt(11f) - 1f) < 1e-6f
                  && !DoorAimRules.AimsCoal(laid.Kind, laid.AgeAt(11f), RealisticFirebox.AimHoldTime)
                  && DoorAimRules.AimsLay(laid.Kind, DoorAimKind.LayWood, laid.AgeAt(11f), RealisticFirebox.AimHoldTime),
                "a wood aim a second old lays the wood and steers no coal");
        }

        private static bool Calls(Type type, string method, Type calleeType, string callee) =>
            type.GetMethods(Declared).Where(m => m.Name == method).Any(m => CallsIn(m, calleeType, callee));

        // An iterator's body runs in its compiler-made state machine's MoveNext.
        private static bool IteratorCalls(Type type, string method, Type calleeType, string callee) =>
            type.GetNestedTypes(BindingFlags.NonPublic)
                .Where(t => t.Name.StartsWith("<" + method + ">", StringComparison.Ordinal))
                .Select(t => t.GetMethod("MoveNext", Declared))
                .Any(m => m != null && CallsIn(m, calleeType, callee));

        private static bool CallsIn(MethodBase method, Type calleeType, string callee) =>
            PatchProcessor.GetOriginalInstructions(method).Any(i => i.operand is MethodBase b && b.DeclaringType == calleeType && b.Name == callee);
    }
}
