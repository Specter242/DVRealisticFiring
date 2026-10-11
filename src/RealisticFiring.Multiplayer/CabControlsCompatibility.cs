using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DV.CabControls;
using DV.HUD;
using DV.Simulation.Brake;
using DV.Simulation.Controllers;
using DV.Simulation.Ports;
using HarmonyLib;
using LocoSim.Definitions;
using UnityEngine;
using ControlReference = DV.HUD.InteriorControlsManager.ControlReference;
using ControlType = DV.HUD.InteriorControlsManager.ControlType;

namespace RealisticFiring.Multiplayer
{
    /// <summary>
    /// Multiplayer 0.1.16 assumes each cab control has a base-control
    /// wrapper and appears once in the HUD dictionary. Port feeders can
    /// have neither (JK3 has two injector handles of the same HUD kind).
    /// Adapt discovery while leaving Multiplayer's authority, event hooks,
    /// network IDs, scrolling, and cleanup in charge.
    /// </summary>
    internal static class CabControlsCompatibility
    {
        private static readonly FieldInfo Controls = AccessTools.Field(typeof(InteriorControlsManager), "controls");
        private static readonly FieldInfo BaseControl = AccessTools.Field(typeof(ControlReference), "overridableBaseControl");
        private static readonly FieldInfo PortId = AccessTools.Field(typeof(OverridableBaseControl), "portId");
        private static readonly MethodInfo ControlKind = AccessTools.PropertyGetter(typeof(OverridableBaseControl), "ControlType");
        private static readonly MethodInfo Enumerate = AccessTools.DeclaredMethod(typeof(Dictionary<ControlType, ControlReference>), "GetEnumerator");

        internal static void Install(string harmonyId, Action<string> log, Action<string> warning)
        {
            var type = AccessTools.TypeByName("Multiplayer.Components.Networking.Train.NetworkedTrainCar");
            if (type == null || type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] != "0.1.16.0")
            {
                warning("Cab control compatibility fixes require Multiplayer 0.1.16; this version keeps its original cab hookup");
                return;
            }
            var harmony = new Harmony(harmonyId);
            try
            {
                // Check the whole group before changing any methods. An
                // altered third-party build must keep its original behavior.
                var targets = PatchTargets(type);
                if (targets.Length != 4)
                    throw new InvalidOperationException($"Expected four cab discovery/logging methods, found {targets.Length}");
                var instructions = targets.SelectMany(method => PatchProcessor.GetOriginalInstructions(method)).ToList();
                if (Enumerable.Range(0, instructions.Count).Count(i => DiscoveryRead(instructions, i)) != 1
                    || Enumerable.Range(0, instructions.Count).Count(i => PortRead(instructions, i)) != 2
                    || Enumerable.Range(0, instructions.Count).Count(i => KindRead(instructions, i)) != 2)
                    throw new InvalidOperationException("Cab discovery instructions differ from Multiplayer 0.1.16");
                var transpiler = new HarmonyMethod(typeof(CabControlsCompatibility), nameof(Transpile));
                foreach (var method in targets)
                    harmony.Patch(method, transpiler: transpiler);
                harmony.Patch(AccessTools.DeclaredMethod(type, "OnTrainCarInteriorLoaded"),
                    prefix: new HarmonyMethod(typeof(CabControlsCompatibility), nameof(InteriorLoaded)));
                log("Multiplayer cab fixes on: port-feeder controls, paired handles, and interior unload notifications");
            }
            catch (Exception e)
            {
                harmony.UnpatchAll(harmonyId);
                warning("Could not apply Multiplayer cab fixes; original hookup retained: " + e.Message);
            }
        }

        // TrainCar.InteriorLoaded(null) means unloaded, not a new load.
        // Non-null notifications still use Multiplayer's load/error path.
        internal static bool InteriorLoaded(GameObject interior) => interior != null;

        internal static string ResolvePort(ref ControlReference reference)
        {
            var feeder = reference.controlImplBase != null
                ? reference.controlImplBase.GetComponent<InteractablePortFeeder>() : null;
            return feeder != null && !string.IsNullOrEmpty(feeder.portId)
                ? feeder.portId : reference.overridableBaseControl?.portId;
        }

        // Debug logging must also tolerate controls without the wrapper.
        private static ControlType Kind(ref ControlReference reference) =>
            reference.overridableBaseControl != null ? reference.overridableBaseControl.ControlType : ControlType.None;

        internal static Dictionary<ControlType, ControlReference> Discover(InteriorControlsManager manager)
        {
            var controls = new Dictionary<ControlType, ControlReference>();
            var seen = new HashSet<ControlImplBase>();
            foreach (var entry in (Dictionary<ControlType, ControlReference>)Controls.GetValue(manager))
            {
                var reference = entry.Value;
                // This button acts directly on the brake system, which MP
                // already syncs through BrakeCylinderReleased. It has no port.
                if (reference.controlImplBase != null
                    && reference.controlImplBase.GetComponent<BrakeCylinderReleaseButtonFeeder>() != null
                    && string.IsNullOrEmpty(ResolvePort(ref reference)))
                    continue;
                if (reference.controlImplBase == null || seen.Add(reference.controlImplBase))
                    controls.Add(entry.Key, reference);
            }
            var flow = manager.Car?.SimController?.simFlow;
            if (flow == null)
                return controls;

            AddFeeders(manager.gameObject);
            if (manager.Car.loadedExternalInteractables != null)
                AddFeeders(manager.Car.loadedExternalInteractables);
            return controls;

            void AddFeeders(GameObject root)
            {
                foreach (var feeder in root.GetComponentsInChildren<InteractablePortFeeder>(true))
                {
                    var control = feeder.GetComponent<ControlImplBase>();
                    if (control == null || seen.Contains(control) || string.IsNullOrEmpty(feeder.portId)
                        || !flow.TryGetPort(feeder.portId, out var port) || port.type != PortType.EXTERNAL_IN)
                        continue;
                    seen.Add(control);
                    // These keys are only diagnostic labels in MP's loop.
                    // Keep extra handles in this private snapshot, never in
                    // the game's HUD map, which allows one handle per kind.
                    var key = (ControlType)(-1);
                    while (controls.ContainsKey(key))
                        key = (ControlType)((int)key - 1);
                    controls.Add(key, new ControlReference
                    {
                        controlImplBase = control,
                        scrollable = control.GetComponent<IScrollable>(),
                    });
                }
            }
        }

        internal static MethodBase[] PatchTargets(Type trainCar)
        {
            return NestedTypes(trainCar).SelectMany(type => type.GetMethods(
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Where(method => method.GetMethodBody() != null)
                .Where(method => HasReferenceRead(PatchProcessor.GetOriginalInstructions(method)))
                .ToArray();
        }

        private static IEnumerable<Type> NestedTypes(Type type)
        {
            yield return type;
            foreach (var child in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
                foreach (var descendant in NestedTypes(child))
                    yield return descendant;
        }

        private static bool IsControlsField(CodeInstruction instruction) =>
            instruction.opcode == OpCodes.Ldfld && Equals(instruction.operand, Controls);

        private static bool DiscoveryRead(IList<CodeInstruction> instructions, int i) =>
            i + 1 < instructions.Count && IsControlsField(instructions[i]) && instructions[i + 1].Calls(Enumerate);

        private static bool HasReferenceRead(IList<CodeInstruction> instructions) =>
            Enumerable.Range(0, instructions.Count).Any(i => DiscoveryRead(instructions, i) || PortRead(instructions, i) || KindRead(instructions, i));

        private static bool ReferenceRead(IList<CodeInstruction> instructions, int i) =>
            i > 0 && instructions[i - 1].opcode == OpCodes.Ldflda
            && instructions[i - 1].operand is FieldInfo field && field.FieldType == typeof(ControlReference)
            && instructions[i].opcode == OpCodes.Ldfld && Equals(instructions[i].operand, BaseControl);

        private static bool KindRead(IList<CodeInstruction> instructions, int i) =>
            i + 1 < instructions.Count && ReferenceRead(instructions, i)
            && instructions[i + 1].Calls(ControlKind);

        private static bool PortRead(IList<CodeInstruction> instructions, int i) =>
            i + 6 < instructions.Count && ReferenceRead(instructions, i)
            && instructions[i + 1].opcode == OpCodes.Dup
            && (instructions[i + 2].opcode == OpCodes.Brtrue || instructions[i + 2].opcode == OpCodes.Brtrue_S)
            && instructions[i + 3].opcode == OpCodes.Pop && instructions[i + 4].opcode == OpCodes.Ldnull
            && (instructions[i + 5].opcode == OpCodes.Br || instructions[i + 5].opcode == OpCodes.Br_S)
            && instructions[i + 6].opcode == OpCodes.Ldfld && Equals(instructions[i + 6].operand, PortId);

        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> original)
        {
            var code = original.Select(instruction => new CodeInstruction(instruction)).ToList();
            for (var i = 0; i < code.Count; i++)
            {
                if (DiscoveryRead(code, i))
                    Replace(i, 1, nameof(Discover));
                else if (PortRead(code, i))
                    Replace(i, 7, nameof(ResolvePort));
                else if (KindRead(code, i))
                    Replace(i, 2, nameof(Kind));
            }
            return code;

            void Replace(int start, int count, string helper)
            {
                code[start].opcode = OpCodes.Call;
                code[start].operand = AccessTools.DeclaredMethod(typeof(CabControlsCompatibility), helper);
                // Retain labels and exception blocks at their original
                // instructions while replacing the null-conditional read.
                for (var j = start + 1; j < start + count; j++)
                {
                    code[j].opcode = OpCodes.Nop;
                    code[j].operand = null;
                }
            }
        }
    }
}
