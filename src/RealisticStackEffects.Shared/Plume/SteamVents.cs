using System;
using System.Collections.Generic;
using System.Linq;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed class SteamVents
    {
        // A parcel's drawn width over its mixture's: the puff's texture
        // shows this share of its quad.
        private const float VisibleShare = 0.6f;

        // The most parcels an engine's vents carry, and how long one lives
        // at most, s.
        private const int MostParcels = 2500;

        // As many parcels as an engine's vents carry without holding back,
        // at the highest smoke quality: past six tenths of it each source
        // leaves fewer, bigger parcels, down to a quarter as many at all of
        // it (every source blowing in a hard frost carried 1,800, and cost
        // 5 ms a frame on the laptop; 2026-11-10).
        private const int Budget = 800;
        private const float LongestLife = 90f;

        // A parcel fainter than this is not drawn; once it has shown and
        // fallen to half of it, it is gone.
        private const float Faint = 0.006f;

        // A parcel that never shows is dropped once the air mixed into it
        // passes this, kg a kg of steam.
        private const float NeverShown = 4000f;

        // Nor does one wait that long whose white, in this air, would have
        // formed already: past this many times the mixture its cloud is
        // thickest at, and at least this much air, kg a kg of steam.
        private const float PastThickest = 3f;
        private const float LeastMixed = 50f;

        // Under a tunnel's roof (TunnelRoof, TunnelSmoke): a parcel this far
        // along the bore from the stack takes the roof over the stack where
        // its own spot was never sampled (an engine standing in a tunnel
        // since it loaded), m.
        private const float RoofAlongEngine = 40f;

        // Fewer, bigger parcels as the steam spreads: at each of these ages,
        // s, half of them fade out over this share of it more while the
        // rest take up their steam, so a long streamer on a frosty day is
        // not thousands of parcels. Which half, by each parcel's seed.
        private static readonly float[] Thinning = { 0.8f, 2.5f, 6f, 14f, 30f };
        private const float ThinningOver = 0.4f;

        private const float SlowestShare = 0.4f;

        // A parcel that takes up a faded one's steam is drawn its breadth
        // to this power wider (a half would keep the puff's whole area, and
        // made a steady jet's old steam too broad; a third is its mass alone).
        private const float BreadthWidth = 0.4f;

        // A fast jet crosses its first meters in a frame or two, and at a
        // slow source's rate left them empty: a safety valve's steam showed
        // only as blobs well above it. It leaves as more, smaller parcels,
        // twice as many for each doubling of its speed over this, m/s, up to
        // as many doublings as there are ages here, s, at each of which half
        // fade out as above.
        private static readonly float[] EarlyThinning = { 0.04f, 0.12f, 0.35f };
        private const float SlowJet = 55f;

        // A parcel is drawn as a puff until this old, s, however thin: a
        // thin parcel drawn as a wisp from the pipe showed as loose curls.
        private const float PuffUntil = 1.2f;
        private const float WispBy = 3f;

        // Steam's color: the plume's own white.
        private static readonly Color White = new Color(0.92f, 0.92f, 0.93f, 1f);

        // How long a parcel takes to turn from a rolling puff to a wisp at
        // the look's ages (PlumeLook.WispAgeFrom), s.
        private const float WispTime = 5f;

        // The ground under the engine: this far below its origin, m (the
        // rail's head is about the origin); a parcel's middle stays this
        // share of its width above it.
        private const float GroundBelow = 0.15f;
        private const float GroundClearance = 0.35f;

        // A stock source with no game value found to size it (the cylinder
        // cocks, which their own reader pulses with the stroke) counts as
        // blowing its hardest at the fastest start speed its driver has given
        // it; softer in proportion, no less than this.
        private const float LeastStrength = 0.35f;

        private sealed class Source
        {
            public string Name;
            public VentKind Kind;
            public VentSpec Spec;
            public Transform Anchor;
            public ParticleSystem[] Stock = new ParticleSystem[0];
            public ParticleSystemRenderer[] Hidden = new ParticleSystemRenderer[0];
            public bool[] WasEnabled = new bool[0];
            public Func<float> Strength;
            // The game's value that sizes its stock effect, as its particle
            // controller takes it, 0 to 1 (VentDrive); null where none was found.
            public Func<float> Driven;
            public string DrivenBy;
            public float FastestSeen;
            public float Due;
            public float Test = -1f;
            public float Now;
        }

        private struct Parcel
        {
            public Vector3 Position;
            public Vector3 Direction;
            public Vector3 Wander;
            public float Speed;
            public float Bore;
            public float Age;
            public float Travel;
            public float Mass;
            public float Ambient;
            public float Hard;
            public float Share;
            public int Early;
            public float ClearCore;
            public uint Seed;
            public bool Shown;
            public SteamCloud Cloud;
            // Drawn before the plume, as behind it from the eye (PlumeOrder), once placed.
            public bool Behind;
            public bool Placed;
        }

        private readonly List<Source> sources = new List<Source>();
        private Parcel[] parcels = new Parcel[256];
        private int count;
        private ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[256];
        private readonly List<Vector4> shapeData = new List<Vector4>();
        private readonly List<Vector4> extraData = new List<Vector4>();
        // Those behind the plume, for their own system.
        private ParticleSystem.Particle[] behindBuffer = new ParticleSystem.Particle[256];
        private ParticleSystem.Particle[] frontBuffer = new ParticleSystem.Particle[256];
        private readonly List<Vector4> behindShape = new List<Vector4>();
        private readonly List<Vector4> behindExtra = new List<Vector4>();
        // The plume's line this pass, in the parcels' space: the stack's
        // lip, then its middle at each age, as far as the engine's steam
        // reaches (the youngest this many, half a second apart). Then each
        // piece of it from the eye: its start, its length along, and their
        // products, worked out once a pass for every parcel's look.
        private const int LinePoints = 32;
        private readonly Vector3[] plumeLine = new Vector3[LinePoints];
        private readonly Vector3[] lineFrom = new Vector3[LinePoints];
        private readonly Vector3[] lineStep = new Vector3[LinePoints];
        private readonly float[] lineFromSq = new float[LinePoints];
        private readonly float[] lineFromStep = new float[LinePoints];
        private readonly float[] lineStepSq = new float[LinePoints];
        private int pass;
        private readonly Dictionary<long, SteamCloud> clouds = new Dictionary<long, SteamCloud>();
        private PlumeAir cloudsAir;
        private TrainCar car;
        private GameObject root;
        private GameObject behindRoot;
        private int puffs = 8;

        /// <summary>The particle system the parcels are drawn by: those in front of the plume from the eye, and any with no plume to sort against.</summary>
        public ParticleSystem Drawn { get; private set; }

 public ParticleSystem Behind { get; private set; }

        /// <summary>Whether <paramref name="renderer"/> is one of the two that draw the parcels.</summary>
        public bool Draws(Renderer renderer) =>
            renderer != null && (Drawn != null && renderer.gameObject == Drawn.gameObject || Behind != null && renderer.gameObject == Behind.gameObject);

        /// <summary>The parcels as particles, the first <see cref="Count"/> of them, for the plume's volume.</summary>
        public ParticleSystem.Particle[] Buffer => buffer;
        public int Count { get; private set; }

        /// <summary>What was found, for the log.</summary>
        public string Setup { get; private set; } = "";

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

        // Places of the mod's own on the engine, removed with it.
        private readonly List<GameObject> anchors = new List<GameObject>();

        // A worn gland weeps at its hardest with this much steam in the
        // chest, bar gauge, as the cylinders' drains do; a fitting's seat at
        // its most from this much in the boiler. The game's own leaks are at
        // their most at the pressure its reader has them (12 bar absolute).
        private const float GlandFullPressure = 12f;
        private const float WeepFullPressure = 6f;
        private const float LeakFullPressure = 11f;

        // The steam chest's pressure, bar gauge, once the engine's own sources are added.
        private Func<float> chestPressure;

        // A piston rod's gland from its cylinder's rear drain cock, m: up to
        // the cylinder's middle and back past its cover; the valve's stem
        // above it.
        private const float GlandUp = 0.5f;
        private const float GlandBack = 0.3f;
        private const float ValveUp = 1.05f;

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
                    () => blowing.Any(s => s.Now > 0.01f) ? 0f : Mathf.Clamp01(damage()) * Mathf.Clamp01(chest() / WeepFullPressure));
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

        /// <summary>
        /// A frame: new parcels from every source that blows, and every
        /// parcel moved, mixed, and drawn. <paramref name="wind"/> is the
        /// air's velocity and <paramref name="carVelocity"/> the engine's,
        /// in the world; <paramref name="share"/> the share of parcels drawn
        /// at the player's smoke quality; <paramref name="look"/> the test
        /// bed's.
        /// </summary>
        public void Step(float dt, Vector3 wind, Vector3 carVelocity, PlumeAir air, float share, PlumeLook look, TunnelRoof roof = null,
            PlumeAloft plume = null, Vector3? eye = null, Vector3 lip = default)
        {
            if (Drawn == null || dt <= 0f)
                return;
            var space = PlumeVolume.SpaceOf(Drawn);
            // The plume's line and the eye, in the parcels' space (the cloud's own).
            var lineCount = 0;
            var eyeIn = Vector3.zero;
            if (plume != null && eye.HasValue && Behind != null)
            {
                plumeLine[0] = space != null ? space.InverseTransformPoint(lip) : lip;
                lineCount = 1 + plume.Middles(plumeLine, 1);
                eyeIn = space != null ? space.InverseTransformPoint(eye.Value) : eye.Value;
                for (var k = 0; k < lineCount; k++)
                {
                    var a = plumeLine[k] - eyeIn;
                    var u = k + 1 < lineCount ? plumeLine[k + 1] - plumeLine[k] : Vector3.zero;
                    lineFrom[k] = a;
                    lineStep[k] = u;
                    lineFromSq[k] = a.sqrMagnitude;
                    lineFromStep[k] = Vector3.Dot(a, u);
                    lineStepSq[k] = u.sqrMagnitude;
                }
            }
            pass++;
            // Fewer from every source as the engine's parcels near their budget.
            var load = count / (Budget * Mathf.Clamp(share, 0.25f, 1f));
            var throttle = Mathf.Lerp(1f, 0.25f, Mathf.InverseLerp(0.6f, 1f, load));
            var mixing = Mathf.Max(0f, look.VentMixing);
            var past = wind - carVelocity;
            var airSpeed = new Vector2(wind.x, wind.z).magnitude;
            foreach (var source in sources)
            {
                if (source.Anchor == null)
                    continue;
                var strength = StrengthOf(source) * Mathf.Max(0f, look.VentStrength);
                source.Now = strength;
                if (!(strength > 0.01f))
                {
                    source.Due = 0f;
                    continue;
                }
                var early = Mathf.Clamp(Mathf.RoundToInt(Mathf.Log(Mathf.Max(1f, source.Spec.Speed / SlowJet), 2f)), 0, EarlyThinning.Length);
                var cloud = CloudFor(air, source.Spec.Wetness);
                // Steam that makes no white at all in this air is not drawn.
                if (!(cloud.Thickness > 0f))
                {
                    source.Due = 0f;
                    continue;
                }
                var rate = source.Spec.Rate * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(strength)) * Mathf.Clamp(share, 0.25f, 1f) * (1 << early) * throttle;
                var mass = source.Spec.Flow * strength / rate;
                // Less steam through the same opening at the same speed: a
                // narrower jet.
                var bore = source.Spec.Bore * Mathf.Sqrt(Mathf.Clamp01(strength));
                source.Due += rate * dt;
                var emitted = (int)source.Due;
                source.Due -= emitted;
                for (var i = 0; i < emitted && count < MostParcels; i++)
                    Emit(source, mass, bore, cloud, past, carVelocity, dt * (i + UnityEngine.Random.value) / emitted, dt, space, strength, early);
            }

            var ground = car != null ? car.transform.position.y - GroundBelow : float.NegativeInfinity;
            roofed = 0;
            nearestRoof = float.PositiveInfinity;
            nearestWall = float.PositiveInfinity;
            var kept = 0;
            for (var i = 0; i < count; i++)
            {
                // In its place, and drawn straight into the buffer: copied
                // out and back, each parcel and its particle cost as much
                // again as its working out.
                ref var parcel = ref parcels[i];
                if (!Advance(ref parcel, dt, wind, carVelocity, airSpeed, air, mixing, look, space, ground, roof, out buffer[kept]))
                    continue;
                // Before the plume or after it: placed as it leaves, then
                // looked at again every eighth pass.
                if (lineCount == 0)
                    parcel.Behind = parcel.Placed = false;
                else if (!parcel.Placed || ((parcel.Seed + (uint)pass) & 7u) == 0u)
                {
                    parcel.Behind = BehindPlume(parcel.Position, eyeIn, lineCount, parcel.Placed && parcel.Behind);
                    parcel.Placed = true;
                }
                if (kept != i)
                    parcels[kept] = parcel;
                kept++;
            }
            count = kept;
            Count = kept;
            if (frontBuffer.Length < buffer.Length)
            {
                frontBuffer = new ParticleSystem.Particle[buffer.Length];
                behindBuffer = new ParticleSystem.Particle[buffer.Length];
            }
            shapeData.Clear();
            extraData.Clear();
            behindShape.Clear();
            behindExtra.Clear();
            var front = 0;
            var behind = 0;
            for (var i = 0; i < kept; i++)
            {
                ref var parcel = ref parcels[i];
                ref var particle = ref buffer[i];
                var shapes = parcel.Behind ? behindShape : shapeData;
                var extras = parcel.Behind ? behindExtra : extraData;
                if (parcel.Behind)
                    behindBuffer[behind++] = particle;
                else
                    frontBuffer[front++] = particle;
                var age = parcel.Age;
                // The rolling steam's own data (ChimneyPlume.Carry): its wisp
                // and turn, how hard it left, and how far to a wisp it is;
                // then how thick the steam is here, its edge's fraying, how
                // far it has rolled, and which puff it is.
                // (By the particle's seed and age, as the plume's own: PlumeAloft.)
                var seed = particle.randomSeed;
                shapes.Add(new Vector4(PlumeAloft.WispRow(seed) + 0.49f * PlumeAloft.WispPhase(seed),
                    PlumeAloft.WispTurn(seed, particle.startLifetime - particle.remainingLifetime, look.WispSpin, look.WispSpinEase),
                    parcel.Hard, Mathf.Clamp01(age / WispTime)));
                var puff = Mathf.Min(puffs - 1, Mathf.Floor(Hash(parcel.Seed ^ 0x68e31da4u) * puffs))
                           + (Hash(parcel.Seed ^ 0xb5297a4du) < 0.5f ? 0.5f : 0f);
                var roll = Hash(parcel.Seed ^ 0x1b56c4e9u)
                           + PlumeAloft.Rolled(age, look.RollRate * look.VentRoll, look.RollSlows) * (0.7f + 0.6f * Hash(parcel.Seed ^ 0x2545f491u));
                // Thick enough to be a puff while young, whatever its opacity.
                var thick = Mathf.Max(((Color)particle.startColor).a, 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PuffUntil, WispBy, age)));
                extras.Add(new Vector4(thick, look.VentEdge, roll, puff));
            }
            Drawn.SetParticles(frontBuffer, front);
            Drawn.SetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
            Drawn.SetCustomParticleData(extraData, ParticleSystemCustomData.Custom2);
            if (Behind != null)
            {
                Behind.SetParticles(behindBuffer, behind);
                Behind.SetCustomParticleData(behindShape, ParticleSystemCustomData.Custom1);
                Behind.SetCustomParticleData(behindExtra, ParticleSystemCustomData.Custom2);
            }
        }

        // Whether a parcel at <paramref name="position"/> is behind the plume
        // from the eye: farther than the plume's line is where the line of
        // sight to the parcel passes it nearest, which is where the two
        // overlap on the screen.
        private bool BehindPlume(Vector3 position, Vector3 eye, int lineCount, bool was)
        {
            var to = position - eye;
            var depth = to.magnitude;
            if (depth < 1e-3f)
                return false;
            var sight = to / depth;
            var nearest = float.MaxValue;
            var plumeDepth = 0f;
            for (var k = 0; k < lineCount; k++)
            {
                // The line's piece from this point to the next: where on it
                // the line of sight passes nearest, how far along the sight
                // that is, and how far off it (squared).
                var a = lineFrom[k];
                var u = lineStep[k];
                var ad = a.x * sight.x + a.y * sight.y + a.z * sight.z;
                var ud = u.x * sight.x + u.y * sight.y + u.z * sight.z;
                var across = lineStepSq[k] - ud * ud;
                var t = across > 1e-6f ? Mathf.Clamp01((ad * ud - lineFromStep[k]) / across) : 0f;
                var along = ad + ud * t;
                if (along <= 0f)
                    continue;
                var off = lineFromSq[k] + t * (2f * lineFromStep[k] + t * lineStepSq[k]) - along * along;
                if (off < nearest)
                {
                    nearest = off;
                    plumeDepth = along;
                }
            }
            return nearest < float.MaxValue && PlumeOrder.Behind(was, depth, plumeDepth);
        }

        private void Emit(Source source, float mass, float bore, SteamCloud cloud, Vector3 past, Vector3 carVelocity, float lead, float dt,
            Transform space, float strength, int early)
        {
            if (count >= parcels.Length)
            {
                Array.Resize(ref parcels, Mathf.Min(MostParcels, parcels.Length * 2));
                Array.Resize(ref buffer, parcels.Length);
            }
            var anchor = source.Anchor;
            var shape = source.Stock.Length > 0 && source.Stock[0] != null ? source.Stock[0].shape : default;
            // A leak's system is a ball over the boiler: anywhere in it, and
            // out from its middle.
            var scattered = source.Kind == VentKind.Leak && source.Stock.Length > 0;
            var local = scattered ? Vector3.Scale(UnityEngine.Random.insideUnitSphere * Mathf.Max(0.01f, shape.radius), shape.scale) : Vector3.zero;
            var from = anchor.TransformPoint((source.Stock.Length > 0 ? shape.position : Vector3.zero) + local);
            var axis = scattered && local.sqrMagnitude > 1e-6f ? anchor.TransformDirection(local.normalized) : anchor.forward;
            var way = (axis + UnityEngine.Random.insideUnitSphere * source.Spec.Spread).normalized;
            // Through the air: its own speed and the engine's against the wind.
            var through = way * source.Spec.Speed - past;
            var speed = through.magnitude;
            // Left that long ago in this frame, the engine that far back.
            var world = from - carVelocity * lead;
            var parcel = new Parcel
            {
                Position = space != null ? space.InverseTransformPoint(world) : world,
                Direction = speed > 1e-3f ? through / speed : way,
                Wander = UnityEngine.Random.onUnitSphere,
                Speed = speed,
                Bore = bore,
                // It is moved this frame as every parcel is: by then it has
                // been out for the part of the frame since it left.
                Age = lead - dt,
                Mass = mass,
                Early = early,
                Share = 1f - (1f - SlowestShare) * UnityEngine.Random.value * UnityEngine.Random.value,
                // The clear core is the valve's own, however far it is open.
                ClearCore = source.Spec.Clear * source.Spec.Bore,
                Hard = 0.6f * Mathf.Clamp01(strength * Mathf.InverseLerp(10f, VentSpec.Sonic, source.Spec.Speed)),
                Seed = (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                Cloud = cloud,
            };
            parcels[count++] = parcel;
        }

        // A parcel a frame on, and as it is drawn; false once it is gone.
        private bool Advance(ref Parcel parcel, float dt, Vector3 wind, Vector3 carVelocity, float airSpeed, PlumeAir air, float mixing, PlumeLook look,
            Transform space, float ground, TunnelRoof roof, out ParticleSystem.Particle particle)
        {
            particle = default;
            parcel.Age += dt;
            if (parcel.Age > LongestLife)
                return false;
            // Mixed as the jet's axis is at its age; gone its own share of the way.
            var axis = VentSteam.Travel(parcel.Age, parcel.Speed, parcel.Bore);
            var travel = parcel.Share * axis;
            var step = travel - parcel.Travel;
            parcel.Travel = travel;
            var mass = parcel.Mass;
            // The steam the parcels that fade hand on: at the nozzle as
            // mass, so a jet's first meters are as thick as its steam;
            // later as breadth, so a puff keeps its spread as it coarsens
            // (as mass, a puff from one stroke shrank to a few blobs).
            var breadth = 1f;
            for (var stage = EarlyThinning.Length - parcel.Early; stage < EarlyThinning.Length; stage++)
            {
                var from = EarlyThinning[stage];
                if (parcel.Age <= from)
                    break;
                var over = Mathf.SmoothStep(0f, 1f, (parcel.Age - from) / (ThinningOver * from));
                mass *= ((parcel.Seed >> (stage + 12)) & 1u) == 0u ? 1f + over : 1f - over;
            }
            for (var stage = 0; stage < Thinning.Length; stage++)
            {
                var from = Thinning[stage];
                if (parcel.Age <= from)
                    break;
                var over = Mathf.SmoothStep(0f, 1f, (parcel.Age - from) / (ThinningOver * from));
                breadth *= ((parcel.Seed >> (stage + 3)) & 1u) == 0u ? 1f + over : 1f - over;
            }
            if (!(mass > 1e-9f) || !(breadth > 1e-3f))
                return false;
            var jet = VentSteam.JetDilution(axis, parcel.Bore) * mixing;
            var dilution = jet + parcel.Ambient;
            var size = VentSteam.Size(mass, dilution);
            var growth = VentSteam.AmbientGrowth(airSpeed, mixing);
            parcel.Ambient += VentSteam.AmbientMixing(dilution, size, growth) * dt;
            dilution = jet + parcel.Ambient;
            size = VentSteam.Size(mass, dilution);
            var liquid = parcel.Cloud.Liquid(dilution);
            var opacity = VentSteam.Opacity(mass, liquid, size, look.VentDensity) * VentSteam.Formed(travel, 1f, parcel.ClearCore * look.VentClear) * Mathf.Min(1f, breadth);
            if (opacity >= Faint)
                parcel.Shown = true;
            else if (parcel.Shown ? opacity < 0.5f * Faint
                         : dilution > NeverShown || dilution > Mathf.Max(LeastMixed, PastThickest * parcel.Cloud.ThickestDilution))
                return false;

            // Under a tunnel's roof the air is the bore's: along it only, and
            // pushed along by a train in it.
            var under = Roofed(roof, parcel.Position, out var tunnel);
            if (under)
            {
                var along = tunnel.Along * Vector3.Dot(wind, tunnel.Along);
                if (roof.OverStack.HasValue)
                    along += (tunnel.Along * Vector3.Dot(carVelocity, tunnel.Along) - along) * TunnelSmoke.PistonShare;
                wind = along;
            }

            var rise = VentSteam.Rise(VentSteam.Excess(air.Temperature, dilution, liquid), air.Temperature, size) * look.VentRise;
            var move = parcel.Direction * step + (wind + Vector3.up * rise + parcel.Wander * (0.5f * growth)) * dt;
            var world = (space != null ? space.TransformPoint(parcel.Position) : parcel.Position) + move;
            // Along the ground, not through it.
            var floor = ground + GroundClearance * size;
            if (world.y < floor)
            {
                world.y = floor;
                if (parcel.Direction.y < 0f)
                {
                    var level = new Vector3(parcel.Direction.x, 0f, parcel.Direction.z);
                    parcel.Direction = level.sqrMagnitude > 1e-6f ? level.normalized : parcel.Direction;
                }
            }
            parcel.Position = space != null ? space.InverseTransformPoint(world) : world;
            var drawn = size * Mathf.Pow(breadth, BreadthWidth) * look.VentFill / VisibleShare;
            if (under)
            {
                // Up to the roof and no higher, then along it; between the
                // walls, a jet that meets one turned along the bore.
                var cap = TunnelSmoke.Cap(tunnel.Point.y, size, float.NegativeInfinity);
                if (parcel.Position.y > cap)
                {
                    parcel.Position.y = cap;
                    if (parcel.Direction.y > 0f)
                        parcel.Direction = Turned(parcel.Direction, Vector3.up, tunnel.Along, parcel.Seed);
                }
                var side = Vector3.Dot(parcel.Position - tunnel.Point, tunnel.Across);
                var reach = TunnelSmoke.WallReach(size);
                if (Mathf.Abs(side) > reach)
                {
                    var wall = Mathf.Sign(side);
                    parcel.Position -= tunnel.Across * (side - wall * reach);
                    if (wall * Vector3.Dot(parcel.Direction, tunnel.Across) > 0f)
                        parcel.Direction = Turned(parcel.Direction, tunnel.Across, tunnel.Along, parcel.Seed);
                }
                drawn = Mathf.Min(drawn, TunnelSmoke.WidestParticle);
                roofed++;
                nearestRoof = Mathf.Min(nearestRoof, tunnel.Point.y - parcel.Position.y);
                nearestWall = Mathf.Min(nearestWall, TunnelSmoke.HalfWidth - Mathf.Abs(Vector3.Dot(parcel.Position - tunnel.Point, tunnel.Across)));
            }

            var color = White;
            color.a = opacity;
            particle.position = parcel.Position;
            particle.startSize = drawn;
            particle.startColor = color;
            particle.randomSeed = parcel.Seed;
            particle.startLifetime = 2f * LongestLife;
            particle.remainingLifetime = 2f * LongestLife - parcel.Age;
            particle.velocity = Vector3.zero;
            return true;
        }

        // The roof over a parcel: its own spot's, or the stack's for one
        // beside the engine where no sample was left.
        private static bool Roofed(TunnelRoof roof, Vector3 position, out TunnelRoof.Sample over)
        {
            over = default;
            if (roof == null)
                return false;
            if (roof.TryFind(position, out over))
                return true;
            if (!(roof.OverStack is TunnelRoof.Sample stack))
                return false;
            var offset = position - stack.Point;
            if (Mathf.Abs(Vector3.Dot(offset, stack.Along)) > RoofAlongEngine || Mathf.Abs(Vector3.Dot(offset, stack.Across)) > TunnelSmoke.HalfWidth + 3f)
                return false;
            over = stack;
            return true;
        }

        // A jet's way once it meets the roof or a wall: what is left of it
        // off that face, or along the bore, either way by its seed, when it
        // met the face head on.
        private static Vector3 Turned(Vector3 way, Vector3 face, Vector3 along, uint seed)
        {
            var left = way - face * Vector3.Dot(way, face);
            if (left.sqrMagnitude < 0.04f)
                left = along * ((seed & 0x100000u) == 0u ? 1f : -1f);
            return left.normalized;
        }

        private static float Hash(uint x)
        {
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (x & 0xffffff) / (float)0x1000000;
        }

        // Under a roof in the last frame: how many parcels, and how near the
        // nearest's middle came to the roof and to a wall, m.
        private int roofed;
        private float nearestRoof;
        private float nearestWall;

        /// <summary>The sources and what they are doing, for the test bed's readout and the log.</summary>
        public string Readout() =>
            $"vents: {Count} parcels, {(Behind != null ? Behind.particleCount : 0)} drawn behind the plume; " + string.Join(", ", sources.Where(s => s.Now > 0.01f).Select(s => $"{s.Kind} {s.Now:F2}"))
            + (roofed > 0 ? System.FormattableString.Invariant($"; under a roof {roofed}, the nearest {nearestRoof:F2} m under it and {nearestWall:F2} m off a wall") : "");

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
