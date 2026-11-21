using System;
using System.Collections.Generic;
using System.Linq;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class SteamVents
    {
        /// <summary>
        /// Takes over <paramref name="car"/>'s stock steam, drawn by a copy
        /// of <paramref name="cloud"/> (the plume's cloud system). Null when
        /// the engine has none.
        /// </summary>
        public static SteamVents Create(TrainCar car, ParticleSystem cloud)
        {
            if (car == null || cloud == null)
                return null;
            var vents = new SteamVents { car = car };
            vents.Find(cloud);
            if (vents.sources.Count == 0)
                return null;
            vents.root = UnityEngine.Object.Instantiate(cloud.gameObject, cloud.transform.parent, false);
            vents.root.name = "Vents";
            vents.Drawn = vents.root.GetComponent<ParticleSystem>();
            Prepare(vents.Drawn, cloud);
            vents.behindRoot = UnityEngine.Object.Instantiate(cloud.gameObject, cloud.transform.parent, false);
            vents.behindRoot.name = "VentsBehind";
            vents.Behind = vents.behindRoot.GetComponent<ParticleSystem>();
            Prepare(vents.Behind, cloud);
            var driven = vents.sources.Where(s => s.DrivenBy != null).ToList();
            vents.Setup = $"steam around the engine from {vents.sources.Count} sources: " +
                          string.Join(", ", vents.sources.GroupBy(s => s.Kind).Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key.ToString())) +
                          (driven.Count > 0 ? "; sized by the game's " + string.Join(", ", driven.Select(s => $"{s.Kind} {s.DrivenBy}").Distinct()) : "");
            return vents;
        }

        // The game's steam: its particle systems that draw with the stock
        // steam's materials, grouped by the object that holds each pair.
        private void Find(ParticleSystem cloud)
        {
            var ours = cloud.transform.parent;
            var groups = new Dictionary<Transform, List<ParticleSystem>>();
            foreach (var system in car.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ours != null && system.transform.IsChildOf(ours))
                    continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                var material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null || !IsSteam(material.name))
                    continue;
                var holder = system.transform.parent != null ? system.transform.parent : system.transform;
                if (!groups.TryGetValue(holder, out var group))
                    groups[holder] = group = new List<ParticleSystem>();
                group.Add(system);
            }
            foreach (var entry in groups)
            {
                if (!KindOf(entry.Key, out var kind))
                    continue;
                // The wispy system's cone is the jet's; the wave rides in it.
                var widest = entry.Value.OrderByDescending(s => s.shape.angle).First();
                var hidden = entry.Value.Select(s => s.GetComponent<ParticleSystemRenderer>()).ToArray();
                sources.Add(new Source
                {
                    Name = PathOf(entry.Key),
                    Kind = kind,
                    Spec = VentSpec.For(kind),
                    Anchor = widest.transform,
                    Stock = entry.Value.ToArray(),
                    Hidden = hidden,
                    WasEnabled = hidden.Select(r => r.enabled).ToArray(),
                });
            }
            foreach (var source in sources)
                foreach (var renderer in source.Hidden)
                    renderer.enabled = false;
            BindDrivers();
        }

        // Each stock source's driver: the port the game's particle controller
        // switches and scales its effect by (the blowdown's flow, the whistle's,
        // the safety valves', the dynamo's, a cracked cylinder's), read as the
        // controller reads it, so the steam follows the valve however it was
        // opened before (pnwr1854, Nexus, 2026-11-13). The reader nearest the
        // source's systems is its own. Leaks keep their chest pressure.
        private void BindDrivers()
        {
            var flow = car != null && car.SimController != null ? car.SimController.SimulationFlow : null;
            if (flow == null)
                return;
            var readers = car.GetComponentsInChildren<DV.Simulation.Controllers.ParticlesPortReadersController>(true)
                .Where(c => c != null && c.particlePortReaders != null)
                .SelectMany(c => c.particlePortReaders)
                .Where(r => r != null && r.particlesParent != null && r.particleUpdaters != null)
                .ToList();
            foreach (var source in sources)
            {
                if (source.Stock.Length == 0 || source.Kind == VentKind.Leak)
                    continue;
                DV.Simulation.Controllers.ParticlesPortReadersController.ParticlePortReader own = null;
                var depth = -1;
                foreach (var reader in readers)
                {
                    var parent = reader.particlesParent.transform;
                    if (!source.Stock.Any(s => s != null && s.transform.IsChildOf(parent)))
                        continue;
                    var d = 0;
                    for (var t = parent; t != null; t = t.parent)
                        d++;
                    if (d > depth)
                    {
                        own = reader;
                        depth = d;
                    }
                }
                if (own == null)
                    continue;
                var updaters = own.particleUpdaters.Where(u => u != null).ToList();
                var index = VentDrive.Driver(updaters
                    .Select(u => (IReadOnlyList<int>)(u.propertiesToUpdate ?? new List<DV.Simulation.Controllers.ParticlesPortReadersController.ParticlePortReader.PropertyChangeDefinition>())
                        .Where(c => c != null).Select(c => (int)c.propertyType).ToList())
                    .ToList());
                if (index < 0)
                    continue;
                var updater = updaters[index];
                if (string.IsNullOrEmpty(updater.portId) || !flow.TryGetPort(updater.portId, out var port) || port == null)
                    continue;
                var modifier = updater.inputModifier;
                var multiplier = modifier != null ? modifier.valueMultiplier : 1f;
                var offset = modifier != null ? modifier.valueOffset : 0f;
                var absoluteInput = modifier != null && modifier.absoluteInputValue;
                var absoluteResult = modifier != null && modifier.absoluteResultValue;
                source.Driven = () => VentDrive.Strength(port.Value, multiplier, offset, absoluteInput, absoluteResult);
                source.DrivenBy = updater.portId;
            }
        }

        private static bool IsSteam(string material) =>
            material.StartsWith("GasWave", StringComparison.Ordinal) || material.StartsWith("WispyCloudWhite", StringComparison.Ordinal);

        // A source's kind by the names over it, as the game's engines and
        // Custom Car Loader's copies of their effects name them.
        private bool KindOf(Transform holder, out VentKind kind)
        {
            for (var t = holder; t != null && t != car.transform; t = t.parent)
            {
                var name = t.name;
                if (Has(name, "Dynamo")) { kind = VentKind.Dynamo; return true; }
                if (Has(name, "CylCock") || Has(name, "CylinderCock")) { kind = VentKind.CylinderCock; return true; }
                if (Has(name, "Safety")) { kind = VentKind.SafetyValve; return true; }
                if (Has(name, "Whistle")) { kind = VentKind.Whistle; return true; }
                if (Has(name, "Blowdown")) { kind = VentKind.Blowdown; return true; }
                if (Has(name, "CylinderCrack")) { kind = VentKind.CylinderCrack; return true; }
                if (Has(name, "Leak")) { kind = VentKind.Leak; return true; }
            }
            kind = default;
            return false;
        }

        private static bool Has(string name, string part) => name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        private string PathOf(Transform holder)
        {
            var names = new List<string>();
            for (var t = holder; t != null && t != car.transform && names.Count < 3; t = t.parent)
                names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // The mod sets every particle each frame: nothing of Unity's moves,
        // sizes, tints, or turns them.
        private static void Prepare(ParticleSystem system, ParticleSystem cloud)
        {
            var main = system.main;
            main.maxParticles = MostParcels;
            main.startLifetime = LongestLife * 2f;
            main.gravityModifier = 0f;
            var cloudMain = cloud.main;
            main.simulationSpace = cloudMain.simulationSpace;
            if (cloudMain.simulationSpace == ParticleSystemSimulationSpace.Custom)
                main.customSimulationSpace = cloudMain.customSimulationSpace;
            var emission = system.emission;
            emission.enabled = false;
            var size = system.sizeOverLifetime;
            size.enabled = false;
            var color = system.colorOverLifetime;
            color.enabled = false;
            var noise = system.noise;
            noise.enabled = false;
            var spin = system.rotationOverLifetime;
            spin.enabled = false;
            var bySpeed = system.sizeBySpeed;
            bySpeed.enabled = false;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = false;
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = false;
            var force = system.forceOverLifetime;
            force.enabled = false;
            var inherit = system.inheritVelocity;
            inherit.enabled = false;
            var external = system.externalForces;
            external.enabled = false;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.sortMode = ParticleSystemSortMode.Distance;
            system.Clear();
            system.Play();
        }

        /// <summary>Draws with the plume's own <paramref name="material"/>, its light and look.</summary>
        public void UseMaterial(Material material)
        {
            var renderer = Drawn != null ? Drawn.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer == null || material == null)
                return;
            renderer.sharedMaterial = material;
            var behind = Behind != null ? Behind.GetComponent<ParticleSystemRenderer>() : null;
            if (behind != null)
                behind.sharedMaterial = material;
            if (material.HasProperty("_SixWay") && material.GetFloat("_SixWay") > 0.5f)
                puffs = Mathf.Max(1, Mathf.RoundToInt(material.GetFloat("_SixColumns") * material.GetFloat("_SixRows")
                                                      / Mathf.Max(1f, material.GetFloat("_SixFrames"))));
        }

        /// <summary>
        /// Adds a source of the mod's own: <paramref name="kind"/>'s steam
        /// leaving <paramref name="anchor"/> along its forward, as hard as
        /// <paramref name="strength"/> says, 0 to 1.
        /// </summary>
        public void Add(string name, VentKind kind, Transform anchor, Func<float> strength)
        {
            if (anchor == null || strength == null)
                return;
            sources.Add(new Source { Name = name, Kind = kind, Spec = VentSpec.For(kind), Anchor = anchor, Strength = strength });
        }

 public string AddRunningGear(Func<float> chest, Func<float> crank, Func<float> boiler, Func<float> damage)
        {
            chestPressure = chest;
            var stock = sources.Where(s => s.Stock.Length > 0 && s.Anchor != null).ToList();
            var cocks = stock.Where(s => s.Kind == VentKind.CylinderCock)
                .Select(s => (Source: s, At: car.transform.InverseTransformPoint(s.Anchor.position))).ToList();
            var glands = 0;
            var weeps = 0;
            foreach (var side in cocks.GroupBy(c => c.At.x < 0f ? -1f : 1f))
            {
                // The rod leaves the cover nearer the engine's middle.
                var rear = side.OrderBy(c => Mathf.Abs(c.At.z)).First();
                var front = side.OrderByDescending(c => Mathf.Abs(c.At.z)).First();
                var back = rear.At.z >= 0f ? -1f : 1f;
                // The two sides' cranks are a quarter turn apart.
                var phase = side.Key < 0f ? 0f : 0.25f;
                float Stroke(float offset) =>
                    Mathf.Clamp01(damage()) * Mathf.Clamp01(chest() / GlandFullPressure) * (0.3f + 0.7f * Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (crank() + phase + offset))));
                Add($"gland {(side.Key < 0f ? "L" : "R")}", VentKind.Gland,
                    Place(rear.At + new Vector3(0f, GlandUp, back * GlandBack), new Vector3(0.5f * side.Key, 0.25f, back)), () => Stroke(0f));
                Add($"valve gland {(side.Key < 0f ? "L" : "R")}", VentKind.Gland,
                    Place(rear.At + new Vector3(-0.1f * side.Key, ValveUp, back * GlandBack), new Vector3(0.4f * side.Key, 0.5f, back)), () => 0.6f * Stroke(0.5f));
                glands += 2;
                // The cylinder's drains, passing steam while shut.
                var blowing = side.Select(c => c.Source).ToList();
                Add($"drain {(side.Key < 0f ? "L" : "R")}", VentKind.Weep,
                    Place(0.5f * (rear.At + front.At), new Vector3(0.3f * side.Key, -1f, 0f)),
                    () => AnyBlowing(blowing) ? 0f : Mathf.Clamp01(damage()) * Mathf.Clamp01(chest() / WeepFullPressure));
                weeps++;
            }
            foreach (var fitting in stock.Where(s => s.Kind == VentKind.SafetyValve || s.Kind == VentKind.Whistle || s.Kind == VentKind.Dynamo))
            {
                var own = fitting;
                Add($"weep at {fitting.Kind}", VentKind.FittingWeep, fitting.Anchor,
                    () => own.Now > 0.01f ? 0f : Mathf.Clamp01(boiler() / WeepFullPressure));
                weeps++;
            }
            return $"{glands} glands and {weeps} weeps";
        }

        /// <summary>A place on the engine, at a point in its own frame, facing a way in it; removed with the vents.</summary>
        public Transform Place(Vector3 at, Vector3 facing)
        {
            var go = new GameObject("RF vent");
            go.transform.SetParent(car.transform, false);
            go.transform.localPosition = at;
            go.transform.localRotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            anchors.Add(go);
            return go.transform;
        }

        /// <summary>
        /// A capture's sources: each kind named in <paramref name="request"/>
        /// ("Dynamo=1;SafetyValve=0.5") blows that hard whatever the engine
        /// is doing; null or empty hands them back to the engine.
        /// </summary>
        public void Test(string request)
        {
            foreach (var source in sources)
                source.Test = -1f;
            if (string.IsNullOrWhiteSpace(request))
                return;
            foreach (var pair in request.Split(';'))
            {
                var parts = pair.Split('=');
                if (parts.Length != 2 || !Enum.TryParse(parts[0].Trim(), true, out VentKind kind)
                    || !float.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
                    continue;
                foreach (var source in sources.Where(s => s.Kind == kind))
                    source.Test = Mathf.Clamp01(value);
            }
        }

        private static bool AnyBlowing(List<Source> candidates)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Now > 0.01f)
                    return true;
            }
            return false;
        }

        // How hard a source blows now, 0 to 1.
        private float StrengthOf(Source source)
        {
            if (source.Test >= 0f)
                return source.Test;
            if (source.Strength != null)
                return Mathf.Clamp01(source.Strength());
            var fastest = 0f;
            var blowing = false;
            foreach (var system in source.Stock)
            {
                if (system == null || !system.gameObject.activeInHierarchy || !system.isEmitting)
                    continue;
                blowing = true;
                fastest = Mathf.Max(fastest, system.main.startSpeed.constantMax);
            }
            if (!blowing)
                return 0f;
            // The game's leaks play whenever the engine stands, by its
            // absolute chest pressure, a cold engine's too, and thin them by
            // their rate alone: here by the steam actually in the chest.
            if (source.Kind == VentKind.Leak)
                return chestPressure != null ? Mathf.Clamp01(chestPressure() / LeakFullPressure) : 0f;
            // On, as the game has it, as hard as the value that drives it.
            if (source.Driven != null)
                return source.Driven();
            source.FastestSeen = Mathf.Max(source.FastestSeen, fastest);
            return source.FastestSeen > 0f ? Mathf.Clamp(fastest / source.FastestSeen, LeastStrength, 1f) : 1f;
        }

        private SteamCloud CloudFor(PlumeAir air, float wetness)
        {
            if (cloudsAir.Temperature != air.Temperature || cloudsAir.Humidity != air.Humidity)
            {
                clouds.Clear();
                cloudsAir = air;
            }
            var key = (long)Mathf.RoundToInt(wetness * 100f);
            if (!clouds.TryGetValue(key, out var cloud))
                clouds[key] = cloud = SteamCloud.For(air, 0f, wetness);
            return cloud;
        }

        /// <summary>Hands the game's steam back and removes the mod's.</summary>
        public void Release()
        {
            foreach (var source in sources)
            {
                for (var i = 0; i < source.Hidden.Length; i++)
                {
                    if (source.Hidden[i] != null)
                        source.Hidden[i].enabled = source.WasEnabled[i];
                }
            }
            sources.Clear();
            foreach (var anchor in anchors)
            {
                if (anchor != null)
                    UnityEngine.Object.Destroy(anchor);
            }
            anchors.Clear();
            count = 0;
            Count = 0;
            uploadState.Reset();
            shapeData.Clear();
            extraData.Clear();
            behindShape.Clear();
            behindExtra.Clear();
            if (root != null)
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                root = null;
            }
            if (behindRoot != null)
            {
                behindRoot.SetActive(false);
                UnityEngine.Object.Destroy(behindRoot);
                behindRoot = null;
            }
            Drawn = null;
            Behind = null;
        }
    }
}
