using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// Drives a realistic firebox's chimney smoke in place of the stock
    /// <see cref="SteamSmokeParticlePortReader"/>: a puff per exhaust beat,
    /// counted from the crank angle and thrown up by the exhaust's force, a
    /// steady plume between beats, and sparks on hard beats, all colored from
    /// the fire model per particle. A puff leaves as a narrow column; drag
    /// spends its speed within the tuned jet length and, as it slows, it
    /// swells into a cloud that rises at the tuned speed. The mod applies the
    /// drag and lift to every smoke particle itself each frame, because Unity's
    /// own drag module did not hold the smoke back in game; the stock systems'
    /// gravity and velocity modules are switched off so nothing else moves it.
    /// The shape follows the smoke tuning settings live.
    /// </summary>
    internal sealed class ChimneyPlume : MonoBehaviour
    {
        // A particle is at its jet width above the fast speed and at its full
        // cloud size below the slow one, in m/s.
        private const float JetFastSpeed = 8f;
        private const float JetSlowSpeed = 1.5f;

        // A beat's particles leave within this angle of the stack's axis; haze wanders more.
        private const float PuffSpread = 0.06f;
        private const float HazeSpread = 0.25f;

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

        // A grate shaken while the engine works hard throws live coal and ash
        // up the stack: "a cloud of smoke and a hail of sparks are belched
        // from the stack" (Baker p.63). Its cinders go out with the next few
        // beats, up to this much a beat and this much waiting, in kg; its
        // ash darkens the smoke, clearing over this time, in s.
        private const float ShakeCindersPerBeat = 0.08f;
        private const float MaxShakeCinderBacklog = 0.5f;
        private const float ShakeSmokePerKg = 8f;
        private const float ShakeSmokeClearing = 3f;

        // A beat's particles leave at a spread of speeds, so they stretch into a column.
        private const float PuffSlowest = 0.45f;
        private const float PuffFastest = 1.1f;

        // Puffs at speed (up to ~15 beats/s, 8 particles each, 5-10 s lifetimes)
        // need more room than the stock cap of 300.
        private const int MinMaxParticles = 1000;

        // Within the narrow-jet zone above the game's smoke emitter (inside the
        // stack, however deep its emitter sits) smoke is at most this wide and
        // moves with the stack, rising at least this fast. Beyond it the plume
        // widens by the spread for every metre from the zone's top, so a
        // particle's camera-facing square never hangs down beside the stack.
        private const float ZoneWidth = 0.15f;
        private const float MinZoneRise = 1f;
        private const float PlumeSpread = 0.9f;

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

        // A frame that crosses more beats than this is a hitch, not a fast engine.
        private const int MaxBeatsPerFrame = 3;

        // Faster than any train: the floating origin moved the world.
        private const float MaxPlausibleSpeed = 80f;

        private SteamSmokeParticlePortReader stock;
        private RealisticFirebox firebox;
        private ExhaustBeatCounter beats;
        private Port crankRotation;
        private Port exhaustPressure;
        private Port totalFlow;
        private Port boilerBroken;
        private ParticleSystem smoke;
        private ParticleSystem sootCore;
        private ParticleSystem jet;
        private ParticleSystem[] embers;
        private float hazeDue;
        private float stokerCinders;
        private float shakeCinders;
        private float shakeCindersSeen = float.NaN;
        private float shakeSmoke;
        private Vector3 lastPosition;
        private Vector3 stackVelocity;
        private bool hasLastPosition;
        private int beatCount;
        private float statusTime;
        private readonly PlumeTuning tuning = new PlumeTuning();
        private float appliedJetWidth = float.NaN;
        private ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[0];
        private ParticleSystemRenderer[] renderers;
        private bool shown = true;
        private float? stackLip;

        // Each smoke particle's intended size by its random seed (stable for
        // its life), because its start size is capped near the stack mouth.
        private readonly Dictionary<ParticleSystem, SeedMemory<float>> intendedSizes = new Dictionary<ParticleSystem, SeedMemory<float>>();

        // The jet's streaks are drawn along their velocity, 0.06 s of it plus
        // 1.5 times their size, which reached past the stack's lip: a streak
        // just above it trailed back down through the stack's wall. Each
        // streak is drawn instead along the line back to the stack's mouth,
        // no longer than that line; the true velocity it moves by is kept
        // here by the particle's seed.
        private readonly SeedMemory<Vector3> jetVelocities = new SeedMemory<Vector3>();
        private float jetVelocityScale;
        private float jetLengthScale;

        /// <summary>True once the plume has taken over from the stock smoke reader.</summary>
        public bool Active { get; private set; }

        public Puff LastPuff { get; private set; }

        /// <summary>
        /// Takes over <paramref name="reader"/>'s smoke on a loco with a
        /// realistic firebox. Returns a sentence for the log: what was set up,
        /// or why the stock smoke stays.
        /// </summary>
        public static string Attach(SteamSmokeParticlePortReader reader, SimulationFlow flow, TrainCar car, RealisticFirebox firebox)
        {
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

            var systems = reader.smokeParticlesParent != null
                ? reader.smokeParticlesParent.GetComponentsInChildren<ParticleSystem>(true)
                : new ParticleSystem[0];
            if (systems.Length == 0)
                return "no smoke particle systems; keeping the stock smoke";

            var plume = reader.gameObject.AddComponent<ChimneyPlume>();
            plume.stock = reader;
            plume.firebox = firebox;
            plume.beats = new ExhaustBeatCounter(2 * engine.numCylinders);
            plume.crankRotation = crank;
            plume.exhaustPressure = pressure;
            plume.totalFlow = total;
            plume.boilerBroken = broken;
            var custom = Main.Settings.CustomSmokeEffects && PlumeAssets.Prefab != null && plume.UseCustomEffect(systems);
            if (!custom)
            {
                // The largest stock system carries the plume; a second one, if any, the sooty core.
                plume.smoke = systems.OrderByDescending(s => s.main.maxParticles).First();
                plume.sootCore = systems.FirstOrDefault(s => s != plume.smoke);
            }
            plume.embers = plume.ChooseSparks(reader, custom);
            plume.TakeOver();
            var sorting = plume.MatchSorting(car);
            plume.stackLip = MeasureStackLip(car, Emitter(plume.smoke), plume.smoke.transform.up, out var lip);
            return $"realistic chimney smoke {(custom ? "with the custom effects" : "on the game's systems")}: " +
                   $"cloud '{plume.smoke.name}'" +
                   (plume.sootCore != null ? $", soot '{plume.sootCore.name}'" : "") +
                   (plume.jet != null ? $", jet '{plume.jet.name}'" : "") +
                   $", sparks {string.Join("/", plume.embers.Select(s => $"'{s.name}'"))}" +
                   $", {2 * engine.numCylinders} beats per revolution from '{engineId}', flow from '{exhaust.ID}'; {sorting}; {lip}";
        }

        // The stack's lip above the game's smoke emitter, along the stack's
        // axis: the highest point of the loco's model near the axis where its
        // meshes can be read, or else the lip measured offline for its livery.
        // Null when neither is known.
        private static float? MeasureStackLip(TrainCar car, Vector3 emitter, Vector3 axis, out string how)
        {
            float? fromMesh = null;
            var readable = 0;
            var unreadable = 0;
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
                    if (rise > 0f && rise <= MaxStackRise
                        && (offset - axis * rise).sqrMagnitude <= StackRadius * StackRadius
                        && (!fromMesh.HasValue || rise > fromMesh.Value))
                        fromMesh = rise;
                }
            }

            if (fromMesh.HasValue)
            {
                how = $"stack lip {fromMesh.Value:F2} m above the smoke emitter, from {readable} readable meshes around the stack";
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
            foreach (var renderer in ours)
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = order;
                renderer.sortingFudge = fudge;
                if (renderer.sharedMaterial != null)
                    renderer.material.renderQueue = queue;
            }
            var seen = string.Join(", ", stock.GroupBy(r => (r.sharedMaterial.name, r.sharedMaterial.renderQueue, r.sortingOrder, r.sortingFudge))
                .Select(g => $"{g.Key.name} q{g.Key.renderQueue} o{g.Key.sortingOrder} f{g.Key.sortingFudge:F0} ×{g.Count()}"));
            return $"sorting matched to the stock steam (queue {queue}, order {order}, fudge {fudge:F0}; was {before}; stock: {seen})";
        }

        // Puts the custom plume where the stock smoke leaves the stack, in the
        // stock systems' simulation space (the game's world-origin shifts move
        // it), and switches the stock smoke systems off.
        private bool UseCustomEffect(ParticleSystem[] stockSystems)
        {
            var stockSmoke = stockSystems.OrderByDescending(s => s.main.maxParticles).First();
            var fx = Instantiate(PlumeAssets.Prefab, stockSmoke.transform.parent, false);
            fx.name = PlumeAssets.PrefabName;
            fx.transform.localPosition = stockSmoke.transform.localPosition;
            fx.transform.localRotation = stockSmoke.transform.localRotation;

            // Leave from exactly where the stock smoke leaves: the same emitter
            // shape, offset, and turn as well as the same simulation space.
            var stockMain = stockSmoke.main;
            var stockShape = stockSmoke.shape;
            foreach (var system in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpace = stockMain.simulationSpace;
                if (stockMain.simulationSpace == ParticleSystemSimulationSpace.Custom)
                    main.customSimulationSpace = stockMain.customSimulationSpace;
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
            foreach (var system in new[] { smoke, sootCore, jet }.Where(s => s != null))
                Prepare(system, buoyant: true);
            var jetRenderer = jet != null ? jet.GetComponent<ParticleSystemRenderer>() : null;
            if (jetRenderer != null && jetRenderer.renderMode == ParticleSystemRenderMode.Stretch)
            {
                jetVelocityScale = jetRenderer.velocityScale;
                jetLengthScale = jetRenderer.lengthScale;
            }
            foreach (var system in embers)
                PrepareSparks(system);
            SyncTuning();
            Active = true;
        }

        // Copies the live settings, and reshapes the smoke systems when the
        // jet drag, jet width, or rise changed.
        private void SyncTuning()
        {
            var settings = Main.Settings;
            tuning.MaxBeatPressure = settings.SmokeMaxBeatPressure;
            tuning.LaunchSpeed = settings.SmokeLaunchSpeed;
            tuning.PuffParticles = settings.SmokePuffParticles;
            tuning.PuffSize = settings.SmokePuffSize;
            tuning.SteamOpacity = settings.SmokeSteamOpacity;
            tuning.SootDarkness = settings.SmokeSootDarkness;
            tuning.HazeOpacity = settings.SmokeHazeOpacity;
            tuning.JetLength = settings.SmokeJetLength;
            tuning.RiseSpeed = settings.SmokeRiseSpeed;
            tuning.Sparks = settings.SmokeSparks;

            if (settings.SmokeJetWidth == appliedJetWidth)
                return;
            appliedJetWidth = settings.SmokeJetWidth;
            foreach (var system in new[] { smoke, sootCore }.Where(s => s != null))
                ApplyJetWidth(system, appliedJetWidth);
        }

        // Size by speed keeps a particle narrow while fast and lets it swell as it slows.
        private static void ApplyJetWidth(ParticleSystem system, float jetWidth)
        {
            var bySpeed = system.sizeBySpeed;
            bySpeed.enabled = true;
            bySpeed.range = new Vector2(JetSlowSpeed, JetFastSpeed);
            bySpeed.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, jetWidth)));
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
            var start = PerfMeter.Start();
            UpdatePlume();
            PerfMeter.Stop(PerfSection.ChimneyPlume, start);
        }

        private void UpdatePlume()
        {
            if (!Active || smoke == null)
                return;
            ShowOrHide();

            var dt = Time.deltaTime;
            var position = smoke.transform.position;
            var carVelocity = hasLastPosition && dt > 0f ? (position - lastPosition) / dt : Vector3.zero;
            if (carVelocity.magnitude > MaxPlausibleSpeed)
                carVelocity = Vector3.zero;
            lastPosition = position;
            stackVelocity = carVelocity;
            hasLastPosition = true;

            var crossed = beats.Advance(crankRotation.Value);
            if (boilerBroken.Value > 0f || dt <= 0f)
                return;

            SyncTuning();
            var fire = firebox.Model;
            var shaken = float.IsNaN(shakeCindersSeen) ? 0f : Mathf.Max(0f, fire.ShakeCinders - shakeCindersSeen);
            shakeCindersSeen = fire.ShakeCinders;
            shakeCinders = Mathf.Min(shakeCinders + shaken, MaxShakeCinderBacklog);
            shakeSmoke = Mathf.Clamp01(shakeSmoke * Mathf.Exp(-dt / ShakeSmokeClearing) + shaken * ShakeSmokePerKg);
            var inputs = new PlumeInputs
            {
                ExhaustPressure = exhaustPressure.Value,
                TotalFlowNormalized = totalFlow.Value,
                Soot = Mathf.Max(fire.SootDensity, shakeSmoke),
                FireOn = fire.FireOn,
            };

            // A beat with no steam behind it (drifting, regulator shut) makes no puff.
            if (inputs.ExhaustPressure > PlumeModel.MinBeatPressure)
            {
                for (var i = 0; i < Mathf.Min(crossed, MaxBeatsPerFrame); i++)
                    EmitPuff(PlumeModel.ForBeat(tuning, inputs), carVelocity);
            }

            if (firebox.LastStoker is StokerFlow stoked)
                stokerCinders = Mathf.Min(stokerCinders + stoked.CinderRate * dt, MaxStokerCinderBacklog);

            var haze = PlumeModel.Between(tuning, inputs);
            hazeDue += haze.Rate * dt;
            for (; hazeDue >= 1f; hazeDue -= 1f)
                Emit(smoke, haze.Speed, 1.2f * tuning.PuffSize, ToColor(haze.Color), carVelocity, HazeSpread, 0.85f, 1.15f);

            Drift(smoke, dt, lift: true);
            Drift(sootCore, dt, lift: true);
            Drift(jet, dt, lift: true);
            foreach (var system in embers)
                Fly(system, dt);

            LogStatus(inputs);
        }

        private void EmitPuff(Puff puff, Vector3 carVelocity)
        {
            LastPuff = puff;
            beatCount++;
            var color = ToColor(puff.Color);
            for (var i = 0; i < puff.Particles; i++)
                Emit(smoke, puff.Speed, puff.Size, color, carVelocity, PuffSpread, PuffSlowest, PuffFastest);

            // The custom effects add streaks for the jet itself, right above the stack.
            if (jet != null)
            {
                var streaks = Mathf.Max(2, puff.Particles / 2);
                for (var i = 0; i < streaks; i++)
                    Emit(jet, puff.Speed, puff.Size * JetStreakSize, color, carVelocity, PuffSpread * 0.5f, 0.85f, 1.1f);
            }

            if (sootCore != null && puff.SootParticles > 0)
            {
                var soot = new Color(color.r * 0.8f, color.g * 0.8f, color.b * 0.8f, Mathf.Min(1f, color.a + 0.1f));
                for (var i = 0; i < puff.SootParticles; i++)
                    Emit(sootCore, puff.Speed * 0.9f, puff.Size * 0.6f, soot, carVelocity, PuffSpread, PuffSlowest, PuffFastest);
            }

            // Usually well under one spark a beat: round at random. A stoker's
            // lost fines go out with the beat too.
            var shook = Mathf.Min(shakeCinders, ShakeCindersPerBeat);
            shakeCinders -= shook;
            var expected = puff.Sparks + (stokerCinders + shook) * StokerCindersPerKg * tuning.Sparks / DefaultSparkSetting;
            stokerCinders = 0f;
            var sparks = Mathf.FloorToInt(expected) + (Random.value < expected % 1f ? 1 : 0);
            for (var i = 0; i < sparks && embers.Length > 0; i++)
            {
                Emit(embers[i % embers.Length], PlumeModel.SparkLaunchSpeed(puff), Random.Range(SparkMinSize, SparkMaxSize),
                    Color.white, carVelocity, SparkSpread, 0.85f, 1.25f, Random.Range(SparkMinLife, SparkMaxLife));
            }
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
            for (var i = 0; i < count; i++)
                buffer[i].velocity = buffer[i].velocity * keep + fall;
            system.SetParticles(buffer, count);
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
        // stack's wall. Above the zone: linear drag toward still air and, for
        // smoke, a lift that balances it at the rise speed, so a puff stops
        // after its launch speed over the drag (the jet length for the hardest
        // beat) and then rises; the stack's velocity it carried decays the
        // same way, so the plume trails behind a moving train. A system in
        // the stack's own space (the jet) moves with the stack already, and
        // the still air there blows back at the train's speed.
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
            var rise = lift ? up * (PlumeModel.Lift(tuning) * dt) : Vector3.zero;
            var axis = local ? Vector3.up : space.up;
            var carried = local ? Vector3.zero : stackVelocity;
            var air = local ? space.InverseTransformDirection(-stackVelocity) : Vector3.zero;
            var zone = stackLip.HasValue
                ? Mathf.Min(Mathf.Max(0f, stackLip.Value) + LipMargin, Main.Settings.SmokeJetZone)
                : Main.Settings.SmokeJetZone;
            var emitter = SimulationPoint(system, Emitter(system));
            var zoneTop = emitter + axis * zone;
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
                var inZone = Vector3.Dot(particle.position - emitter, axis) < zone;
                float drawn;
                if (inZone)
                {
                    drawn = Mathf.Min(shown, ZoneWidth);
                    var upward = Mathf.Max(Vector3.Dot(velocity - carried, axis), MinZoneRise);
                    velocity = carried + axis * upward;
                }
                else
                {
                    drawn = Mathf.Min(shown, ZoneWidth + PlumeSpread * Vector3.Distance(particle.position, zoneTop));
                    velocity = air + (velocity - air) * keep + rise;
                }
                particle.startSize = shown > drawn ? intended * drawn / shown : intended;

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
            system.SetParticles(buffer, count);
        }

        // A world point in the space the system simulates in (the game moves
        // the world origin, so the stock smoke simulates in a custom space).
        private static Vector3 SimulationPoint(ParticleSystem system, Vector3 world)
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
        private static Vector3 Emitter(ParticleSystem system) =>
            system.transform.TransformPoint(system.shape.position);

        // Every particle starts at the emitter, at a random point across its
        // radius, placed here in the system's simulation space rather than
        // through Unity's shape module.
        private static void Emit(ParticleSystem system, float speed, float size, Color color, Vector3 carVelocity,
            float spread, float slowest, float fastest, float lifetime = 0f)
        {
            var transform = system.transform;
            var up = transform.up;
            var disk = Random.insideUnitCircle * system.shape.radius;
            var start = Emitter(system) + transform.right * disk.x + transform.forward * disk.y;
            var jitter = Random.insideUnitSphere * spread;
            var launch = (up + jitter).normalized * (speed * Random.Range(slowest, fastest));
            var emit = new ParticleSystem.EmitParams
            {
                position = SimulationPoint(system, start),
                applyShapeToPosition = false,
                // A system in the stack's own space already moves with it.
                velocity = system.main.simulationSpace == ParticleSystemSimulationSpace.Local
                    ? transform.InverseTransformDirection(launch)
                    : launch + carVelocity,
                startSize = size * Random.Range(0.8f, 1.2f),
                startColor = color,
            };
            if (lifetime > 0f)
                emit.startLifetime = lifetime;
            system.Emit(emit, 1);
        }

        private static Color ToColor(PlumeColor c) => new Color(c.R, c.G, c.B, c.A);

        private void LogStatus(PlumeInputs inputs)
        {
            var interval = Main.Settings.FireStatusLogInterval;
            if (interval <= 0 || Time.time - statusTime < interval
                || !FireboxRegistry.TryGetTended(PlayerManager.Car, out var tended) || tended != firebox)
                return;

            statusTime = Time.time;
            var puff = LastPuff;
            Main.Mod.Logger.Log($"{firebox.Car.ID} smoke: {beatCount} beats in {interval} s, exhaust {inputs.ExhaustPressure:F1} bar " +
                                $"(strength {PlumeModel.BeatStrength(tuning, inputs.ExhaustPressure):P0}), flow {inputs.TotalFlowNormalized:P0}, " +
                                $"soot {inputs.Soot:P0}; last puff {puff.Speed:F1} m/s, {puff.Particles}+{puff.SootParticles} particles, " +
                                $"{puff.Sparks:F2} sparks, color #{ColorUtility.ToHtmlStringRGBA(ToColor(puff.Color))}; " +
                                $"alive {string.Join(", ", Systems().Select(s => $"{s.name} {s.particleCount}"))}" +
                                (shown ? "" : " (hidden)"));
            beatCount = 0;
        }

        private IEnumerable<ParticleSystem> Systems() =>
            new[] { smoke, sootCore, jet }.Concat(embers ?? new ParticleSystem[0]).Where(s => s != null).Distinct();

        // The settings can hide the plume's rendering at once, to see what it costs the frame.
        private void ShowOrHide()
        {
            var show = Main.Settings.ShowChimneySmoke;
            if (show == shown)
                return;
            renderers = renderers ?? Systems().Select(s => s.GetComponent<ParticleSystemRenderer>()).Where(r => r != null).ToArray();
            foreach (var renderer in renderers)
                renderer.enabled = show;
            shown = show;
        }
    }
}
