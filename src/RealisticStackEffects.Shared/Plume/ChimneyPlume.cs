using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    /// <summary>
    /// Drives an engine's chimney smoke, read from its fire
    /// (<see cref="IStackFire"/>: a realistic firebox or oil burner), in place
    /// of the stock <see cref="SteamSmokeParticlePortReader"/>: a puff per exhaust beat,
    /// counted from the crank angle and thrown up by the exhaust's force, a
    /// steady plume between beats, and sparks on hard beats, all colored from
    /// the fire model per particle. A puff leaves as a narrow column; drag
    /// spends its speed within the tuned jet length and, as it slows, it
    /// swells into a cloud carried up by the stack's heat as the plume
    /// (<see cref="PlumeAloft"/>), without a ceiling but a tunnel's roof
    /// (<see cref="TunnelRoof"/>), which holds it, the jet, and the cinders
    /// under it. The mod applies the
    /// drag and lift to every smoke particle itself each frame, because Unity's
    /// own drag module did not hold the smoke back in game; the stock systems'
    /// gravity and velocity modules are switched off so nothing else moves it.
    /// The shape follows the smoke tuning settings live.
    /// </summary>
    internal sealed class ChimneyPlume : MonoBehaviour
    {
        // A beat's particles leave within this angle of the stack's axis; haze wanders more.
        private const float PuffSpread = 0.06f;
        private const float HazeSpread = 0.25f;

        // The shimmer shows past this strength, leaving at least this fast,
        // in m/s (its rate and width are the look's).
        private const float MinShimmer = 0.02f;
        private const float ShimmerSpeed = 2f;

        // Jet streaks start at this share of the puff's spread-out size.
        private const float JetStreakSize = 0.3f;

        // A cinder glows for a couple of seconds while it arcs up and falls back.
        private const float SparkMinLife = 1.5f;
        private const float SparkMaxLife = 3f;

        // Cinders fan out more than the smoke and vary in size and speed.
        private const float SparkSpread = 0.25f;
        private const float SparkMinSize = 0.05f;
        private const float SparkMaxSize = 0.09f;

        // Fines a stoker loses up the stack, as visible cinders per kg, at the
        // default Sparks setting (0.25); they wait for the next beat to carry
        // them out, up to this much, in kg.
        private const float StokerCindersPerKg = 60f;
        private const float DefaultSparkSetting = 0.25f;
        private const float MaxStokerCinderBacklog = 0.05f;

        // A grate shaken while the engine works hard throws live coal up the
        // stack (CoalStack): its cinders go out with the next few beats, up
        // to this much a beat and this much waiting, in kg.
        private const float ShakeCindersPerBeat = 0.08f;
        private const float MaxShakeCinderBacklog = 0.5f;

        // A melted fusible plug's steam finds its way through the tubes and
        // out of the stack, pushed by its own pressure: this many puffs a
        // second per kg/s blowing, white, rising at this speed, in m/s.
        private const float PlugSteamPerKg = 15f;
        private const float PlugSteamSpeed = 3f;
        private static readonly Color PlugSteamColor = new Color(0.92f, 0.92f, 0.93f, 0.8f);

        // A beat's particles leave at a spread of speeds, so they stretch into a column.
        private const float PuffSlowest = 0.45f;
        private const float PuffFastest = 1.1f;

        // A beat as a cluster (Look.BeatCluster): its lobes' spread and least
        // speed, and the core's and lobes' sizes against a beat's particle.
        private const float ClusterSpread = 0.3f;
        private const float ClusterSlowest = 0.85f;
        private const float ClusterCore = 1.4f;
        private const float ClusterLobe = 0.75f;

        // Puffs at speed (up to ~15 beats/s, 8 particles each) and heavy
        // beats (up to 30 particles) need more room than the stock cap of
        // 300; the plume above keeps its particles while they show, fewer as
        // it rises (PlumeRise.Survival).
        private const int MinMaxParticles = 4000;

        // Within the narrow-jet zone above the game's smoke emitter (inside the
        // stack, however deep its emitter sits) smoke moves with the stack,
        // rising at least this fast, m/s.
        private const float MinZoneRise = 1f;

        // Smoke this far from the stack's axis, m, has left it, however low
        // it lies (the stack's outside radius is at most about half a metre).
        private const float ZoneReach = 1.5f;

        private const float VisibleShare = 0.6f;

        // A stack the mod can't read: its bore's and outside radius, m.
        private const float DefaultBore = 0.25f;
        private const float DefaultOutside = 0.32f;

        // Its rim: the model's points within this of the lip's height, m; a
        // bore narrower than this share of the outside is a capped model's.
        private const float RimBand = 0.12f;
        private const float LeastBoreShare = 0.3f;
        private const float BoreOfOutside = 0.8f;

        // The zone ends at the stack's lip where the mod can find it: the
        // highest point of the loco's model within this radius of the stack's
        // axis and this far above the emitter, plus a margin, in m. Beyond
        // the lip smoke held to the stack showed as a thin column of its own,
        // with the plume swept back behind it.
        private const float StackRadius = 0.5f;
        private const float MaxStackRise = 3f;
        private const float LipMargin = 0.05f;

        // Stack lips above the smoke emitter, in m, for locos whose stacks the
        // mod cannot read in game, measured offline from their models
        // (2026-10-09). The S484-840's stack piece ('smoke box.002' in its CCL
        // bundle) rises 0.37 m above both its emitters; the smallest piece
        // around its stack, which the mod once took instead, only 0.02 m.
        private static readonly Dictionary<string, float> MeasuredStackLips = new Dictionary<string, float>
        {
            ["S484-840A"] = 0.37f,
        };

        // The custom effects' prefab in the effects bundle, and the name its copy takes.
        private const string PlumeEffectsName = "ChimneyPlumeFX";

        // A frame that crosses more beats than this is a hitch, not a fast engine.
        private const int MaxBeatsPerFrame = 3;

        // Faster than any train: the floating origin moved the world.
        private const float MaxPlausibleSpeed = 80f;

        private SteamSmokeParticlePortReader stock;
        // The fire and draft the smoke comes from, and what it sent up this frame.
        private IStackFire fire;
        private StackReading fireNow;
        private TrainCar car;
        private float stackHeat;

        // The plume on each car, for the oil burner's status.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, ChimneyPlume> OnCar =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, ChimneyPlume>();

        /// <summary>The highest of the plume last frame, m above the stack.</summary>
        public float PlumeTop => plumeTop;

        public static bool TryGet(TrainCar car, out ChimneyPlume plume)
        {
            plume = null;
            return car != null && OnCar.TryGetValue(car, out plume) && plume != null && plume.Active;
        }
        private ExhaustBeatCounter beats;
        private Port crankRotation;
        private Port exhaustPressure;
        private Port totalFlow;
        private SteamExhaust exhaustSim;
        private Port boilerBroken;
        private ParticleSystem smoke;
        private ParticleSystem sootCore;
        private ParticleSystem jet;
        // The cloned effects belong to this driver, including when only the
        // component is removed while the stock reader's object stays alive.
        private GameObject effectsRoot;

        // The plume lit as one volume (PlumeVolume), on its materials.
        private PlumeVolume volume;
        private Material[] volumeMaterials = new Material[0];
        private ParticleSystem heat;
        private ParticleSystem[] embers;
        private float hazeDue;
        private float heatDue;
        private float plugSteamDue;
        // The fractions of particles left over at a lower smoke quality
        // (SmokeDetail.Scaled): a puff's, its sooty core's, and its jet's.
        private float puffParticlesDue;
        private float sootParticlesDue;
        private float jetStreaksDue;
        private float stokerCinders;
        private float shakeCinders;
        private Vector3 lastPosition;
        private Vector3 stackVelocity;
        private bool hasLastPosition;
        private int beatCount;
        private float statusTime;
        // The model's tuning and the plume's look, shared by every plume and
        // set live by the test bed (PlumeTestBed).
        private static PlumeTuning tuning => PlumeTestBed.Tuning;
        private static PlumeLook Look => PlumeTestBed.Look;

        // The share of the plume's particles thrown at the player's smoke quality.
        private static float ParticleShare => SmokeDetail.ParticleShare(StackHost.Settings.SmokeQuality, PlumeOffscreen.Drawing);
        private int lookVersion = -1;

        // The daylight the look was last applied for (PlumeWeather.Daylight).
        private float lookDaylight = -1f;
        private const float DaylightStep = 0.02f;

        // The stack's bore and outside radius at its lip, m.
        private float boreRadius = DefaultBore;
        private float outsideRadius = DefaultOutside;

        // This frame's air past the stack (the wind, less a test's train
        // speed), the stack's buoyancy, and the white its steam, the fire's
        // own water, and a melted plug's steam make in the air.
        private Vector3 airVelocity;
        private float stackFlux;
        private SteamCloud steamCloud = SteamCloud.Reference;
        // How white the beats' steam shows, its cloud's or its core's (SteamCloud.ForBeats).
        private float steamWhite = 1f;

        // The steady stream's between beats, the blower's mostly.
        private SteamCloud streamCloud = SteamCloud.Reference;
        private SteamCloud waterCloud = SteamCloud.Reference;
        private SteamCloud plugCloud = SteamCloud.Reference;
        private float flueGas = 2f;
        private float wetness;
        private float shimmerNow;
        private float steamFlowNow;
        private StackTest testing;
        private float testBeats;

        // The flue gas and the exhaust's wetness settle over this long, s,
        // so a cloud is not worked out afresh every frame.
        private const float SteamSettling = 2f;

        // The flue gas a kg of steam at most, kg (a standing fire with no steam).
        private const float MostFlueGas = 30f;

        // Every plume, to find the one nearest the camera for the test bed.
        private static readonly List<ChimneyPlume> All = new List<ChimneyPlume>();

        /// <summary>Every plume.</summary>
        internal static IReadOnlyList<ChimneyPlume> Plumes => All;

        // Drawn with the custom effects (the mod's shader), not the game's systems.
        private bool custom;

        /// <summary>
        /// Adds the systems drawn at half resolution at a lower smoke quality
        /// (<see cref="PlumeOffscreen"/>), in the order they are drawn
        /// (<see cref="DrawnBefore"/>): the custom effects' cloud, its sooty
        /// core, and the jet's streaks.
        /// </summary>
        internal void AddOffscreen(List<ParticleSystemRenderer> into)
        {
            if (!custom || !Active)
                return;
            AddRenderer(smoke, into);
            AddRenderer(sootCore, into);
            AddRenderer(jet, into);
        }

        private static void AddRenderer(ParticleSystem system, List<ParticleSystemRenderer> into)
        {
            var renderer = system != null ? system.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer != null)
                into.Add(renderer);
        }

        private ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[0];
        private float? stackLip;

        // Each smoke particle's intended size by its random seed (stable for
        // its life), because its start size is capped near the stack mouth.
        private readonly Dictionary<ParticleSystem, SeedMemory<float>> intendedSizes = new Dictionary<ParticleSystem, SeedMemory<float>>();

        // The cloud and the sooty core, carried as the plume once above the stack.
        private readonly Dictionary<ParticleSystem, PlumeAloft> aloft = new Dictionary<ParticleSystem, PlumeAloft>();
        private float plumeTop;

        // The tunnel roof along the engine's path, in the cloud's space.
        private readonly TunnelRoof roof = new TunnelRoof();

        // A particle this close under the roof is at it, m; a cinder that
        // strikes it keeps this share of its speed back down.
        private const float RoofBand = 0.2f;
        private const float CinderBounce = 0.3f;

        // The jet's streaks are drawn along their velocity, 0.06 s of it plus
        // 1.5 times their size, which reached past the stack's lip: a streak
        // just above it trailed back down through the stack's wall. Each
        // streak is drawn instead along the line back to the stack's mouth,
        // no longer than that line; the true velocity it moves by is kept
        // here by the particle's seed.
        private readonly SeedMemory<Vector3> jetVelocities = new SeedMemory<Vector3>();

        // Each jet streak's puff, so its white forms with its age as the
        // cloud's beside it does.
        private struct JetParcel
        {
            public PlumeParcel Thickness;
            public Color SmokeColor;
        }

        private readonly SeedMemory<JetParcel> jetParcels = new SeedMemory<JetParcel>();

        // Each shimmer particle's strength as it left the stack.
        private readonly SeedMemory<float> heatStrengths = new SeedMemory<float>();

        private readonly Dictionary<ParticleSystem, SeedMemory<float>> beatsHard = new Dictionary<ParticleSystem, SeedMemory<float>>();
        private float beatHard;
        private float jetVelocityScale;
        private float jetLengthScale;

        /// <summary>True once the plume has taken over from the stock smoke reader.</summary>
        public bool Active { get; private set; }

        public Puff LastPuff { get; private set; }

        /// <summary>
        /// Takes over <paramref name="reader"/>'s smoke on a loco, its smoke
        /// read from <paramref name="fire"/>. Returns a sentence for the log:
        /// what was set up, or why the stock smoke stays.
        /// </summary>
        public static string Attach(SteamSmokeParticlePortReader reader, SimulationFlow flow, TrainCar car, IStackFire fire)
        {
            // One plume to a stack: Realistic Firing and Realistic Stack
            // Effects each carry this one, and the first to take a stack keeps it.
            foreach (var other in reader.GetComponents<MonoBehaviour>())
            {
                if (other != null && other.GetType().Name == nameof(ChimneyPlume) && other.GetType() != typeof(ChimneyPlume))
                    return $"{other.GetType().Assembly.GetName().Name}'s plume already drives this stack";
            }

            var graph = car.SimController != null ? car.SimController.connectionsDefinition : null;
            if (graph == null || graph.executionOrder == null)
                return "no sim graph; keeping the stock smoke";

            var engineId = ComponentOf(reader.chuffEventPortId);
            var engine = graph.executionOrder.OfType<ReciprocatingSteamEngineDefinition>().FirstOrDefault(d => d.ID == engineId);
            var exhaust = graph.executionOrder.OfType<SteamExhaustDefinition>().FirstOrDefault();
            if (engine == null || exhaust == null)
                return $"no steam engine '{engineId}' or exhaust in the graph; keeping the stock smoke";

            if (!flow.TryGetPort($"{engineId}.{engine.crankRotationReadOut.ID}", out var crank)
                || !flow.TryGetPort(reader.exhaustPressurePortId, out var pressure)
                || !flow.TryGetPort($"{exhaust.ID}.{exhaust.totalFlowNormalizedReadOut.ID}", out var total)
                || !flow.TryGetPort(reader.isBoilerBrokenPortId, out var broken))
                return "a smoke input port is missing; keeping the stock smoke";

            // Cab refreshes call the same stock reader's Init again. Rebind its
            // ports and fire, but keep its particles, materials and driver:
            // another clone would leave the previous plume emitting as well.
            var plume = reader.GetComponent<ChimneyPlume>();
            if (plume != null)
            {
                if (!plume.Active || plume.smoke == null)
                {
                    plume.enabled = false;
                    plume.Release();
                    Destroy(plume);
                    return "incomplete plume retired; keeping the stock smoke until the next initialization";
                }
                Bind(plume);
                plume.stock.StopAllCoroutines();
                // A cab refresh can replace its steam renderers too. Restore
                // their old sorting before gathering the current set once.
                plume.AlignSteam(false);
                plume.steamSorting.Clear();
                plume.MatchSorting(car);
                plume.ShimmerFirst();
                plume.enabled = true;
                return "realistic chimney smoke rebound to the existing plume";
            }

            var systems = reader.smokeParticlesParent != null
                ? reader.smokeParticlesParent.GetComponentsInChildren<ParticleSystem>(true)
                : new ParticleSystem[0];
            if (systems.Length == 0)
                return "no smoke particle systems; keeping the stock smoke";

            plume = reader.gameObject.AddComponent<ChimneyPlume>();
            Bind(plume);
            All.Add(plume);
            var custom = StackHost.Settings.CustomEffects && StackHost.Assets.PlumePrefab != null && plume.UseCustomEffect(systems);
            plume.custom = custom;
            if (!custom)
            {
                // The largest stock system carries the plume; a second one, if any, the sooty core.
                plume.smoke = systems.OrderByDescending(s => s.main.maxParticles).First();
                plume.sootCore = systems.FirstOrDefault(s => s != plume.smoke);
            }
            plume.embers = plume.ChooseSparks(reader, custom);
            // The stock systems take the origin-shift space only once awake.
            if (!custom)
            {
                foreach (var system in new[] { plume.smoke, plume.sootCore }.Concat(plume.embers))
                    WorldParticles.FollowOriginShifts(system);
            }
            plume.TakeOver();
            var sorting = plume.MatchSorting(car);
            plume.ShimmerFirst();
            plume.stackLip = MeasureStackLip(car, Emitter(plume.smoke), plume.smoke.transform.up, out var lip, out plume.boreRadius, out plume.outsideRadius);
            lip += $"; aloft: {string.Join("; ", plume.aloft.Values.Select(a => a.Setup))}";
            return $"realistic chimney smoke {(custom ? "with the custom effects" : "on the game's systems")}: " +
                   $"cloud '{plume.smoke.name}'" +
                   (plume.sootCore != null ? $", soot '{plume.sootCore.name}'" : "") +
                   (plume.jet != null ? $", jet '{plume.jet.name}'" : "") +
                   $", sparks {string.Join("/", plume.embers.Select(s => $"'{s.name}'"))}" +
                   $", {2 * engine.numCylinders} beats per revolution from '{engineId}', flow from '{exhaust.ID}'; {sorting}; {lip}; " +
                   $"simulated in {(plume.smoke.main.simulationSpace == ParticleSystemSimulationSpace.Custom && plume.smoke.main.customSimulationSpace != null ? $"'{plume.smoke.main.customSimulationSpace.name}'" : plume.smoke.main.simulationSpace.ToString())}";

            void Bind(ChimneyPlume target)
            {
                if (target.car != null && target.car != car && OnCar.TryGetValue(target.car, out var previous) && previous == target)
                    OnCar.Remove(target.car);
                target.stock = reader;
                target.car = car;
                target.BindInputs(fire, 2 * engine.numCylinders, crank, pressure, total,
                    flow.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault(c => c.id == exhaust.ID), broken);
                OnCar.Remove(car);
                OnCar.Add(car, target);
            }
        }

        // Refreshing a sim graph replaces its ports and fire source, not the
        // plume's effects or material ownership. A new beat phase and position
        // sample avoid a false chuff or velocity jump across that change.
        private void BindInputs(IStackFire source, int beatsPerRevolution, Port crank, Port pressure, Port total,
            SteamExhaust exhaust, Port broken)
        {
            fire = source;
            beats = new ExhaustBeatCounter(beatsPerRevolution);
            crankRotation = crank;
            exhaustPressure = pressure;
            totalFlow = total;
            exhaustSim = exhaust;
            boilerBroken = broken;
            hasLastPosition = false;
        }

        // The stack's lip above the game's smoke emitter, along the stack's
        // axis: the highest point of the loco's model near the axis where its
        // meshes can be read, or else the lip measured offline for its livery.
        // Null when neither is known. Its rim gives the stack's outside radius
        // (the farthest of its points from the axis) and bore (the nearest,
        // unless the model is capped there); else the defaults.
        private static float? MeasureStackLip(TrainCar car, Vector3 emitter, Vector3 axis, out string how, out float bore, out float outside)
        {
            float? fromMesh = null;
            var readable = 0;
            var unreadable = 0;
            bore = DefaultBore;
            outside = DefaultOutside;
            var near = new List<Vector2>();
            foreach (var filter in car.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                var renderer = filter.GetComponent<MeshRenderer>();
                if (mesh == null || renderer == null)
                    continue;
                var bounds = renderer.bounds;
                if (bounds.size == Vector3.zero || bounds.max.y <= emitter.y)
                    continue;
                var nearest = bounds.ClosestPoint(new Vector3(emitter.x, bounds.center.y, emitter.z));
                if (new Vector2(nearest.x - emitter.x, nearest.z - emitter.z).sqrMagnitude > StackRadius * StackRadius)
                    continue;
                if (!mesh.isReadable)
                {
                    unreadable++;
                    continue;
                }
                readable++;
                var toWorld = filter.transform.localToWorldMatrix;
                foreach (var vertex in mesh.vertices)
                {
                    var offset = toWorld.MultiplyPoint3x4(vertex) - emitter;
                    var rise = Vector3.Dot(offset, axis);
                    var across = (offset - axis * rise).magnitude;
                    if (rise > 0f && rise <= MaxStackRise && across <= StackRadius)
                    {
                        near.Add(new Vector2(rise, across));
                        if (!fromMesh.HasValue || rise > fromMesh.Value)
                            fromMesh = rise;
                    }
                }
            }

            if (fromMesh.HasValue)
            {
                var rim = near.Where(p => p.x >= fromMesh.Value - RimBand).Select(p => p.y).ToList();
                var widest = rim.Count > 0 ? rim.Max() : 0f;
                var nearest = rim.Count > 0 ? rim.Min() : 0f;
                var capped = !(nearest >= LeastBoreShare * widest);
                if (widest > 0.05f)
                {
                    outside = Mathf.Clamp(widest, 0.1f, StackRadius);
                    bore = Mathf.Clamp(capped ? BoreOfOutside * outside : nearest, 0.08f, outside);
                }
                how = $"stack lip {fromMesh.Value:F2} m above the smoke emitter, from {readable} readable meshes around the stack; " +
                      $"its rim {rim.Count} points, outside radius {outside:F2} m, bore {bore:F2} m{(capped ? " (capped model: a share of the outside)" : "")}";
                return fromMesh;
            }
            var livery = car.carLivery != null ? car.carLivery.id : null;
            if (livery != null && MeasuredStackLips.TryGetValue(livery, out var measured))
            {
                how = $"stack lip {measured:F2} m above the smoke emitter, as measured offline for {livery} " +
                      $"({unreadable} meshes around the stack can't be read in game)";
                return measured;
            }
            how = unreadable > 0
                ? $"the stack can't be read in game ({unreadable} meshes around it); the narrow-jet zone is the setting's"
                : "no stack found around the smoke emitter; the narrow-jet zone is the setting's";
            return null;
        }

        // The loco's own steam (dynamo, whistle, safety valves, leaks) and the
        // plume must share a render queue, sorting layer, order, and fudge, or
        // one always draws over the other whichever is nearer; matched, Unity
        // sorts them by distance. Takes the commonest of the stock settings.
        private string MatchSorting(TrainCar car)
        {
            var ours = Systems().Select(s => s.GetComponent<ParticleSystemRenderer>()).Where(r => r != null).ToList();
            var stock = car.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Where(r => !ours.Contains(r) && r.sharedMaterial != null)
                .ToList();
            if (stock.Count == 0)
                return "no stock steam to sort against";

            T Commonest<T>(System.Func<ParticleSystemRenderer, T> key) =>
                stock.GroupBy(key).OrderByDescending(g => g.Count()).First().Key;
            var queue = Commonest(r => r.sharedMaterial.renderQueue);
            var layer = Commonest(r => r.sortingLayerID);
            var order = Commonest(r => r.sortingOrder);
            var fudge = Commonest(r => r.sortingFudge);
            var before = string.Join(", ", ours.Select(r => $"{r.name} q{r.sharedMaterial?.renderQueue} o{r.sortingOrder} f{r.sortingFudge:F0}"));
            sortingFudge = fudge;
            steamSortingOrder = order;
            // The dynamo pairs GasWave at order 4 with WispyCloudWhite at 5.
            // Matching only the plume to 5 leaves GasWave underneath it at
            // every distance. Put the stock steam in the same distance-sorted
            // group, keeping a small bias between its own component layers.
            foreach (var renderer in stock)
            {
                var name = renderer.sharedMaterial.name;
                if (renderer.sortingLayerID == layer && renderer.sharedMaterial.renderQueue == queue
                    && (name.StartsWith("GasWave", System.StringComparison.Ordinal)
                        || name.StartsWith("WispyCloudWhite", System.StringComparison.Ordinal)
                        || name.StartsWith("SmokeWhiteFull", System.StringComparison.Ordinal)))
                    steamSorting.Add((renderer, renderer.sortingOrder, renderer.sortingFudge));
            }
            foreach (var renderer in ours)
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = order;
                renderer.sortingFudge = fudge;
                if (renderer.sharedMaterial != null)
                    Own(renderer).renderQueue = queue;
            }
            var seen = string.Join(", ", stock.GroupBy(r => (r.sharedMaterial.name, r.sharedMaterial.renderQueue, r.sortingOrder, r.sortingFudge))
                .Select(g => $"{g.Key.name} q{g.Key.renderQueue} o{g.Key.sortingOrder} f{g.Key.sortingFudge:F0} ×{g.Count()}"));
            return $"sorting matched to the stock steam (queue {queue}, order {order}, fudge {fudge:F0}; was {before}; stock: {seen})";
        }

        // The stock steam's sorting fudge, matched: the plume's before its
        // distance is made the stack's.
        private float sortingFudge;
        private int steamSortingOrder;
        private readonly List<(ParticleSystemRenderer Renderer, int Order, float Fudge)> steamSorting =
            new List<(ParticleSystemRenderer, int, float)>();
        private bool? steamAligned;

        private void AlignSteam(bool align)
        {
            if (steamAligned == align)
                return;
            steamAligned = align;
            foreach (var entry in steamSorting)
            {
                if (entry.Renderer == null)
                    continue;
                entry.Renderer.sortingOrder = align ? steamSortingOrder : entry.Order;
                entry.Renderer.sortingFudge = align
                    ? sortingFudge + DrawOrderStep * (steamSortingOrder - entry.Order)
                    : entry.Fudge;
            }
        }

        private void OnEnable() => Camera.onPreCull += BeforeCull;

        private void OnDisable()
        {
            Camera.onPreCull -= BeforeCull;
            AlignSteam(false);
        }

        private void BeforeCull(Camera camera)
        {
            if (Active && camera.cameraType == CameraType.Game)
                SortAtStack(camera);
        }

        // Captures move a camera in their own pre-cull callback, after ours.
        internal static void SortForCamera(Camera camera)
        {
            foreach (var plume in All)
                if (plume != null && plume.Active && plume.isActiveAndEnabled)
                    plume.SortAtStack(camera);
            PlumeOffscreen.SortForCamera(camera);
        }

        internal string SortingReadout() =>
            $"wisp speed {wispTrainSpeed * 3.6f:F1} km/h, puff trail {Look.PuffTrail:F1} m; " +
            string.Join("; ", steamSorting.Where(e => e.Renderer != null && e.Renderer.gameObject.activeInHierarchy)
                .Select(e => $"{string.Join("/", e.Renderer.GetComponentsInParent<Transform>().Take(4).Reverse().Select(t => t.name))} o{e.Renderer.sortingOrder} f{e.Renderer.sortingFudge:F2} n{e.Renderer.GetComponent<ParticleSystem>()?.particleCount} {e.Renderer.sharedMaterial.shader.name}"));

        // The heat shimmer's sorting order and render queue: before every
        // plume's and the game's own steam's (orders 2 to 6), in any engine.
        public const int ShimmerOrder = 0;
        public const int ShimmerQueue = 3001;

        // The heat shimmer shows the scene behind it, one copy of the screen
        // a frame shared by every shimmer, and with TurboTurbo's: its grab is
        // named for where they draw, _SharedGrab_Transparent1_Order0 (geluk,
        // DM, 2026-10-29). The copy is taken when the first shimmer is drawn.
        // Drawn back to front among the smoke, a farther engine's shimmer took
        // it before a nearer plume was drawn, and the nearer shimmer laid that
        // copy, without its own plume, over it: the plume's middle cut away in
        // the shimmer's shape (0Krait0, Nexus, 2026-10-21). Drawn after all
        // the smoke (order 1000), it bent the smoke in front of it, ragging
        // the plumes' edges, and would have laid its smoke-free copy over them
        // had TurboTurbo's shimmer, at order 0, taken it first. Every shimmer
        // is drawn before all the smoke: Unity sorts a sorting order before a
        // queue, so at order 0 it goes before every plume and the stock steam,
        // and in the next queue after what else draws at order 0 (the stock
        // embers, queue 3000, are in the copy, bent with what is behind them).
        // Dense smoke in front of it hides it, as it would the real thing.
        private void ShimmerFirst()
        {
            var renderer = heat != null ? heat.GetComponent<ParticleSystemRenderer>() : null;
            if (renderer == null || renderer.sharedMaterial == null)
                return;
            renderer.sortingOrder = ShimmerOrder;
            Own(renderer).renderQueue = ShimmerQueue;
        }

        private const float DrawOrderStep = 0.05f;

        private void SortAtStack(Camera camera)
        {
            if (camera == null || smoke == null || (camera.cullingMask & (1 << smoke.gameObject.layer)) == 0)
                return;
            AlignSteam(Look.MatchSteamOrder > 0.5f);
            var eye = camera.transform.position;
            var stack = Vector3.Distance(eye, Emitter(smoke));
            foreach (var system in Systems())
            {
                if (system == null)
                    continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    renderer.sortingFudge = sortingFudge + stack - Vector3.Distance(eye, renderer.bounds.center) + DrawOrderStep * DrawnBefore(system);
            }
        }

        // How many of the plume's systems a system is drawn before (a higher
        // fudge sorts it farther, so it is drawn earlier, under the rest).
        private int DrawnBefore(ParticleSystem system) =>
            system == smoke ? 4 : system == sootCore ? 3 : system == jet ? 2 : system == heat ? 0 : 1;

        // Puts the custom plume where the stock smoke leaves the stack, in the
        // game's world-origin-shift space, as the stock smoke has once awake,
        // and switches the stock smoke systems off.
        private bool UseCustomEffect(ParticleSystem[] stockSystems)
        {
            var stockSmoke = stockSystems.OrderByDescending(s => s.main.maxParticles).First();
            var fx = Instantiate(StackHost.Assets.PlumePrefab, stockSmoke.transform.parent, false);
            effectsRoot = fx;
            fx.name = PlumeEffectsName;
            fx.transform.localPosition = stockSmoke.transform.localPosition;
            fx.transform.localRotation = stockSmoke.transform.localRotation;

            // Leave from exactly where the stock smoke leaves: the same emitter
            // shape, offset, and turn as well as the same simulation space.
            var stockMain = stockSmoke.main;
            var stockShape = stockSmoke.shape;
            foreach (var system in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                // The stock smoke only takes the world's origin-shift space
                // when it wakes, which can be after this: set it directly, or
                // the smoke is left behind at each origin shift.
                if (!WorldParticles.FollowOriginShifts(system))
                {
                    var main = system.main;
                    main.simulationSpace = stockMain.simulationSpace;
                    if (stockMain.simulationSpace == ParticleSystemSimulationSpace.Custom)
                        main.customSimulationSpace = stockMain.customSimulationSpace;
                }
                var shape = system.shape;
                shape.shapeType = stockShape.shapeType;
                shape.angle = stockShape.angle;
                shape.radius = stockShape.radius;
                shape.position = stockShape.position;
                shape.rotation = stockShape.rotation;
                shape.scale = stockShape.scale;
            }

            smoke = Child(fx, "Cloud");
            sootCore = Child(fx, "Soot");
            jet = Child(fx, "Jet");
            heat = Child(fx, "Heat");
            if (heat != null && !StackHost.Settings.HeatShimmer)
            {
                heat.gameObject.SetActive(false);
                heat = null;
            }

            // The jet's streaks are drawn along each particle's velocity. In
            // the world's space that includes the train's speed, so at speed
            // they lay down level and reached out of the stack's back wall
            // from inside it. In the stack's own space they lie along the
            // jet's path out of the stack, as the gas leaving a moving stack
            // does; they live half a second, so the train's turning in that
            // time does not matter.
            if (jet != null)
            {
                var jetMain = jet.main;
                jetMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            }
            if (smoke == null)
            {
                Destroy(fx);
                effectsRoot = null;
                return false;
            }
            foreach (var system in stockSystems)
            {
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.gameObject.SetActive(false);
            }
            return true;
        }

        // The custom effects' own spark system, with the stock ones switched
        // off; otherwise the stock single sparks. The stock "EmberClusters"
        // system draws a sprite of a whole clump of dots that grows over its
        // life, which read as a round cloud of cinders.
        private ParticleSystem[] ChooseSparks(SteamSmokeParticlePortReader reader, bool custom)
        {
            var stock = reader.emberParticlesParent != null
                ? reader.emberParticlesParent.GetComponentsInChildren<ParticleSystem>(true)
                : new ParticleSystem[0];
            var customSparks = custom && smoke != null ? Child(smoke.transform.parent.gameObject, "Sparks") : null;
            if (customSparks != null)
            {
                foreach (var system in stock)
                {
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    system.gameObject.SetActive(false);
                }
                return new[] { customSparks };
            }
            var singles = stock.Where(s => s.name.IndexOf("Cluster", System.StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            foreach (var system in stock.Except(singles))
                system.gameObject.SetActive(false);
            return singles.Length > 0 ? singles : stock;
        }

        private static ParticleSystem Child(GameObject root, string name) =>
            root.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(s => s.name == name);

        private static string ComponentOf(string portId)
        {
            var dot = portId != null ? portId.IndexOf('.') : -1;
            return dot > 0 ? portId.Substring(0, dot) : portId;
        }

        private void TakeOver()
        {
            // The stock reader's chuff and stop coroutines would fight over the systems.
            stock.StopAllCoroutines();
            foreach (var system in new[] { smoke, sootCore, jet, heat }.Where(s => s != null))
                Prepare(system, buoyant: true);
            foreach (var system in new[] { smoke, sootCore }.Where(s => s != null))
                aloft[system] = new PlumeAloft(system, MinMaxParticles);
            var jetRenderer = jet != null ? jet.GetComponent<ParticleSystemRenderer>() : null;
            if (jetRenderer != null && jetRenderer.renderMode == ParticleSystemRenderMode.Stretch)
            {
                jetVelocityScale = jetRenderer.velocityScale;
                jetLengthScale = jetRenderer.lengthScale;
            }
            foreach (var system in embers)
                PrepareSparks(system);
            // The plume holds a fast particle narrow itself (PlumeAloft), by its
            // speed through the air: Unity's size by speed took the train's
            // speed as well, narrowing the whole plume near a moving train.
            foreach (var system in new[] { smoke, sootCore }.Where(s => s != null))
            {
                var bySpeed = system.sizeBySpeed;
                bySpeed.enabled = false;
                // Drawn far to near. The bundle's systems drew their particles
                // unsorted, in the order Unity keeps them, which reshuffles as
                // particles die: steam and smoke clipped in and out of each
                // other (ThinBlueWarrior, Nexus, 2026-10-19).
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    renderer.sortMode = ParticleSystemSortMode.Distance;
            }
            volume = new PlumeVolume(PlumeVolume.SpaceOf(smoke));
            ApplyLook();
            Active = true;
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            Active = false;
            Camera.onPreCull -= BeforeCull;
            AlignSteam(false);
            All.Remove(this);
            if (car != null && OnCar.TryGetValue(car, out var registered) && registered == this)
                OnCar.Remove(car);
            volume?.Destroy();
            volume = null;
            foreach (var material in ownMaterials)
            {
                if (material != null)
                    Destroy(material);
            }
            ownMaterials.Clear();
            if (effectsRoot != null)
            {
                effectsRoot.SetActive(false);
                Destroy(effectsRoot);
                effectsRoot = null;
            }
        }

        // The copies of its systems' materials the plume has made: Unity
        // makes a renderer its own copy the first time it is asked for its
        // material, and lets the copy outlive it (20 of 20 kept after their
        // renderers went; performance review, 2026-10-25).
        private readonly HashSet<Material> ownMaterials = new HashSet<Material>();

        private Material Own(Renderer renderer)
        {
            var material = renderer.material;
            if (material != null)
                ownMaterials.Add(material);
            return material;
        }

        // The custom smoke shader lights the wisps' surface across each
        // particle, turned with it: it needs the particles' tangents.
        // Its part of the plume's surface (Custom1, PlumeAloft.ShapeOf) after
        // its UV, then its billows' onset and shade (Custom2, PlumeAloft.ExtraOf),
        // then its middle, about which rolling steam rebuilds its quad upright.
        private static readonly List<ParticleSystemVertexStream> SurfaceStreams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.Custom1XYZW, ParticleSystemVertexStream.Custom2XYZW,
            ParticleSystemVertexStream.Center, ParticleSystemVertexStream.Tangent,
        };
        private readonly List<Vector4> shapeData = new List<Vector4>();
        private readonly List<Vector4> extraData = new List<Vector4>();
        // The puffs the rolling steam's flipbooks hold (0: not rolling steam).
        private int rollingPuffs;
        private static readonly int PuffAgeScaleId = Shader.PropertyToID("_PuffAgeScale");
        private readonly Dictionary<ParticleSystem, Material> puffMaterials = new Dictionary<ParticleSystem, Material>();
        private float wispTrainSpeed;

        // The plume's light and fraying on its materials, and the shimmer's bend.
        private void ApplyLook()
        {
            lookVersion = PlumeTestBed.LookVersion;
            lookDaylight = PlumeWeather.Daylight;
            volumeMaterials = new[] { smoke, sootCore, jet }.Where(s => s != null)
                .Select(s => s.GetComponent<ParticleSystemRenderer>()).Where(r => r != null)
                .Select(Own).Where(m => m != null && m.HasProperty("_Volume")).ToArray();
            var skyLight = Mathf.Lerp(Look.NightSkyLight, Look.SkyLight, lookDaylight);
            var grey = Mathf.Lerp(Look.NightGrey, Look.LightGrey, lookDaylight);
            foreach (var system in new[] { smoke, sootCore, jet }.Where(s => s != null))
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                var material = renderer != null ? Own(renderer) : null;
                if (material == null || !material.HasProperty("_SkyStrength"))
                    continue;
                material.SetFloat("_SkyStrength", skyLight * Look.Light);
                material.SetFloat("_SunStrength", Look.SunLight * Look.Light);
                material.SetFloat("_MaxLight", Look.MaxLight * Look.Light);
                if (!material.HasProperty("_Surface"))
                    continue;
                material.SetFloat("_Wrap", Look.Wrap);
                material.SetFloat("_Relief", Look.Relief);
                material.SetFloat("_Bulge", Look.Bulge);
                material.SetFloat("_Shade", Look.SelfShadow);
                material.SetFloat("_Scatter", Look.Scatter);
                material.SetFloat("_BinormalSign", Look.BinormalSign);
                if (material.HasProperty("_PlumeShape"))
                    material.SetFloat("_PlumeShape", Look.PlumeShape);
                if (material.HasProperty("_Flat"))
                {
                    material.SetFloat("_Flat", Look.Flat);
                    material.SetFloat("_Terminator", Look.Terminator);
                    material.SetFloat("_ShadeLight", Look.ShadeLight);
                    material.SetFloat("_SunShadow", Look.SunShadow);
                }
                if (material.HasProperty("_VolumeShape"))
                    material.SetFloat("_VolumeShape", Look.VolumeShape);
                if (material.HasProperty("_NearFadeFrom"))
                {
                    material.SetFloat("_NearFadeFrom", Look.NearFadeFrom);
                    material.SetFloat("_NearFadeTo", Look.NearFadeTo);
                }
                // Only the cloud's material has billows (the jet's streaks have none).
                if (material.HasProperty("_Billow"))
                {
                    material.SetFloat("_Billow", material.GetTexture("_BillowTex") != null ? Look.Billow : 0f);
                    material.SetFloat("_BillowFrom", Look.BillowFrom);
                    material.SetFloat("_BillowFull", Mathf.Max(Look.BillowFrom + 0.01f, Look.BillowFull));
                }
                if (material.HasProperty("_Grey"))
                    material.SetFloat("_Grey", grey);
                if (material.HasProperty("_SixPlume"))
                {
                    material.SetFloat("_SixPlume", Look.SixPlume);
                    material.SetFloat("_ShadeSky", Look.ShadeSky);
                    material.SetFloat("_WispBelow", Look.WispBelow);
                    material.SetFloat("_WispAbove", Mathf.Max(Look.WispBelow + 0.01f, Look.WispAbove));
                    material.SetFloat("_WispAgeFrom", Look.WispAgeFrom);
                    material.SetFloat("_WispAgeTo", Mathf.Max(Look.WispAgeFrom + 0.01f, Look.WispAgeTo));
                    material.SetFloat("_WispBlend", Look.WispBlend);
                    material.SetFloat("_WispSheet", Look.WispSheet);
                    material.SetFloat("_WispCoherence", Look.WispCoherence);
                    material.SetFloat("_WispHold", Look.WispHold);
                    material.SetFloat("_WispFadeFrom", Look.WispFadeFrom);
                    material.SetFloat("_ThinWispAgeFrom", Look.ThinWispAgeFrom);
                    material.SetFloat("_ThinWispAgeTo", Mathf.Max(Look.ThinWispAgeFrom + 0.01f, Look.ThinWispAgeTo));
                    material.SetFloat("_WispBaked", Look.WispBaked);
                    material.SetFloat("_WispTurn", Look.WispTurn);
                    material.SetFloat("_WispBakedLight", Look.WispBakedLight);
                    material.SetFloat("_WispPuffLit", Look.WispPuffLit);
                    material.SetFloat("_WispPuffLight", Look.WispPuffLight);
                    material.SetFloat("_WispRelief", Look.WispRelief);
                    material.SetFloat("_WispBulge", Look.WispBulge);
                    material.SetFloat("_WispScatter", Look.WispScatter);
                    material.SetFloat("_WispSunStrength", Look.WispSunLight * Look.Light);
                    material.SetFloat("_SootAlbedo", Look.SootAlbedo);
                    material.SetFloat("_LeastOpacity", Look.LeastOpacity);
                    material.SetFloat("_Fringe", Look.Fringe);
                    material.SetFloat("_Upright", Look.Upright);
                    material.SetFloat("_Grow", Look.Grow);
                    material.SetFloat("_WispReach", Mathf.Min(Look.WispReach, Look.Grow));
                    material.SetFloat("_FringeShare", Look.FringeShare);
                    material.SetFloat("_FringeRimFrom", Look.FringeRimFrom);
                    material.SetFloat("_FringeRimTo", Mathf.Max(Look.FringeRimFrom + 0.01f, Look.FringeRimTo));
                    material.SetFloat("_FringeCore", Look.FringeCore);
                    material.SetFloat("_SixShade", Look.RollShade);
                    material.SetFloat("_ThickFloor", Look.ThickFloor);
                    material.SetFloat("_DebugAlpha", Look.DebugAlpha);
                }
                if (material.GetFloat("_Surface") > 0.5f)
                    renderer.SetActiveVertexStreams(SurfaceStreams);
            }
            // Development: the jet's streaks hidden, to see what they draw.
            var jetShown = jet != null ? jet.GetComponent<ParticleSystemRenderer>() : null;
            if (jetShown != null)
                jetShown.enabled = Look.HideJet <= 0f;
            var heatShown = heat != null ? heat.GetComponent<ParticleSystemRenderer>() : null;
            if (heatShown != null)
            {
                heatShown.enabled = Look.HideHeat <= 0f;
            }
            rollingPuffs = 0;
            foreach (var system in new[] { smoke, sootCore }.Where(s => s != null))
            {
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                // Development: hidden, to time what drawing the plume costs.
                if (renderer != null)
                    renderer.enabled = Look.HidePlume <= 0f;
                var material = renderer != null ? Own(renderer) : null;
                if (material != null && material.HasProperty("_Fray"))
                    material.SetFloat("_Fray", Look.Fray);
                if (material != null)
                    puffMaterials[system] = material;
                // Rolling steam: the puffs its flipbooks hold, a loop of frames each.
                if (material != null && material.HasProperty("_SixWay") && material.GetFloat("_SixWay") > 0.5f)
                    rollingPuffs = Mathf.Max(1, Mathf.RoundToInt(material.GetFloat("_SixColumns") * material.GetFloat("_SixRows")
                                                                 / Mathf.Max(1f, material.GetFloat("_SixFrames"))));
            }
            if (heat != null)
            {
                var heatMain = heat.main;
                heatMain.startLifetime = new ParticleSystem.MinMaxCurve(0.8f * Look.ShimmerLife, 1.2f * Look.ShimmerLife);
            }
            var heatRenderer = heat != null ? heat.GetComponent<ParticleSystemRenderer>() : null;
            var heatMaterial = heatRenderer != null ? Own(heatRenderer) : null;
            if (heatMaterial != null && heatMaterial.HasProperty("_Strength"))
                heatMaterial.SetFloat("_Strength", Look.ShimmerStrength);
        }

        // The widest a particle is drawn <paramref name="above"/> m from the
        // lip's middle (below 0, in the stack): its shown disk no wider than
        // the stack in it, and above it reaching no lower than the lip beside
        // the stack.
        private float Widest(float above) =>
            2f * (above <= 0f ? outsideRadius : Mathf.Sqrt(outsideRadius * outsideRadius + above * above)) / VisibleShare;

        private const float NeckReach = 10f;
        private const float FarSpread = 0.5f * PlumeRise.WidthPerHeight;

        private float Least(float above) => Spreading(above, Look.LipWidth);

        // The width, drawn, of what leaves the lip <paramref name="lip"/> ×
        // the bore wide and spreads as the exhaust does <paramref name="above"/> m over it.
        private float Spreading(float above, float lip)
        {
            var neck = Look.Neck * 2f * boreRadius;
            var near = Mathf.Clamp(above - neck, 0f, NeckReach);
            var far = Mathf.Max(0f, above - neck - NeckReach);
            return (lip * 2f * boreRadius + 2f * Look.NeckSpread * near + 2f * FarSpread * far) / VisibleShare;
        }

        // Cinders fly on the mod's ballistics alone: no gravity modifier, drag,
        // turbulence, or inherited velocity of their own (Emit already adds
        // the chimney's velocity; inheriting it as well doubled it).
        private static void PrepareSparks(ParticleSystem system)
        {
            system.gameObject.SetActive(true);
            var emission = system.emission;
            emission.enabled = false;
            var main = system.main;
            main.gravityModifier = 0f;
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = false;
            var noise = system.noise;
            noise.enabled = false;
            var inherit = system.inheritVelocity;
            inherit.enabled = false;
            var force = system.forceOverLifetime;
            force.enabled = false;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = false;
            system.Play();
        }

        // Everything is emitted by hand with its own color and velocity.
        private static void Prepare(ParticleSystem system, bool buoyant)
        {
            system.gameObject.SetActive(true);
            var emission = system.emission;
            emission.enabled = false;
            if (buoyant)
            {
                var main = system.main;
                main.maxParticles = Mathf.Max(main.maxParticles, MinMaxParticles);

                // The plume moves these particles itself (see Drift).
                main.gravityModifier = 0f;
                var limit = system.limitVelocityOverLifetime;
                limit.enabled = false;
                var force = system.forceOverLifetime;
                force.enabled = false;
                var velocity = system.velocityOverLifetime;
                velocity.enabled = false;
                var inherit = system.inheritVelocity;
                inherit.enabled = false;
                var external = system.externalForces;
                external.enabled = false;

                // Keep the fade in and out but drop the brown tint, so the
                // emitted color is the color seen.
                var tint = system.colorOverLifetime;
                if (tint.enabled)
                {
                    var gradient = new Gradient();
                    gradient.SetKeys(
                        new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.9f, 0.9f), 1f) },
                        tint.color.gradient != null ? tint.color.gradient.alphaKeys : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    tint.color = new ParticleSystem.MinMaxGradient(gradient);
                }
            }
            system.Play();
        }

        private void Update()
        {
            var start = Stopwatch.GetTimestamp();
            PlumeOffscreen.Decide();
            UpdatePlume();
            StackHost.PlumeTimed?.Invoke(start);
        }

        private void UpdatePlume()
        {
            if (!Active || smoke == null)
                return;
            if (lookVersion != PlumeTestBed.LookVersion || Mathf.Abs(PlumeWeather.Daylight - lookDaylight) > DaylightStep)
                ApplyLook();
            testing = PlumeTestBed.StackFor(this);

            var dt = Time.deltaTime;
            var position = smoke.transform.position;
            var carVelocity = hasLastPosition && dt > 0f ? (position - lastPosition) / dt : Vector3.zero;
            if (carVelocity.magnitude > MaxPlausibleSpeed)
                carVelocity = Vector3.zero;
            lastPosition = position;
            stackVelocity = carVelocity;
            hasLastPosition = true;
            var wispSpeed = testing != null ? Mathf.Max(0f, testing.Speed / 3.6f) : carVelocity.magnitude;
            wispTrainSpeed += (wispSpeed - wispTrainSpeed) * (1f - Mathf.Exp(-dt / 0.5f));

            var crossed = beats.Advance(crankRotation.Value);
            if (testing != null)
            {
                // The test's beats, the engine standing.
                testBeats += Mathf.Max(0f, testing.Beats) * dt;
                crossed = (int)testBeats;
                testBeats -= crossed;
            }
            if (boilerBroken.Value > 0f || dt <= 0f)
                return;
            roof.Follow(car, Emitter(smoke), smoke, PlumeAloft.Lifetime);

            // The air past the stack: the wind, and a test's train speed blowing back.
            airVelocity = PlumeWeather.Wind - (testing != null ? car.transform.forward * (testing.Speed / 3.6f) : Vector3.zero);
            fireNow = default;
            fire.Read(dt, exhaustSim, ref fireNow);
            shakeCinders = Mathf.Min(shakeCinders + fireNow.ShakenCoal, MaxShakeCinderBacklog);
            var inputs = testing != null ? TestInputs(testing) : FireInputs();
            Weather(ref inputs, dt);
            beatHard = PlumeModel.BeatStrength(tuning, inputs.ExhaustPressure);

            // A beat with no steam behind it (drifting, regulator shut) makes no puff.

            if (inputs.ExhaustPressure > PlumeModel.MinBeatPressure)
            {
                for (var i = 0; i < Mathf.Min(crossed, MaxBeatsPerFrame); i++)
                    EmitPuff(PlumeModel.ForBeat(tuning, inputs), carVelocity);
            }

            stokerCinders = Mathf.Min(stokerCinders + fireNow.Fines, MaxStokerCinderBacklog);

            var haze = PlumeModel.Between(tuning, inputs);
            // The blower's stream is steam; a clean fire's haze, its own water.
            var hazeCloud = PlumeModel.SteamThroughStack(inputs.TotalFlowNormalized) > 0.3f ? streamCloud : waterCloud;
            hazeDue += haze.Rate * ParticleShare * dt;
            for (; hazeDue >= 1f; hazeDue -= 1f)
                Emit(smoke, haze.Speed, 1.2f * tuning.PuffSize, ToColor(haze.Color), carVelocity, HazeSpread, 0.85f, 1.15f,
                    plume: Aloft(smoke), smokeShare: haze.SmokeShare, smokeColor: ToColor(haze.SmokeColor), cloud: hazeCloud);

            // The hot gas's shimmer, plainest over a clean fire.
            shimmerNow = PlumeModel.Shimmer(tuning, inputs);
            if (heat != null)
            {
                heatDue = shimmerNow > MinShimmer ? Mathf.Min(heatDue + Look.ShimmerRate * dt, MaxBeatsPerFrame) : 0f;
                var shimmerColor = new Color(1f, 1f, 1f, shimmerNow);
                var shimmerSize = Look.ShimmerSize * 2f * boreRadius / VisibleShare;
                for (; heatDue >= 1f; heatDue -= 1f)
                    Emit(heat, Mathf.Max(haze.Speed, ShimmerSpeed), shimmerSize, shimmerColor, carVelocity, HazeSpread, 0.8f, 1.2f);
            }

            var vent = fireNow.PlugSteam;
            plugSteamDue = vent > 0f ? Mathf.Min(plugSteamDue + PlugSteamPerKg * vent * ParticleShare * dt, MaxBeatsPerFrame * 4f) : 0f;
            for (; plugSteamDue >= 1f; plugSteamDue -= 1f)
                Emit(smoke, PlugSteamSpeed, 1.2f * tuning.PuffSize, PlugSteamColor, carVelocity, HazeSpread, 0.7f, 1.3f,
                    plume: Aloft(smoke), smokeShare: 0f, smokeColor: PlugSteamColor, cloud: plugCloud);

            plumeTop = 0f;
            volume?.Begin(Look.Volume);
            Drift(smoke, dt, lift: true);
            Drift(sootCore, dt, lift: true);
            Drift(jet, dt, lift: true);
            Drift(heat, dt, lift: true);
            var sun = Sun();
            volume?.End(sun != null && sun.isActiveAndEnabled ? -sun.transform.forward : Vector3.zero,
                Look.Volume, Look.VolumeDensity, volumeMaterials);
            foreach (var system in embers)
                Fly(system, dt);

            LogStatus(inputs);
        }

        // The fire's smoke as the plume takes it, with the engine's exhaust:
        // the smokebox's flow less the stack's natural draft, and a well-set
        // fire's haze riding in the beats by how clear the stack is.
        private PlumeInputs FireInputs()
        {
            stackHeat = fireNow.Heat;
            var total = totalFlow.Value;
            return new PlumeInputs
            {
                ExhaustPressure = exhaustPressure.Value,
                TotalFlowNormalized = float.IsNaN(fireNow.NaturalFlow) ? total : Mathf.Max(0f, total - fireNow.NaturalFlow),
                Soot = fireNow.Soot,
                FireOn = fireNow.FireOn,
                FireboxGas = fireNow.FireboxGas,
                FireboxSmokeOut = fireNow.FireboxSmokeOut,
                ClearStack = fireNow.ClearStack,
                PuffHaze = PlumeModel.WellFiredHaze(tuning, fireNow.ClearStack),
                HeatShare = Mathf.Clamp01(fireNow.Heat / Mathf.Max(1f, fireNow.MostHeat)),
            };
        }

        // The test bed's stack in place of the fire and engine.
        private PlumeInputs TestInputs(StackTest test)
        {
            stackHeat = test.FireOn ? Mathf.Clamp01(test.Heat) * fireNow.MostHeat : 0f;
            return new PlumeInputs
            {
                ExhaustPressure = test.Exhaust,
                TotalFlowNormalized = test.Flow,
                Soot = test.FireOn ? test.Soot : 0f,
                FireOn = test.FireOn,
                ClearStack = test.Clear,
                PuffHaze = PlumeModel.WellFiredHaze(tuning, test.Clear),
                HeatShare = test.FireOn ? Mathf.Clamp01(test.Heat) : 0f,
            };
        }

        // The white the stack's steam makes in the air (SteamCloud): its
        // flue gas, the air through the fire over the steam up the stack, a
        // share of it mixing with the beats' steam before the air; the
        // exhaust's water from the cylinders' walls; and the stack's buoyancy.
        private void Weather(ref PlumeInputs inputs, float dt)
        {
            float steam, through, wet, blower;
            if (testing != null)
            {
                var engineMax = exhaustSim != null ? exhaustSim.engineMaxFlowReader.Value : 5f;
                steam = Mathf.Max(0f, testing.Flow) * engineMax;
                through = Mathf.Max(0f, testing.AirPerSteam) * steam;
                wet = testing.Wetness;
                // A test's steam between beats is the blower's when it stands.
                blower = testing.Beats > 0.1f ? 0f : 1f;
            }
            else
            {
                steam = fireNow.StackSteam;
                through = fireNow.AirThrough;
                // The exhaust's water, unchanged while the engine takes no steam.
                wet = float.IsNaN(fireNow.ExhaustWetness) ? wetness : fireNow.ExhaustWetness;
                blower = steam > 0.02f ? Mathf.Clamp01(fireNow.BlowerSteam / steam) : 1f;
            }
            steamFlowNow = steam;
            var flue = steam > 0.02f ? Mathf.Min(MostFlueGas, Mathf.Max(0f, through) / steam) : MostFlueGas;
            var settle = 1f - Mathf.Exp(-dt / SteamSettling);
            flueGas += (flue - flueGas) * settle;
            wetness += (wet - wetness) * settle;
            var air = PlumeWeather.Air;
            steamCloud = SteamCloud.ForBeats(air, tuning.FlueGasMixing * flueGas, wetness, out steamWhite);
            // The stream between beats: the blower's narrow jet meets less of
            // the flue gas, and its steam is dry.
            streamCloud = SteamCloud.For(air, Mathf.Lerp(tuning.FlueGasMixing, tuning.BlowerFlueGasMixing, blower) * flueGas, wetness * (1f - blower));
            waterCloud = SteamCloud.For(air, SteamCloud.FlueGasPerFireWater);
            plugCloud = SteamCloud.For(air, 0f);
            inputs.SteamWhiteness = steamWhite;
            inputs.WaterWhiteness = waterCloud.Whiteness;
            inputs.BlowerWhiteness = streamCloud.Whiteness;
            stackFlux = PlumeRise.StackFlux(stackHeat, steam) * tuning.Buoyancy;
        }

        private void EmitPuff(Puff puff, Vector3 carVelocity)
        {
            LastPuff = puff;
            beatCount++;
            var color = ToColor(puff.Color);
            // Strung up the jet (each its own speed, the spread narrow) or, as
            // clusters, a bigger core with lobes beside it leaving together.
            var cluster = Mathf.Clamp01(Look.BeatCluster);
            var spread = Mathf.Lerp(PuffSpread, ClusterSpread, cluster);
            var slowest = Mathf.Lerp(PuffSlowest, ClusterSlowest, cluster);
            var share = ParticleShare;
            var particles = SmokeDetail.Scaled(puff.Particles, share, ref puffParticlesDue, least: 1);
            for (var i = 0; i < particles; i++)
            {
                var size = puff.Size * (i == 0 ? Mathf.Lerp(1f, ClusterCore, cluster) : Mathf.Lerp(1f, ClusterLobe, cluster));
                Emit(smoke, puff.Speed, size, color, carVelocity, i == 0 ? PuffSpread : spread, slowest, PuffFastest, Lifetime(smoke, puff.LifetimeScale),
                    Aloft(smoke), puff.SmokeShare, ToColor(puff.SmokeColor), steamCloud);
            }

            // The custom effects add streaks for the jet itself, right above
            // the stack: its smoke, and its white only as far as it has formed
            // in their half second.
            if (jet != null)
            {
                var streaks = SmokeDetail.Scaled(Mathf.Max(2, puff.Particles / 2), share, ref jetStreaksDue, least: 1);
                var parcel = new JetParcel { Thickness = PlumeRise.Parcel(puff.Color.A, puff.SmokeShare), SmokeColor = ToColor(puff.SmokeColor) };
                for (var i = 0; i < streaks; i++)
                {
                    var seed = (uint)Random.Range(int.MinValue, int.MaxValue);
                    jetParcels.Expect(seed, parcel);
                    Emit(jet, puff.Speed, puff.Size * JetStreakSize, JetColor(parcel, 0f), carVelocity, PuffSpread * 0.5f, 0.85f, 1.1f, seed: seed);
                }
            }

            // A dark puff's sooty core goes into the cloud's own system, drawn
            // among its particles by distance: in a system of its own it was
            // drawn over the whole cloud, white steam in front of it too, and
            // a fresh charge's black smoke cut through the steam as a layer
            // (ThinBlueWarrior, Nexus, 2026-10-19). It keeps its shorter
            // life, drawn as the Soot system's.
            var sootParticles = SmokeDetail.Scaled(puff.SootParticles, share, ref sootParticlesDue);
            if (sootParticles > 0)
            {
                var soot = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, Mathf.Min(1f, color.a + 0.1f));
                for (var i = 0; i < sootParticles; i++)
                    Emit(smoke, puff.Speed * 0.9f, puff.Size * 0.6f, soot, carVelocity, PuffSpread, PuffSlowest, PuffFastest,
                        SootLifetime(puff.LifetimeScale), Aloft(smoke), 1f, soot, steamCloud);
            }

            // Usually well under one spark a beat: round at random. A stoker's
            // lost fines go out with the beat too.
            var shook = Mathf.Min(shakeCinders, ShakeCindersPerBeat);
            shakeCinders -= shook;
            // An oil burner throws no cinders.
            var expected = !fireNow.Cinders ? 0f : puff.Sparks + (stokerCinders + shook) * StokerCindersPerKg * tuning.Sparks / DefaultSparkSetting;
            stokerCinders = 0f;
            var sparks = Mathf.FloorToInt(expected) + (Random.value < expected % 1f ? 1 : 0);
            for (var i = 0; i < sparks && embers.Length > 0; i++)
            {
                Emit(embers[i % embers.Length], PlumeModel.SparkLaunchSpeed(puff), Random.Range(SparkMinSize, SparkMaxSize),
                    Color.white, carVelocity, SparkSpread, 0.85f, 1.25f, Random.Range(SparkMinLife, SparkMaxLife));
            }
        }

        // A jet streak's color <paramref name="age"/> s out of the stack: its
        // smoke, and as much of its white as has formed by then, as the
        // cloud's particles beside it.
        private Color JetColor(JetParcel parcel, float age)
        {
            var past = airVelocity - stackVelocity;
            var steam = PlumeRise.SteamLeft(parcel.Thickness, steamCloud, age, new Vector2(past.x, past.z).magnitude, tuning.SteamMixing, tuning.SteamForming);
            var whole = steam + parcel.Thickness.SmokeDepth;
            var tint = whole > 1e-4f ? PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / whole) : 1f;
            var white = PlumeModel.Steam;
            return new Color(Mathf.Lerp(white.R, parcel.SmokeColor.r, tint), Mathf.Lerp(white.G, parcel.SmokeColor.g, tint),
                Mathf.Lerp(white.B, parcel.SmokeColor.b, tint), PlumeRise.OpacityOf(whole, 0f));
        }

        // Cinders are heavy for their size: light drag toward still air and
        // full gravity, so they arc up and fall back (PlumeModel.SparkDrag).
        private void Fly(ParticleSystem system, float dt)
        {
            if (system == null)
                return;
            var count = Particles(system);
            if (count == 0)
                return;
            var keep = Mathf.Exp(-PlumeModel.SparkDrag * dt);
            var fall = Vector3.down * (PlumeModel.Gravity * dt);
            var local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var wind = local
                ? system.transform.InverseTransformDirection(airVelocity - stackVelocity)
                : airVelocity;
            var up = local ? system.transform.InverseTransformDirection(Vector3.up) : Vector3.up;
            var hasCap = RoofLevel(system, up, out var cap);
            for (var i = 0; i < count; i++)
            {
                var velocity = wind + (buffer[i].velocity - wind) * keep + fall;
                // A cinder that strikes a tunnel's roof falls back off it.
                var level = Vector3.Dot(buffer[i].position, up);
                var upward = Vector3.Dot(velocity, up);
                if (hasCap && level > cap)
                {
                    buffer[i].position += up * (cap - level);
                    if (upward > 0f)
                        velocity -= up * ((1f + CinderBounce) * upward);
                }
                buffer[i].velocity = velocity;
            }
            system.SetParticles(buffer, count);
        }

        // In the narrow-jet zone: below its top along the stack's axis, and
        // near the axis. By height alone, smoke the train had left behind
        // below the stack's level (past a crest, or where the line falls
        // away) was taken back into the zone: carried along with the train
        // and never retired until it rose back above the stack (loady740,
        // Nexus, 2026-10-27: a line of puffs left in the air).
        private static bool InZone(Vector3 position, Vector3 emitter, Vector3 axis, float zone)
        {
            var offset = position - emitter;
            var along = Vector3.Dot(offset, axis);
            return along < zone && (offset - axis * along).sqrMagnitude < ZoneReach * ZoneReach;
        }

        private int Particles(ParticleSystem system)
        {
            var capacity = system.main.maxParticles;
            if (buffer.Length < capacity)
                buffer = new ParticleSystem.Particle[capacity];
            return system.GetParticles(buffer);
        }

        // In the narrow-jet zone a particle is held thin and carried with the
        // stack: each frame it takes the stack's current velocity plus its own
        // rise, so speeding up, braking, or curving cannot push it through the
        // stack's wall. Above the zone: linear drag toward still air plus the
        // plume's rise at its height (PlumeRise), so a puff stops after its
        // launch speed over the drag (the jet length for the hardest beat)
        // and then rises with the plume; the stack's velocity it carried
        // decays the same way, so the plume trails behind a moving train. The
        // cloud and sooty core are carried as the plume (PlumeAloft): wide,
        // fading, and fewer as they rise. A system in the stack's own space
        // (the jet) moves with the stack already, and the still air there
        // blows back at the train's speed.
        private void Drift(ParticleSystem system, float dt, bool lift)
        {
            if (system == null)
                return;
            var count = Particles(system);
            if (count == 0)
                return;

            var keep = Mathf.Exp(-PlumeModel.JetDrag(tuning) * dt);
            var space = system.transform;
            var local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var up = local ? space.InverseTransformDirection(Vector3.up) : Vector3.up;
            var flux = lift ? stackFlux : 0f;
            // The air the smoke drifts in: the weather's wind (and a test's
            // train speed); past the stack, the wind less the train's own speed.
            var wind = airVelocity;
            var past = wind - stackVelocity;
            var airSpeed = new Vector2(past.x, past.z).magnitude;
            var sun = Sun();
            var toSun = sun != null && sun.isActiveAndEnabled ? -sun.transform.forward : Vector3.zero;
            // Where the plume is seen from, for its silhouette (a capture's held view first).
            var eyeCamera = PlayerManager.ActiveCamera;
            var eye = PlumeCapture.HeldAt ?? (eyeCamera != null ? eyeCamera.transform.position : (Vector3?)null);
            var axis = local ? Vector3.up : space.up;
            var carried = local ? Vector3.zero : stackVelocity;
            var air = local ? space.InverseTransformDirection(past) : wind;
            var zone = stackLip.HasValue
                ? Mathf.Min(Mathf.Max(0f, stackLip.Value) + LipMargin, Look.JetZone)
                : Look.JetZone;
            var emitter = SimulationPoint(system, Emitter(system));
            var zoneTop = emitter + axis * zone;
            if (aloft.TryGetValue(system, out var plume))
            {
                var ageScale = 1f;
                if (puffMaterials.TryGetValue(system, out var puffMaterial) && puffMaterial != null && puffMaterial.HasProperty(PuffAgeScaleId))
                {
                    ageScale = PlumeRise.PuffAgeScale(wispTrainSpeed, plume.PlayingTime,
                        Mathf.Max(Look.WispAgeTo, Look.ThinWispAgeTo), Look.PuffTrail);
                    puffMaterial.SetFloat(PuffAgeScaleId, ageScale);
                }
                Carry(system, plume, count, new PlumeAloft.Air
                {
                    Up = up,
                    Still = air,
                    Keep = keep,
                    Flux = flux,
                    Speed = airSpeed,
                    Train = carried,
                    Cloud = steamCloud,
                    Mixing = tuning.SteamMixing,
                    Forming = tuning.SteamForming,
                    WakeCarry = tuning.WakeCarry,
                    WakeDepth = tuning.WakeDepth,
                    JetWidth = Look.JetWidth,
                    Fray = Look.Fray,
                    Eddies = Look.Eddies,
                    RiseShown = Look.RiseShown,
                    Spread = tuning.TrailSpread,
                    FillWidth = Look.FillWidth,
                    SmokeFillWidth = Look.SmokeFillWidth,
                    Disperse = Look.Disperse,
                    Overlap = Look.Overlap,
                    SmokeAsSteamFrom = Look.SmokeAsSteamFrom,
                    WispLargestFrom = Look.WispLargestFrom,
                    WispLargestTo = Look.WispLargestTo,
                    WispShare = Look.WispShare,
                    WispAgeFrom = Look.WispAgeFrom * ageScale,
                    WispAgeTo = Look.WispAgeTo * ageScale,
                    WispFrayEdge = Look.WispFrayEdge,
                    WispBelow = Look.WispBelow,
                    WispAbove = Look.WispAbove,
                    ShapeAcross = Look.ShapeAcross > 0.5f,
                    ShapeSmoothing = Look.ShapeSmoothing,
                    SmokeAsSteamTo = Look.SmokeAsSteamTo,
                    Shape = Look.PlumeShape > 0f,
                    ToSun = local ? space.InverseTransformDirection(toSun) : toSun,
                    HasEye = eye.HasValue,
                    Eye = eye.HasValue ? (local ? space.InverseTransformPoint(eye.Value) : eye.Value) : Vector3.zero,
                    ShadowDensity = Look.SunShadow > 0f ? Look.ShadowDensity : 0f,
                    BillowAge = Look.BillowAge,
                    Puffs = rollingPuffs,
                    RollRate = Look.RollRate,
                    RollSlows = Look.RollSlows,
                    Roof = SameSpace(system, smoke) ? roof : null,
                    InTunnel = roof.OverStack.HasValue,
                    Faint = SmokeDetail.Faint(StackHost.Settings.SmokeQuality),
                    Floor = Vector3.Dot(zoneTop, up),
                }, emitter, axis, zone, zoneTop, carried);
                return;
            }
            var hasCap = RoofLevel(system, up, out var cap);
            var growth = system.sizeOverLifetime.enabled ? system.sizeOverLifetime.size : new ParticleSystem.MinMaxCurve(1f);
            if (!intendedSizes.TryGetValue(system, out var sizes))
                intendedSizes[system] = sizes = new SeedMemory<float>();
            var streaks = system == jet && jetVelocityScale > 0f;
            for (var i = 0; i < count; i++)
            {
                var particle = buffer[i];
                var intended = sizes.Recall(particle.randomSeed, particle.startSize);
                var life = particle.startLifetime > 0f ? 1f - particle.remainingLifetime / particle.startLifetime : 0f;
                var shown = intended * growth.Evaluate(life);
                var velocity = streaks ? jetVelocities.Recall(particle.randomSeed, particle.velocity) : particle.velocity;
                var inZone = InZone(particle.position, emitter, axis, zone);
                float drawn;
                if (inZone)
                {
                    drawn = Mathf.Min(shown, Widest(0f));
                    var upward = Mathf.Max(Vector3.Dot(velocity - carried, axis), MinZoneRise);
                    velocity = carried + axis * upward;
                }
                else
                {
                    drawn = Mathf.Min(system == heat ? Mathf.Max(shown, HotWidth(particle, zoneTop, up, airSpeed)) : shown,
                        Widest(Vector3.Distance(particle.position, zoneTop)));
                    var rise = PlumeRise.RiseSpeed(flux, Vector3.Dot(particle.position - zoneTop, up), airSpeed);
                    var target = air + up * rise;
                    velocity = target + (velocity - target) * keep;
                    // A tunnel's roof stops it.
                    var level = Vector3.Dot(particle.position, up);
                    if (hasCap && level > cap)
                        particle.position += up * (cap - level);
                    var upward = Vector3.Dot(velocity, up);
                    if (hasCap && level >= cap - RoofBand && upward > 0f)
                        velocity -= up * upward;
                }
                particle.startSize = shown > 1e-4f ? intended * drawn / shown : intended;

                if (system == heat)
                {
                    var strength = heatStrengths.Recall(particle.randomSeed, ((Color)particle.startColor).a);
                    var born = Mathf.Max(1e-3f, intended * growth.Evaluate(0f));
                    var diffused = strength * Mathf.Pow(Mathf.Min(1f, born / Mathf.Max(born, drawn)), Look.ShimmerDiffuse);
                    particle.startColor = new Color(1f, 1f, 1f, diffused);
                }

                if (system == jet && jetParcels.TryRecall(particle.randomSeed, out var jetParcel))
                {
                    var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
                    particle.startColor = JetColor(jetParcel, age);
                }

                if (streaks)
                {
                    // Inside the stack a streak runs straight down it. Above,
                    // it points back at the mouth, and the particle is moved
                    // by the difference so it still travels by its true velocity.
                    var drawnVelocity = velocity;
                    if (!inZone)
                    {
                        var toMouth = particle.position - zoneTop;
                        var room = Mathf.Max(0f, toMouth.magnitude - jetLengthScale * drawn);
                        var length = Mathf.Min(jetVelocityScale * velocity.magnitude, room);
                        drawnVelocity = toMouth.normalized * (length / jetVelocityScale);
                    }
                    particle.position += (velocity - drawnVelocity) * dt;
                    particle.velocity = drawnVelocity;
                    jetVelocities.Keep(particle.randomSeed, velocity);
                }
                else
                {
                    particle.velocity = velocity;
                }
                buffer[i] = particle;
            }
            sizes.Forget();
            if (streaks)
                jetVelocities.Forget();
            if (system == jet)
                jetParcels.Forget();
            if (system == heat)
                heatStrengths.Forget();
            system.SetParticles(buffer, count);
        }

        // The width the hot gas has spread to at a shimmer particle: the lip's
        // (× the shimmer's width) spreading as the exhaust does with its
        // height, and by its age's turbulence, as the smoke's plume.
        private float HotWidth(ParticleSystem.Particle particle, Vector3 zoneTop, Vector3 up, float airSpeed)
        {
            var height = Mathf.Max(0f, Vector3.Dot(particle.position - zoneTop, up));
            var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
            var plume = PlumeRise.PlumeWidth(height, PlumeRise.SpreadWidth(age, airSpeed, tuning.TrailSpread)) * HotPlumeShare;
            return Look.ShimmerSize * Mathf.Max(Spreading(height, 1f), plume / VisibleShare);
        }

        // The hot core's share of the plume's visible width.
        private const float HotPlumeShare = 0.5f;

        // The cloud's and sooty core's particles: held thin in the narrow-jet
        // zone and moved with the stack as Drift does there; above it carried
        // as the plume.
        private void Carry(ParticleSystem system, PlumeAloft plume, int count, PlumeAloft.Air air,
            Vector3 emitter, Vector3 axis, float zone, Vector3 zoneTop, Vector3 carried)
        {
            plume.Begin(air);
            var compact = Look.StableParticleData > 0.5f;
            var kept = 0;
            for (var i = 0; i < count; i++)
            {
                var particle = buffer[i];
                var inZone = InZone(particle.position, emitter, axis, zone);
                if (inZone)
                {
                    var upward = Mathf.Max(Vector3.Dot(particle.velocity - carried, axis), MinZoneRise);
                    particle.velocity = carried + axis * upward;
                }
                // From the lip's middle: below it in the stack, above it out;
                // its least width by its height over the lip.
                var height = Vector3.Dot(particle.position - zoneTop, air.Up);
                var above = inZone ? Vector3.Dot(particle.position - zoneTop, axis) : Vector3.Distance(particle.position, zoneTop);
                plume.Carry(ref particle, height, Least(inZone ? above : Mathf.Max(0f, height)), Widest(above), inZone);
                // SetParticles drops the dead and moves the last survivors
                // into their places, but the custom data written after it
                // goes by place: a moved particle drew a dead one's wisp, turn
                // and age for a frame (Sol, 2026-10-23). Dropped here first,
                // each keeps its own.
                if (compact && particle.remainingLifetime <= 0f)
                    continue;
                buffer[kept++] = particle;
            }
            count = kept;
            plume.End();
            plumeTop = Mathf.Max(plumeTop, plume.Top);
            system.SetParticles(buffer, count);
            if (rollingPuffs > 0)
            {
                // Rolling steam's wisp is one of the wisp flipbook's rows (x,
                // rounded; its fraction its own start in the row × 0.49), at
                // its own turn (y), how hard its beat was (z), its frames by
                // age, played over the system's own lifetime as the flipbook
                // was (PlumeAloft), the last held: its share of that time (w).
                var longFrom = Mathf.Min(Look.LongPuffsFrom, Look.LongPuffsBy - 0.01f);
                if (!beatsHard.TryGetValue(system, out var hardness))
                    beatsHard[system] = hardness = new SeedMemory<float>();
                system.GetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
                for (var i = 0; i < count && i < shapeData.Count; i++)
                {
                    var hard = Mathf.SmoothStep(0f, 1f, (hardness.Recall(buffer[i].randomSeed, beatHard) - longFrom) / (Look.LongPuffsBy - longFrom));
                    shapeData[i] = new Vector4(PlumeAloft.WispRow(buffer[i]) + 0.49f * PlumeAloft.WispPhase(buffer[i]),
                        PlumeAloft.WispTurn(buffer[i], Look.WispSpin, Look.WispSpinEase), hard, plume.FlipbookAt(buffer[i]));
                }
                system.SetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
                hardness.Forget();
            }
            else if (air.Shape)
            {
                system.GetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
                for (var i = 0; i < count && i < shapeData.Count; i++)
                    shapeData[i] = plume.ShapeOf(buffer[i].randomSeed);
                system.SetCustomParticleData(shapeData, ParticleSystemCustomData.Custom1);
            }
            system.GetCustomParticleData(extraData, ParticleSystemCustomData.Custom2);
            for (var i = 0; i < count && i < extraData.Count; i++)
                extraData[i] = plume.ExtraOf(buffer[i]);
            system.SetCustomParticleData(extraData, ParticleSystemCustomData.Custom2);
            volume?.Gather(system, buffer, count);
        }

        private PlumeAloft Aloft(ParticleSystem system) => system != null && aloft.TryGetValue(system, out var plume) ? plume : null;

        // The roof over the stack on <paramref name="up"/> in the system's
        // own space, a little under it; false in the open.
        private bool RoofLevel(ParticleSystem system, Vector3 up, out float level)
        {
            level = 0f;
            if (!(roof.OverStack is TunnelRoof.Sample over))
                return false;
            var world = WorldPoint(smoke, over.Point);
            level = Vector3.Dot(SimulationPoint(system, world), up) - RoofBand;
            return true;
        }

        private static bool SameSpace(ParticleSystem a, ParticleSystem b)
        {
            var one = a.main;
            var other = b.main;
            return one.simulationSpace == other.simulationSpace
                   && (one.simulationSpace != ParticleSystemSimulationSpace.Custom || one.customSimulationSpace == other.customSimulationSpace)
                   && (one.simulationSpace != ParticleSystemSimulationSpace.Local || a.transform == b.transform);
        }

        // A point in the space the system simulates in, back in the world.
        internal static Vector3 WorldPoint(ParticleSystem system, Vector3 point)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform.TransformPoint(point);
                case ParticleSystemSimulationSpace.Custom when main.customSimulationSpace != null:
                    return main.customSimulationSpace.TransformPoint(point);
                default:
                    return point;
            }
        }

        // A world point in the space the system simulates in (the game moves
        // the world origin, so the stock smoke simulates in a custom space).
        internal static Vector3 SimulationPoint(ParticleSystem system, Vector3 world)
        {
            var main = system.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.Local:
                    return system.transform.InverseTransformPoint(world);
                case ParticleSystemSimulationSpace.Custom when main.customSimulationSpace != null:
                    return main.customSimulationSpace.InverseTransformPoint(world);
                default:
                    return world;
            }
        }

        // The game's smoke emitter in world space (its shape's point). Its depth
        // inside the stack differs between locos, so smoke starts here and the
        // narrow-jet zone (see Drift) keeps it inside the stack.
        internal static Vector3 Emitter(ParticleSystem system) =>
            system.transform.TransformPoint(system.shape.position);

        // Every particle starts at the emitter, at a random point across its
        // radius, placed here in the system's simulation space rather than
        // through Unity's shape module.
        private static void Emit(ParticleSystem system, float speed, float size, Color color, Vector3 carVelocity,
            float spread, float slowest, float fastest, float lifetime = 0f, PlumeAloft plume = null, float smokeShare = 0f,
            Color smokeColor = default, SteamCloud cloud = null, uint seed = 0)
        {
            var transform = system.transform;
            var up = transform.up;
            var disk = Random.insideUnitCircle * system.shape.radius;
            var start = Emitter(system) + transform.right * disk.x + transform.forward * disk.y;
            var jitter = Random.insideUnitSphere * spread;
            var launch = (up + jitter).normalized * (speed * Random.Range(slowest, fastest));
            // A system in the stack's own space already moves with it.
            var local = system.main.simulationSpace == ParticleSystemSimulationSpace.Local;
            var lead = local ? Vector3.zero : carVelocity * Time.deltaTime;
            var emit = new ParticleSystem.EmitParams
            {
                position = SimulationPoint(system, start - lead),
                applyShapeToPosition = false,
                velocity = local
                    ? transform.InverseTransformDirection(launch)
                    : launch + carVelocity,
                startSize = size * Random.Range(0.8f, 1.2f),
                startColor = color,
            };
            if (lifetime > 0f)
                emit.startLifetime = lifetime;
            if (seed != 0)
                emit.randomSeed = seed;
            if (plume != null)
            {
                // The plume knows the particle by its seed: how much is smoke.
                emit.randomSeed = (uint)Random.Range(int.MinValue, int.MaxValue);
                plume.Expect(emit.randomSeed, color, emit.startSize, smokeShare, smokeShare > 0f ? smokeColor : color, cloud);
            }
            system.Emit(emit, 1);
        }

        private static Color ToColor(PlumeColor c) => new Color(c.R, c.G, c.B, c.A);

        // A particle's lifetime drawn as the system draws it, scaled; 0 (the
        // system's own) when unscaled.
        private static float Lifetime(ParticleSystem system, float scale)
        {
            if (!(scale > 1.001f))
                return 0f;
            return DrawLifetime(system) * scale;
        }

        // The sooty core's life: the Soot system's, 4 to 7 s against the
        // cloud's 6 to 10, or seven tenths of the cloud's in a bundle without it.
        private float SootLifetime(float scale) =>
            (sootCore != null ? DrawLifetime(sootCore) : 0.7f * DrawLifetime(smoke)) * Mathf.Max(1f, scale);

        private static float DrawLifetime(ParticleSystem system)
        {
            var curve = system.main.startLifetime;
            var life = curve.mode == ParticleSystemCurveMode.TwoConstants
                ? Random.Range(curve.constantMin, curve.constantMax)
                : curve.mode == ParticleSystemCurveMode.Constant ? curve.constant : curve.Evaluate(Random.value);
            return life;
        }

        private void LogStatus(PlumeInputs inputs)
        {
            var interval = StackHost.Settings.StatusLogInterval;
            if (interval <= 0 || Time.time - statusTime < interval
                || !fire.Tended)
                return;

            statusTime = Time.time;
            var puff = LastPuff;
            StackHost.Mod.Logger.Log($"{car.ID} smoke: {beatCount} beats in {interval} s, exhaust {inputs.ExhaustPressure:F1} bar " +
                                $"(strength {PlumeModel.BeatStrength(tuning, inputs.ExhaustPressure):P0}, heavy {PlumeModel.Heaviness(tuning, inputs.ExhaustPressure):P0}), " +
                                $"flow {inputs.TotalFlowNormalized:P0}, soot {inputs.Soot:P0} (firebox {PlumeModel.FireboxSoot(inputs):P0}); " +
                                $"last puff {puff.Speed:F1} m/s, {puff.Size:F1} m, {puff.Particles}+{puff.SootParticles} particles, life ×{puff.LifetimeScale:F1}, " +
                                $"{puff.Sparks:F2} sparks, color #{ColorUtility.ToHtmlStringRGBA(ToColor(puff.Color))}; " +
                                $"plume top {plumeTop:F0} m, buoyancy {stackFlux:F0} m⁴/s³, {PlumeWeather.Describe()}, {SteamLine()}, " +
                                (roof.OverStack is TunnelRoof.Sample over ? $"under a roof {over.Height:F1} m over the rail " : "in the open ") +
                                $"({string.Join("; ", aloft.Select(a => $"{a.Key.name} {a.Value.Describe()}"))}); " +
                                $"alive {string.Join(", ", Systems().Where(s => s != null).Select(s => $"{s.name} {s.particleCount}"))}");
            beatCount = 0;
        }

        // The white steam in a few words: how white the air lets it be, how
        // long it lasts, and what makes it so.
        private string SteamLine()
        {
            var past = airVelocity - stackVelocity;
            var speed = new Vector2(past.x, past.z).magnitude;
            return $"steam white {steamWhite:F2} formed half by {Seconds(steamCloud.Forming(speed, tuning.SteamMixing, tuning.SteamForming, 0.5f))}, " +
                   $"lasting {steamCloud.Lasting(speed, tuning.SteamMixing, 1f / Mathf.Exp(1f), tuning.SteamForming):F1} s at {speed:F1} m/s " +
                   $"(flue gas {flueGas:F1} kg/kg, {tuning.FlueGasMixing * flueGas:F2} mixing; exhaust wetness {wetness:F2}; {steamFlowNow:F2} kg/s up the stack), " +
                   $"the fire's water white {waterCloud.Whiteness:F2}, shimmer {shimmerNow:F2}";
        }

        /// <summary>The plume in a few lines, for the test bed.</summary>
        public string Readout()
        {
            var past = airVelocity - stackVelocity;
            var speed = new Vector2(past.x, past.z).magnitude;
            var half = steamCloud.Forming(speed, tuning.SteamMixing, tuning.SteamForming, 0.5f);
            var third = steamCloud.Lasting(speed, tuning.SteamMixing, 1f / Mathf.Exp(1f), tuning.SteamForming);
            var gone = steamCloud.Lasting(speed, tuning.SteamMixing, 0.05f, tuning.SteamForming);
            return $"Steam white: {steamWhite:F2} of the tuned air's; half formed at {Seconds(half)}, a third left at {third:F1} s, gone by {gone:F1} s, the air past at {speed:F1} m/s\n" +
                   $"Flue gas {flueGas:F1} kg a kg of steam ({tuning.FlueGasMixing * flueGas:F2} mixing in); exhaust wetness {wetness:F2}; {steamFlowNow:F2} kg/s of steam up the stack\n" +
                   $"The fire's own water: white {waterCloud.Whiteness:F2}. The stream between beats (the blower's): white {streamCloud.Whiteness:F2}. Heat shimmer: {shimmerNow:F2}\n" +
                   $"Stack: bore {2f * boreRadius:F2} m, outside {2f * outsideRadius:F2} m, lip {(stackLip.HasValue ? $"{stackLip.Value:F2} m over the emitter" : "not found")}\n" +
                   $"Last puff: {LastPuff.Speed:F1} m/s, {LastPuff.Size:F1} m, opacity {LastPuff.Color.A:F2}, color #{ColorUtility.ToHtmlStringRGB(ToColor(LastPuff.Color))}, " +
                   $"{LastPuff.SmokeShare:P0} smoke; plume top {plumeTop:F0} m\n" +
                   $"Particles: {string.Join("; ", aloft.Select(a => $"{a.Key.name} {a.Value.Describe()}"))}; " +
                   $"as one volume, {(volume != null ? volume.ShadedShare : 0f):P0} of its smoke under half the sun\n" +
                   ShaderLight();
        }

        // The light the smoke shader puts on the plume's top facing the sun,
        // worked out as it does: the sky's from above and the sun's, capped.
        private static string ShaderLight()
        {
            var sky = new Color[1];
            RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up }, sky);
            var sun = Sun();
            var sunLight = 0f;
            if (sun != null && sun.isActiveAndEnabled)
            {
                var color = QualitySettings.activeColorSpace == ColorSpace.Linear ? sun.color.linear : sun.color;
                sunLight = color.grayscale * sun.intensity;
            }
            var skyShare = sky[0].grayscale * Mathf.Lerp(Look.NightSkyLight, Look.SkyLight, PlumeWeather.Daylight) * Look.Light;
            var sunShare = sunLight * Look.SunLight * Look.Light;
            var cap = Look.MaxLight * Look.Light;
            return $"Light on the smoke: sky {skyShare:F2} + sun {sunShare:F2} = {Mathf.Min(skyShare + sunShare, cap):F2} (cap {cap:F2}), " +
                   $"{(sun != null ? sun.name : "no sun")}, {QualitySettings.activeColorSpace} color";
        }

        // The scene's main light, the sun (or the moon), as the shader takes
        // it: the render settings' sun, else the strongest directional light,
        // looked for again no more often than this, s.
        private const float SunSearchEvery = 5f;
        private static Light sunFound;
        private static float sunSearched = float.NegativeInfinity;

        internal static Light Sun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            if (sunFound != null && sunFound.isActiveAndEnabled || Time.unscaledTime - sunSearched < SunSearchEvery)
                return sunFound;
            sunSearched = Time.unscaledTime;
            sunFound = null;
            foreach (var light in FindObjectsOfType<Light>())
                if (light.type == LightType.Directional && light.isActiveAndEnabled && (sunFound == null || light.intensity > sunFound.intensity))
                    sunFound = light;
            return sunFound;
        }

        private static string Seconds(float seconds) => float.IsInfinity(seconds) ? "never" : $"{seconds:F2} s";

        /// <summary>The engine's car, as the game names it.</summary>
        public string CarName => car != null ? car.ID : name;

        /// <summary>The engine's car.</summary>
        internal TrainCar Car => car;

        /// <summary>Where the smoke leaves the stack, in the world.</summary>
        internal Vector3 StackTop => smoke != null ? Emitter(smoke) : car.transform.position;

        /// <summary>The active plume on the car named <paramref name="id"/>, or null.</summary>
        internal static ChimneyPlume Named(string id) =>
            All.FirstOrDefault(p => p != null && p.Active && p.smoke != null && p.car != null && p.car.ID == id);

        /// <summary>The engines with an active plume, by car ID and livery, for the log.</summary>
        internal static string ActiveCars() =>
            string.Join(", ", All.Where(p => p != null && p.Active && p.car != null)
                .Select(p => $"{p.car.ID} ({(p.car.carLivery != null ? p.car.carLivery.id : "?")})"));

        /// <summary>The plume nearest the camera, or null.</summary>
        public static ChimneyPlume Nearest()
        {
            var camera = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
            if (camera == null)
                return null;
            ChimneyPlume best = null;
            var nearest = float.MaxValue;
            foreach (var plume in All)
            {
                if (plume == null || !plume.Active || plume.smoke == null)
                    continue;
                var distance = (Emitter(plume.smoke) - camera.transform.position).sqrMagnitude;
                if (distance < nearest)
                {
                    best = plume;
                    nearest = distance;
                }
            }
            return best;
        }

        // The plume's systems, gathered once it is set up: gathered each
        // frame for its sorting, they were garbage every frame.
        private ParticleSystem[] systems;

        private ParticleSystem[] Systems()
        {
            if (systems != null)
                return systems;
            var gathered = new[] { smoke, sootCore, jet, heat }.Concat(embers ?? new ParticleSystem[0]).Where(s => s != null).Distinct().ToArray();
            if (Active)
                systems = gathered;
            return gathered;
        }
    }
}
