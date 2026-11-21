using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DV.Simulation.Controllers;
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
    internal sealed partial class ChimneyPlume : MonoBehaviour
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

        // A beat's particles leave as its steam does (ExhaustBlast): its
        // head at the blast's speed, its tail slowly, each within this of
        // its speed either way. A drifting beat's, and the last of any
        // beat's steam, leave at the least, m/s.
        private const float BeatSpeedSlowest = 0.9f;
        private const float BeatSpeedFastest = 1.1f;
        private const float LeastBeatSpeed = 3f;

        // A beat as a cluster (Look.BeatCluster): its lobes' spread, and its
        // head's and lobes' sizes against a beat's particle.
        private const float ClusterSpread = 0.3f;
        private const float ClusterCore = 1.4f;
        private const float ClusterLobe = 0.75f;

        // Beats still letting their steam out: at speed a beat's blowdown
        // outlasts the next few beats, and its return stroke takes two more.
        private const int MostPendingBeats = 16;

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
        private const float DefaultBore = StackGeometryHint.DefaultBoreRadius;
        private const float DefaultOutside = StackGeometryHint.DefaultOutsideRadius;

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
        private const float StackRadius = StackGeometryHint.MaxOutsideRadius;
        private const float MaxStackRise = StackGeometryHint.MaxLipHeight;
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
        private CarPlumes<ChimneyPlume> carRoster;
        private float stackHeat;

        // Every plume attached to a car (articulated and double-chimney locomotives
        // carry more than one), so vents ownership stays on one active stack and
        // lookups always find it.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, CarPlumes<ChimneyPlume>> OnCar =
            new System.Runtime.CompilerServices.ConditionalWeakTable<TrainCar, CarPlumes<ChimneyPlume>>();

        private static bool IsAlivePlume(ChimneyPlume plume) => plume != null;
        private static bool IsActivePlume(ChimneyPlume plume) => plume != null && plume.Active;
        private static bool HasVents(ChimneyPlume plume) => plume != null && plume.vents != null;
        private static bool HasSteamSorting(ChimneyPlume plume) => plume != null && plume.steamSorting.Count > 0;
        private static bool CanHostVents(ChimneyPlume plume) =>
            plume != null && plume.Active && plume.custom && plume.smoke != null && StackHost.Settings.EngineSteam;

        /// <summary>The highest of the plume last frame, m above the stack.</summary>
        public float PlumeTop => plumeTop;

        /// <summary>
        /// The active plume on <paramref name="car"/>: the active plume that owns
        /// the car's <see cref="SteamVents"/> first, or else any active plume on
        /// that car.
        /// </summary>
        public static bool TryGet(TrainCar car, out ChimneyPlume plume)
        {
            plume = null;
            if (car == null || !OnCar.TryGetValue(car, out var roster))
                return false;
            plume = roster.Primary(IsActivePlume, HasVents);
            return plume != null;
        }

        /// <summary>
        /// The highest plume top across every active stack on <paramref name="car"/>,
        /// in m above the stack; false when no active plume is attached to the car.
        /// </summary>
        public static bool TryGetMaxPlumeTop(TrainCar car, out float top)
        {
            top = 0f;
            return car != null && OnCar.TryGetValue(car, out var roster)
                && roster.TryGetMax(IsActivePlume, p => p.plumeTop, out top);
        }
        private ExhaustBeatCounter beats;
        private int beatsPerTurn = 4;

        // A beat letting its steam out: its puff as it was released, how
        // many jet streaks and sooty core particles go with its particles,
        // and how many of each have gone.
        private struct PendingBeat
        {
            public Puff Puff;
            public BeatRelease Release;
            public int Particles;
            public int Out;
            public int Streaks;
            public int StreaksOut;
            public int Soot;
            public int SootOut;
        }

        private readonly PendingBeat[] pendingBeats = new PendingBeat[MostPendingBeats];
        private int pendingBeatCount;
        private Port crankRotation;
        private Port exhaustPressure;
        private Port totalFlow;
        private SteamExhaust exhaustSim;
        private Port boilerBroken;

        // The steam chest's and the boiler's pressures, bar, for the steam around the engine; null without.
        private Port steamChest;
        private Port boilerPressure;
        private ParticleSystem smoke;
        private ParticleSystem sootCore;
        private ParticleSystem jet;
        // The cloned effects belong to this driver, including when only the
        // component is removed while the stock reader's object stays alive.
        private GameObject effectsRoot;

        // The plume lit as one volume (PlumeVolume), on its materials.
        private PlumeVolume volume;

        // The engine's other steam, drawn as the plume is (SteamVents); null with none.
        private SteamVents vents;

        /// <summary>The engine's other steam, or null: for the mod's own sources and a capture's.</summary>
        internal SteamVents Vents => vents;
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
#if !PUBLIC_RELEASE
        private float statusTime;
#endif
        // The model's tuning and the plume's look, shared by every plume (set
        // live by the test bed in a development build).
        private static PlumeTuning tuning => PlumeCurrent.Tuning;
        private static PlumeLook Look => PlumeCurrent.Look;

        // The share of the plume's particles thrown at the player's smoke quality.
        private static float ParticleShare => SmokeDetail.ParticleShare(StackHost.Quality, PlumeOffscreen.Drawing);
        private int lookVersion = -1;

        // The daylight the look was last applied for (PlumeWeather.Daylight).
        private float lookDaylight = -1f;
        private float lookDirectLight = -1f;
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
#if !PUBLIC_RELEASE
        private StackTest testing;
        private float testBeats;
#endif

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
        /// (<see cref="DrawnBefore"/>): the engine's steam behind the plume,
        /// the custom effects' cloud, its sooty core, the jet's streaks, and
        /// the engine's steam in front of it.
        /// </summary>
        internal void AddOffscreen(List<ParticleSystemRenderer> into)
        {
            if (!custom || !Active)
                return;
            AddRenderer(vents?.Behind, into);
            AddRenderer(smoke, into);
            AddRenderer(sootCore, into);
            AddRenderer(jet, into);
            AddRenderer(vents?.Drawn, into);
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

        // How often the plume's particles are carried, by where it is seen
        // from (PlumeTurns.FramesApart): in view close by, every frame, its
        // newest particles worked out every 25 ms; farther, or out of the
        // camera's view, every few frames, the particles' turns as much
        // farther apart. The game moves them in between, and the drag is
        // taken over the time since. A plume that comes into view is carried
        // the same frame. Out of view its light volume is made again only
        // every UnseenVolumeEvery s, so it is not long stale when the plume
        // is next seen.
        private const float LongestPass = 1f;
        private const float UnseenVolumeEvery = 0.5f;
        private float nextUnseenVolume;
        private static readonly Plane[] LeftView = new Plane[6];
        private static readonly Plane[] RightView = new Plane[6];
        private static int viewFrame = -1;
        private static Camera viewCamera;
        private static bool viewStereo;
        private static int plumesMade;
        private readonly int passOffset = plumesMade++;
        private ParticleSystemRenderer[] renderers;
        private float sinceCarried;
        private int passEvery = 1;
        private float passPeriod = PlumeTurns.NewestEvery;
        private bool inView = true;

        /// <summary>The frames between its particles' carrying, for the log: 1 in view close by.</summary>
        public int PassEvery => passEvery;

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

        private const int SmokeCounts = 30;

        private float beatHard;
        private float exitSpeed;
        private float jetVelocityScale;
        private float jetLengthScale;

        /// <summary>True once the plume has taken over from the stock smoke reader.</summary>
        public bool Active { get; private set; }

        /// <summary>The fire this plume currently reads, or null before binding.</summary>
        internal IStackFire BoundFire => fire;

        /// <summary>The car this plume is bound to, or null before binding.</summary>
        internal TrainCar BoundCar => car;

        public Puff LastPuff { get; private set; }

        /// <summary>The engine's car, as the game names it.</summary>
        public string CarName => car != null ? car.ID : name;

        /// <summary>The engine's car.</summary>
        internal TrainCar Car => car;

        /// <summary>Where the smoke leaves the stack, in the world.</summary>
        internal Vector3 StackTop => smoke != null ? Emitter(smoke) : car.transform.position;

 internal static ChimneyPlume Named(string id) =>
            CarPlumes.SelectPrimary(All, p => p != null && p.Active && p.smoke != null && p.car != null && p.car.ID == id, HasVents);

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

        private void Update()
        {
            var start = Stopwatch.GetTimestamp();
            StackHost.Govern();
            using (StackHost.TimePhase(PlumePhase.OffscreenSetup))
                PlumeOffscreen.Decide();
            UpdatePlume();
            StackHost.PlumeTimed?.Invoke(start);
        }

        private void UpdatePlume()
        {
            if (!Active || smoke == null)
                return;
            if (lookVersion != PlumeCurrent.LookVersion || Mathf.Abs(PlumeWeather.Daylight - lookDaylight) > DaylightStep
                || Mathf.Abs(PlumeWeather.DirectLight - lookDirectLight) > DaylightStep)
                ApplyLook();
            SyncHeatShimmer();
            if (carRoster != null && carRoster.NeedsAnyHandoff)
            {
                var takeVents = carRoster.TryConsumeVentsHandoff(this, IsActivePlume, HasVents, CanHostVents);
                var takeSorting = carRoster.TryConsumeSortingHandoff(this, IsActivePlume, HasSteamSorting,
                    takeVents ? null : (carRoster.NeedsVentsHandoff ? (System.Func<ChimneyPlume, bool>)CanHostVents : HasVents));
                if (takeVents || takeSorting)
                    TakeOverCarSteam(takeVents);
            }
#if !PUBLIC_RELEASE
            testing = PlumeTestBed.StackFor(this);
#endif

            var dt = Time.deltaTime;
            var position = smoke.transform.position;
            var carVelocity = hasLastPosition && dt > 0f ? (position - lastPosition) / dt : Vector3.zero;
            if (carVelocity.magnitude > MaxPlausibleSpeed)
                carVelocity = Vector3.zero;
            lastPosition = position;
            stackVelocity = carVelocity;
            hasLastPosition = true;
#if PUBLIC_RELEASE
            var wispSpeed = carVelocity.magnitude;
#else
            var wispSpeed = testing != null ? Mathf.Max(0f, testing.Speed / 3.6f) : carVelocity.magnitude;
#endif
            wispTrainSpeed += (wispSpeed - wispTrainSpeed) * (1f - Mathf.Exp(-dt / 0.5f));

            var crossed = beats.Advance(crankRotation.Value);
            var turned = beats.Turned;
            var sinceBeat = beats.SinceBeat;
#if !PUBLIC_RELEASE
            if (testing != null)
            {
                // The test's beats, the engine standing.
                var testTurned = Mathf.Max(0f, testing.Beats) * dt;
                testBeats += testTurned;
                crossed = (int)testBeats;
                testBeats -= crossed;
                turned = testTurned / beatsPerTurn;
                sinceBeat = testBeats;
            }
#endif
            if (boilerBroken.Value > 0f || dt <= 0f)
                return;
            roof.Follow(car, Emitter(smoke), smoke, PlumeAloft.Lifetime);

            // The air past the stack: the wind, and a test's train speed blowing back.
#if PUBLIC_RELEASE
            airVelocity = PlumeWeather.Wind;
#else
            airVelocity = PlumeWeather.Wind - (testing != null ? car.transform.forward * (testing.Speed / 3.6f) : Vector3.zero);
#endif
            fireNow = default;
            fire.Read(dt, exhaustSim, ref fireNow);
            shakeCinders = Mathf.Min(shakeCinders + fireNow.ShakenCoal, MaxShakeCinderBacklog);
#if PUBLIC_RELEASE
            var inputs = FireInputs();
#else
            var inputs = testing != null ? TestInputs(testing) : FireInputs();
#endif
            using (StackHost.TimePhase(PlumePhase.Weather))
                Weather(ref inputs, dt);
            beatHard = PlumeModel.BeatStrength(tuning, inputs.ExhaustPressure);

            using (StackHost.TimePhase(PlumePhase.Emission))
            {
                // A beat with no steam behind it (drifting, regulator shut) makes no puff.

                // Those already releasing go on by the frame, whatever the
                // regulator does now; a beat crossed this frame has gone on
                // only since the crank passed it.
                for (var i = 0; i < pendingBeatCount; i++)
                    ExhaustBlast.Step(ref pendingBeats[i].Release, dt, turned);
                // One exhaust: the beats' puffs and what leaves between them
                // take the mixture of the two (PlumeModel.AsOne), by the
                // crank's pace now, not by the beats counted, which come a
                // frame at a time.
                var beating = inputs.ExhaustPressure > PlumeModel.MinBeatPressure;
                var puffNow = PlumeModel.ForBeat(tuning, inputs);
                var haze = PlumeModel.Between(tuning, inputs);
                exitSpeed = beating ? Mathf.Max(puffNow.Speed, haze.Speed) : haze.Speed;
                var beatsShare = Look.OneExhaust > 0.5f
                    ? PlumeModel.AsOne(ref puffNow, ref haze, beating ? Mathf.Abs(turned) * beatsPerTurn / dt : 0f)
                    : 0f;
                if (beating)
                {
                    var beatSeconds = turned > 1e-6f ? dt / (turned * beatsPerTurn) : 0f;
                    var count = Mathf.Min(crossed, MaxBeatsPerFrame);
                    for (var i = 0; i < count; i++)
                    {
                        var since = Mathf.Min(dt, (sinceBeat + (count - 1 - i)) * beatSeconds);
                        BeginBeat(puffNow, inputs.ExhaustPressure, since, since / dt * turned, carVelocity);
                    }
                }
                ReleaseBeats(carVelocity);

                stokerCinders = Mathf.Min(stokerCinders + fireNow.Fines, MaxStokerCinderBacklog);

                // The blower's stream is steam; a clean fire's haze, its own
                // water; with the engine working, the beats' own steam.
                var hazeCloud = beatsShare > 0f ? steamCloud
                    : PlumeModel.SteamThroughStack(inputs.TotalFlowNormalized) > 0.3f ? streamCloud : waterCloud;
                hazeDue += haze.Rate * ParticleShare * dt;
                for (; hazeDue >= 1f; hazeDue -= 1f)
                    Emit(smoke, haze.Speed, 1.2f * tuning.PuffSize, ToColor(haze.Color), carVelocity, HazeSpread, 0.85f, 1.15f,
                        plume: Aloft(smoke), smokeShare: haze.SmokeShare, smokeColor: ToColor(haze.SmokeColor), cloud: hazeCloud);

                // The hot gas's shimmer, plainest over a clean fire.
                shimmerNow = PlumeModel.Shimmer(tuning, inputs);
                if (heat != null && HeatShimmerOn)
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

            }
            sinceCarried = Mathf.Min(sinceCarried + dt, LongestPass);
            if (!PassDue())
            {
#if !PUBLIC_RELEASE
                LogStatus(inputs);
#endif
                return;
            }
            var passed = sinceCarried;
            sinceCarried = 0f;
            using (StackHost.TimePhase(PlumePhase.Motion))
            {
                plumeTop = 0f;
                // Its light volume is for the drawing alone: out of view,
                // its worker and its texture's upload wait.
                var light = inView || Time.time >= nextUnseenVolume;
                if (light)
                {
                    nextUnseenVolume = Time.time + UnseenVolumeEvery;
                    volume?.Begin(Look.Volume);
                }
                Drift(smoke, passed, lift: true);
                Drift(sootCore, passed, lift: true);
                Drift(jet, passed, lift: true);
                if (HeatShimmerOn)
                    Drift(heat, passed, lift: true);
                // The engine's own steam, carried on the plume's passes too:
                // its parcels count toward whether the plume is seen
                // (Systems), so steam in view nearby moves every frame. Its
                // jets travel by their age, whatever the step.
                if (vents != null)
                {
                    // From the eye: a capture's held view (its camera is put
                    // there only as it draws), else the player's camera.
                    var viewer = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera : Camera.main;
#if PUBLIC_RELEASE
                    var eye = viewer != null ? viewer.transform.position : (Vector3?)null;
#else
                    var eye = PlumeCapture.HeldAt ?? (viewer != null ? viewer.transform.position : (Vector3?)null);
#endif
                    using (StackHost.TimePhase(PlumePhase.Vents))
                        vents.Step(passed, airVelocity, carVelocity, PlumeWeather.Air, ParticleShare, Look, roof,
                            Look.MatchSteamOrder > 0.5f ? Aloft(smoke) : null, eye, Emitter(smoke));
                    if (light)
                        volume?.Gather(vents.Drawn, vents.Buffer, vents.Count);
                }
                if (light)
                {
                    var sun = Sun();
                    using (StackHost.TimePhase(PlumePhase.VolumeSubmit))
                        volume?.End(sun != null && sun.isActiveAndEnabled ? -sun.transform.forward : Vector3.zero,
                            Look.Volume, Look.VolumeDensity, volumeMaterials);
                }
                foreach (var system in embers)
                    Fly(system, passed);
            }
#if !PUBLIC_RELEASE

            LogStatus(inputs);
#endif
        }

        // Whether the particles are carried this frame, and how far apart
        // their turns are: by the plume's distance from the camera and
        // whether any of its particles' bounds is in the camera's view (in
        // either eye's, in VR). Not by Renderer.isVisible: at half
        // resolution the renderers are hidden and drawn by PlumeOffscreen.
        // A capture or a test bed's stack is always carried in full.
        private bool PassDue()
        {
            if (renderers == null)
                renderers = Systems().Select(s => s.GetComponent<ParticleSystemRenderer>()).Where(r => r != null).ToArray();
            var camera = PlayerManager.ActiveCamera;
            var seen = false;
            if (camera != null)
            {
                foreach (var renderer in renderers)
                {
                    if (renderer != null && renderer.gameObject.activeInHierarchy && InView(camera, renderer.bounds))
                    {
                        seen = true;
                        break;
                    }
                }
            }
            var away = camera != null ? Vector3.Distance(camera.transform.position, smoke.transform.position) : 0f;
#if PUBLIC_RELEASE
            var every = camera == null ? 1 : PlumeTurns.FramesApart(seen, away);
#else
            var every = PlumeCapture.Running || testing != null || camera == null ? 1 : PlumeTurns.FramesApart(seen, away);
#endif
            var cameIntoView = seen && !inView;
            inView = seen || every == 1;
            passEvery = every;
            passPeriod = PlumeTurns.NewestEvery * every;
            return every == 1 || cameIntoView || (Time.frameCount + passOffset) % every == 0;
        }

        private static bool InView(Camera camera, Bounds bounds)
        {
            if (viewFrame != Time.frameCount || viewCamera != camera)
            {
                viewFrame = Time.frameCount;
                viewCamera = camera;
                viewStereo = camera.stereoEnabled;
                if (viewStereo)
                {
                    GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)
                                                           * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left), LeftView);
                    GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)
                                                           * camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right), RightView);
                }
                else
                {
                    GeometryUtility.CalculateFrustumPlanes(camera, LeftView);
                }
            }
            return GeometryUtility.TestPlanesAABB(LeftView, bounds) || viewStereo && GeometryUtility.TestPlanesAABB(RightView, bounds);
        }
    }
}
