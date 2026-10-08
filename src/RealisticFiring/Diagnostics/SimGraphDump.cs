using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using LocoSim.Attributes;
using LocoSim.Definitions;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
    /// <summary>
    /// Writes the part of a car's simulation graph that touches its firebox to
    /// the UMM log: every component in execution order, every connection and
    /// port reference naming a firebox port, and every [PortId] field on the
    /// car's MonoBehaviours that points at the firebox. Logs once per livery.
    /// </summary>
    internal static class SimGraphDump
    {
        private const int MaxFieldDepth = 3;

        private static readonly HashSet<string> DumpedLiveries = new HashSet<string>();

        public static void DumpOnce(TrainCar car, SimConnectionDefinition graph)
        {
            if (car == null || graph == null || graph.executionOrder == null)
                return;

            var fireboxes = graph.executionOrder.OfType<FireboxDefinition>().ToList();
            if (fireboxes.Count == 0)
                return;

            var livery = car.carLivery != null ? car.carLivery.id : car.name;
            if (!DumpedLiveries.Add(livery))
                return;

            var log = new StringBuilder();
            log.AppendLine($"Sim graph of livery '{livery}' (car type '{car.carLivery?.parentType?.id}'):");

            log.AppendLine("  Execution order:");
            for (var i = 0; i < graph.executionOrder.Length; i++)
            {
                var def = graph.executionOrder[i];
                log.AppendLine(def == null
                    ? $"    [{i}] <missing definition>"
                    : $"    [{i}] {def.ID} : {def.GetType().FullName}");
            }

            foreach (var firebox in fireboxes)
            {
                var prefix = firebox.ID + ".";
                log.AppendLine($"  Firebox '{firebox.ID}': capacity {firebox.maxCoalCapacity} kg, burn time {firebox.burnTime} s, " +
                               $"efficiency at max {firebox.efficiencyAtMaxCombustion}, dump rate {firebox.coalDumpRate} kg/s, " +
                               $"smoothing {firebox.combustionRateSmoothTime}/{firebox.temperatureSmoothTime} s, " +
                               $"startup pressure {firebox.startupMaxPressure} bar");

                log.AppendLine("    Connections:");
                foreach (var c in graph.connections ?? Array.Empty<Connection>())
                {
                    if (StartsWith(c.fullPortIdOut, prefix) || StartsWith(c.fullPortIdIn, prefix))
                        log.AppendLine($"      {c.fullPortIdOut} -> {c.fullPortIdIn}");
                }

                log.AppendLine("    Port references:");
                foreach (var r in graph.portReferenceConnections ?? Array.Empty<PortReferenceConnection>())
                {
                    if (StartsWith(r.portReferenceId, prefix) || StartsWith(r.portId, prefix))
                        log.AppendLine($"      {r.portReferenceId} reads {r.portId}");
                }

                log.AppendLine("    Car components naming firebox ports:");
                foreach (var behaviour in car.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null || behaviour is SimComponentDefinition)
                        continue;
                    foreach (var (path, value) in PortIdFields(behaviour, behaviour.GetType().Name, 0))
                    {
                        if (StartsWith(value, prefix))
                            log.AppendLine($"      {path} = {value} (on '{behaviour.gameObject.name}')");
                    }
                }
            }

            Main.Mod.Logger.Log(log.ToString().TrimEnd());
        }

        private static bool StartsWith(string value, string prefix) =>
            value != null && value.StartsWith(prefix, StringComparison.Ordinal);

        // Walks serialized fields for strings tagged [PortId], descending into
        // nested serializable objects and arrays a few levels deep.
        private static IEnumerable<(string path, string value)> PortIdFields(object target, string path, int depth)
        {
            if (target == null || depth > MaxFieldDepth)
                yield break;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var field in target.GetType().GetFields(flags))
            {
                var type = field.FieldType;
                if (type == typeof(string))
                {
                    if (field.IsDefined(typeof(PortIdAttribute), true))
                        yield return ($"{path}.{field.Name}", (string)field.GetValue(target));
                    continue;
                }

                if (type.IsPrimitive || type.IsEnum || typeof(UnityEngine.Object).IsAssignableFrom(type))
                    continue;

                var value = field.GetValue(target);
                if (value is IEnumerable sequence && !(value is string))
                {
                    var index = 0;
                    foreach (var item in sequence)
                    {
                        if (item is string text)
                        {
                            if (field.IsDefined(typeof(PortIdAttribute), true))
                                yield return ($"{path}.{field.Name}[{index}]", text);
                        }
                        else if (item != null && !(item is UnityEngine.Object) && item.GetType().IsSerializable)
                        {
                            foreach (var nested in PortIdFields(item, $"{path}.{field.Name}[{index}]", depth + 1))
                                yield return nested;
                        }
                        index++;
                    }
                }
                else if (value != null && type.IsSerializable && !type.IsValueType)
                {
                    foreach (var nested in PortIdFields(value, $"{path}.{field.Name}", depth + 1))
                        yield return nested;
                }
            }
        }
    }
}
