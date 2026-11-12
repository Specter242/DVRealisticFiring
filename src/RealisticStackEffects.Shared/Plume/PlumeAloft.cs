using System.Collections.Generic;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed class PlumeAloft
    {
 public const float Lifetime = 240f;

        // A particle this close under its cap is at the roof, m.
        private const float RoofBand = 0.2f;

        private const float FadeIn = 0.4f;
        private const float FadeOut = 0.75f;

        // A particle is held narrow (the jet's width) going faster than this
        // through the air, and at its full size slower than this, in m/s.
        private const float JetFast = 8f;
        private const float JetSlow = 1.5f;

        private const float FrayEdge = 0.82f;
        private const float SoftEdge = 0.15f;
        private const float MostThinGrowth = 2.2f;

        // A particle is drawn a share of the plume's width (Air.FillWidth),
        // and drifts out from where it left the stack by up to a share of the
        // plume's widening (its rise's and the air's spread; Air.Disperse),
        // so the particles fill it.

        // The plume's eddies are about this share of its width (at least the
        // smallest, in m), stir the smoke at up to this share of its rise,
        // and drift through it at this rate; each particle turns at up to this
        // many radians a second, either way.
        private const float EddyPerWidth = 0.5f;
        private const float SmallestEddy = 2f;
        private const float EddyStir = 0.6f;
        private const float EddyDrift = 0.2f;
        private const float MostSpin = 0.3f;

        internal struct Parcel
        {
            public PlumeParcel Thickness;
            public Color Color;
            // What it turns as its white steam evaporates.
            public Color SmokeColor;
            public float Size;
            public PlumeRetirement Retirement;
            // The white its steam makes in the air it left into (null: the frame's).
            public SteamCloud Cloud;
            // Emitted from the stack by the mod: shown at once, no fade in.
            public bool FromStack;
            // The particles emitted a second as it left: those overlapping it
            // left with it (Drawn).
            public float Rate;
            // How wide the air has spread it, m, by the age it was last
            // carried at, s (PlumeRise.SpreadOn).
            public float Spread;
            public float SpreadAge;
            // When it was last carried aloft, by the game's clock, s (0: not yet).
            public float CarriedAt;
        }

        /// <summary>
        /// What the plume keeps for one particle while it lives, found by
        /// its seed once a pass (performance audit, 2026-11-11: kept in six
        /// maps by seed, each read and written again every frame, the
        /// bookkeeping was a third of the plume's cost).
        /// </summary>
        internal sealed class Record
        {
            internal Parcel Parcel;
            // The plume's opacity where it is, before its share with the
            // particles overlapping it (ExtraOf: how wispy it is drawn).
            internal float Here;
            // Its part of the plume's surface (Shape), once measured.
            internal Vector4 Shape;
            internal bool Shaped;
            // Its shade from the plume between it and the sun, of the round it was worked out in.
            internal float Shade;
            internal int ShadeRound;
            // How hard the beat was that it left on (ChimneyPlume's, for its wisp); NaN until asked.
            internal float Hard;
            // Its smoke's opacity as it left the lip (its parcel's, which does not change).
            internal float SmokeAtLip;
            // Fading out: worked out every pass, so the fade is even.
            internal bool Retiring;
            // The last cycle it was seen in: forgotten when a whole cycle passes without it.
            internal int Cycle;
        }

        private static readonly Color SteamColor = new Color(PlumeModel.Steam.R, PlumeModel.Steam.G, PlumeModel.Steam.B);

        /// <summary>The air around the stack this frame, in the system's simulation space.</summary>
        public struct Air
        {
            /// <summary>The world's up.</summary>
            public Vector3 Up;

            /// <summary>The still air's velocity (zero but in a space that moves with the stack).</summary>
            public Vector3 Still;

            /// <summary>The share of a particle's speed through the air the drag leaves after this frame.</summary>
            /// <summary>The drag toward the still air, a second: a particle keeps exp(−Drag × t) of its speed past it over t s.</summary>
            public float Drag;

            /// <summary>The time since the plume was last carried, s: a frame's, or several for a plume worked less often.</summary>
            public float Elapsed;

            /// <summary>How often its newest particles are worked out in full, s (see YoungestTier).</summary>
            public float Period;

            /// <summary>The stack's buoyancy flux, m⁴/s³.</summary>
            public float Flux;

            /// <summary>The air's speed past the stack, m/s: the wind less the train's own.</summary>
            public float Speed;

            /// <summary>The train's velocity, whose wake carries the plume's base with it.</summary>
            public Vector3 Train;

            /// <summary>The white the frame's steam makes in the air, for a particle that brought none.</summary>
            public SteamCloud Cloud;

            /// <summary>Multiplier on how fast the steam mixes into the air (<see cref="PlumeTuning.SteamMixing"/>).</summary>
            public float Mixing;

            /// <summary>Multiplier on how fast its white forms above the stack (<see cref="PlumeTuning.SteamForming"/>).</summary>
            public float Forming;

            /// <summary>The train's wake: its share of the train's speed at the lip, and the height it falls away over, m.</summary>
            public float WakeCarry;
            public float WakeDepth;

            /// <summary>How narrow a particle is held while it goes fast through the air, against its size, 0 to 1.</summary>
            public float JetWidth;

            /// <summary>The smoke shader's fraying, 0 to 1: as a particle thins its thin edges go first.</summary>
            public float Fray;

            /// <summary>Rolling steam's wisp's edge lost as it thins, × <see cref="Fray"/> (<see cref="Thinning"/>).</summary>
            public float WispFrayEdge;

            /// <summary>Rolling steam is wholly a wisp where the plume is this opaque or less, wholly a puff from the next.</summary>
            public float WispBelow;
            public float WispAbove;

            /// <summary>How hard the plume's eddies stir it, × (0 none).</summary>
            public float Eddies;

            /// <summary>
            /// The plume's rise in still air as its particles show it, × its
            /// mean (<see cref="PlumeRise.RiseSpeed"/>): a plume's visible
            /// surface moves slower than its gas does on the whole.
            /// </summary>
            public float RiseShown;

            /// <summary>How fast the air's turbulence spreads the plume as it ages, × (<see cref="PlumeTuning.TrailSpread"/>).</summary>
            public float Spread;

            /// <summary>A particle's size, × the plume's width, and how far out it drifts, × the plume's widening.</summary>
            public float FillWidth;

            /// <summary>Dense smoke's particle size, × the plume's width (drawn as the steam is: <see cref="AsSteam"/>).</summary>
            public float SmokeFillWidth;
            public float Disperse;

            /// <summary>How far a particle's smoke's opacity is shared with those overlapping it, 0 (each as opaque as the plume) to 1.</summary>
            public float Overlap;

            /// <summary>A particle this wide, m, begins to stay a puff however thin, and wholly by the next (<see cref="Remember"/>).</summary>
            public float WispLargestFrom;
            public float WispLargestTo;

            /// <summary>Past the puffs (by its age: <see cref="WispAgeFrom"/> to <see cref="WispAgeTo"/> of the flipbook's time), how far a particle's steam's opacity is shared with those overlapping it, 0 to 1.</summary>
            public float WispShare;
            public float WispAgeFrom;
            public float WispAgeTo;

            /// <summary>A particle's part of the plume's surface measured across the plume at its own age (else from its age band's middle, along the trail too).</summary>
            public bool ShapeAcross;

            /// <summary>A particle's part of the plume's surface eases to each new measure over this many s (0: at once).</summary>
            public float ShapeSmoothing;

            /// <summary>Smoke this dense as it left the stack begins to be drawn as the steam is (its opacity not shared), wholly by the next.</summary>
            public float SmokeAsSteamFrom;
            public float SmokeAsSteamTo;

            /// <summary>Whether to work out each particle's part of the plume's surface (<see cref="ShapeOf"/>).</summary>
            public bool Shape;

            /// <summary>The way to the sun (the scene's main light), or zero for none.</summary>
            public Vector3 ToSun;

            /// <summary>Where the plume is seen from, in this system's space, if known: for its silhouette.</summary>
            public bool HasEye;
            public Vector3 Eye;

            /// <summary>How dark the plume's shadow on itself is, × its particles' opacity (0: none worked out; <see cref="ExtraOf"/>).</summary>
            public float ShadowDensity;

            /// <summary>How long a particle takes to roll up into billows off the jet, s.</summary>
            public float BillowAge;

            /// <summary>
            /// Rolling steam (the smoke shader lit six ways): how many puffs
            /// its flipbooks hold (0: none), how many loops a second a puff
            /// rolls as it leaves the jet, and the age over which that halves, s.
            /// </summary>
            public int Puffs;
            public float RollRate;
            public float RollSlows;

            /// <summary>
            /// A particle first seen here, not emitted by the mod, takes this
            /// share of smoke and, if already above the stack, the stack's
            /// velocity (the game's own emission does not carry it; in the
            /// stack the caller moves it with the stack).
            /// </summary>
            public Vector3 Carried;
            public float SmokeShare;

            /// <summary>The tunnel roof along the engine's path, in this system's space, or null.</summary>
            public TunnelRoof Roof;

            /// <summary>Whether the engine is under a roof now, pushing the bore's air along.</summary>
            public bool InTunnel;

            /// <summary>Where the plume shows fainter than this its particles fade out (<see cref="SmokeDetail.Faint"/>).</summary>
            public float Faint;

            /// <summary>The stack's mouth's height on <see cref="Up"/>: a roof holds smoke down to no lower.</summary>
            public float Floor;
        }

        // A renderer draws a particle no bigger than this share of the view.
        private const float MaxScreenShare = 4f;

        // A particle is worked out in full in turns by its seed, how often
        // by its age (PlumeTurns): the passes are ChimneyPlume's, which
        // makes fewer for a plume far off or out of view.

        private const float RateSmoothing = 2f;
        private int emitted;
        private float emissionRate;

        private const float ShapeAgeStep = 0.5f;
        private const int ShapeAges = 240;
        private const int FewestForMiddle = 3;
        // How many bands either side a particle's middle is looked for in.
        private const int MiddleReach = 4;
        private readonly Vector3[] middleSum = new Vector3[ShapeAges];
        private readonly int[] middleCount = new int[ShapeAges];
        private readonly Vector3[] middle = new Vector3[ShapeAges];
        private readonly bool[] middleKnown = new bool[ShapeAges];
        // Each band's mean age: its middle is the plume's middle at that age.
        private readonly float[] middleAgeSum = new float[ShapeAges];
        private readonly float[] middleAge = new float[ShapeAges];
        /// <summary>
        /// How far through the flipbook a particle <paramref name="age"/> s
        /// old is, 0 to 1: its age over the system's own lifetime, as the
        /// flipbook plays (then holds its last frame).
        /// </summary>
        public float FlipbookAt(float age) => nominalLife > 0f ? Mathf.Clamp01(age / nominalLife) : 1f;
        internal float PlayingTime => nominalLife;

        /// <summary>
        /// Which of the wisp flipbook's eight wisps (its rows) a particle is, 0
        /// to 7: each its own, as the plume's flipbook picked a row at random.
        /// </summary>
        public static float WispRow(uint seed) => Mathf.Min(7f, Mathf.Floor(Hash(seed ^ 0x3c6ef372u) * 8f));

        /// <summary>Where a particle's wisp starts in its row, 0 to 1, apart from its row and turn.</summary>
        public static float WispPhase(uint seed) => Hash(seed ^ 0xa54ff53au);

        /// <summary>
        /// A particle's wisp's own turn, radians: any way at birth, spinning
        /// up to <paramref name="spin"/> rad/s either way as 1.13's particles
        /// did, apart from the rolling puffs' near-upright pose; slowing as
        /// the wisp grows, as an eddy's turn slows with its size, its rate
        /// over 1 + its age over <paramref name="ease"/> s (0: steady).
        /// </summary>
        public static float WispTurn(uint seed, float age, float spin, float ease)
        {
            var turned = ease > 0f ? ease * Mathf.Log(1f + age / ease) : age;
            return Hash(seed ^ 0x9e3779b9u) * 2f * Mathf.PI + (2f * Hash(seed ^ 0x7f4a7c15u) - 1f) * spin * turned;
        }

        /// <summary>How hard the beat was that <paramref name="record"/>'s particle left on: <paramref name="now"/>'s, the first time it is asked.</summary>
        public float HardOf(Record record, float now)
        {
            if (float.IsNaN(record.Hard))
                record.Hard = now;
            return record.Hard;
        }

        private const int ShadowCells = 40;
        private const float SmallestCell = 1f;
        private const float MarchFrom = 1.5f;
        private const float ShadowEvery = 0.1f;
        private const float ShadeEasing = 0.5f;
        private float nextShadow;
        private bool shadowTurn;
        private struct Caster
        {
            public Vector3 Position;
            public float Size;
            public float Opacity;
            public Record Record;
        }
        private readonly List<Caster> casters = new List<Caster>();
        private float[] grid = new float[0];
        // The round of shades in force: a record's shade of another round counts as none.
        private int shadeRound;
        private float meanShade;
        private float shadedShare;

        /// <summary>
        /// A particle's second custom data for the smoke shader: how far it
        /// has rolled up into billows, 0 to 1 (x), how much of the sun the
        /// plume between keeps from it, 0 to 1 (y), and as rolling steam how
        /// far its puff has rolled, in loops (z), and which puff it is, plus
        /// a half when mirrored (w). As rolling steam x is instead the
        /// plume's opacity where it is, before its share with the particles
        /// overlapping it: a dense plume's particles are each faint, and by
        /// their own opacity dense black smoke turned to wisps; and y how far
        /// it is on the plume's silhouette as seen, 0 to 1 (its way out from
        /// the plume's middle across the view, × how far out it is): there
        /// its thin edge frays into strands, which over the plume's faces
        /// turned toward the view veiled its lobes.
        /// </summary>
        public Vector4 ExtraOf(Record record, uint seed, float age, Vector3 position)
        {
            age = Mathf.Max(0f, age);
            var onset = air.BillowAge > 0f ? Mathf.SmoothStep(0f, 1f, age / air.BillowAge) : 1f;
            var roll = 0f;
            var puff = 0f;
            if (air.Puffs > 0)
            {
                // Each its own puff, mirrored or not, from its own place in
                // the loop; fast off the jet, slowing as its eddies spend.
                puff = Mathf.Min(air.Puffs - 1, Mathf.Floor(Hash(seed ^ 0x68e31da4u) * air.Puffs))
                       + (Hash(seed ^ 0xb5297a4du) < 0.5f ? 0.5f : 0f);
                // Each at its own pace, so puffs of an age do not turn together.
                roll = Hash(seed ^ 0x1b56c4e9u) + Rolled(age, air.RollRate, air.RollSlows) * (0.7f + 0.6f * Hash(seed ^ 0x2545f491u));
            }
            var shade = record.ShadeRound == shadeRound ? record.Shade : 0f;
            if (air.Puffs > 0)
            {
                onset = record.Here;
                shade = 0f;
                var shape = record.Shape;
                if (air.HasEye && shape.w > 0f)
                {
                    var toEye = (air.Eye - position).normalized;
                    var facing = Vector3.Dot(new Vector3(shape.x, shape.y, shape.z), toEye);
                    shade = shape.w * Mathf.Sqrt(Mathf.Max(0f, 1f - facing * facing));
                }
            }
            return new Vector4(onset, shade, roll, puff);
        }

        /// <summary>
        /// How many loops a puff has rolled by <paramref name="age"/> s,
        /// rolling <paramref name="rate"/> loops a second off the jet and
        /// half that by <paramref name="slows"/> s: the turbulence the jet
        /// leaves dies away as the puff mixes into the air.
        /// </summary>
        public static float Rolled(float age, float rate, float slows) =>
            slows > 0f ? rate * slows * Mathf.Log(1f + Mathf.Max(0f, age) / slows) : rate * Mathf.Max(0f, age);

        // A particle not carried for longer than this, s, is carried as if for this long.
        private const float MostSince = 4f;

        private readonly ParticleSystem system;
        private readonly float nominalLife;
        private readonly ParticleSystem.MinMaxCurve growth;
        // Every living particle's record, by its seed; those of the dead, to use again.
        private readonly Dictionary<uint, Record> records = new Dictionary<uint, Record>();
        private readonly Stack<Record> spare = new Stack<Record>();
        private readonly List<uint> gone = new List<uint>();
        private int cycle;

        /// <summary>The record of the particle last carried.</summary>
        public Record Last { get; private set; }

        private Record Fresh(uint seed, Parcel parcel)
        {
            var record = spare.Count > 0 ? spare.Pop() : new Record();
            record.Parcel = parcel;
            record.SmokeAtLip = PlumeRise.OpacityOf(parcel.Thickness.SmokeDepth, 0f);
            record.Here = 1f;
            record.Shape = Vector4.zero;
            record.Shaped = false;
            record.Shade = 0f;
            record.ShadeRound = -1;
            record.Hard = float.NaN;
            record.Retiring = false;
            record.Cycle = cycle;
            records[seed] = record;
            return record;
        }
        private Air air;
        // The pass within the cycle, and the cycle's turns.
        private int turn;
        private PlumeTurns turns;
        private float now;
        private bool shaping;
        private float cycleTop;
        // The horizontal in the simulation space, across the world's up.
        private Vector3 across;
        private Vector3 along;

        /// <summary>The highest particle carried in the last full turn, m above the stack.</summary>
        public float Top { get; private set; }

        /// <summary>What the particles looked like in the last full turn, for the status log.</summary>
        public struct Stats
        {
            public int InStack;
            public int Aloft;
            public int Retired;
            public float SizeSum;
            public float AlphaSum;
            public float Widest;
            // The widest particle as the system draws it, after its size modules.
            public float WidestDrawn;
            public float WidestHeight;
        }

        public Stats Seen { get; private set; }
        private Stats stats;

        /// <summary>The system's settings that shape how its particles are drawn, for the log.</summary>
        public string Setup { get; }

        public PlumeAloft(ParticleSystem system, int maxParticles)
        {
            this.system = system;
            var main = system.main;
            nominalLife = MeanLifetime(main.startLifetime);
            main.startLifetime = Lifetime;
            main.maxParticles = Mathf.Max(main.maxParticles, maxParticles);

            var grows = system.sizeOverLifetime;
            growth = grows.enabled ? grows.size : new ParticleSystem.MinMaxCurve(1f);
            grows.enabled = false;

            // The flipbook, played over the particle's life, would stay on its
            // first frames for a life of minutes: it plays over the system's
            // own lifetime instead, then holds its last frame.
            var frames = system.textureSheetAnimation;
            var flipbook = "no flipbook";
            if (frames.enabled && frames.timeMode == ParticleSystemAnimationTimeMode.Lifetime)
            {
                frames.frameOverTime = Compress(frames.frameOverTime, nominalLife / Lifetime);
                flipbook = $"flipbook {frames.numTilesX}×{frames.numTilesY} over {nominalLife:F1} s";
            }

            // A slow turn, each its own way, so the wisps do not hold one pose.
            var spin = system.rotationOverLifetime;
            if (!spin.enabled)
            {
                spin.enabled = true;
                spin.separateAxes = false;
                spin.z = new ParticleSystem.MinMaxCurve(-MostSpin, MostSpin);
            }

            // Turbulence that builds over the particle's life builds over the system's own lifetime.
            var turbulence = system.noise;
            if (turbulence.enabled && !turbulence.separateAxes)
                turbulence.strength = Compress(turbulence.strength, nominalLife / Lifetime);

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
                renderer.maxParticleSize = Mathf.Max(renderer.maxParticleSize, MaxScreenShare);
            Setup = $"{system.name}: life {nominalLife:F1} s, {flipbook}, scaling {main.scalingMode}, scale {system.transform.lossyScale.x:F2}, " +
                    $"space {main.simulationSpace}, up·world {Vector3.Dot(system.transform.up, Vector3.up):F2}, " +
                    $"renderer {(renderer != null ? $"{renderer.renderMode}, largest {renderer.maxParticleSize:F1} of the view" : "none")}";

            // Keep the darkening with age in the colors; the fade is the parcel's.
            var tint = system.colorOverLifetime;
            if (tint.enabled && tint.color.mode == ParticleSystemGradientMode.Gradient && tint.color.gradient != null)
            {
                var flat = new Gradient { mode = tint.color.gradient.mode };
                flat.SetKeys(tint.color.gradient.colorKeys,
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.99f), new GradientAlphaKey(0f, 1f) });
                tint.color = new ParticleSystem.MinMaxGradient(flat);
            }
        }

        /// <summary>
        /// Records what a particle about to be emitted with <paramref name="seed"/>
        /// carries: its steam and smoke, and the color of its smoke.
        /// </summary>
        public void Expect(uint seed, Color color, float size, float smokeShare, Color smokeColor, SteamCloud cloud)
        {
            emitted++;
            // Kept through the next cycle's end, though the engine has yet to show the particle.
            Fresh(seed, new Parcel
            {
                Thickness = PlumeRise.Parcel(color.a, smokeShare),
                Color = color,
                SmokeColor = smokeColor,
                Size = size,
                Cloud = cloud,
                FromStack = true,
                Rate = emissionRate,
            }).Cycle = cycle + 1;
        }

        public void Begin(Air frame)
        {
            var dt = Mathf.Max(frame.Elapsed, 1e-4f);
            emissionRate += (emitted / dt - emissionRate) * (1f - Mathf.Exp(-dt / RateSmoothing));
            emitted = 0;
            air = frame;
            now = Time.time;
            shaping = frame.Shape || frame.Puffs > 0;
            if (turn == 0)
            {
                turns = PlumeTurns.Plan(frame.Period, dt);
                stats = default;
                cycleTop = 0f;
                shadowTurn = frame.ShadowDensity > 0f && Time.time >= nextShadow;
                if (frame.ShadowDensity <= 0f)
                    shadeRound++;
            }
            across = Vector3.Cross(frame.Up, Vector3.forward);
            if (across.sqrMagnitude < 1e-4f)
                across = Vector3.Cross(frame.Up, Vector3.right);
            across.Normalize();
            along = Vector3.Cross(across, frame.Up).normalized;
        }

        // The particle Turn last looked at, for Work.
        private float turnAge;
        private Record turnRecord;

        /// <summary>
        /// Finds <paramref name="particle"/>'s record (<see cref="Last"/>),
        /// and whether this pass is its turn to be worked out in full
        /// (<see cref="Work"/>, with its height and widths, which only a
        /// particle on its turn needs): by its age, or still fading in, or
        /// fading out, or first seen. Else the game moves it as it is.
        /// </summary>
        public bool Turn(ref ParticleSystem.Particle particle, bool inStack)
        {
            var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
            var seed = particle.randomSeed;
            // Among those of its age, for the plume's middle there (Shape).
            if (shaping && !inStack)
            {
                var band = Mathf.Min(ShapeAges - 1, (int)(age / ShapeAgeStep));
                middleSum[band] += particle.position;
                middleAgeSum[band] += age;
                middleCount[band]++;
            }
            turnAge = age;
            if (records.TryGetValue(seed, out var record))
            {
                if (record.Cycle < cycle)
                    record.Cycle = cycle;
                Last = turnRecord = record;
                return age < FadeIn || record.Retiring || turns.Due(seed, age, turn);
            }
            else
            {
                // First seen (not emitted from the stack by the mod): worked out at once.
                record = Fresh(seed, new Parcel
                {
                    Thickness = PlumeRise.Parcel(((Color)particle.startColor).a, air.SmokeShare),
                    Color = particle.startColor,
                    SmokeColor = particle.startColor,
                    Size = particle.startSize,
                    Rate = emissionRate,
                });
                Last = turnRecord = record;
                if (!inStack)
                    particle.velocity += air.Carried;
                return true;
            }
        }

        /// <summary>
        /// Works out the particle <see cref="Turn"/> just found due,
        /// <paramref name="height"/> m above the stack and drawn no narrower
        /// than <paramref name="least"/> (the bore, at the lip) and no wider
        /// than <paramref name="widest"/>; still <paramref name="inStack"/>,
        /// only its size and fade (the caller moves it). False when it is
        /// retired.
        /// </summary>
        public bool Work(ref ParticleSystem.Particle particle, float height, float least, float widest, bool inStack)
        {
            var age = turnAge;
            var seed = particle.randomSeed;
            var record = turnRecord;
            ref var parcel = ref record.Parcel;
            // Once a cycle, for the cycle's figures.
            var onTurn = turns.Counted(seed, turn);
            // The drag over the time since it was last carried: a pass, or
            // as many as its turns are apart.
            var since = parcel.CarriedAt > 0f ? Mathf.Clamp(now - parcel.CarriedAt, 1e-4f, MostSince) : Mathf.Max(air.Elapsed, 1e-4f);
            var keep = Mathf.Exp(-air.Drag * since);

            var grown = parcel.Size * growth.Evaluate(nominalLife > 0f ? Mathf.Clamp01(age / nominalLife) : 1f);
            var shown = 1f;
            var color = parcel.Color;
            var leaving = 1f;
            var cloud = parcel.Cloud ?? air.Cloud;
            if (inStack)
            {
                // Moving with the stack: its speed through the air is its rise.
                var narrow = JetNarrowing((particle.velocity - air.Train).magnitude);
                particle.startSize = Mathf.Min(Mathf.Max(grown * narrow, least), widest);
                // Its smoke shows at once, its white only as it forms.
                var formed = PlumeRise.SteamLeft(parcel.Thickness, cloud, age, air.Speed, air.Mixing, air.Forming);
                var whole = formed + parcel.Thickness.SmokeDepth;
                var smokeAtLip = record.SmokeAtLip;
                var steamAtLip = PlumeRise.OpacityOf(formed, 0f);
                Remember(record, 1f - (1f - steamAtLip) * (1f - smokeAtLip), particle.startSize);
                shown = Drawn(steamAtLip, smokeAtLip,
                    particle.startSize, 1f, (particle.velocity - air.Train).magnitude, AsSteam(smokeAtLip), parcel.Rate);
                if (whole > 1e-4f)
                    color = Color.Lerp(SteamColor, parcel.SmokeColor, PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / whole));
                if (onTurn)
                    stats.InStack++;
            }
            else
            {
                // Near the train its wake carries the plume along, and the air
                // goes past it the slower.
                var wake = PlumeRise.Wake(height, air.WakeCarry, air.WakeDepth);
                var airSpeed = air.Speed * (1f - wake);
                var still = air.Still + (air.Train - air.Still) * wake;
                // Under a tunnel's roof, a train in the bore pushes its air along.
                var roof = default(TunnelRoof.Sample);
                var under = air.Roof != null && air.Roof.TryFind(particle.position, out roof);
                if (under && air.InTunnel)
                {
                    still += (air.Train - still) * TunnelSmoke.PistonShare;
                    airSpeed *= 1f - TunnelSmoke.PistonShare;
                }
                var rise = PlumeRise.RiseSpeed(air.Flux, height, airSpeed, air.RiseShown);
                // Out its own way, evenly over the plume's breadth as it widens,
                // with its rise and the air's turbulence.
                var angle = Hash(particle.randomSeed ^ 0x5bd1e995u) * 2f * Mathf.PI;
                var share = air.Disperse * Mathf.Sqrt(Hash(particle.randomSeed ^ 0x27d4eb2du));
                var smokeAtLip = record.SmokeAtLip;
                var asSteam = AsSteam(smokeAtLip);
                // Spread on from where it was, so a train that slows leaves it as spread.
                parcel.Spread = PlumeRise.SpreadOn(parcel.Spread, age - parcel.SpreadAge, air.Speed, air.Spread);
                parcel.SpreadAge = age;
                var spread = parcel.Spread;
                // How thin, how wide, and how few the plume is here: once for all that follows.
                var dilution = PlumeRise.Dilution(height, spread);
                var width = PlumeRise.PlumeWidth(height, spread);
                var survival = PlumeRise.SurvivalAt(width);
                var smokeHere = PlumeRise.OpacityAt(parcel.Thickness.SmokeDepth, dilution);
                var widening = PlumeRise.WidthPerHeight * rise + PlumeRise.SpreadRate(air.Speed, air.Spread);
                var outward = (across * Mathf.Cos(angle) + along * Mathf.Sin(angle)) * (share * widening);
                var stir = Stir(particle.position, height, rise, now) * air.Eddies;
                // White steam tinted by its smoke, turning the smoke's color
                // as the steam evaporates (as its puff was tinted at the stack).
                var steam = PlumeRise.SteamLeft(parcel.Thickness, cloud, age, air.Speed, air.Mixing, air.Forming);
                var depth = steam + parcel.Thickness.SmokeDepth;
                var plumeShown = PlumeRise.OpacityAt(depth, dilution);
                shown = plumeShown;
                if (depth > 1e-4f)
                    color = Color.Lerp(SteamColor, parcel.SmokeColor, PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / depth));
                var narrow = JetNarrowing((particle.velocity - still).magnitude);
                var fill = Mathf.Lerp(air.FillWidth, air.SmokeFillWidth, asSteam);
                var steamHere = PlumeRise.OpacityAt(steam, dilution);
                var here = 1f - (1f - steamHere) * (1f - smokeHere);
                var wisp = air.WispAgeTo > air.WispAgeFrom
                    ? Mathf.SmoothStep(0f, 1f, (FlipbookAt(age) - air.WispAgeFrom) / (air.WispAgeTo - air.WispAgeFrom))
                    : 1f;
                // Rolling steam drawn as a wisp, as the shader has it: where
                // the plume is thin, or past the puffs by its age.
                var asWisp = air.Puffs > 0
                    ? Mathf.Max(1f - Mathf.SmoothStep(0f, 1f, (here - air.WispBelow) / Mathf.Max(0.01f, air.WispAbove - air.WispBelow)), wisp)
                    : 0f;
                var size = Mathf.Min(Mathf.Max(Mathf.Max(grown, width * fill) * narrow, least) * Thinning(shown, asWisp), widest);
                Remember(record, here, size);
                shown = Drawn(steamHere, smokeHere,
                    size, survival, Mathf.Max(airSpeed, rise), asSteam, parcel.Rate, air.WispShare * wisp);
                if (under)
                    size = Mathf.Min(size, TunnelSmoke.WidestParticle);
                else
                {
                    var target = still + air.Up * rise + outward + stir;
                    particle.velocity = target + (particle.velocity - target) * keep;
                }
                particle.startSize = size;
                if (under)
                    UnderRoof(ref particle, roof, still, rise, outward + stir, age, keep);
                if (shaping)
                    Shape(record, particle.position, particle.velocity, age, width, since);
                // Fewer, wider particles carry the plume as it widens: one past
                // its share fades out, then goes; one too thin to see goes at once.
                // At a lower smoke quality one where the plume is faint fades
                // out as well (Air.Faint), unless its white is still forming.
                // (Whether its white has formed is asked only of one faint
                // enough to go: it decides nothing else.)
                var faint = age > FadeIn && plumeShown < Mathf.Max(air.Faint, PlumeRise.Invisible);
                var formedAll = faint && (parcel.Thickness.SteamDepth < 1e-3f || cloud == null
                                          || cloud.PastThickest(age, air.Speed, air.Mixing, air.Forming));
                leaving = parcel.Retirement.Fade(now,
                    PlumeRetirement.ShouldRetire(age, FadeIn, plumeShown, air.Faint, formedAll, Hash(seed) <= survival), FadeOut);
                parcel.CarriedAt = now;
                if (leaving < 1f)
                    record.Retiring = true;
                // Faintness begins retirement; only the completed fade removes
                // the parcel. Physical opacity is not the shader's visible alpha.
                if (leaving <= 0f)
                {
                    particle.remainingLifetime = 0f;
                    if (onTurn)
                        stats.Retired++;
                    return false;
                }
                cycleTop = Mathf.Max(cycleTop, height);
                if (onTurn)
                {
                    stats.Aloft++;
                    stats.SizeSum += particle.startSize;
                    stats.AlphaSum += shown;
                    if (particle.startSize > stats.Widest)
                    {
                        stats.Widest = particle.startSize;
                        stats.WidestDrawn = particle.GetCurrentSize(system);
                        stats.WidestHeight = height;
                    }
                }
            }
            var fade = parcel.FromStack ? 1f : Mathf.Clamp01(age / FadeIn);
            particle.startColor = new Color(color.r, color.g, color.b, shown * fade * leaving);
            if (onTurn && shadowTurn)
                casters.Add(new Caster { Position = particle.position, Size = particle.startSize, Opacity = shown * fade * leaving, Record = record });
            return true;
        }

        // Under a tunnel's roof: up to the roof, then along it its own way as
        // a ceiling jet that slows, its spread and stirring only along the
        // bore, and held between the walls.
        private void UnderRoof(ref ParticleSystem.Particle particle, TunnelRoof.Sample roof, Vector3 still, float rise,
            Vector3 drift, float age, float keep)
        {
            var size = particle.startSize;
            var cap = TunnelSmoke.Cap(Vector3.Dot(roof.Point, air.Up), size, air.Floor);
            var level = Vector3.Dot(particle.position, air.Up);
            var atRoof = level >= cap - RoofBand;
            var way = Hash(particle.randomSeed ^ 0x165667b1u) < 0.5f ? -1f : 1f;
            var jet = atRoof ? way * TunnelSmoke.JetSpeed(rise, age) : 0f;
            var target = still + air.Up * (atRoof ? 0f : rise) + roof.Along * (Vector3.Dot(drift, roof.Along) + jet);
            particle.velocity = target + (particle.velocity - target) * keep;
            if (level > cap)
                particle.position += air.Up * (cap - level);
            if (atRoof)
            {
                var upward = Vector3.Dot(particle.velocity, air.Up);
                if (upward > 0f)
                    particle.velocity -= air.Up * upward;
            }
            var side = Vector3.Dot(particle.position - roof.Point, roof.Across);
            var reach = TunnelSmoke.WallReach(size);
            if (Mathf.Abs(side) <= reach)
                return;
            var sign = Mathf.Sign(side);
            particle.position -= roof.Across * (side - sign * reach);
            var outwards = sign * Vector3.Dot(particle.velocity, roof.Across);
            if (outwards > 0f)
                particle.velocity -= roof.Across * (sign * outwards);
        }

        // A particle's opacity drawn, its white steam's <paramref name="steam"/>
        // and its smoke's <paramref name="smoke"/>: its smoke's shared with
        // the particles overlapping it, <paramref name="size"/> m wide, the
        // share <paramref name="kept"/> still carried, moving away at
        // <paramref name="speed"/> m/s, <paramref name="rate"/> a second
        // emitted as it was, so together they show the plume's.
        // The steam's is not: its opacity was set by eye drawn this way, and
        // white steam is thick enough that its overlap only fills it in
        // (shared, a frosty day's billows drew as thin, frayed wisps). Past
        // the puffs it is, by <paramref name="steamSharing"/> (Air.WispShare):
        // stacked ten and more deep, the wisps drew an opaque, soft mass.
        private float Drawn(float steam, float smoke, float size, float kept, float speed, float asSteam, float rate, float steamSharing = 0f)
        {
            // Dense smoke is drawn as the steam is, by how dense it left the
            // stack (<paramref name="asSteam"/>, AsSteam of its opacity at
            // the lip): thick enough that its overlap only fills it in.
            var sharing = air.Overlap * (1f - asSteam);
            var overlaps = rate * size * kept / Mathf.Max(1f, speed);
            var smokeDrawn = 1f - Mathf.Pow(1f - Mathf.Min(smoke, 0.999f), 1f / (1f + sharing * overlaps));
            var steamDrawn = 1f - Mathf.Pow(1f - Mathf.Min(steam, 0.999f), 1f / (1f + steamSharing * overlaps));
            return 1f - (1f - steamDrawn) * (1f - smokeDrawn);
        }

        private float Thinning(float shown, float asWisp)
        {
            var edge = Mathf.Lerp(FrayEdge * Mathf.Clamp01(air.Fray) + SoftEdge, air.WispFrayEdge * Mathf.Clamp01(air.Fray), asWisp);
            return Mathf.Min(MostThinGrowth, 1f / Mathf.Max(0.05f, 1f - edge * (1f - Mathf.Clamp01(shown))));
        }

        // While a particle goes fast through the air it is held narrow, the
        // jet's width; slowing, it swells to its size.
        private float JetNarrowing(float speed) =>
            Mathf.Lerp(1f, Mathf.Clamp01(air.JetWidth), Mathf.Clamp01((speed - JetSlow) / (JetFast - JetSlow)));

        // The plume's eddies at a point: a smooth field drifting through the
        // plume, its whorls as big as half its width there.
        private static Vector3 Stir(Vector3 position, float height, float rise, float now)
        {
            var p = position / Mathf.Max(SmallestEddy, EddyPerWidth * PlumeRise.Width(height));
            var t = now * EddyDrift;
            var x = Mathf.PerlinNoise(p.y + t, p.z + 13.1f) * 2f - 1f;
            var y = Mathf.PerlinNoise(p.z + t, p.x + 7.7f) * 2f - 1f;
            var z = Mathf.PerlinNoise(p.x + t, p.y + 3.3f) * 2f - 1f;
            return new Vector3(x, 0.5f * y, z) * (EddyStir * rise);
        }

        // Records a particle among those of its age, and its part of the
        // plume's surface from their middle as the last full turn found it.
        private void Shape(Record record, Vector3 position, Vector3 velocity, float age, float width, float since)
        {
            var at = Mathf.Min(ShapeAges - 1, (int)(age / ShapeAgeStep));
            // Its way out from the plume's middle, × how far out.
            var seen = Vector3.zero;
            var measured = false;
            if (air.ShapeAcross)
            {
                int below = -1, above = -1;
                for (var k = Mathf.Min(at + 1, ShapeAges - 1); k >= Mathf.Max(0, at - MiddleReach); k--)
                    if (middleKnown[k] && middleAge[k] <= age) { below = k; break; }
                for (var k = Mathf.Max(0, at - 1); k <= Mathf.Min(ShapeAges - 1, at + MiddleReach); k++)
                    if (middleKnown[k] && middleAge[k] > age) { above = k; break; }
                if (below >= 0 || above >= 0)
                {
                    Vector3 centre, along;
                    if (below >= 0 && above >= 0)
                    {
                        var t = (age - middleAge[below]) / Mathf.Max(1e-3f, middleAge[above] - middleAge[below]);
                        centre = Vector3.Lerp(middle[below], middle[above], t);
                        along = middle[above] - middle[below];
                    }
                    else
                    {
                        centre = middle[below >= 0 ? below : above];
                        along = velocity;
                    }
                    var off = position - centre;
                    if (along.sqrMagnitude > 1e-6f)
                    {
                        along.Normalize();
                        off -= along * Vector3.Dot(off, along);
                    }
                    var outward = Mathf.Clamp01(off.magnitude / Mathf.Max(0.5f, 0.5f * width));
                    if (outward > 1e-3f)
                        seen = off.normalized * outward;
                    measured = true;
                }
            }
            else if (middleKnown[at])
            {
                var off = position - middle[at];
                var outward = Mathf.Clamp01(off.magnitude / Mathf.Max(0.5f, 0.5f * width));
                if (outward > 1e-3f)
                    seen = off.normalized * outward;
                measured = true;
            }
            // Eased to it, kept while there is nothing to measure it from.
            if (air.ShapeSmoothing > 0f && record.Shaped)
            {
                var before = record.Shape;
                var was = new Vector3(before.x, before.y, before.z) * before.w;
                seen = measured
                    ? Vector3.Lerp(was, seen, 1f - Mathf.Exp(-since / air.ShapeSmoothing))
                    : was;
            }
            var far = seen.magnitude;
            record.Shape = far > 1e-3f ? new Vector4(seen.x / far, seen.y / far, seen.z / far, Mathf.Clamp01(far)) : Vector4.zero;
            record.Shaped = true;
        }

        // The turn ends; when every particle has had its turn, the dead are
        // forgotten and the turn's figures shown.
        // How far smoke as dense as smokeAtLip as it left the stack is drawn as
        // the steam is, 0 to 1.
        private float AsSteam(float smokeAtLip) =>
            Mathf.SmoothStep(0f, 1f, (smokeAtLip - air.SmokeAsSteamFrom) / Mathf.Max(0.01f, air.SmokeAsSteamTo - air.SmokeAsSteamFrom));

        // The plume's opacity at a particle <paramref name="size"/> m wide,
        // for how wispy it is drawn, kept for this turn and seen from the
        // last. Thinning smoke of any kind frays into wisps (the photos'
        // black smoke tears into streaks where it thins), but a giant
        // particle far down the trail stays a puff: its wisp's strands, tens
        // of meters across, drew hard, straight shards.
        private void Remember(Record record, float opacity, float size)
        {
            var giant = air.WispLargestTo > air.WispLargestFrom
                ? Mathf.SmoothStep(0f, 1f, (size - air.WispLargestFrom) / (air.WispLargestTo - air.WispLargestFrom))
                : 0f;
            opacity = Mathf.Lerp(opacity, 1f, giant);
            record.Here = opacity;
        }

        public void End()
        {
            // The plume's middle at each age, from every particle this pass.
            if (shaping)
            {
                for (var i = 0; i < ShapeAges; i++)
                {
                    middleKnown[i] = middleCount[i] >= FewestForMiddle;
                    if (middleKnown[i])
                    {
                        middle[i] = middleSum[i] / middleCount[i];
                        middleAge[i] = middleAgeSum[i] / middleCount[i];
                    }
                    middleSum[i] = Vector3.zero;
                    middleAgeSum[i] = 0f;
                    middleCount[i] = 0;
                }
            }
            if (++turn < turns.Passes)
                return;
            turn = 0;
            // Every particle has had its turn: those not seen all cycle are dead.
            gone.Clear();
            foreach (var kept in records)
                if (kept.Value.Cycle < cycle)
                    gone.Add(kept.Key);
            foreach (var seed in gone)
            {
                spare.Push(records[seed]);
                records.Remove(seed);
            }
            cycle++;
            Seen = stats;
            Top = cycleTop;
            if (shadowTurn)
            {
                nextShadow = Time.time + ShadowEvery;
                Shadows();
            }
        }

        // Each particle's shade from the plume between it and the sun, from the turn's particles.
        private void Shadows()
        {
            using var timing = StackHost.TimePhase(PlumePhase.Shadows);
            var toSun = air.ToSun;
            if (casters.Count == 0 || air.ShadowDensity <= 0f || toSun.sqrMagnitude < 0.5f)
            {
                casters.Clear();
                shadeRound++;
                meanShade = shadedShare = 0f;
                return;
            }
            toSun.Normalize();
            var low = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var high = -low;
            foreach (var caster in casters)
            {
                var reach = Vector3.one * (0.5f * caster.Size);
                low = Vector3.Min(low, caster.Position - reach);
                high = Vector3.Max(high, caster.Position + reach);
            }
            var extent = high - low;
            var cell = Mathf.Max(SmallestCell, Mathf.Max(extent.x, Mathf.Max(extent.y, extent.z)) / (ShadowCells - 2));
            // A cell's margin round the plume, so the spread smoke stays in.
            low -= Vector3.one * cell;
            var nx = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.x / cell) + 3);
            var ny = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.y / cell) + 3);
            var nz = Mathf.Min(ShadowCells, Mathf.CeilToInt(extent.z / cell) + 3);
            var cells = nx * ny * nz;
            if (grid.Length < cells)
                grid = new float[cells];
            else
                System.Array.Clear(grid, 0, cells);

            // Each particle's smoke, spread over the eight cells round it.
            var perVolume = 1f / (cell * cell * cell);
            foreach (var caster in casters)
            {
                var depth = -Mathf.Log(1f - Mathf.Clamp(caster.Opacity, 0f, 0.95f));
                var smoke = depth * (Mathf.PI / 6f) * caster.Size * caster.Size * perVolume;
                Splat((caster.Position - low) / cell, smoke, nx, ny, nz);
            }

            // Each particle's way to the sun through it, a cell a step: the light left is its sun.
            var total = 0f;
            var shaded = 0;
            // A new round: a particle not among this turn's casters has no shade.
            var before = shadeRound++;
            foreach (var caster in casters)
            {
                var from = Mathf.Max(MarchFrom, 0.35f * caster.Size / cell);
                var at = (caster.Position - low) / cell + toSun * from;
                var depth = 0f;
                for (var k = 0; k < 2 * ShadowCells; k++)
                {
                    if (at.x < 0f || at.y < 0f || at.z < 0f || at.x > nx - 1 || at.y > ny - 1 || at.z > nz - 1)
                        break;
                    depth += Sample(at, nx, ny, nz);
                    at += toSun;
                }
                var shade = 1f - Mathf.Exp(-depth * cell * air.ShadowDensity);
                if (caster.Record.ShadeRound == before)
                    shade = caster.Record.Shade + (shade - caster.Record.Shade) * ShadeEasing;
                caster.Record.Shade = shade;
                caster.Record.ShadeRound = shadeRound;
                total += shade;
                if (shade > 0.5f)
                    shaded++;
            }
            meanShade = total / casters.Count;
            shadedShare = (float)shaded / casters.Count;
            casters.Clear();
        }

        private void Splat(Vector3 at, float amount, int nx, int ny, int nz)
        {
            var x0 = Mathf.Clamp(Mathf.FloorToInt(at.x), 0, nx - 2);
            var y0 = Mathf.Clamp(Mathf.FloorToInt(at.y), 0, ny - 2);
            var z0 = Mathf.Clamp(Mathf.FloorToInt(at.z), 0, nz - 2);
            var fx = Mathf.Clamp01(at.x - x0);
            var fy = Mathf.Clamp01(at.y - y0);
            var fz = Mathf.Clamp01(at.z - z0);
            for (var dz = 0; dz < 2; dz++)
            for (var dy = 0; dy < 2; dy++)
            for (var dx = 0; dx < 2; dx++)
            {
                var weight = (dx == 0 ? 1f - fx : fx) * (dy == 0 ? 1f - fy : fy) * (dz == 0 ? 1f - fz : fz);
                grid[((z0 + dz) * ny + y0 + dy) * nx + x0 + dx] += amount * weight;
            }
        }

        private float Sample(Vector3 at, int nx, int ny, int nz)
        {
            var x0 = Mathf.Min(Mathf.FloorToInt(at.x), nx - 2);
            var y0 = Mathf.Min(Mathf.FloorToInt(at.y), ny - 2);
            var z0 = Mathf.Min(Mathf.FloorToInt(at.z), nz - 2);
            var fx = at.x - x0;
            var fy = at.y - y0;
            var fz = at.z - z0;
            var row = (z0 * ny + y0) * nx + x0;
            var up = nx;
            var deep = nx * ny;
            var a = grid[row] + (grid[row + 1] - grid[row]) * fx;
            var b = grid[row + up] + (grid[row + up + 1] - grid[row + up]) * fx;
            var c = grid[row + deep] + (grid[row + deep + 1] - grid[row + deep]) * fx;
            var d = grid[row + deep + up] + (grid[row + deep + up + 1] - grid[row + deep + up]) * fx;
            var near = a + (b - a) * fy;
            var far = c + (d - c) * fy;
            return near + (far - near) * fz;
        }

        /// <summary>This frame's particles in a sentence, for the status log.</summary>
        public string Describe()
        {
            var s = Seen;
            return $"{s.InStack} in the stack, {s.Aloft} aloft (mean {(s.Aloft > 0 ? s.SizeSum / s.Aloft : 0f):F1} m, " +
                   $"widest {s.Widest:F1} m drawn {s.WidestDrawn:F1} m at {s.WidestHeight:F0} m, mean opacity {(s.Aloft > 0 ? s.AlphaSum / s.Aloft : 0f):F2}), " +
                   $"{s.Retired} retired" +
                   (air.ShadowDensity > 0f ? $"; its own shadow: mean shade {meanShade:F2}, {shadedShare:P0} over half" : "");
        }

        // A curve over a particle's life squeezed into the first share of it,
        // then held at its end.
        private static ParticleSystem.MinMaxCurve Compress(ParticleSystem.MinMaxCurve curve, float share)
        {
            share = Mathf.Clamp(share, 0.001f, 1f);
            AnimationCurve Squeeze(AnimationCurve c)
            {
                if (c == null || c.length == 0)
                    return c;
                var keys = new Keyframe[c.length + 1];
                for (var i = 0; i < c.length; i++)
                {
                    var k = c[i];
                    keys[i] = new Keyframe(k.time * share, k.value, k.inTangent / share, k.outTangent / share);
                }
                var end = c.Evaluate(1f);
                keys[c.length] = new Keyframe(1f, end, 0f, 0f);
                keys[c.length - 1].outTangent = 0f;
                return new AnimationCurve(keys);
            }
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Curve:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Squeeze(curve.curveMax));
                case ParticleSystemCurveMode.TwoCurves:
                    return new ParticleSystem.MinMaxCurve(curve.curveMultiplier, Squeeze(curve.curveMin), Squeeze(curve.curveMax));
                default:
                    return curve;
            }
        }

        // An even spread from 0 to 1 by the particle's seed.
        private static float Hash(uint seed) => unchecked(seed * 2654435761u) / 4294967296f;

        private static float MeanLifetime(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return 0.5f * (curve.constantMin + curve.constantMax);
                default:
                    return curve.Evaluate(0.5f);
            }
        }
    }
}
