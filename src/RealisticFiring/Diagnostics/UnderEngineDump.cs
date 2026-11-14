using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace RealisticFiring.Diagnostics
{
 internal static class UnderEngineDump
    {
        private const float Every = 4f;
        private const float Reach = 40f;
        private const int MostLines = 90;

        // Under and beside the firebox, in the car's frame, m (x right, y up from the rail, z forward).
        private static readonly Bounds Region = new Bounds(new Vector3(0f, 0.45f, 1.5f), new Vector3(5f, 2.1f, 9f));

        private static float nextAt;
        private static int lastSignature;

        // The probe: what could draw the fault, hidden one at a time.
        private const float HideFor = 4f;
        private static readonly List<Renderer> Suspects = new List<Renderer>();
        private static readonly List<string> SuspectNames = new List<string>();
        // What is there now, taken up as a round begins: a round is not cut short as things come and go.
        private static readonly List<Renderer> Found = new List<Renderer>();
        private static int hidden = -1;
        private static float hiddenUntil;
        private static bool hooked;

        /// <summary>What the probe hides now, for the screen; null when it hides nothing.</summary>
        public static string Showing { get; private set; }

        /// <summary>A step of the probe: the next suspect hidden for a few seconds, then none, and round again.</summary>
        public static void Probe()
        {
            if (!hooked)
            {
                hooked = true;
                Camera.onPreCull += _ => Hide(true);
                Camera.onPostRender += _ => Hide(false);
            }
            if (Time.unscaledTime < hiddenUntil)
                return;
            hiddenUntil = Time.unscaledTime + HideFor;
            Hide(false);
            hidden = hidden + 1 > Suspects.Count ? 0 : hidden + 1;
            if (hidden == 0)
            {
                Suspects.Clear();
                Suspects.AddRange(Found.Where(r => r != null));
                SuspectNames.Clear();
                SuspectNames.AddRange(Suspects.Select(r => $"{r.name} ({(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "no shader")}) under {(r.transform.parent != null ? r.transform.parent.name : "nothing")}"));
            }
            // Once round: with nothing hidden, for the fault as it is.
            if (hidden >= Suspects.Count)
            {
                Showing = Suspects.Count > 0 ? $"Probe: nothing hidden (of {Suspects.Count})" : "Probe: no engine with a realistic firebox near";
                return;
            }
            Showing = $"Probe: hiding {hidden + 1} of {Suspects.Count}: {SuspectNames[hidden]}";
            Main.Mod.Logger.Log(Showing);
        }

        // Rendering forced off as the cameras draw, whatever the mod sets on it meanwhile.
        private static void Hide(bool hide)
        {
            if (hidden >= 0 && hidden < Suspects.Count && Suspects[hidden] != null)
                Suspects[hidden].forceRenderingOff = hide && Main.Settings.ProbeUnderEngine;
        }

        public static void Tick()
        {
            if (Time.unscaledTime < nextAt)
                return;
            nextAt = Time.unscaledTime + Every;
            var camera = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
            if (camera == null)
                return;
            var eye = camera.transform.position;
            TrainCar car = null;
            var nearest = Reach * Reach;
            foreach (var candidate in Object.FindObjectsOfType<TrainCar>())
            {
                if (!Sim.FireboxRegistry.TryGet(candidate, out _))
                    continue;
                var distance = (candidate.transform.position - eye).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    car = candidate;
                }
            }
            if (car == null)
                return;
            var frame = car.transform;
            var lines = new List<string>();
            var suspects = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsOfType<Renderer>())
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                var material = renderer.sharedMaterial;
                var shader = material != null && material.shader != null ? material.shader.name : "none";
                var ours = shader.StartsWith("RealisticFiring", System.StringComparison.Ordinal);
                var bounds = renderer.bounds;
                var at = frame.InverseTransformPoint(bounds.center);
                var reach = Mathf.Min(0.5f, bounds.extents.magnitude);
                var inside = Region.SqrDistance(at) <= reach * reach;
                if (!inside && !(ours && (bounds.center - frame.position).sqrMagnitude < Reach * Reach))
                    continue;
                var particles = renderer is ParticleSystemRenderer ? renderer.GetComponent<ParticleSystem>() : null;
                var count = particles != null ? particles.particleCount : 0;
                var foreign = !renderer.transform.IsChildOf(frame);
                var seeThrough = material != null && material.renderQueue > 2450;
                var glowing = material != null && material.HasProperty("_EmissionColor") && material.IsKeywordEnabled("_EMISSION")
                              && material.GetColor("_EmissionColor").maxColorComponent > 0.05f;
                // A suspect whether or not it has particles now, so the probe's round holds.
                // (The chimney's own plume is not under the engine.)
                if ((ours || particles != null || glowing) && (inside || !Path(renderer.transform).Contains("ChimneyPlumeFX")))
                    suspects.Add(renderer);
                if (particles != null && count == 0)
                    continue;
                if (!(ours || foreign || seeThrough || glowing || particles != null))
                    continue;
                lines.Add($"  {(inside ? "under" : "near ")} {Path(renderer.transform)} [{renderer.GetType().Name}{(particles != null ? $" {count} particles" : "")}] " +
                          $"'{(material != null ? material.name : "no material")}' shader '{shader}' queue {(material != null ? material.renderQueue : 0)} " +
                          $"at ({at.x:F2}, {at.y:F2}, {at.z:F2}) size ({bounds.size.x:F2}, {bounds.size.y:F2}, {bounds.size.z:F2})" +
                          $"{(foreign ? ", not the car's child" : "")}{(glowing ? ", glowing" : "")}");
            }
            foreach (var light in Object.FindObjectsOfType<Light>())
            {
                if (light == null || !light.enabled || !light.gameObject.activeInHierarchy)
                    continue;
                var at = frame.InverseTransformPoint(light.transform.position);
                if (Region.Contains(at))
                    lines.Add($"  light {Path(light.transform)} {light.type} intensity {light.intensity:F2} range {light.range:F1} at ({at.x:F2}, {at.y:F2}, {at.z:F2})");
            }
            lines.Sort(System.StringComparer.Ordinal);
            suspects.Sort((a, b) => string.CompareOrdinal(Path(a.transform), Path(b.transform)));
            Found.Clear();
            Found.AddRange(suspects);
            // What is there, not how many particles or where to the cm: the same set is logged once.
            var signature = lines.Count;
            foreach (var line in lines)
                signature = signature * 31 + line.Substring(0, Mathf.Min(line.Length, line.IndexOf('[') > 0 ? line.IndexOf('[') : line.Length)).GetHashCode();
            if (signature == lastSignature)
                return;
            lastSignature = signature;
            var from = frame.InverseTransformPoint(eye);
            var log = new StringBuilder();
            log.AppendLine($"Under {car.ID} ({car.carLivery?.id}), the camera at ({from.x:F1}, {from.y:F1}, {from.z:F1}) in its frame, " +
                           $"{car.GetForwardSpeed() * 3.6f:F0} km/h, interior {(car.IsInteriorLoaded ? "loaded" : "not loaded")}: {lines.Count} things");
            foreach (var line in lines.Take(MostLines))
                log.AppendLine(line);
            if (lines.Count > MostLines)
                log.AppendLine($"  and {lines.Count - MostLines} more");
            Main.Mod.Logger.Log(log.ToString().TrimEnd());
        }

        private static string Path(Transform transform)
        {
            var names = new List<string>();
            for (var t = transform; t != null && names.Count < 5; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
