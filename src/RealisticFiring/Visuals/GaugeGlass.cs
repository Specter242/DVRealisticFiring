using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Ports;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class GaugeGlass
    {
        private readonly struct Span
        {
            public Span(Indicator glass, IndicatorPortReader reader)
            {
                Min = glass.minValue;
                Max = glass.maxValue;
                Absolute = glass.absoluteNormalizedValue;
                Multiplier = reader.valueMultiplier;
                Offset = reader.valueOffset;
                AbsoluteValue = reader.useAbsoluteValue;
            }

            public float Min { get; }
            public float Max { get; }
            public bool Absolute { get; }
            public float Multiplier { get; }
            public float Offset { get; }
            public bool AbsoluteValue { get; }

            // As the cab's indicator works its reading out.
            public float Show(float level)
            {
                var value = level * Multiplier + Offset;
                if (AbsoluteValue)
                    value = Mathf.Abs(value);
                return Absolute ? Mathf.InverseLerp(0f, Max, Mathf.Abs(value)) : Mathf.InverseLerp(Min, Max, value);
            }
        }

        private static readonly Dictionary<string, Span> Spans = new Dictionary<string, Span>();
        private static readonly HashSet<GameObject> Searched = new HashSet<GameObject>();

        /// <summary>The water in <paramref name="rig"/>'s glass, 0 to 1; the boiler's level where the cab shows no glass.</summary>
        public static float Read(InjectorRig rig)
        {
            if (rig == null)
                return 0f;
            return Read(rig.Car, rig.WaterLevelPortId, rig.WaterLevel);
        }

        /// <summary>Shows a boiler level or target on the same cab scale as F6.</summary>
        public static float Read(TrainCar car, string port, float level)
        {
            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (livery == null)
                return level;
            if (!Spans.ContainsKey(livery))
                Learn(car, port);
            return Spans.TryGetValue(livery, out var span) ? span.Show(level) : level;
        }

        // The cab's glasses: indicators reading the boiler's water level.
        private static void Learn(TrainCar car, string port)
        {
            var interior = car.loadedInterior;
            if (interior == null || !Searched.Add(interior))
                return;
            Searched.RemoveWhere(o => o == null);
            var glass = interior.GetComponentsInChildren<IndicatorPortReader>(true)
                .Where(r => r.portId == port)
                .Select(r => (Reader: r, Indicator: r.GetComponent<Indicator>()))
                .FirstOrDefault(g => g.Indicator != null);
            if (glass.Indicator == null)
                return;
            var span = new Span(glass.Indicator, glass.Reader);
            Spans[car.carLivery.id] = span;
            if (Main.Settings.LogFireboxCreation)
                Main.Mod.Logger.Log($"{car.ID}: gauge glass '{glass.Indicator.name}' shows '{port}' from {span.Min:F2} to {span.Max:F2}" +
                                    (span.Multiplier != 1f || span.Offset != 0f ? $" (× {span.Multiplier:F2} + {span.Offset:F2})" : ""));
        }
    }
}
