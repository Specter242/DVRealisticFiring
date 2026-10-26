using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class StokerSpray : MonoBehaviour
    {
        private const float LumpsPerKg = 80f;
        private const float FinesPerKg = 250f;
        private const int MaxPerFrame = 8;

        private const float LumpMinSize = 0.02f;
        private const float LumpMaxSize = 0.045f;
        private const float FineMinSize = 0.02f;
        private const float FineMaxSize = 0.04f;

        // How often the spray reports itself to the log, in s.
        private const float ReportEvery = 30f;

        private static readonly Vector3 DefaultPlate = new Vector3(0.5f, 0.02f, 1.3f);
        private const float PlateInside = 0.01f;

        private const float PlateBelowHole = 0.093f;

        // A shovel target reaching further along the bed than this share is
        // not a box over the door's opening.
        private const float DoorReach = 0.15f;
        private const float NozzleAside = 0.08f;

        private const float ShortestFlight = 0.12f;
        private const float FlightPerMetre = 0.16f;

        // Fines: how long they burn in the air, in s; the draft's pull along
        // the firebox and up, in m/s²; and the air slowing them.
        private const float FineMinLife = 0.6f;
        private const float FineMaxLife = 1.3f;
        private const float DraftAlong = 2.5f;
        private const float DraftUp = 1.5f;
        private const float FineDrag = 0.12f;
        private const float FineSpeedLimit = 1.2f;

        // Steam: wisps a second from a jet wide open at full manifold
        // pressure, how long each lasts, in s, and how fast it leaves, in m/s.
        // Dust: puffs a second at the full feed.
        private const float WispsAtFull = 14f;
        private const float WispMinLife = 0.45f;
        private const float WispMaxLife = 0.8f;
        private const float WispSpeed = 1.6f;
        // The visible droplets evaporate in the hot firebox. The jets still
        // consume steam and throw coal; only their condensation is hidden.
        // One restores the old hot white puffs for developer comparisons.
        internal static float HotJetSteam = 0f;
        internal static float JetStreams = 1f;
        private float steamVisibility;

        private const float FineRise = 1.4f;
        private const float WispRise = 0.9f;

        private const float AheadOfPlate = 0.03f;
        private const float FineInward = 1.2f;
        private const float DustAtFullFeed = 8f;

        // At or under this opening the door is shut and nothing shows.
        private const float DoorShut = 0.02f;

        // Pieces set in the air at most, of each kind, as the door opens.
        private const int SeedMost = 60;

        // Steps a fine set in the air is carried through, in s.
        private const float SeedStep = 1f / 30f;

        private static readonly Color SteamColor = new Color(0.85f, 0.8f, 0.75f, 0.4f);
        private static readonly Color DustColor = new Color(0.12f, 0.11f, 0.1f, 0.5f);

        // Where each jet's wisp heads, as a point in the bed's box: the back
        // jets to the back corners, the front jets up each side, the fines
        // jet up the middle.
        private static readonly (StokerJet Jet, float Aside, Vector3 Toward)[] JetLanes =
        {
            (StokerJet.LeftBack, -1f, new Vector3(0.08f, 0.12f, 0.6f)),
            (StokerJet.RightBack, 1f, new Vector3(0.92f, 0.12f, 0.6f)),
            (StokerJet.LeftFront, -1f, new Vector3(0.25f, 0.7f, 0.9f)),
            (StokerJet.RightFront, 1f, new Vector3(0.75f, 0.7f, 0.9f)),
            (StokerJet.Fines, 0f, new Vector3(0.5f, 0.6f, 1f)),
        };

        private static Material lumpMaterial;
        private static Mesh[] lumpMeshes;

        private RealisticFirebox firebox;
        private ZonedCoalBed bed;
        private ParticleSystem lumps;
        private ParticleSystem fines;
        private ParticleSystem steam;
        private StokerJetStream jets;
        private float lumpsDue;
        private float finesDue;
        private readonly float[] wispsDue = new float[JetLanes.Length];
        private float dustDue;
        private bool draftSet;
        private Vector3 draftPull;
        private Vector3 bedUp = Vector3.up;
        private Vector3 bedAlong = Vector3.forward;
        private bool culled;
        private float nextReport;
        private Vector3 plateBox = DefaultPlate;
        private MaterialPropertyBlock lumpGlow;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static readonly Color IdleGlow = new Color(0.025f, 0.01f, 0.003f);
        private static readonly Color ForcedGlow = new Color(0.12f, 0.05f, 0.012f);

        /// <summary>Adds the spray to a firebox's zoned coal bed.</summary>
        public static void Attach(ZonedCoalBed bed, RealisticFirebox firebox)
        {
            if (bed == null || bed.GetComponent<StokerSpray>() != null)
                return;
            var spray = bed.gameObject.AddComponent<StokerSpray>();
            spray.bed = bed;
            spray.firebox = firebox;
            spray.lumps = spray.CreateLumps();
            var sparks = SparkMaterial();
            spray.fines = sparks != null ? spray.CreateFines(sparks) : null;
            var smoke = SmokeSystem(bed);
            spray.steam = smoke != null ? spray.CreateSteam(smoke) : null;
            var jetMaterial = PlumeAssets.StokerJetMaterial;
            if (jetMaterial != null)
                spray.jets = new StokerJetStream(spray.transform, bed.Effects, JetLanes.Length, jetMaterial);
        }

        private ParticleSystem NewSystem(string name, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            return system;
        }

        private ParticleSystem CreateLumps()
        {
            var system = NewSystem("stoker lumps", 200);
            var main = system.main;
            main.gravityModifier = 1f;
            main.startRotation3D = true;
            var rotation = system.rotationOverLifetime;
            rotation.enabled = true;
            rotation.separateAxes = true;
            rotation.x = new ParticleSystem.MinMaxCurve(-12f, 12f);
            rotation.y = new ParticleSystem.MinMaxCurve(-12f, 12f);
            rotation.z = new ParticleSystem.MinMaxCurve(-12f, 12f);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            var meshes = LumpMeshes();
            renderer.mesh = meshes[0];
            renderer.SetMeshes(meshes);
            renderer.sharedMaterial = LumpMaterial();
            lumpGlow = new MaterialPropertyBlock();
            system.Play();
            return system;
        }

        private ParticleSystem CreateFines(Material sparks)
        {
            var system = NewSystem("stoker fines", 300);
            var main = system.main;
            main.gravityModifier = 0.35f;
            // Catching as they leave the plate, burning brightest midway, gone.
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.45f, 0.08f, 0.02f), 0f),
                    new GradientColorKey(new Color(1f, 0.5f, 0.12f), 0.3f),
                    new GradientColorKey(new Color(1f, 0.82f, 0.45f), 0.6f),
                    new GradientColorKey(new Color(0.9f, 0.35f, 0.08f), 1f),
                },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.25f)));
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = FineSpeedLimit;
            limit.dampen = FineDrag;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = sparks;
            system.Play();
            return system;
        }

        private ParticleSystem CreateSteam(ParticleSystem smoke)
        {
            var system = NewSystem("stoker steam", 120);
            var main = system.main;
            main.gravityModifier = 0f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var color = system.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1f)));
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = WispSpeed;
            limit.dampen = 0.2f;
            // The firebox smoke's flipbook, played as it plays it, from a
            // random frame (as OilFireLook does: the start frame a share).
            var source = smoke.textureSheetAnimation;
            var sheet = system.textureSheetAnimation;
            sheet.enabled = source.enabled;
            sheet.mode = source.mode;
            sheet.numTilesX = source.numTilesX;
            sheet.numTilesY = source.numTilesY;
            sheet.animation = source.animation;
            sheet.rowMode = source.rowMode;
            sheet.rowIndex = source.rowIndex;
            sheet.frameOverTime = source.frameOverTime;
            sheet.frameOverTimeMultiplier = source.frameOverTimeMultiplier;
            sheet.cycleCount = source.cycleCount;
            sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f);
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = smoke.GetComponent<ParticleSystemRenderer>().sharedMaterial;
            system.Play();
            return system;
        }

        private void LateUpdate()
        {
            if (firebox == null || bed == null || !bed.Ready || lumps == null)
                return;
            var stoker = firebox.StokerModel;
            if (stoker == null || !(firebox.LastStoker is StokerFlow flow))
                return;
            if (!draftSet)
                SetDraft();

            Report(flow.Fed > 0f ? stoker.EngineSpeed * stoker.Spec.MaxFeedRate : 0f, flow);

            // Shut, nothing in the firebox shows.
            if (firebox.DoorOpening <= DoorShut)
            {
                if (!culled)
                    Cull(true);
                return;
            }
            var seed = culled;
            if (culled)
                Cull(false);

            var dt = Time.deltaTime;
            var fedRate = flow.Fed > 0f ? stoker.EngineSpeed * stoker.Spec.MaxFeedRate : 0f;
            var plate = InFrame(plateBox);
            var gravity = transform.InverseTransformDirection(Physics.gravity);
            var (lumpShare, fineTotal) = Shares(stoker);
            Firelight();
            if (seed && fedRate > 0f)
                Seed(stoker, fedRate, lumpShare, fineTotal, plate, gravity);
            Steam(flow, stoker, fedRate, dt);
            if (!(fedRate > 0f))
                return;

            lumpsDue = Mathf.Min(lumpsDue + fedRate * lumpShare * LumpsPerKg * dt, MaxPerFrame);
            finesDue = Mathf.Min(finesDue + fedRate * fineTotal * FinesPerKg * dt, MaxPerFrame);

            for (; lumpsDue >= 1f; lumpsDue -= 1f)
            {
                var target = Pick(stoker, lumps: true, lumpShare, out var column, out var overArch);
                Throw(lumps, Nozzle(column), target, gravity, Random.Range(LumpMinSize, LumpMaxSize), Color.white, overArch ? 0.8f : 1f);
            }
            // Fines burn only over a fire.
            var lit = firebox.Model.FireOn;
            for (; finesDue >= 1f && fines != null; finesDue -= 1f)
            {
                var target = Pick(stoker, lumps: false, fineTotal, out _, out _);
                if (lit)
                    Burn(plate, target);
            }
        }

        // The fire's light on the lumps, by how hard it burns.
        private void Firelight()
        {
            var glow = firebox.Model.FireOn ? Color.Lerp(IdleGlow, ForcedGlow, Mathf.Sqrt(Mathf.Clamp01(bed.FireForcing))) : IdleGlow * 0.3f;
            var renderer = lumps.GetComponent<ParticleSystemRenderer>();
            renderer.GetPropertyBlock(lumpGlow);
            lumpGlow.SetColor(EmissionColorId, glow);
            renderer.SetPropertyBlock(lumpGlow);
        }

        // What the spray is doing, for the log: the door, the feed, and what is in the air.
        private void Report(float fedRate, StokerFlow flow)
        {
            if (!Main.Settings.LogFireboxCreation || Time.time < nextReport)
                return;
            nextReport = Time.time + ReportEvery;
            Main.Mod.Logger.Log($"{firebox.Car?.ID}: stoker spray: door {firebox.DoorOpening:P0}{(culled ? " (shut, not drawn)" : "")}, " +
                                $"feed {fedRate:F2} kg/s, jets {flow.ManifoldPressure:F1} bar; in the air {lumps.particleCount} lumps, " +
                                $"{(fines != null ? fines.particleCount.ToString() : "no")} fines, {(steam != null ? steam.particleCount.ToString() : "no")} wisps; " +
                                LumpsDrawn());
        }

        // Whether the camera draws the lumps, and where the first one is.
        private string LumpsDrawn()
        {
            var renderer = lumps.GetComponent<ParticleSystemRenderer>();
            var first = new ParticleSystem.Particle[1];
            var where = lumps.GetParticles(first) > 0 && firebox.Car != null
                ? firebox.Car.transform.InverseTransformPoint(lumps.transform.TransformPoint(first[0].position)).ToString("F2") + $" {first[0].GetCurrentSize(lumps):F3} m"
                : "none";
            return $"lumps {(renderer.isVisible ? "drawn" : "not drawn")} ({renderer.meshCount} meshes, '{(renderer.sharedMaterial != null ? renderer.sharedMaterial.shader.name : "no material")}'), first at {where}";
        }

        // The lumps' share of the coal fed, and the fines' with those lost.
        private static (float Lumps, float Fines) Shares(Stoker stoker)
        {
            var lumpShare = 0f;
            var fineShare = 0f;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                lumpShare += stoker.LumpShares[i];
                fineShare += stoker.FineShares[i];
            }
            return (lumpShare, fineShare + stoker.LostShare);
        }

        // With the door shut nothing is thrown or drawn; open, it all shows again.
        private void Cull(bool shut)
        {
            culled = shut;
            jets?.Hide();
            lumpsDue = finesDue = dustDue = 0f;
            for (var j = 0; j < wispsDue.Length; j++)
                wispsDue[j] = 0f;
            foreach (var system in new[] { lumps, fines, steam })
            {
                if (system == null)
                    continue;
                if (shut)
                    system.Clear();
                system.GetComponent<ParticleSystemRenderer>().enabled = !shut;
            }
        }

        // The lumps and fines a running stoker has in the air, set at once at
        // their ages along their flights as the door opens.
        private void Seed(Stoker stoker, float fedRate, float lumpShare, float fineTotal, Vector3 plate, Vector3 gravity)
        {
            var meanFlight = ShortestFlight + FlightPerMetre * 1.5f;
            var count = Mathf.Min(SeedMost, Mathf.RoundToInt(fedRate * lumpShare * LumpsPerKg * meanFlight));
            if (count > 0)
            {
                var pieces = new ParticleSystem.Particle[count];
                for (var i = 0; i < count; i++)
                {
                    var target = Pick(stoker, lumps: true, lumpShare, out var column, out var overArch);
                    var from = Nozzle(column);
                    var flight = ShortestFlight + FlightPerMetre * Vector3.Distance(from, target);
                    var launch = (target - from - 0.5f * gravity * flight * flight) / flight;
                    var life = overArch ? flight * 0.8f : flight;
                    var age = Random.value * life * 0.95f;
                    pieces[i] = new ParticleSystem.Particle
                    {
                        position = from + launch * age + 0.5f * gravity * age * age,
                        velocity = launch + gravity * age,
                        startLifetime = life,
                        remainingLifetime = life - age,
                        startSize = Random.Range(LumpMinSize, LumpMaxSize),
                        startColor = Color.white,
                        rotation3D = Random.insideUnitSphere * 180f,
                        randomSeed = (uint)Random.Range(1, int.MaxValue),
                    };
                }
                lumps.SetParticles(pieces, count);
            }

            if (fines == null || !firebox.Model.FireOn)
                return;
            var meanLife = 0.5f * (FineMinLife + FineMaxLife);
            count = Mathf.Min(SeedMost, Mathf.RoundToInt(fedRate * fineTotal * FinesPerKg * meanLife));
            if (count <= 0)
                return;
            var sparks = new ParticleSystem.Particle[count];
            var fall = gravity * fines.main.gravityModifier.constant;
            for (var i = 0; i < count; i++)
            {
                var target = Pick(stoker, lumps: false, fineTotal, out _, out _);
                var life = Random.Range(FineMinLife, FineMaxLife);
                var age = Random.value * life * 0.95f;
                var position = plate + Random.insideUnitSphere * 0.03f;
                var velocity = Inward((target - plate).normalized * Mathf.Lerp(2f, 4f, Random.value) + bedUp * FineRise + Random.insideUnitSphere * 0.4f);
                // Carried as the system carries it: gravity, the draft, and the air.
                for (var t = 0f; t < age; t += SeedStep)
                {
                    velocity += (fall + draftPull) * SeedStep;
                    if (velocity.magnitude > FineSpeedLimit)
                        velocity *= 1f - FineDrag;
                    position += velocity * SeedStep;
                }
                sparks[i] = new ParticleSystem.Particle
                {
                    position = position,
                    velocity = velocity,
                    startLifetime = life,
                    remainingLifetime = life - age,
                    startSize = Random.Range(FineMinSize, FineMaxSize),
                    startColor = Color.white,
                    randomSeed = (uint)Random.Range(1, int.MaxValue),
                };
            }
            fines.SetParticles(sparks, count);
        }

        // The plate under the door's opening: under the foot of the cab's
        // firehole as the aim has it, its middle across, just inside the
        // door's shovel target's face toward the fire.
        private void FindPlate()
        {
            var target = bed.GetComponent<DV.Interaction.ItemUseTarget>();
            var colliders = target != null && target.targetColliders != null
                ? System.Array.FindAll(target.targetColliders, c => c != null)
                : new Collider[0];
            if (colliders.Length == 0)
                return;
            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var collider in colliders)
            {
                var bounds = collider.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var box = bed.BoxPoint(new Vector3(
                        (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (i & 4) == 0 ? bounds.min.z : bounds.max.z));
                    min = Vector3.Min(min, box);
                    max = Vector3.Max(max, box);
                }
            }
            // A target reaching well into the firebox is not the door's opening.
            if (max.y > DoorReach)
            {
                if (Main.Settings.LogFireboxCreation && firebox.Car != null)
                    Main.Mod.Logger.Log($"{firebox.Car.ID}: stoker plate: the door's shovel target reaches {max.y:P0} along the bed, not the door's opening; the plate stays at its default");
                return;
            }
            plateBox = new Vector3(0.5f * (min.x + max.x), Mathf.Max(max.y, 0f) + PlateInside, min.z);
            var car = firebox.Car;
            if (car != null)
            {
                var reach = CarSpace.LocalBounds(car.transform, colliders[0]);
                for (var i = 1; i < colliders.Length; i++)
                    reach.Encapsulate(CarSpace.LocalBounds(car.transform, colliders[i]));
                var hole = FireholeOpening.For(car, reach);
                var foot = bed.BoxPoint(car.transform.TransformPoint(new Vector3(0.5f * (hole.Min.x + hole.Max.x), hole.Min.y - PlateBelowHole, hole.Min.z)));
                plateBox = new Vector3(foot.x, plateBox.y, foot.z);
            }
            if (Main.Settings.LogFireboxCreation && firebox.Car != null)
                Main.Mod.Logger.Log($"{firebox.Car.ID}: stoker plate under the door at {firebox.Car.transform.InverseTransformPoint(bed.BedPoint(plateBox.x, plateBox.y, plateBox.z)):F2} m " +
                                    $"(across {plateBox.x:F2}, along {plateBox.y:F2}, up {plateBox.z:F2} of the bed's box)");
        }

        // The draft's pull on the fines, along the bed toward the tubes and up, in the spray's frame.
        private void SetDraft()
        {
            draftSet = true;
            FindPlate();
            var along = transform.InverseTransformDirection(bed.BedPoint(0.5f, 1f, 0f) - bed.BedPoint(0.5f, 0f, 0f)).normalized;
            var up = transform.InverseTransformDirection(bed.BedPoint(0.5f, 0.5f, 1f) - bed.BedPoint(0.5f, 0.5f, 0f)).normalized;
            bedUp = up;
            bedAlong = along;
            if (fines == null)
                return;
            var pull = along * DraftAlong + up * DraftUp;
            draftPull = pull;
            var force = fines.forceOverLifetime;
            force.enabled = true;
            force.space = ParticleSystemSimulationSpace.Local;
            force.x = new ParticleSystem.MinMaxCurve(pull.x);
            force.y = new ParticleSystem.MinMaxCurve(pull.y);
            force.z = new ParticleSystem.MinMaxCurve(pull.z);
        }

        // A point in the bed's box, in the spray's frame.
        private Vector3 InFrame(Vector3 box) => transform.InverseTransformPoint(bed.BedPoint(box.x, box.y, box.z));

        // The jet a column's lane leaves from: the side jets either side of the plate's middle.
        private Vector3 Nozzle(int column) =>
            InFrame(new Vector3(plateBox.x + (column - 1) * NozzleAside, plateBox.y, plateBox.z));

        // A landing point for the next piece, by the stoker's last
        // distribution, in the spray's frame, and its column; coal lost goes
        // past the front, high.
        private Vector3 Pick(Stoker stoker, bool lumps, float total, out int column, out bool overArch)
        {
            var roll = Random.value * total;
            var shares = lumps ? stoker.LumpShares : stoker.FineShares;
            overArch = false;
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                roll -= shares[i];
                if (roll > 0f)
                    continue;
                var zone = (GrateZone)i;
                column = GrateLayout.Column(zone);
                var across = (column + Random.value) / GrateLayout.Columns;
                var along = Mathf.Max((GrateLayout.Row(zone) + Random.value) / GrateLayout.Rows, plateBox.y + AheadOfPlate);
                return transform.InverseTransformPoint(bed.BedPoint(across, along, bed.ZoneHeight(zone)));
            }
            overArch = true;
            column = 1;
            return InFrame(new Vector3(Random.value, 1.15f, 3f));
        }

        // Launches a lump on the low, fast arc that lands on the target under gravity.
        private static void Throw(ParticleSystem system, Vector3 from, Vector3 to, Vector3 gravity, float size, Color color, float lifeShare)
        {
            var flight = ShortestFlight + FlightPerMetre * Vector3.Distance(from, to);
            var velocity = (to - from - 0.5f * gravity * flight * flight) / flight;
            system.Emit(new ParticleSystem.EmitParams
            {
                position = from + Random.insideUnitSphere * 0.02f,
                velocity = velocity,
                startLifetime = flight * lifeShare,
                startSize = size,
                startColor = color,
                rotation3D = Random.insideUnitSphere * 180f,
                applyShapeToPosition = false,
            }, 1);
        }

        // Sends a fine off the plate toward its zone to burn on the way and
        // drift with the draft.
        private void Burn(Vector3 from, Vector3 toward)
        {
            var heading = toward - from;
            var speed = Mathf.Lerp(2f, 4f, Random.value);
            fines.Emit(new ParticleSystem.EmitParams
            {
                position = from + Random.insideUnitSphere * 0.03f,
                velocity = Inward(heading.normalized * speed + bedUp * FineRise + Random.insideUnitSphere * 0.4f),
                startLifetime = Random.Range(FineMinLife, FineMaxLife),
                startSize = Random.Range(FineMinSize, FineMaxSize),
                startColor = Color.white,
                applyShapeToPosition = false,
            }, 1);
        }

        // A fine's launch, at least FineInward into the firebox.
        private Vector3 Inward(Vector3 velocity)
        {
            var inward = Vector3.Dot(velocity, bedAlong);
            return inward >= FineInward ? velocity : velocity + bedAlong * (FineInward - inward);
        }

        // Jet steam along each lane as strong as its jet, and coal dust while
        // the screw feeds.
        private void Steam(StokerFlow flow, Stoker stoker, float fedRate, float dt)
        {
            var cold = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(100f, 200f, firebox.Model.Temperature));
            steamVisibility = Mathf.Lerp(cold, 1f, Mathf.Clamp01(HotJetSteam));
            var color = SteamColor;
            color.a *= steamVisibility;
            var full = Mathf.Max(0.1f, stoker.Spec.ManifoldMaxPressure);
            var streams = JetStreams > 0.5f && jets != null;
            jets?.Begin();
            for (var j = 0; j < JetLanes.Length; j++)
            {
                var lane = JetLanes[j];
                var pressure = JetPressure(flow, lane.Jet);
                var from = InFrame(new Vector3(plateBox.x + lane.Aside * NozzleAside, plateBox.y, plateBox.z));
                var heading = InFrame(lane.Toward) - from;
                if (streams)
                {
                    // A low fan off the plate, always directed into the box.
                    var direction = heading.normalized + bedUp * 0.5f;
                    if (Vector3.Dot(direction, bedAlong) < 0.1f)
                        direction += bedAlong * (0.1f - Vector3.Dot(direction, bedAlong));
                    jets.Set(j, from, direction, bedUp, pressure / full, 1f - cold);
                    wispsDue[j] = 0f;
                    continue;
                }
                wispsDue[j] = steamVisibility > 0f
                    ? Mathf.Min(wispsDue[j] + Mathf.Clamp01(pressure / full) * WispsAtFull * dt, 2f)
                    : 0f;
                if (wispsDue[j] < 1f)
                    continue;
                wispsDue[j] -= 1f;
                if (steam != null)
                    Puff(from, heading, color, Random.Range(0.06f, 0.1f));
            }
            if (streams) jets.End(); else jets?.Hide();
            if (steam == null)
                return;
            dustDue = Mathf.Min(dustDue + Mathf.Clamp01(fedRate / Mathf.Max(0.01f, stoker.Spec.MaxFeedRate)) * DustAtFullFeed * dt, 2f);
            for (; dustDue >= 1f; dustDue -= 1f)
            {
                var from = InFrame(plateBox);
                Puff(from, InFrame(new Vector3(Random.value, 0.5f, 1f)) - from, DustColor, Random.Range(0.04f, 0.07f));
            }
        }

        internal string Describe() => $"stoker speed {firebox.StokerEngineSpeed:F2}, jet pressure {firebox.LastStoker?.ManifoldPressure:F2}, " +
            $"condensation {steamVisibility:F2}, particles {steam?.particleCount ?? 0}, {jets?.Describe()}";

        private void OnDestroy() => jets?.Destroy();

        private void Puff(Vector3 from, Vector3 heading, Color color, float size)
        {
            steam.Emit(new ParticleSystem.EmitParams
            {
                position = from + Random.insideUnitSphere * 0.02f,
                velocity = heading.normalized * WispSpeed * Random.Range(0.7f, 1.2f) + bedUp * WispRise + Random.insideUnitSphere * 0.3f,
                startLifetime = Random.Range(WispMinLife, WispMaxLife),
                startSize = size * 4f,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            }, 1);
        }

        private static float JetPressure(StokerFlow flow, StokerJet jet)
        {
            switch (jet)
            {
                case StokerJet.LeftBack: return flow.LeftBackPressure;
                case StokerJet.RightBack: return flow.RightBackPressure;
                case StokerJet.LeftFront: return flow.LeftFrontPressure;
                case StokerJet.RightFront: return flow.RightFrontPressure;
                default: return flow.FinesPressure;
            }
        }

        // Faceted lumps about a unit across: an icosahedron's corners pushed
        // in and out and squashed, flat-shaded, each on its own shade of the
        // coal texture.
        private static Mesh[] LumpMeshes()
        {
            if (lumpMeshes != null)
                return lumpMeshes;
            var random = new System.Random(588031);
            lumpMeshes = new Mesh[4];
            for (var m = 0; m < lumpMeshes.Length; m++)
                lumpMeshes[m] = Lump(random, (m + 0.5f) / lumpMeshes.Length);
            return lumpMeshes;
        }

        private static Mesh Lump(System.Random random, float shade)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var corners = new[]
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var faces = new[]
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
            var squash = new Vector3(1f, Range(0.6f, 0.85f), Range(0.75f, 1.05f));
            for (var i = 0; i < corners.Length; i++)
                corners[i] = Vector3.Scale(corners[i].normalized * Range(0.75f, 1.15f), squash) * 0.5f;
            var vertices = new Vector3[faces.Length];
            var uvs = new Vector2[faces.Length];
            var triangles = new int[faces.Length];
            for (var i = 0; i < faces.Length; i++)
            {
                vertices[i] = corners[faces[i]];
                uvs[i] = new Vector2(shade, 0.5f);
                triangles[i] = i;
            }
            var mesh = new Mesh { name = "RealisticFiring stoker lump", vertices = vertices, uv = uvs, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // Coal: dark and glossy, a little brown, in four shades for the four
        // lump shapes, with a faint warm glint from the firelight.
        private static Material LumpMaterial()
        {
            if (lumpMaterial != null)
                return lumpMaterial;
            var shades = new Texture2D(4, 1, TextureFormat.RGBA32, false) { name = "RealisticFiring coal shades", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            shades.SetPixels(new[]
            {
                new Color(0.07f, 0.065f, 0.06f), new Color(0.1f, 0.092f, 0.082f),
                new Color(0.13f, 0.12f, 0.105f), new Color(0.17f, 0.155f, 0.135f),
            });
            shades.Apply(false, true);
            lumpMaterial = new Material(Shader.Find("Standard")) { name = "RealisticFiring stoker coal", mainTexture = shades, color = Color.white };
            lumpMaterial.SetFloat("_Glossiness", 0.55f);
            lumpMaterial.EnableKeyword("_EMISSION");
            lumpMaterial.SetColor("_EmissionColor", IdleGlow);
            return lumpMaterial;
        }

        // The chimney's own cinder material, from the effects bundle.
        private static Material SparkMaterial()
        {
            var prefab = PlumeAssets.Prefab;
            var sparks = prefab != null ? prefab.transform.Find("Sparks") : null;
            var renderer = sparks != null ? sparks.GetComponent<ParticleSystemRenderer>() : null;
            return renderer != null ? renderer.sharedMaterial : null;
        }

        // The firebox smoke, from the bed's effects: its material and flipbook.
        private static ParticleSystem SmokeSystem(ZonedCoalBed bed)
        {
            var smoke = bed.Effects != null ? bed.Effects.Find("Smoke") : null;
            var system = smoke != null ? smoke.GetComponent<ParticleSystem>() : null;
            var renderer = smoke != null ? smoke.GetComponent<ParticleSystemRenderer>() : null;
            return system != null && renderer != null && renderer.sharedMaterial != null ? system : null;
        }
    }
}
