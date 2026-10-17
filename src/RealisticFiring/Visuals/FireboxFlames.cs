using System.Linq;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The flames and smoke over the fire bed, zone by zone, in place of the
    /// stock fire's one big flame. Flames are cards standing on the coal, a
    /// few to a zone, each turned to the camera and drawing licking tongues in
    /// the flame shader: tall and bright where the zone burns hard, lazy and
    /// orange over green coal, none on dead coal or ash. The draft makes them
    /// lick faster and leans them, taller and narrower, toward the tube plate.
    /// Each exhaust beat pulls a gulp of air through the fire: the flames
    /// surge forward and stretch, the bed and the light brighten, and they
    /// settle until the next. Green coal gives off thick smoke lit by the fire
    /// under it until it catches; air over the fire burns it off. A shovelful
    /// smokes the box out: a puff of dust as it lands, then thick, dark,
    /// low smoke filling the firebox for a few seconds, so the fireman waits
    /// to see where it landed. The smoke stays in the firebox, drawn into
    /// the tubes at the tube sheet. The
    /// firebox's light takes the fire's color and wavers with it.
    /// </summary>
    internal sealed class FireboxFlames : MonoBehaviour
    {
        private static readonly int DraftId = Shader.PropertyToID("_Draft");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");

        // Flame cards per zone, and their height. "A hot fire is indicated when
        // flames are bright and short. A low firebox temperature is indicated
        // by long, smoky flames" (1944 New York firing course, Unit I-11): a
        // hot zone's flames are short and bright, green or cool coal's long.
        private const int CardsPerZone = 8;

        // Of those, this many are sheets: a row of tongues of their own
        // heights over a continuous band of flame, a line of fire.
        private const int SheetsPerZone = 2;
        private const int SheetTongues = 5;
        private const float SheetWidth = 3f;
        private const float CardBase = 0.12f;
        private const float CardCool = 0.18f;
        private const float CardGreen = 0.28f;

        // The most puffs of each kind a zone emits in one frame. How much
        // smoke the fire makes comes from the firebox's FireboxSmoke, which
        // the stack lets out too.
        private const int MaxPerFrame = 12;

        // An exhaust beat: how fast its pulse dies away (s), and how much more
        // the draft draws the smoke while it lasts.
        private const float PulseDecay = 0.15f;
        private const float BeatDraw = 1.5f;

        // The draft carries the firebox's smoke up off the bed and forward to
        // the tubes, slowly enough that it fills the box (in m/s, from no draft
        // to full), and a puff takes up that drift over this time, in s.
        // Beats only add to the drift while their pulse lasts, so they cannot
        // pile speed onto the smoke.
        private const float FlowRiseSlow = 0.08f;
        private const float FlowRiseFast = 0.25f;
        private const float FlowForwardSlow = 0.1f;
        private const float FlowForwardFast = 0.5f;
        private const float FlowSettle = 0.5f;

        // Exhaust pressure for a full-strength beat, absolute bar.
        private const float FullBeatPressure = 6f;

        // Air through the grate at the stock fire's most, per kg/s of coal it burns.
        private const float AirPerCoal = 13.9f;

        // Firebox smoke is lit by the fire under it: a warm grey-brown,
        // brighter over a brighter fire.
        private static readonly Color SmokeColor = new Color(0.42f, 0.33f, 0.25f, 0.95f);
        private const float SmokeUnlit = 0.3f;

        // A shovelful of green coal on a hot fire smokes the box out: its dust
        // as it lands, this many puffs per kg, then its gas, thick and dark,
        // anywhere in the box's height over and around the zone, filling the
        // firebox until it catches and the draft clears it.
        private const float DustPerKg = 5f;
        private static readonly Color SmokeOutColor = new Color(0.2f, 0.17f, 0.15f, 1f);
        private static readonly Color DustColor = new Color(0.17f, 0.16f, 0.15f, 0.9f);
        private const int MinSmokeParticles = 1500;

        // A shovelful smothers the flames over its zone until its gas
        // catches: with this much charge left, in kg, they burn at the share
        // left by this much dimming.
        private const float SmotherCharge = 2f;
        private const float SmotherDim = 0.7f;

        // A melted fusible plug blows steam and water down from the crown
        // sheet's highest point, toward the front of the box: this many puffs
        // a second per kg/s blowing, the jet spending itself on the fire and
        // the steam rolling out to fill the box. Lit only by what is left of
        // the fire.
        private const float SteamPerKg = 60f;
        private static readonly Color SteamColor = new Color(0.86f, 0.87f, 0.89f, 0.9f);
        private const float SteamUnlit = 0.5f;

        private enum SmokeKind
        {
            // Green coal's gas and the fire's own smoke.
            Gas,
            // A shovelful's gas, thick and dark, filling the box.
            SmokeOut,
            // A shovelful's dust, thrown out as it lands.
            Dust,
            // Steam from a melted fusible plug.
            Steam,
        }

        // The smoke stays in the firebox: the firebox's own shell where the
        // loco has one (the S282's runs from the backhead to the tube plate,
        // a metre past the coal), from the grate up; else the bed's box up to
        // a crown sheet at this many grate widths. A shell counts if it holds
        // the bed and is no bigger than these multiples of it, and no taller
        // than this above the grate, in m. A puff keeps this far inside the
        // walls and no bigger than its room there (the puff texture fades out
        // at this share of its size from the middle), growing back as it
        // moves into the open; at the tube sheet, the front, the draft draws
        // it into the tubes.
        private const float CrownPerWidth = 0.9f;
        private const float MaxShellWidth = 2f;
        private const float MaxShellLength = 2.5f;
        private const float MaxShellHeight = 3f;
        private const int ShellSearchLevels = 4;
        private const float WallClearance = 0.03f;
        private const float PuffRadius = 0.4f;

        private RealisticFirebox firebox;
        private ZonedCoalBed bed;
        private Fire fire;
        private ParticleSystem oldFlames;
        private ParticleSystem smoke;
        private Renderer[] stockFlames = new Renderer[0];
        private int stockFlameChildren = -1;

        private ParticleSystem[] stockSparks = new ParticleSystem[0];
        private int stockSparkChildren = -1;
        private readonly float[] smokeDue = new float[GrateLayout.ZoneCount];
        private readonly float[] smokeOutDue = new float[GrateLayout.ZoneCount];
        private bool dustDrained;
        private Color lightColor = Color.white;
        private ExhaustBeatCounter beats;
        private Port crank;
        private Port exhaustPressure;
        private ParticleSystem.Particle[] live = new ParticleSystem.Particle[256];
        private Bounds smokeBox;
        private float front = 1f;
        private readonly SeedMemory<float> smokeSizes = new SeedMemory<float>();
        private float glowLevel;
        private float diagnoseAt = float.NaN;
        private BoilerWaterRig boilerWater;
        private float steamDue;

        private MeshRenderer cardRenderer;
        private Mesh cards;
        private Vector3[] cardVertices;
        private Color[] cardColors;
        private Vector2[] cardSpots;
        private float[] cardSeeds;
        private readonly MaterialPropertyBlock cardProperties = new MaterialPropertyBlock();

        /// <summary>The last exhaust beat's surge, dying away: 0 to 1.</summary>
        public float Pulse { get; private set; }

        public static string Attach(Fire fire, ZonedCoalBed bed, RealisticFirebox firebox)
        {
            if (bed == null || bed.Effects == null || fire.GetComponent<FireboxFlames>() != null)
                return null;
            var flames = fire.gameObject.AddComponent<FireboxFlames>();
            flames.fire = fire;
            flames.bed = bed;
            flames.firebox = firebox;
            flames.oldFlames = bed.Effects.Find("Flames")?.GetComponent<ParticleSystem>();
            flames.smoke = bed.Effects.Find("Smoke")?.GetComponent<ParticleSystem>();
            if (fire.fireLight != null)
                flames.lightColor = fire.fireLight.color;
            var flow = firebox.Car != null && firebox.Car.SimController != null ? firebox.Car.SimController.SimulationFlow : null;
            var engine = flow != null ? flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault() : null;
            if (engine != null)
            {
                flames.beats = new ExhaustBeatCounter(2 * engine.numCylinders);
                flames.crank = engine.crankRotationReadOut;
                flames.exhaustPressure = engine.exhaustPressureReadOut;
            }
            flames.BuildCards(bed.Effects.Find("FlameCards"));
            if (flames.smoke == null)
                return null;
            var smokeMain = flames.smoke.main;
            smokeMain.maxParticles = Mathf.Max(smokeMain.maxParticles, MinSmokeParticles);
            // The flames and the smoke are each one renderer, sorted as a whole;
            // the smoke goes over the flames, so smoke in view hides them.
            var smokeRenderer = flames.smoke.GetComponent<ParticleSystemRenderer>();
            if (smokeRenderer != null && flames.cardRenderer != null)
            {
                smokeRenderer.sortingLayerID = flames.cardRenderer.sortingLayerID;
                smokeRenderer.sortingOrder = flames.cardRenderer.sortingOrder + 1;
            }
            var shell = flames.MeasureSmokeBox();
            var box = flames.smokeBox.size;
            return $"firebox smoke kept in {box.x:F2} by {box.z:F2} m, {box.y:F2} m up from the grate, " +
                   (shell != null ? $"inside the firebox shell '{shell}'" : "over the bed (no firebox shell found)");
        }

        // The firebox in the smoke's space (which follows the grate's slope):
        // the firebox's own shell from the grate up where one holds the bed,
        // else the bed's box raised to a crown sheet. Returns the shell's name.
        private string MeasureSmokeBox()
        {
            var space = smoke.transform;
            Vector3 Corner(int i) => space.InverseTransformPoint(bed.BedPoint(i & 1, (i >> 1) & 1, (i >> 2) & 1));
            var bedBox = new Bounds(Corner(0), Vector3.zero);
            for (var i = 1; i < 8; i++)
                bedBox.Encapsulate(Corner(i));
            front = Mathf.Sign(Corner(2).z - Corner(0).z);

            var shell = FindShell(space, bedBox, out var shellBox);
            if (shell == null)
            {
                smokeBox = bedBox;
                smokeBox.max = new Vector3(bedBox.max.x, Mathf.Max(bedBox.max.y, bedBox.min.y + CrownPerWidth * bedBox.size.x), bedBox.max.z);
                return null;
            }
            // The grate is the floor; the shell's walls, tube plate, and crown
            // the rest. Never further back than the bed, whose back is the door.
            var back = front > 0f ? Mathf.Max(shellBox.min.z, bedBox.min.z) : Mathf.Min(shellBox.max.z, bedBox.max.z);
            var min = new Vector3(shellBox.min.x, bedBox.min.y, front > 0f ? back : shellBox.min.z);
            var max = new Vector3(shellBox.max.x, shellBox.max.y, front > 0f ? shellBox.max.z : back);
            smokeBox = new Bounds();
            smokeBox.SetMinMax(min, max);
            return shell;
        }

        // The stock firebox's own shell: the smallest mesh near the coal pile
        // (in the same part of the cab) that holds the whole bed, measured
        // from its mesh's bounds in the smoke's space. Its top is taken where
        // it is lowest, as the grate's slope tilts it.
        private string FindShell(Transform space, Bounds bedBox, out Bounds shellBox)
        {
            shellBox = default;
            var coal = bed.StockCoal;
            if (coal == null)
                return null;
            var top = coal.transform;
            var car = firebox.Car != null ? firebox.Car.transform : null;
            for (var level = 0; level < ShellSearchLevels && top.parent != null && top.parent != car; level++)
                top = top.parent;

            string found = null;
            var smallest = float.MaxValue;
            foreach (var filter in top.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = filter.sharedMesh;
                if (mesh == null || filter.gameObject == coal.gameObject || filter.transform.IsChildOf(bed.Effects))
                    continue;
                var local = mesh.bounds;
                var box = new Bounds(space.InverseTransformPoint(filter.transform.TransformPoint(local.center)), Vector3.zero);
                var lowestTop = float.MaxValue;
                for (var i = 0; i < 8; i++)
                {
                    var corner = space.InverseTransformPoint(filter.transform.TransformPoint(new Vector3(
                        (i & 1) == 0 ? local.min.x : local.max.x,
                        (i & 2) == 0 ? local.min.y : local.max.y,
                        (i & 4) == 0 ? local.min.z : local.max.z)));
                    box.Encapsulate(corner);
                    if ((i & 2) != 0)
                        lowestTop = Mathf.Min(lowestTop, corner.y);
                }
                var holds = box.min.x <= bedBox.min.x + 0.05f && box.max.x >= bedBox.max.x - 0.05f
                            && box.min.z <= bedBox.min.z + 0.05f && box.max.z >= bedBox.max.z - 0.05f
                            && lowestTop > bedBox.max.y;
                var fits = box.size.x <= MaxShellWidth * bedBox.size.x && box.size.z <= MaxShellLength * bedBox.size.z
                           && lowestTop - bedBox.min.y <= MaxShellHeight;
                var volume = box.size.x * box.size.y * box.size.z;
                if (!holds || !fits || volume >= smallest)
                    continue;
                smallest = volume;
                found = filter.name;
                box.max = new Vector3(box.max.x, lowestTop, box.max.z);
                shellBox = box;
            }
            return found;
        }

        // The flame cards: four corners each, all at the card's root; the
        // shader stands them up and turns them to the camera.
        private void BuildCards(Transform holder)
        {
            var filter = holder != null ? holder.GetComponent<MeshFilter>() : null;
            cardRenderer = holder != null ? holder.GetComponent<MeshRenderer>() : null;
            if (filter == null || cardRenderer == null)
                return;
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            var count = GrateLayout.ZoneCount * CardsPerZone;
            cardVertices = new Vector3[count * 4];
            cardColors = new Color[count * 4];
            cardSpots = new Vector2[count];
            cardSeeds = new float[count];
            var corners = new Vector2[count * 4];
            var sheets = new Vector2[count * 4];
            var triangles = new int[count * 6];
            var random = new System.Random(4449);
            for (var c = 0; c < count; c++)
            {
                var zone = (GrateZone)(c / CardsPerZone);
                cardSpots[c] = new Vector2(
                    (GrateLayout.Column(zone) + 0.1f + 0.8f * (float)random.NextDouble()) / GrateLayout.Columns,
                    (GrateLayout.Row(zone) + 0.1f + 0.8f * (float)random.NextDouble()) / GrateLayout.Rows);
                cardSeeds[c] = (float)random.NextDouble();
                var sheet = c % CardsPerZone < SheetsPerZone ? new Vector2(SheetTongues, SheetWidth) : Vector2.one;
                var v = c * 4;
                for (var k = 0; k < 4; k++)
                    sheets[v + k] = sheet;
                corners[v] = new Vector2(-0.5f, 0f);
                corners[v + 1] = new Vector2(0.5f, 0f);
                corners[v + 2] = new Vector2(-0.5f, 1f);
                corners[v + 3] = new Vector2(0.5f, 1f);
                var t = c * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            cards = new Mesh { name = "RealisticFiring flame cards" };
            cards.MarkDynamic();
            cards.vertices = cardVertices;
            cards.colors = cardColors;
            cards.uv2 = corners;
            cards.uv3 = sheets;
            cards.triangles = triangles;
            filter.sharedMesh = cards;
        }

        private void LateUpdate()
        {
            var start = PerfMeter.Start();
            Step();
            PerfMeter.Stop(PerfSection.CoalBed, start);
        }

        private void Step()
        {
            if (firebox == null || bed == null || !bed.Ready)
                return;
            var shown = Main.Settings.ShowZonedCoalBed;
            var stockFlame = fire != null ? fire.fireObj : null;
            if (stockFlame != null && stockFlame.transform.childCount != stockFlameChildren)
            {
                stockFlameChildren = stockFlame.transform.childCount;
                stockFlames = stockFlame.GetComponentsInChildren<Renderer>(true);
            }
            foreach (var renderer in stockFlames)
            {
                if (renderer != null)
                    renderer.enabled = !shown && cards == null;
            }
            KeepSparksOff();
            if (oldFlames != null && oldFlames.isPlaying)
                oldFlames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (cardRenderer != null)
                cardRenderer.enabled = shown;
            var model = firebox.Model;
            if (smoke != null && shown)
                BlowSteam(Time.deltaTime);
            if (!shown || !model.FireOn)
            {
                UpdateCards(model, 0f, false);
                Light(0f, 0f);
                if (smoke != null)
                    Confine(0f, Time.deltaTime);
                return;
            }

            var dt = Time.deltaTime;
            var draft = Mathf.Clamp01(model.PrimaryAirFlow / Mathf.Max(1e-3f, model.Parameters.MaxCombustionRate * AirPerCoal));

            // The exhaust beats: each one a surge through the fire.
            Pulse *= Mathf.Exp(-dt / PulseDecay);
            if (beats != null && crank != null && beats.Advance(crank.Value) > 0 && exhaustPressure != null)
            {
                var beat = Mathf.Clamp01((exhaustPressure.Value - 1f) / (FullBeatPressure - 1f));
                if (beat > 0.02f)
                    Pulse = Mathf.Max(Pulse, beat);
            }

            var hottest = 0f;
            var meanHeat = 0f;
            var source = firebox.Smoke;
            // Coal that landed before the view was here throws no dust now.
            if (!dustDrained)
            {
                for (var i = 0; i < GrateLayout.ZoneCount; i++)
                    source.TakeLanded((GrateZone)i);
                dustDrained = true;
            }
            for (var i = 0; i < GrateLayout.ZoneCount; i++)
            {
                var zone = (GrateZone)i;
                var heat = bed.ZoneHeat(zone);
                meanHeat += heat / GrateLayout.ZoneCount;
                hottest = Mathf.Max(hottest, heat);
                if (smoke == null)
                    continue;

                // A shovelful landing throws its dust at once.
                for (var n = Mathf.RoundToInt(DustPerKg * source.TakeLanded(zone)); n > 0; n--)
                    EmitSmoke(zone, draft, SmokeKind.Dust);

                // Green coal's gas and the fire's own smoke rise over the zone;
                // a shovelful's gas fills the box before it catches.
                smokeDue[i] = Mathf.Min(smokeDue[i] + dt * source.GasRate(zone), MaxPerFrame);
                for (; smokeDue[i] >= 1f; smokeDue[i] -= 1f)
                    EmitSmoke(zone, draft, SmokeKind.Gas);
                smokeOutDue[i] = Mathf.Min(smokeOutDue[i] + dt * source.SmokeOutRate(zone), MaxPerFrame);
                for (; smokeOutDue[i] >= 1f; smokeOutDue[i] -= 1f)
                    EmitSmoke(zone, draft, SmokeKind.SmokeOut);
            }
            if (smoke != null)
                Confine(draft, dt);
            glowLevel = meanHeat;
            UpdateCards(model, draft, true);
            EnsureDepth();
            Diagnose(model);
            Light(meanHeat, hottest);
        }

        // The flame cards fade into the coal by the camera's depth texture, which
        // a camera in forward rendering only draws when something asks for it.
        private static void EnsureDepth()
        {
            var camera = Camera.main;
            if (camera != null && (camera.depthTextureMode & DepthTextureMode.Depth) == 0)
                camera.depthTextureMode |= DepthTextureMode.Depth;
        }

        // Once, ten seconds after the flames start: what they are doing and
        // what the camera gives them, for the log.
        private void Diagnose(FireModel model)
        {
            if (float.IsNaN(diagnoseAt))
                diagnoseAt = Time.time + 10f;
            if (Time.time < diagnoseAt || float.IsInfinity(diagnoseAt))
                return;
            diagnoseAt = float.PositiveInfinity;
            if (!Main.Settings.LogFireboxCreation || cardColors == null)
                return;
            var burning = 0;
            var burnSum = 0f;
            var heightSum = 0f;
            for (var c = 0; c < cardColors.Length; c += 4)
            {
                if (cardColors[c].a <= 0f)
                    continue;
                burning++;
                burnSum += cardColors[c].r;
                heightSum += cardColors[c].a;
            }
            var camera = Camera.main;
            var smother = firebox.Smoke.TotalCharge;
            Main.Mod.Logger.Log($"{firebox.Car?.ID}: flames: {burning} of {cardColors.Length / 4} cards burning" +
                                (burning > 0 ? $", strength {burnSum / burning:F2}, height {heightSum / burning:F2} m" : "") +
                                $", smoke-out charge {smother:F1} kg, renderer {(cardRenderer != null && cardRenderer.enabled ? "on" : "off")}; " +
                                (camera != null ? $"camera {camera.actualRenderingPath}, depth texture {camera.depthTextureMode}" : "no main camera"));
        }

        // Each card stands on its zone's coal, as tall and bright as the zone burns.
        private void UpdateCards(FireModel model, float draft, bool lit)
        {
            if (cards == null)
                return;
            for (var c = 0; c < cardSpots.Length; c++)
            {
                var zone = (GrateZone)(c / CardsPerZone);
                var state = model.Zones[(int)zone];
                var smothered = SmotherDim * Mathf.Clamp01(firebox.Smoke.Charge(zone) / SmotherCharge);
                var burn = lit && state.Coal >= 0.2f ? bed.ZoneForcing(zone) * (1f - smothered) : 0f;
                var green = bed.ZoneCover(zone);
                var cool = 1f - bed.ZoneHeat(zone);
                var height = burn > 0.01f ? (CardBase + CardCool * cool + CardGreen * green) * (0.75f + 0.5f * cardSeeds[c]) : 0f;
                var root = bed.Effects.InverseTransformPoint(bed.BedPoint(cardSpots[c].x, cardSpots[c].y, bed.SurfaceHeight(cardSpots[c].x, cardSpots[c].y) * 0.9f));
                var color = new Color(burn, green, cardSeeds[c], Mathf.Clamp01(height));
                for (var k = 0; k < 4; k++)
                {
                    cardVertices[c * 4 + k] = root;
                    cardColors[c * 4 + k] = color;
                }
            }
            cards.vertices = cardVertices;
            cards.colors = cardColors;
            // The shader moves the corners; keep the cards drawn wherever they reach.
            cards.bounds = new Bounds(Vector3.zero, Vector3.one * 8f);

            cardRenderer.GetPropertyBlock(cardProperties);
            cardProperties.SetFloat(DraftId, draft);
            cardProperties.SetFloat(PulseId, Pulse);
            cardRenderer.SetPropertyBlock(cardProperties);
        }

        // The smoke drifts with the draft, surging on each beat, and is kept
        // in the firebox: each puff inside the walls, under the crown, and no
        // bigger than its room, growing back to its own size in the open;
        // what reaches the tube sheet goes into the tubes.
        private void Confine(float draft, float dt)
        {
            var count = smoke.particleCount;
            if (count == 0)
                return;
            if (live.Length < count)
                live = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(count)];
            count = smoke.GetParticles(live, count);
            var inner = new Bounds(smokeBox.center, smokeBox.size - 2f * WallClearance * Vector3.one);
            var tubes = front > 0f ? inner.max.z : inner.min.z;
            var drawing = 1f + BeatDraw * Pulse;
            var flow = new Vector3(0f, Mathf.Lerp(FlowRiseSlow, FlowRiseFast, draft),
                front * Mathf.Lerp(FlowForwardSlow, FlowForwardFast, draft)) * drawing;
            var settle = 1f - Mathf.Exp(-dt / FlowSettle);
            var growth = smoke.sizeOverLifetime.enabled ? smoke.sizeOverLifetime.size : new ParticleSystem.MinMaxCurve(1f);
            for (var i = 0; i < count; i++)
            {
                var particle = live[i];
                var position = particle.position;
                if ((position.z - tubes) * front >= 0f)
                {
                    particle.remainingLifetime = 0f;
                    live[i] = particle;
                    continue;
                }
                var velocity = particle.velocity + (flow - particle.velocity) * settle;
                for (var axis = 0; axis < 3; axis++)
                {
                    if (position[axis] < inner.min[axis])
                    {
                        position[axis] = inner.min[axis];
                        velocity[axis] = Mathf.Max(0f, velocity[axis]);
                    }
                    else if (position[axis] > inner.max[axis])
                    {
                        position[axis] = inner.max[axis];
                        velocity[axis] = Mathf.Min(0f, velocity[axis]);
                    }
                }
                particle.position = position;
                particle.velocity = velocity;

                var room = Mathf.Min(
                    Mathf.Min(position.x - smokeBox.min.x, smokeBox.max.x - position.x),
                    Mathf.Min(position.y - smokeBox.min.y, smokeBox.max.y - position.y),
                    Mathf.Min(position.z - smokeBox.min.z, smokeBox.max.z - position.z));
                var intended = smokeSizes.Recall(particle.randomSeed, particle.startSize);
                var life = particle.startLifetime > 0f ? 1f - particle.remainingLifetime / particle.startLifetime : 0f;
                var shown = intended * growth.Evaluate(life);
                var fits = room / PuffRadius;
                particle.startSize = shown > fits && shown > 0f ? intended * fits / shown : intended;
                live[i] = particle;
            }
            smokeSizes.Forget();
            smoke.SetParticles(live, count);
        }

        // A melted plug's steam, for as long as the boiler has pressure to blow it.
        private void BlowSteam(float dt)
        {
            if (boilerWater == null && !BoilerWaterRig.TryGet(firebox.Car, out boilerWater))
                return;
            var vent = boilerWater.PlugVent;
            if (!(vent > 0f))
            {
                steamDue = 0f;
                return;
            }
            steamDue = Mathf.Min(steamDue + dt * SteamPerKg * vent, MaxPerFrame);
            for (; steamDue >= 1f; steamDue -= 1f)
                EmitSmoke(GrateZone.Center, 0f, SmokeKind.Steam);
        }

        private void EmitSmoke(GrateZone zone, float draft, SmokeKind kind)
        {
            // Over the zone; a shovelful's gas rolls out over its neighbors too.
            var reach = kind == SmokeKind.SmokeOut ? 0.5f : -0.1f;
            var across = Mathf.Clamp01((GrateLayout.Column(zone) + Random.Range(-reach, 1f + reach)) / GrateLayout.Columns);
            var along = Mathf.Clamp01((GrateLayout.Row(zone) + Random.Range(-reach, 1f + reach)) / GrateLayout.Rows);
            var from = smoke.transform.InverseTransformPoint(bed.BedPoint(across, along, bed.SurfaceHeight(across, along))) + Vector3.up * 0.06f;
            // Lit by the whole fire's glow: dim over a dull fire, warm and bright
            // over a hot one. Thick smoke hides the glow under it and stays dark.
            var glow = Mathf.Clamp01(glowLevel * 1.6f);
            Color color;
            Vector3 velocity;
            float lifetime, size;
            switch (kind)
            {
                case SmokeKind.SmokeOut:
                    color = Lit(SmokeOutColor, Mathf.Lerp(0.3f, 0.6f, glow));
                    // Anywhere from the coal up to the crown, more of it low, and
                    // on forward to the tube plate, thickest over the zone: the
                    // gas rolls forward and fills the box as the draft takes it.
                    from.y = Mathf.Lerp(from.y, smokeBox.max.y - 0.1f, Mathf.Pow(Random.value, 1.3f));
                    from.z += ((front > 0f ? smokeBox.max.z : smokeBox.min.z) - from.z) * Random.value * Random.value;
                    velocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.1f, 0.2f), Random.Range(-0.3f, 0.3f));
                    lifetime = Random.Range(4f, 7f) * Mathf.Lerp(1f, 0.7f, draft);
                    size = Random.Range(1f, 1.8f);
                    break;
                case SmokeKind.Steam:
                    color = Lit(SteamColor, Mathf.Lerp(SteamUnlit, 1f, glow));
                    // From the plug, at the crown sheet a third of the way to the tube plate.
                    var tubePlate = front > 0f ? smokeBox.max.z : smokeBox.min.z;
                    from = new Vector3(smokeBox.center.x + Random.Range(-0.05f, 0.05f), smokeBox.max.y - 0.1f,
                        Mathf.Lerp(smokeBox.center.z, tubePlate, 0.3f) + Random.Range(-0.05f, 0.05f));
                    velocity = new Vector3(Random.Range(-1.5f, 1.5f), -Random.Range(2f, 4f), Random.Range(-1.5f, 1.5f));
                    lifetime = Random.Range(3f, 5f);
                    size = Random.Range(0.8f, 1.6f);
                    break;
                case SmokeKind.Dust:
                    color = Lit(DustColor, Mathf.Lerp(0.35f, 0.8f, glow));
                    // Thrown out across the bed from where the coal lands.
                    var outward = Random.insideUnitCircle.normalized * Random.Range(0.4f, 1f);
                    velocity = new Vector3(outward.x, Random.Range(0.1f, 0.3f), outward.y);
                    lifetime = Random.Range(1.5f, 2.5f);
                    size = Random.Range(0.4f, 0.7f);
                    break;
                default:
                    color = Lit(SmokeColor, Mathf.Lerp(SmokeUnlit, 1.1f, glow));
                    velocity = new Vector3(Random.Range(-0.08f, 0.08f), Random.Range(0.12f, 0.3f), front * Mathf.Lerp(FlowForwardSlow, FlowForwardFast, draft));
                    lifetime = Random.Range(2f, 3.6f) * Mathf.Lerp(1f, 0.6f, draft);
                    size = Random.Range(0.6f, 1.1f);
                    break;
            }
            smoke.Emit(new ParticleSystem.EmitParams
            {
                position = from,
                velocity = velocity,
                startLifetime = lifetime,
                startSize = size,
                startColor = color,
                rotation = Random.Range(0f, 360f),
                applyShapeToPosition = false,
            }, 1);
        }

        private static Color Lit(Color color, float light) =>
            new Color(color.r * light, color.g * light, color.b * light, color.a * Random.Range(0.85f, 1f));

        // The firebox light takes the fire's color: dull red, red-orange, orange; wavers, and jumps with each beat.
        private void Light(float meanHeat, float hottest)
        {
            if (fire == null || fire.fireLight == null)
                return;
            if (meanHeat <= 0f)
            {
                fire.fireLight.color = lightColor;
                return;
            }
            var heat = Mathf.Clamp01(0.5f * (meanHeat + hottest));
            var color = heat < 0.5f
                ? Color.Lerp(new Color(1f, 0.3f, 0.08f), new Color(1f, 0.45f, 0.15f), heat * 2f)
                : Color.Lerp(new Color(1f, 0.45f, 0.15f), new Color(1f, 0.62f, 0.3f), heat * 2f - 1f);
            fire.fireLight.color = color;
            fire.fireLight.intensity *= (0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 6f, 0.37f)) * (1f + 0.5f * Pulse);
        }

        // The game's sparks, kept off: gathered again as their object's
        // children change (a Custom Car Loader engine makes its copy of the
        // game's sparks under it once it starts), and stopped whenever the
        // game's fire plays them.
        private void KeepSparksOff()
        {
            var sparks = fire != null ? fire.sparksObj : null;
            if (sparks == null)
                return;
            if (sparks.transform.childCount != stockSparkChildren)
            {
                stockSparkChildren = sparks.transform.childCount;
                stockSparks = sparks.GetComponentsInChildren<ParticleSystem>(true);
                foreach (var renderer in sparks.GetComponentsInChildren<Renderer>(true))
                    renderer.enabled = false;
            }
            foreach (var system in stockSparks)
            {
                if (system != null && (system.isPlaying || system.particleCount > 0))
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnDestroy()
        {
            foreach (var renderer in stockFlames)
            {
                if (renderer != null)
                    renderer.enabled = true;
            }

            if (fire != null && fire.fireLight != null)
                fire.fireLight.color = lightColor;
            if (cards != null)
                Destroy(cards);
        }
    }
}
