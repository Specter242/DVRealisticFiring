using System.Collections.Generic;
using System.Linq;
using System.Text;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.HUD;
using DV.Interaction;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
    /// <summary>
    /// Writes where a loco's firebox interior sits to the UMM log, in the car's
    /// frame: the fire, the colliders the shovel targets, the coal-level
    /// indicator and its meshes, and the renderers the fire lights up. The
    /// prefab data is not in the decompiled code, and the shovel aim and a
    /// zoned coal bed are laid out from it. Logs once per livery.
    /// </summary>
    internal static class FireboxGeometryDump
    {
        private static readonly HashSet<string> DumpedLiveries = new HashSet<string>();

        public static void DumpOnce(TrainCar car, Fire fire)
        {
            if (car == null || fire == null || !DumpedLiveries.Add(car.carLivery.id))
                return;

            var carFrame = car.transform;
            var log = new StringBuilder();
            log.AppendLine($"Firebox geometry of livery '{car.carLivery.id}' (car frame in metres, +Z to the front):");
            log.AppendLine($"  Fire '{Path(car, fire.transform)}' at {Position(carFrame, fire.transform)}, " +
                           $"rotation {Rotation(carFrame, fire.transform)}, scale {Format(fire.transform.lossyScale)}");
            if (fire.fireObj != null)
                log.AppendLine($"  Fire particles at {Position(carFrame, fire.fireObj.transform)}");
            if (fire.fireLight != null)
                log.AppendLine($"  Fire light at {Position(carFrame, fire.fireLight.transform)}, range {fire.fireLight.range:F2}");

            log.AppendLine("  Shovel coal targets:");
            foreach (var target in Roots(car, fire).SelectMany(r => r.GetComponentsInChildren<ItemUseTarget>(true)).Distinct())
            {
                var kind = target.GetComponent<NonPhysicsCoalTarget>() != null ? "NonPhysicsCoalTarget"
                    : target.GetComponent<Fire>() != null ? "Fire" : null;
                if (kind == null)
                    continue;
                var pose = target.transform.Find("[animation_pose_target]");
                log.AppendLine($"    '{Path(car, target.transform)}' ({kind}) at {Position(carFrame, target.transform)}" +
                               (pose != null ? $", pose target at {Position(carFrame, pose)}" : ""));
                foreach (var collider in target.targetColliders ?? new Collider[0])
                {
                    if (collider == null)
                        continue;
                    log.AppendLine($"      {collider.GetType().Name} '{Path(car, collider.transform)}'" +
                                   $"{(collider.isTrigger ? " trigger" : "")}{(collider.enabled ? "" : " disabled")}: " +
                                   Format(CarSpace.LocalBounds(carFrame, collider)));
                }
            }

            var coalLevel = Roots(car, fire)
                .SelectMany(r => r.GetComponentsInChildren<LocoIndicatorReader>(true))
                .Select(reader => reader.locoCoalLevel)
                .FirstOrDefault(indicator => indicator != null);
            log.AppendLine(coalLevel != null
                ? $"  Coal level indicator: {Describe(car, coalLevel)}"
                : "  Coal level indicator: none found");
            if (coalLevel != null)
                AppendRenderers(log, car, coalLevel.GetComponentsInChildren<Renderer>(true), "    ");

            foreach (var controls in Roots(car, fire).SelectMany(r => r.GetComponentsInChildren<LocoControlsReader>(true)).Distinct())
            {
                log.AppendLine($"  Cab coal dump control: {DescribeControl(car, controls.coalDump)}");
                log.AppendLine($"  Cab fire door control: {DescribeControl(car, controls.firedoor)}");
            }

            foreach (var indicator in fire.GetComponentsInChildren<Indicator>(true).Where(i => i != coalLevel))
                log.AppendLine($"  Indicator under the fire: {Describe(car, indicator)}");

            log.AppendLine("  Renderers the fire lights up:");
            AppendRenderers(log, car, fire.emissiveRenderersAffectedByFire ?? new Renderer[0], "    ");
            log.AppendLine("  Renderers under the fire:");
            AppendRenderers(log, car, fire.GetComponentsInChildren<Renderer>(true), "    ");

            Main.Mod.Logger.Log(log.ToString().TrimEnd());
        }

        // The cab interior is a separate hierarchy from the car body.
        private static IEnumerable<Transform> Roots(TrainCar car, Fire fire)
        {
            var roots = new List<Transform> { car.transform, fire.transform.root };
            if (car.loadedInterior != null)
                roots.Add(car.loadedInterior.transform);
            return roots.Distinct();
        }

        private static string Describe(TrainCar car, Indicator indicator)
        {
            var text = $"{indicator.GetType().Name} '{Path(car, indicator.transform)}' at {Position(car.transform, indicator.transform)}, " +
                       $"range {indicator.minValue:F2}..{indicator.maxValue:F2}, value {indicator.Value:F2}";
            switch (indicator)
            {
                case IndicatorScaler scaler when scaler.indicatorToScale != null:
                    return text + $", scales '{Path(car, scaler.indicatorToScale)}' from {Format(scaler.startScale)} " +
                           $"to {Format(scaler.endScale)}, now {Format(scaler.indicatorToScale.localScale)}";
                case IndicatorShaderValue shader when shader.renderer != null:
                    return text + $", drives the shader of '{Path(car, shader.renderer.transform)}'";
                default:
                    return text;
            }
        }

        // The lever spec decides whether the dump lever can be rocked: notches,
        // spring return, and travel are prefab data.
        private static string DescribeControl(TrainCar car, GameObject control)
        {
            if (control == null)
                return "none";
            var impl = control.GetComponent<ControlImplBase>();
            var text = $"'{Path(car, control.transform)}' {(impl != null ? impl.GetType().Name : "no control")}" +
                       (impl != null ? $", value {impl.Value:F2}" : "");
            var lever = control.GetComponent<Lever>();
            if (lever != null)
            {
                text += $", lever: stepped {lever.useSteppedJoint} ({lever.notches} notches, stepped value {lever.steppedValueUpdate}), " +
                        $"spring {lever.useSpring} ({lever.jointSpring:F0}), limits {lever.useLimits} {lever.jointLimitMin:F0}..{lever.jointLimitMax:F0}°, " +
                        $"inverted {lever.invertDirection}, scroll {lever.scrollWheelHoverScroll:F3}";
            }
            return text;
        }

        private static void AppendRenderers(StringBuilder log, TrainCar car, IEnumerable<Renderer> renderers, string indent)
        {
            foreach (var renderer in renderers.Where(r => r != null).Distinct())
            {
                var filter = renderer.GetComponent<MeshFilter>();
                var material = renderer.sharedMaterial;
                log.AppendLine($"{indent}{renderer.GetType().Name} '{Path(car, renderer.transform)}'" +
                               $"{(renderer.enabled ? "" : " disabled")}: " +
                               $"mesh '{(filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "-")}', " +
                               $"material '{(material != null ? material.name : "-")}' " +
                               $"(shader '{(material != null && material.shader != null ? material.shader.name : "-")}'), " +
                               Format(CarSpace.LocalBounds(car.transform, renderer)));
            }
        }

        private static string Path(TrainCar car, Transform t) => CarSpace.PathFrom(car.transform, t);

        private static string Position(Transform car, Transform t) => Format(car.InverseTransformPoint(t.position));

        private static string Rotation(Transform car, Transform t) =>
            Format((Quaternion.Inverse(car.rotation) * t.rotation).eulerAngles);

        private static string Format(Vector3 v) => $"({v.x:F3}, {v.y:F3}, {v.z:F3})";

        private static string Format(Bounds b) => $"min {Format(b.min)} max {Format(b.max)}";
    }
}
