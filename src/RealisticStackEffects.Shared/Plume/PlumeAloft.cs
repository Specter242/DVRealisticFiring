using System;
using System.Collections.Generic;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
 internal sealed partial class PlumeAloft
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

        // Each particle turns at up to this many radians a second, either way.
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
            // Extra lateral width accumulated under a thermal inversion ceiling,
            // m (PlumeRise.InversionSpreadOn), kept apart from Spread so
            // dilution, width, and retirement are not accelerated.
            public float InversionSpread;
            // When it was last carried aloft, by the game's clock, s (0: not yet).
            public float CarriedAt;
            // Its place across the plume, in the unit ball (PlumeParcels.Roll
            // turns it over), and how far from the plume's middle it has
            // been set by it, m.
            public Vector3 Place;
            public bool Placed;
            public Vector3 Offset;
            // Where it was last to be set, and the velocity it was given to get there.
            public Vector3 Set;
            public Vector3 Outward;
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
            // Whose it is while it is kept (0 once forgotten): a record found
            // by its particle's place in the buffer is the particle's only
            // with its seed.
            internal uint Seed;
            // What its seed alone gives the shader's values, worked out once:
            // its wisp's row and start, its turn at birth and which way it
            // spins, where its puff's roll starts and its pace, its puff.
            internal float Wisp;
            internal float TurnFrom;
            internal float TurnWay;
            internal float RollFrom;
            internal float RollPace;
            internal float Puff;
            internal bool Mirrored;
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

            /// <summary>The atmospheric wind velocity in the system's simulation space (for altitude shear and crosswind meander).</summary>
            public Vector3 Wind;

            /// <summary>Diurnal thermal inversion ceiling above the stack, m (<see cref="float.PositiveInfinity"/> or 0 when none), and strength (0 to 1).</summary>
            public float InversionCeiling;
            public float InversionStrength;

            /// <summary>The train's forward speed through the air and the stack exhaust's exit speed, m/s (for slipstream downwash).</summary>
            public float TrainSpeed;
            public float ExitSpeed;

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

            /// <summary>
            /// The plume as parcels that carry their smoke (<see cref="PlumeParcels"/>),
            /// above 0.5: a particle a lump <see cref="Lump"/> of the plume's
            /// envelope across (no smaller than <see cref="LeastLump"/> m
            /// shown, nor larger than the envelope), <see cref="LumpsAcross"/>
            /// kept in each envelope's length, each carrying its depth over
            /// <see cref="ParcelArea"/> m², a light haze's smoke
            /// <see cref="LightSmoke"/> of that.
            /// </summary>
            public float Parcels;

            /// <summary>The stack's bore, m, and the bore diameters over the lip by which the white has formed across the jet (<see cref="SteamCloud.Sheath"/>).</summary>
            public float Bore;
            public float WhiteByHeight;
            public float Lump;
            public float LeastLump;
            public float LumpsAcross;
            public float ParcelArea;
            public float LightSmoke;

            /// <summary>Black smoke's amount, × steam's for the same depth in the model.</summary>
            public float DenseSmoke;

            /// <summary>
            /// How hard the beats are now, 0 to 1 (a particle keeps what it
            /// left on: <see cref="HardOf"/>), and how far a hard beat
            /// billows out as its blast is spent: this much wider, m, for
            /// each metre from the lip, over the jet's length, m.
            /// </summary>
            public float BeatHard;
            public float BlastSpread;
            public float JetLength;

            /// <summary>The exhaust's neck over the lip, m: it holds the bore's width this far before it billows.</summary>
            public float Neck;

            /// <summary>How fast the plume's lumps turn over within it, × its rise over its half-width (0: each keeps its place).</summary>
            public float Roll;

            /// <summary>Where the plume's optical depth across is this or less its lumps have mixed into an even haze, and from the next they stand apart.</summary>
            public float MixedBelow;
            public float MixedAbove;
        }

        // A particle is moved out to its place across the plume over this long, s.
        private const float PlaceEase = 0.25f;

        // No particle is moved to its place faster than this, m/s (a hitch's long step).
        private const float MostPlaceSpeed = 25f;

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

        internal float PlayingTime => nominalLife;

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

        // Each particle's record by its place in the system's buffer, as the last pass left them.
        private Record[] placed = new Record[0];

        /// <summary>The record of the particle kept at <paramref name="place"/> this pass.</summary>
        public Record At(int place) => placed[place];

        /// <summary>Keeps the particle last carried at <paramref name="place"/> in the buffer: never past the place it was read from.</summary>
        public void Keep(int place) => placed[place] = Last;

        private Record Fresh(uint seed, Parcel parcel)
        {
            var record = spare.Count > 0 ? spare.Pop() : new Record();
            // Parcel-to-parcel soot contrast: deterministic by seed, stronger on
            // smoky fires and subtle/absent on clean steam, preserving the mean.
            var smokiness = PlumeParcels.Smokiness(parcel.Thickness);
            if (smokiness > 0f)
            {
                parcel.Thickness = PlumeParcels.VarySoot(parcel.Thickness, seed);
                var shade = PlumeParcels.SootShadeFactor(seed, smokiness);
                parcel.SmokeColor = new Color(
                    Mathf.Clamp01(parcel.SmokeColor.r * shade),
                    Mathf.Clamp01(parcel.SmokeColor.g * shade),
                    Mathf.Clamp01(parcel.SmokeColor.b * shade),
                    parcel.SmokeColor.a);
            }
            record.SmokeAtLip = PlumeRise.OpacityOf(parcel.Thickness.SmokeDepth, 0f);
            record.Parcel = parcel;
            record.Here = 1f;
            record.Shape = Vector4.zero;
            record.Shaped = false;
            record.Shade = 0f;
            record.ShadeRound = -1;
            record.Hard = float.NaN;
            record.Retiring = false;
            record.Cycle = cycle;
            record.Seed = seed;
            record.Wisp = WispRow(seed) + 0.49f * WispPhase(seed);
            record.TurnFrom = Hash(seed ^ 0x9e3779b9u) * 2f * Mathf.PI;
            record.TurnWay = 2f * Hash(seed ^ 0x7f4a7c15u) - 1f;
            record.RollFrom = Hash(seed ^ 0x1b56c4e9u);
            record.RollPace = 0.7f + 0.6f * Hash(seed ^ 0x2545f491u);
            record.Puff = Hash(seed ^ 0x68e31da4u);
            record.Mirrored = Hash(seed ^ 0xb5297a4du) < 0.5f;
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
        // The horizontal in the simulation space, across the world's up, and
        // frame-constant turbulence and downwash scalars precomputed in Begin.
        private Vector3 across;
        private Vector3 along;
        private float eddySpeed;
        private float windAcross;
        private float windAlong;
        private float downwashStrength;

        // The particle Turn last looked at, for Work.
        private float turnAge;
        private Record turnRecord;

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

        public void Begin(Air frame, int count)
        {
            if (placed.Length < count)
                Array.Resize(ref placed, Mathf.NextPowerOfTwo(count));
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
            eddySpeed = frame.Eddies > 0f
                ? PlumeTurbulence.EddyStir * PlumeRise.RiseSpeed(frame.Flux, 0f, frame.Speed, frame.RiseShown) * frame.Eddies
                : 0f;
            windAcross = Vector3.Dot(frame.Wind, across);
            windAlong = Vector3.Dot(frame.Wind, along);
            downwashStrength = PlumeRise.DownwashStrength(frame.TrainSpeed, frame.ExitSpeed, frame.Flux);
        }

        // The turn ends; when every particle has had its turn, the dead are
        // forgotten and the turn's figures shown.
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
                var dead = records[seed];
                dead.Seed = 0;
                spare.Push(dead);
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

        /// <summary>This frame's particles in a sentence, for the status log.</summary>
        public string Describe()
        {
            var s = Seen;
            return $"{s.InStack} in the stack, {s.Aloft} aloft (mean {(s.Aloft > 0 ? s.SizeSum / s.Aloft : 0f):F1} m, " +
                   $"widest {s.Widest:F1} m drawn {s.WidestDrawn:F1} m at {s.WidestHeight:F0} m, mean opacity {(s.Aloft > 0 ? s.AlphaSum / s.Aloft : 0f):F2}), " +
                   $"{s.Retired} retired" +
                   (air.ShadowDensity > 0f ? $"; its own shadow: mean shade {meanShade:F2}, {shadedShare:P0} over half" : "");
        }
    }
}
