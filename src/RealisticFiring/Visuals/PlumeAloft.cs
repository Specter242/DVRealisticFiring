using RealisticFiring.Core;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class PlumeAloft
    {
        /// <summary>Particles live up to this long, in s; a plume thins out long before.</summary>
        public const float Lifetime = 300f;

        private const float FadeIn = 0.4f;
        private const float FadeOut = 0.75f;

        // A particle is drawn this share of the plume's width, and drifts out
        // from where it left the stack by up to this share of the plume's
        // widening (its rise's and the air's spread), so the particles fill it.
        private const float SizePerWidth = 0.75f;
        private const float MostSpread = 0.3f;

        // The plume's eddies are about this share of its width (at least the
        // smallest, in m), stir the smoke at up to this share of its rise,
        // and drift through it at this rate; each particle turns at up to this
        // many radians a second, either way.
        private const float EddyPerWidth = 0.5f;
        private const float SmallestEddy = 2f;
        private const float EddyStir = 0.6f;
        private const float EddyDrift = 0.2f;
        private const float MostSpin = 0.3f;

        private struct Parcel
        {
            public PlumeParcel Thickness;
            public Color Color;
            // What it turns as its white steam evaporates.
            public Color SmokeColor;
            public float Size;
            // When it began fading out, retired as the plume widened; below 0 not yet.
            public float RetiredAt;
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
            public float Keep;

            /// <summary>The stack's buoyancy flux, m⁴/s³.</summary>
            public float Flux;

            /// <summary>The air's speed past the stack, m/s: the wind less the train's own.</summary>
            public float Speed;

            /// <summary>The train's velocity, whose wake carries the plume's base with it.</summary>
            public Vector3 Train;

            /// <summary>How long the exhaust steam takes to evaporate, s.</summary>
            public float SteamLinger;

            /// <summary>How hard the plume's eddies stir it, × (0 none).</summary>
            public float Eddies;

            /// <summary>
            /// A particle first seen here, not emitted by the mod, takes this
            /// share of smoke and, if already above the stack, the stack's
            /// velocity (the game's own emission does not carry it; in the
            /// stack the caller moves it with the stack).
            /// </summary>
            public Vector3 Carried;
            public float SmokeShare;
        }

        // A renderer draws a particle no bigger than this share of the view.
        private const float MaxScreenShare = 4f;

        // A particle is worked out in full about this often, in s, in turns
        // over at most this many frames.
        private const float UpdateInterval = 0.025f;
        private const int MostStride = 3;

        private readonly ParticleSystem system;
        private readonly float nominalLife;
        private readonly ParticleSystem.MinMaxCurve growth;
        private readonly SeedMemory<Parcel> parcels = new SeedMemory<Parcel>();
        private Air air;
        private int stride = 1;
        private int turn;
        private float keepStride;
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
            // first frames for a 300 s life: it plays over the system's own
            // lifetime instead, then holds its last frame.
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
        public void Expect(uint seed, Color color, float size, float smokeShare, Color smokeColor) =>
            parcels.Expect(seed, new Parcel
            {
                Thickness = PlumeRise.Parcel(color.a, smokeShare),
                Color = color,
                SmokeColor = smokeColor,
                Size = size,
                RetiredAt = -1f,
            });

        public void Begin(Air frame)
        {
            air = frame;
            if (turn == 0)
            {
                stride = Mathf.Clamp(Mathf.RoundToInt(UpdateInterval / Mathf.Max(Time.deltaTime, 1e-4f)), 1, MostStride);
                stats = default;
                cycleTop = 0f;
            }
            keepStride = Mathf.Pow(frame.Keep, stride);
            across = Vector3.Cross(frame.Up, Vector3.forward);
            if (across.sqrMagnitude < 1e-4f)
                across = Vector3.Cross(frame.Up, Vector3.right);
            across.Normalize();
            along = Vector3.Cross(across, frame.Up).normalized;
        }

        /// <summary>
        /// Carries <paramref name="particle"/>, <paramref name="height"/> m
        /// above the stack and drawn no wider than <paramref name="widest"/>;
        /// still <paramref name="inStack"/>, only its size and fade (the
        /// caller moves it). False when it is retired.
        /// </summary>
        public bool Carry(ref ParticleSystem.Particle particle, float height, float widest, bool inStack)
        {
            // Its turn this frame, or young: else the game moves it as it is.
            var age = Mathf.Max(0f, particle.startLifetime - particle.remainingLifetime);
            var young = age < FadeIn;
            var onTurn = stride == 1 || particle.randomSeed % (uint)stride == (uint)turn;
            if (!onTurn && !young)
                return true;
            var keep = young ? air.Keep : keepStride;

            if (!parcels.TryRecall(particle.randomSeed, out var parcel))
            {
                parcel = new Parcel
                {
                    Thickness = PlumeRise.Parcel(((Color)particle.startColor).a, air.SmokeShare),
                    Color = particle.startColor,
                    SmokeColor = particle.startColor,
                    Size = particle.startSize,
                    RetiredAt = -1f,
                };
                parcels.Expect(particle.randomSeed, parcel);
                if (!inStack)
                    particle.velocity += air.Carried;
            }

            var grown = parcel.Size * growth.Evaluate(nominalLife > 0f ? Mathf.Clamp01(age / nominalLife) : 1f);
            var shown = 1f;
            var color = parcel.Color;
            var leaving = 1f;
            if (inStack)
            {
                particle.startSize = Mathf.Min(grown, widest);
                if (onTurn)
                    stats.InStack++;
            }
            else
            {
                // Near the train its wake carries the plume along, and the air
                // goes past it the slower.
                var wake = PlumeRise.Wake(height);
                var airSpeed = air.Speed * (1f - wake);
                var still = air.Still + (air.Train - air.Still) * wake;
                var rise = PlumeRise.RiseSpeed(air.Flux, height, airSpeed);
                // Out its own way, evenly over the plume's breadth as it widens,
                // with its rise and the air's turbulence.
                var angle = Hash(particle.randomSeed ^ 0x5bd1e995u) * 2f * Mathf.PI;
                var share = MostSpread * Mathf.Sqrt(Hash(particle.randomSeed ^ 0x27d4eb2du));
                var widening = PlumeRise.WidthPerHeight * rise + PlumeRise.SpreadRate(air.Speed);
                var outward = (across * Mathf.Cos(angle) + along * Mathf.Sin(angle)) * (share * widening);
                var target = still + air.Up * rise + outward + Stir(particle.position, height, rise) * air.Eddies;
                particle.velocity = target + (particle.velocity - target) * keep;
                var spread = PlumeRise.SpreadWidth(age, air.Speed);
                particle.startSize = Mathf.Min(Mathf.Max(grown, PlumeRise.PlumeWidth(height, spread) * SizePerWidth), widest);
                // As its white steam evaporates it turns its smoke's color.
                var steam = PlumeRise.SteamLeft(parcel.Thickness, age, air.SteamLinger);
                var depth = steam + parcel.Thickness.SmokeDepth;
                shown = PlumeRise.OpacityOf(depth, height, spread);
                if (depth > 1e-4f)
                    color = Color.Lerp(SteamColor, parcel.SmokeColor, parcel.Thickness.SmokeDepth / depth);
                // Fewer, wider particles carry the plume as it widens: one past
                // its share fades out, then goes; one too thin to see goes at once.
                if (parcel.RetiredAt < 0f && Hash(particle.randomSeed) > PlumeRise.Survival(height, spread))
                {
                    parcel.RetiredAt = Time.time;
                    parcels.Keep(particle.randomSeed, parcel);
                }
                if (parcel.RetiredAt >= 0f)
                    leaving = 1f - (Time.time - parcel.RetiredAt) / FadeOut;
                if (age > FadeIn && shown < PlumeRise.Invisible || leaving <= 0f)
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
            var fade = Mathf.Clamp01(age / FadeIn);
            particle.startColor = new Color(color.r, color.g, color.b, (inStack ? parcel.Color.a : shown) * fade * leaving);
            return true;
        }

        // The plume's eddies at a point: a smooth field drifting through the
        // plume, its whorls as big as half its width there.
        private static Vector3 Stir(Vector3 position, float height, float rise)
        {
            var p = position / Mathf.Max(SmallestEddy, EddyPerWidth * PlumeRise.Width(height));
            var t = Time.time * EddyDrift;
            var x = Mathf.PerlinNoise(p.y + t, p.z + 13.1f) * 2f - 1f;
            var y = Mathf.PerlinNoise(p.z + t, p.x + 7.7f) * 2f - 1f;
            var z = Mathf.PerlinNoise(p.x + t, p.y + 3.3f) * 2f - 1f;
            return new Vector3(x, 0.5f * y, z) * (EddyStir * rise);
        }

        // The turn ends; when every particle has had its turn, the dead are
        // forgotten and the turn's figures shown.
        public void End()
        {
            if (++turn < stride)
                return;
            turn = 0;
            parcels.Forget();
            Seen = stats;
            Top = cycleTop;
        }

        /// <summary>This frame's particles in a sentence, for the status log.</summary>
        public string Describe()
        {
            var s = Seen;
            return $"{s.InStack} in the stack, {s.Aloft} aloft (mean {(s.Aloft > 0 ? s.SizeSum / s.Aloft : 0f):F1} m, " +
                   $"widest {s.Widest:F1} m drawn {s.WidestDrawn:F1} m at {s.WidestHeight:F0} m, mean opacity {(s.Aloft > 0 ? s.AlphaSum / s.Aloft : 0f):F2}), " +
                   $"{s.Retired} retired";
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
