using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class ChimneyPlume
    {
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

#if !PUBLIC_RELEASE
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
                FireboxGas = test.FireOn ? test.Gas : 0f,
                ClearStack = test.Clear,
                PuffHaze = PlumeModel.WellFiredHaze(tuning, test.Clear),
                HeatShare = test.FireOn ? Mathf.Clamp01(test.Heat) : 0f,
            };
        }
#endif

        // The white the stack's steam makes in the air (SteamCloud): its
        // flue gas, the air through the fire over the steam up the stack, a
        // share of it mixing with the beats' steam before the air; the
        // exhaust's water from the cylinders' walls; and the stack's buoyancy.
        private void Weather(ref PlumeInputs inputs, float dt)
        {
            float steam, through, wet, blower;
#if !PUBLIC_RELEASE
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
#endif
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
            var visibility = StackHost.SteamVisibility;
            inputs.SteamWhiteness = PlumeSettings.ScaleSteamWhiteness(steamWhite, visibility);
            inputs.WaterWhiteness = PlumeSettings.ScaleSteamWhiteness(waterCloud.Whiteness, visibility);
            inputs.BlowerWhiteness = PlumeSettings.ScaleSteamWhiteness(streamCloud.Whiteness, visibility);
            stackFlux = PlumeRise.StackFlux(stackHeat, steam) * tuning.Buoyancy;
        }

        // A beat's exhaust begins: its particles leave over the blowdown and
        // the return stroke (ExhaustBlast), its cinders with its first
        // surge. <paramref name="since"/> s ago, the crank having turned
        // <paramref name="turned"/> revolutions since.
        private void BeginBeat(Puff puff, float pressure, float since, float turned, Vector3 carVelocity)
        {
            LastPuff = puff;
            beatCount++;
            var share = ParticleShare;
            var particles = SmokeDetail.Scaled(puff.Particles, share, ref puffParticlesDue, least: 1);
            var beat = new PendingBeat
            {
                Puff = puff,
                Release = ExhaustBlast.Begin(particles, pressure),
                Particles = particles,
                // The custom effects add streaks for the jet itself, right
                // above the stack.
                Streaks = jet != null ? SmokeDetail.Scaled(Mathf.Max(2, puff.Particles / 2), share, ref jetStreaksDue, least: 1) : 0,
                Soot = SmokeDetail.Scaled(puff.SootParticles, share, ref sootParticlesDue),
            };
            ExhaustBlast.Step(ref beat.Release, since, turned);
            // The oldest gives way when too many wait (a crank stopped mid-stroke).
            if (pendingBeatCount == pendingBeats.Length)
            {
                System.Array.Copy(pendingBeats, 1, pendingBeats, 0, pendingBeatCount - 1);
                pendingBeatCount--;
            }
            pendingBeats[pendingBeatCount++] = beat;

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

        // The particles the beats' steam has let out by now, each at the
        // speed of the stack's flow as it left and as far up as it has come
        // since.
        private void ReleaseBeats(Vector3 carVelocity)
        {
            var kept = 0;
            for (var i = 0; i < pendingBeatCount; i++)
            {
                ref var beat = ref pendingBeats[i];
                while (ExhaustBlast.Next(ref beat.Release, out var flow, out var due))
                    EmitBeatParticle(ref beat, flow, due, carVelocity);
                if (beat.Release.Done)
                    continue;
                if (kept != i)
                    pendingBeats[kept] = beat;
                kept++;
            }
            pendingBeatCount = kept;
        }

        private void EmitBeatParticle(ref PendingBeat beat, float flow, float due, Vector3 carVelocity)
        {
            var puff = beat.Puff;
            var color = ToColor(puff.Color);
            var speed = ExhaustBlast.Speed(flow, puff.Speed, Mathf.Min(LeastBeatSpeed, puff.Speed));
            var late = Mathf.Min(due, Time.deltaTime);
            // As a cluster, a bigger head with lobes beside it; else each
            // the puff's size up the jet.
            var cluster = Mathf.Clamp01(Look.BeatCluster);
            var head = beat.Out == 0;
            beat.Out++;
            var size = puff.Size * (head ? Mathf.Lerp(1f, ClusterCore, cluster) : Mathf.Lerp(1f, ClusterLobe, cluster));
            Emit(smoke, speed, size, color, carVelocity, head ? PuffSpread : Mathf.Lerp(PuffSpread, ClusterSpread, cluster),
                BeatSpeedSlowest, BeatSpeedFastest, Lifetime(smoke, puff.LifetimeScale),
                Aloft(smoke), puff.SmokeShare, ToColor(puff.SmokeColor), steamCloud, late: late);

            // The jet's streaks: its smoke, and its white only as far as it
            // has formed in their half second.
            if (jet != null)
            {
                var parcel = new JetParcel { Thickness = PlumeRise.Parcel(puff.Color.A, puff.SmokeShare), SmokeColor = ToColor(puff.SmokeColor) };
                for (; beat.StreaksOut * beat.Particles < beat.Out * beat.Streaks; beat.StreaksOut++)
                {
                    var seed = (uint)Random.Range(int.MinValue, int.MaxValue);
                    jetParcels.Expect(seed, parcel);
                    Emit(jet, speed, puff.Size * JetStreakSize, JetColor(parcel, 0f), carVelocity, PuffSpread * 0.5f, 0.85f, 1.1f, seed: seed, late: late);
                }
            }

            // A dark puff's sooty core goes into the cloud's own system, drawn
            // among its particles by distance: in a system of its own it was
            // drawn over the whole cloud, white steam in front of it too, and
            // a fresh charge's black smoke cut through the steam as a layer
            // (ThinBlueWarrior, Nexus, 2026-10-19). It keeps its shorter
            // life, drawn as the Soot system's.
            // As one exhaust it carries the puff's own steam with its smoke,
            // a little denser and its smoke a little darker: all smoke, it
            // stayed dark among puffs that kept their white (2026-11-12).
            if (beat.SootOut * beat.Particles < beat.Out * beat.Soot)
            {
                var one = Look.OneExhaust > 0.5f;
                var soot = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, Mathf.Min(1f, color.a + 0.1f));
                var sootSmoke = new Color(puff.SmokeColor.R * 0.8f, puff.SmokeColor.G * 0.8f, puff.SmokeColor.B * 0.8f, 1f);
                for (; beat.SootOut * beat.Particles < beat.Out * beat.Soot; beat.SootOut++)
                    Emit(smoke, speed * 0.9f, puff.Size * 0.6f, soot, carVelocity, PuffSpread, BeatSpeedSlowest, BeatSpeedFastest,
                        SootLifetime(puff.LifetimeScale), Aloft(smoke), one ? puff.SmokeShare : 1f, one ? sootSmoke : soot, steamCloud, late: late);
            }
        }

        // A jet streak's color <paramref name="age"/> s out of the stack: its
        // smoke, and as much of its white as has formed by then, as the
        // cloud's particles beside it.
        private Color JetColor(JetParcel parcel, float age, float height = 0f)
        {
            var past = airVelocity - stackVelocity;
            var steam = PlumeRise.SteamLeft(parcel.Thickness, steamCloud, age, new Vector2(past.x, past.z).magnitude, tuning.SteamMixing, tuning.SteamForming,
                SteamCloud.Sheath(height, 2f * boreRadius, tuning.WhiteByHeight));
            var whole = steam + parcel.Thickness.SmokeDepth;
            var tint = whole > 1e-4f ? PlumeModel.SmokeTint(parcel.Thickness.SmokeDepth / whole) : 1f;
            var white = PlumeModel.Steam;
            return new Color(Mathf.Lerp(white.R, parcel.SmokeColor.r, tint), Mathf.Lerp(white.G, parcel.SmokeColor.g, tint),
                Mathf.Lerp(white.B, parcel.SmokeColor.b, tint), PlumeRise.OpacityOf(whole, 0f));
        }

        // Every particle starts at the emitter, at a random point across its
        // radius, placed here in the system's simulation space rather than
        // through Unity's shape module.
        private static void Emit(ParticleSystem system, float speed, float size, Color color, Vector3 carVelocity,
            float spread, float slowest, float fastest, float lifetime = 0f, PlumeAloft plume = null, float smokeShare = 0f,
            Color smokeColor = default, SteamCloud cloud = null, uint seed = 0, float late = 0f)
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
                // One that left a moment ago (late) has come this far up the stack.
                position = SimulationPoint(system, start - lead + launch * late),
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
    }
}
