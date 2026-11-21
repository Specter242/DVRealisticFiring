using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Controllers;
using LocoSim.Definitions;
using LocoSim.Implementations;
using RealisticStackEffects.Core;
using UnityEngine;

namespace RealisticStackEffects
{
    internal sealed partial class ChimneyPlume
    {
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
                var reboundSteam = plume.AttachVents();
                plume.MatchSorting(car);
                plume.ShimmerFirst();
                plume.SyncHeatShimmer();
                plume.enabled = true;
                return $"realistic chimney smoke rebound to the existing plume; {reboundSteam}";
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
            var steam = plume.AttachVents();
            var sorting = plume.MatchSorting(car);
            plume.ShimmerFirst();
            plume.stackLip = MeasureStackLip(car, Emitter(plume.smoke), plume.smoke.transform.up, out var lip, out plume.boreRadius, out plume.outsideRadius);
            lip += $"; aloft: {string.Join("; ", plume.aloft.Values.Select(a => a.Setup))}";
            return $"realistic chimney smoke {(custom ? "with the custom effects" : "on the game's systems")}: " +
                   $"cloud '{plume.smoke.name}'" +
                   (plume.sootCore != null ? $", soot '{plume.sootCore.name}'" : "") +
                   (plume.jet != null ? $", jet '{plume.jet.name}'" : "") +
                   $", sparks {string.Join("/", plume.embers.Select(s => $"'{s.name}'"))}" +
                   $", {2 * engine.numCylinders} beats per revolution from '{engineId}', flow from '{exhaust.ID}'; {sorting}; {lip}; {steam}; " +
                   $"simulated in {(plume.smoke.main.simulationSpace == ParticleSystemSimulationSpace.Custom && plume.smoke.main.customSimulationSpace != null ? $"'{plume.smoke.main.customSimulationSpace.name}'" : plume.smoke.main.simulationSpace.ToString())}";

            void Bind(ChimneyPlume target)
            {
                if (target.car != null && target.car != car)
                {
                    var hadVents = target.vents != null;
                    var hadSorting = target.steamSorting.Count > 0;
                    target.AlignSteam(false);
                    target.steamSorting.Clear();
                    target.vents?.Release();
                    target.vents = null;
                    target.systems = null;
                    target.renderers = null;
                    if (OnCar.TryGetValue(target.car, out var previous))
                    {
                        previous.Remove(target, hadVents, hadSorting);
                        if (previous.Count == 0)
                            OnCar.Remove(target.car);
                    }
                    target.carRoster = null;
                }
                target.stock = reader;
                target.car = car;
                target.BindInputs(fire, 2 * engine.numCylinders, crank, pressure, total,
                    flow.OrderedSimComps.OfType<SteamExhaust>().FirstOrDefault(c => c.id == exhaust.ID), broken);
                var boiler = graph.executionOrder.OfType<BoilerDefinition>().FirstOrDefault();
                target.steamChest = flow.TryGetPort($"{engineId}.{engine.steamChestPressureReadOut.ID}", out var chest) ? chest : null;
                target.boilerPressure = boiler != null && flow.TryGetPort($"{boiler.ID}.{boiler.pressureReadOut.ID}", out var boilerPort) ? boilerPort : null;
                var roster = OnCar.GetValue(car, _ => new CarPlumes<ChimneyPlume>());
                roster.Prune(IsAlivePlume);
                roster.Register(target);
                target.carRoster = roster;
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
            beatsPerTurn = Mathf.Max(1, beatsPerRevolution);
            pendingBeatCount = 0;
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
            if (car != null)
            {
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
            }

            string meshHow = null;
            if (fromMesh.HasValue)
            {
                var rim = near.Where(p => p.x >= fromMesh.Value - RimBand).Select(p => p.y).ToList();
                var widest = rim.Count > 0 ? rim.Max() : 0f;
                var nearest = rim.Count > 0 ? rim.Min() : 0f;
                var capped = !(nearest >= LeastBoreShare * widest);
                if (widest > 0.05f)
                {
                    outside = Mathf.Clamp(widest, StackGeometryHint.MinOutsideRadius, StackRadius);
                    bore = Mathf.Clamp(capped ? BoreOfOutside * outside : nearest, StackGeometryHint.MinBoreRadius, outside);
                }
                meshHow = $"stack lip {fromMesh.Value:F2} m above the smoke emitter, from {readable} readable meshes around the stack; " +
                          $"its rim {rim.Count} points, outside radius {outside:F2} m, bore {bore:F2} m{(capped ? " (capped model: a share of the outside)" : "")}";
            }

            var livery = car != null && car.carLivery != null ? car.carLivery.id : null;
            if (StackHost.StackGeometry != null)
            {
                bool hasHint = false;
                var rawHint = default(StackGeometryHint);
                string by = null;
                try
                {
                    hasHint = StackHost.StackGeometry(car, out rawHint, out by);
                }
                catch (System.Exception e)
                {
                    StackHost.Mod?.Logger.LogException("Reading stack geometry hint failed; falling back to mesh or default", e);
                }
                if (hasHint)
                {
                    var clean = rawHint.Sanitize();
                    if (clean.HasAny)
                    {
                        var lip = fromMesh;
                        if (!lip.HasValue && !clean.LipHeight.HasValue && livery != null
                            && MeasuredStackLips.TryGetValue(livery, out var fallbackLip))
                            lip = fallbackLip;
                        clean.Apply(ref lip, ref bore, ref outside);
                        var parts = new List<string>();
                        if (clean.LipHeight.HasValue)
                            parts.Add($"lip {clean.LipHeight.Value:F2} m");
                        if (clean.OutsideRadius.HasValue)
                            parts.Add($"outside {clean.OutsideRadius.Value:F2} m");
                        if (clean.BoreRadius.HasValue)
                            parts.Add($"bore {clean.BoreRadius.Value:F2} m");
                        var lipText = lip.HasValue ? $"stack lip {lip.Value:F2} m above the smoke emitter" : "stack lip not set";
                        var source = string.IsNullOrEmpty(by) ? "a provider" : by;
                        how = $"{lipText}, from {source} ({string.Join(", ", parts)}); outside radius {outside:F2} m, bore {bore:F2} m"
                              + (fromMesh.HasValue
                                  ? $" (overriding {readable} readable meshes)"
                                  : unreadable > 0
                                      ? $" ({unreadable} meshes around the stack can't be read in game)"
                                      : "");
                        return lip;
                    }
                }
            }

            if (fromMesh.HasValue)
            {
                how = meshHow;
                return fromMesh;
            }
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

        private static IEnumerable<ParticleSystemRenderer> RenderersOf(IEnumerable<ParticleSystem> systems)
        {
            if (systems == null)
                yield break;
            foreach (var system in systems)
            {
                var renderer = system != null ? system.GetComponent<ParticleSystemRenderer>() : null;
                if (renderer != null)
                    yield return renderer;
            }
        }

        private static IEnumerable<ParticleSystemRenderer> RenderersUnder(GameObject root) =>
            root != null ? root.GetComponentsInChildren<ParticleSystemRenderer>(true) : System.Array.Empty<ParticleSystemRenderer>();

        // The loco's own steam (dynamo, whistle, safety valves, leaks) and the
        // plume must share a render queue, sorting layer, order, and fudge, or
        // one always draws over the other whichever is nearer; matched, Unity
        // sorts them by distance. Takes the commonest of the stock settings.
        private string MatchSorting(TrainCar car)
        {
            var ours = RenderersOf(Systems()).ToList();
            var carPlumes = new HashSet<ChimneyPlume> { this };
            if (car != null && OnCar.TryGetValue(car, out var roster))
            {
                foreach (var other in roster.All)
                    if (other != null)
                        carPlumes.Add(other);
            }
            if (car != null)
            {
                foreach (var other in car.GetComponentsInChildren<ChimneyPlume>(true))
                    if (other != null)
                        carPlumes.Add(other);
            }
            var readers = car != null
                ? car.GetComponentsInChildren<SteamSmokeParticlePortReader>(true)
                : System.Array.Empty<SteamSmokeParticlePortReader>();
            var allOurs = CarPlumes.CollectStackRenderers(
                ours,
                carPlumes,
                readers,
                p => p != null ? RenderersOf(p.Systems()) : System.Array.Empty<ParticleSystemRenderer>(),
                p => p != null ? p.effectsRoot : null,
                r => r != null ? r.smokeParticlesParent : null,
                r => r != null ? r.emberParticlesParent : null,
                RenderersUnder);
            var stock = car.GetComponentsInChildren<ParticleSystemRenderer>(true)
                .Where(r => !allOurs.Contains(r) && r.sharedMaterial != null)
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
            // Only one active stack per car records and restores the car's stock
            // steam sorting so multiple stacks never record already-aligned values.
            var ownSteamSorting = carRoster == null || carRoster.CanOwnSorting(this, IsActivePlume, HasSteamSorting);
            if (ownSteamSorting)
            {
                foreach (var renderer in stock)
                {
                    var name = renderer.sharedMaterial.name;
                    if (renderer.sortingLayerID == layer && renderer.sharedMaterial.renderQueue == queue
                        && (name.StartsWith("GasWave", System.StringComparison.Ordinal)
                            || name.StartsWith("WispyCloudWhite", System.StringComparison.Ordinal)
                            || name.StartsWith("SmokeWhiteFull", System.StringComparison.Ordinal)))
                        steamSorting.Add((renderer, renderer.sortingOrder, renderer.sortingFudge));
                }
                if (steamSorting.Count > 0)
                    carRoster?.ClearSortingHandoff();
            }
            foreach (var renderer in ours)
            {
                renderer.sortingLayerID = layer;
                renderer.sortingOrder = order;
                renderer.sortingFudge = fudge;
                // The vents draw with the cloud's own material, set here through the cloud.
                if (renderer.sharedMaterial != null && (vents == null || !vents.Draws(renderer)))
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
            var start = StackHost.CameraTimed != null ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;
            try { SortAtStackCore(camera); }
            finally { StackHost.CameraTimed?.Invoke(start); }
        }

        private void SortAtStackCore(Camera camera)
        {
            if (camera == null || smoke == null || (camera.cullingMask & (1 << smoke.gameObject.layer)) == 0)
                return;
            AlignSteam(Look.MatchSteamOrder > 0.5f);
            var eye = camera.transform.position;
            var stack = Vector3.Distance(eye, Emitter(smoke));
            foreach (var system in Systems())
            {
                if (system == null || !system.gameObject.activeInHierarchy)
                    continue;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (renderer != null)
                    renderer.sortingFudge = sortingFudge + stack - Vector3.Distance(eye, renderer.bounds.center) + DrawOrderStep * DrawnBefore(system);
            }
        }

        // How many of the plume's systems a system is drawn before (a higher
        // fudge sorts it farther, so it is drawn earlier, under the rest).
        // The engine's other steam is in two: its parcels behind the plume
        // from the eye first of all, the rest after the jet (SteamVents).
        private int DrawnBefore(ParticleSystem system) =>
            vents != null && system == vents.Behind ? 5
            : system == smoke ? 4 : system == sootCore ? 3 : system == jet ? 2 : system == heat ? 0 : 1;

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

        // The engine's other steam, taken from the game's systems as they
        // are now (a cab refresh can replace them): with the custom effects
        // only, whose material and volume it is drawn with, and owned by at
        // most one active stack per car so multi-stack locomotives do not
        // duplicate vents or fight over the same stock renderers.
        private string AttachVents()
        {
            vents?.Release();
            vents = null;
            // The systems and their renderers (PassDue's view check) gather the vents' anew.
            systems = null;
            renderers = null;
            if (!custom || smoke == null || !StackHost.Settings.EngineSteam)
                return "the game's own steam around the engine";
            if (carRoster != null && !carRoster.CanOwnVents(this, IsActivePlume, HasVents))
                return "another stack on this engine owns its steam";
            vents = SteamVents.Create(car, smoke);
            carRoster?.ClearVentsHandoff();
            if (vents == null)
                return "no steam around the engine found";
            if (puffMaterials.TryGetValue(smoke, out var material))
                vents.UseMaterial(material);
            var more = vents.AddRunningGear(
                () => steamChest != null ? VentSteam.Gauge(steamChest.Value) : 0f,
                () => crankRotation != null ? crankRotation.Value : 0f,
                () => boilerPressure != null ? VentSteam.Gauge(boilerPressure.Value) : 0f,
                RunningGearDamage);
            return $"{vents.Setup}; {more}";
        }

        private void TakeOverCarSteam(bool attachVents)
        {
            if (car == null)
                return;
            AlignSteam(false);
            steamSorting.Clear();
            var steam = attachVents && vents == null ? AttachVents() : null;
            MatchSorting(car);
            ShimmerFirst();
            if (attachVents && vents != null && StackHost.Mod != null)
                StackHost.Mod.Logger.Log($"{SafeCarName()}: took over engine steam after another stack was released ({steam})");
        }

        private string SafeCarName() =>
            car == null ? name
            : car.logicCar != null ? car.logicCar.ID
            : car.carLivery != null ? car.carLivery.id
            : car.name;

        // How far the engine's running gear is damaged, 0 to 1, by the game's
        // own count (its mechanical powertrain's); 0 without one.
        private DV.Damage.DamageController damageController;

        private float RunningGearDamage()
        {
            if (damageController == null && car != null)
                damageController = car.GetComponent<DV.Damage.DamageController>();
            var gear = damageController != null ? damageController.mechanicalPT : null;
            return gear != null ? Mathf.Clamp01(gear.DamagePercentage) : 0f;
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            Active = false;
            Camera.onPreCull -= BeforeCull;
            var hadVents = vents != null;
            var hadSorting = steamSorting.Count > 0;
            AlignSteam(false);
            steamSorting.Clear();
            All.Remove(this);
            // Release vents and restore the car's stock renderers before marking a
            // handoff so any surviving stack takes over cleanly on its next update.
            vents?.Release();
            vents = null;
            systems = null;
            renderers = null;
            var roster = carRoster;
            carRoster = null;
            if (roster == null && car != null)
                OnCar.TryGetValue(car, out roster);
            if (roster != null)
            {
                roster.Remove(this, hadVents, hadSorting);
                if (roster.Count == 0 && car != null)
                    OnCar.Remove(car);
            }
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
    }
}
