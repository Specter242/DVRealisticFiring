using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV.Simulation.Controllers;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
    /// <summary>
    /// Writes a loco's chimney smoke setup to the UMM log: the stock smoke
    /// reader's ports and per-beat curves, every particle system under it with
    /// its colors, size, lifetime, and material, and the generic particle and
    /// color readers with the ports they follow. Curves, colors, and bindings
    /// are prefab data, not in the decompiled code. Logs once per livery.
    /// </summary>
    internal static class SmokeDump
    {
        private static readonly HashSet<string> DumpedLiveries = new HashSet<string>();

        public static void DumpOnce(TrainCar car, ParticlesPortReadersController controller)
        {
            if (car == null || !DumpedLiveries.Add(car.carLivery.id))
                return;

            var log = new StringBuilder();
            log.AppendLine($"Chimney smoke setup of livery '{car.carLivery.id}':");

            foreach (var smoke in car.GetComponentsInChildren<SteamSmokeParticlePortReader>(true))
            {
                log.AppendLine($"  SteamSmokeParticlePortReader '{Path(car, smoke.transform)}': fire on '{smoke.fireOnPortId}', " +
                               $"chuff '{smoke.chuffEventPortId}', boiler broken '{smoke.isBoilerBrokenPortId}', " +
                               $"exhaust pressure '{smoke.exhaustPressurePortId}'");
                log.AppendLine($"    smoke curves: speed {Curve(smoke.smokeStartSpeedMultiplier)}, " +
                               $"rate {Curve(smoke.smokeEmissionRateMultiplier)}, max {Curve(smoke.smokeMaxParticlesMultiplier)}");
                log.AppendLine($"    ember curves: speed {Curve(smoke.emberStartSpeedMultiplier)}, " +
                               $"rate {Curve(smoke.emberEmissionRateMultiplier)}, max {Curve(smoke.emberMaxParticlesMultiplier)}");
                AppendSystems(log, car, "smoke", smoke.smokeParticlesParent);
                AppendSystems(log, car, "embers", smoke.emberParticlesParent);
            }

            if (controller != null)
            {
                foreach (var reader in controller.particlePortReaders ?? new List<ParticlesPortReadersController.ParticlePortReader>())
                {
                    var ports = (reader.particleUpdaters ?? new List<ParticlesPortReadersController.ParticlePortReader.PortParticleUpdateDefinition>())
                        .Select(u => $"'{u.portId}' -> {string.Join("/", (u.propertiesToUpdate ?? new List<ParticlesPortReadersController.ParticlePortReader.PropertyChangeDefinition>()).Select(p => $"{p.propertyType} {Curve(p.propertyChangeCurve)}"))}");
                    log.AppendLine($"  Particle reader on '{(reader.particlesParent != null ? Path(car, reader.particlesParent.transform) : "-")}': {string.Join("; ", ports)}");
                }
                foreach (var reader in controller.particleColorPortReaders ?? new List<ParticlesPortReadersController.ParticleColorPortReader>())
                {
                    log.AppendLine($"  Color reader on '{(reader.particlesParent != null ? Path(car, reader.particlesParent.transform) : "-")}': " +
                                   $"port '{reader.portId}' ×{reader.inputModifier?.valueMultiplier ?? 1f:F2} +{reader.inputModifier?.valueOffset ?? 0f:F2}, " +
                                   $"{reader.changeType} from {Format(reader.startColorMin)} to {Format(reader.startColorMax)} along {Curve(reader.colorLerpCurve)}");
                }
            }

            Main.Mod.Logger.Log(log.ToString().TrimEnd());
        }

        private static void AppendSystems(StringBuilder log, TrainCar car, string role, GameObject parent)
        {
            if (parent == null)
            {
                log.AppendLine($"    {role}: no parent");
                return;
            }
            foreach (var system in parent.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                var emission = system.emission;
                var colorOverLifetime = system.colorOverLifetime;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                log.AppendLine($"    {role} '{Path(car, system.transform)}': start color {Format(main.startColor)}, " +
                               $"size {Format(main.startSize)}, lifetime {Format(main.startLifetime)}, speed {Format(main.startSpeed)}, " +
                               $"rate {Format(emission.rateOverTime)}, bursts {emission.burstCount}, max {main.maxParticles}, " +
                               $"space {main.simulationSpace}, gravity {Format(main.gravityModifier)}");
                log.AppendLine($"      modules on: {string.Join(", ", ModulesOn(system))}");
                log.AppendLine($"      color over lifetime {(colorOverLifetime.enabled ? Format(colorOverLifetime.color) : "off")}, " +
                               $"size over lifetime {(system.sizeOverLifetime.enabled ? "on" : "off")}, " +
                               $"material '{(material != null ? material.name : "-")}' " +
                               $"(shader '{(material != null && material.shader != null ? material.shader.name : "-")}'" +
                               $"{MaterialColor(material)})");
            }
        }

        // Modules that move or reshape particles, which the plume must not fight.
        private static IEnumerable<string> ModulesOn(ParticleSystem system)
        {
            if (system.velocityOverLifetime.enabled) yield return "velocity over lifetime";
            if (system.limitVelocityOverLifetime.enabled) yield return $"limit velocity (drag {Format(system.limitVelocityOverLifetime.drag)})";
            if (system.forceOverLifetime.enabled) yield return "force over lifetime";
            if (system.inheritVelocity.enabled) yield return "inherit velocity";
            if (system.externalForces.enabled) yield return "external forces";
            if (system.noise.enabled) yield return $"noise (strength {Format(system.noise.strength)})";
            if (system.sizeBySpeed.enabled) yield return "size by speed";
            if (system.collision.enabled) yield return "collision";
            if (system.shape.enabled) yield return $"shape {system.shape.shapeType} (angle {system.shape.angle:0.#}, radius {system.shape.radius:0.##})";
        }

        private static string MaterialColor(Material material)
        {
            if (material == null)
                return "";
            foreach (var property in new[] { "_TintColor", "_Color" })
            {
                if (material.HasProperty(property))
                    return $", {property} {Format(material.GetColor(property))}";
            }
            return "";
        }

        private static string Curve(AnimationCurve curve) =>
            curve == null || curve.length == 0
                ? "-"
                : "[" + string.Join(" ", curve.keys.Select(k => $"{k.time:0.##}:{k.value:0.##}")) + "]";

        private static string Format(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant.ToString("0.###");
                case ParticleSystemCurveMode.TwoConstants:
                    return $"{curve.constantMin:0.###}..{curve.constantMax:0.###}";
                default:
                    return $"{curve.mode} ×{curve.curveMultiplier:0.###}";
            }
        }

        private static string Format(ParticleSystem.MinMaxGradient gradient)
        {
            switch (gradient.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return Format(gradient.color);
                case ParticleSystemGradientMode.TwoColors:
                    return $"{Format(gradient.colorMin)}..{Format(gradient.colorMax)}";
                case ParticleSystemGradientMode.Gradient:
                    return Format(gradient.gradient);
                case ParticleSystemGradientMode.TwoGradients:
                    return $"{Format(gradient.gradientMin)}..{Format(gradient.gradientMax)}";
                default:
                    return gradient.mode.ToString();
            }
        }

        private static string Format(Gradient gradient) =>
            gradient == null
                ? "-"
                : "{" + string.Join(" ", gradient.colorKeys.Select(k => $"{k.time:0.##}:#{ColorUtility.ToHtmlStringRGB(k.color)}")) +
                  " | alpha " + string.Join(" ", gradient.alphaKeys.Select(k => $"{k.time:0.##}:{k.alpha:0.##}")) + "}";

        private static string Format(Color c) => $"#{ColorUtility.ToHtmlStringRGBA(c)}";

        private static string Path(TrainCar car, Transform t) => CarSpace.PathFrom(car.transform, t);
    }
}
