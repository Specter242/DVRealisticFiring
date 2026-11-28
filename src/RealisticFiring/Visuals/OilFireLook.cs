using System.Linq;
using DV.Simulation.Cars;
#if !PUBLIC_RELEASE
using RealisticFiring.Diagnostics;
#endif
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class OilFireLook : MonoBehaviour
    {
        private const string RoarClip = "FireDraft_01";

        // Atomizing below the first shows starved; at the second, clean.
        private const float StarvedBelow = 0.4f;
        private const float CleanFrom = 1f;

        // Drumming throbs this many times a second, at most this deep.
        private const float DrumRate = 5f;
        private const float DrumDepth = 0.5f;

        private const float SheetFromPeepHole = 0.95f;
        private const float SheetAbovePeepHole = 0.35f;
        private const float SheetWidth = 1f;
        private const float SheetRateLight = 40f;
        private const float SheetRateHard = 140f;

        private static readonly Color Starved = new Color(1f, 0.32f, 0.06f);
        private static readonly Color Clean = new Color(1f, 0.84f, 0.58f);

        private static AudioClip roarClip;

        private const float PanRiseLight = 0.9f;
        private const float PanRiseHard = 2.6f;
        private const float PanRateLight = 130f;
        private const float PanRateHard = 420f;
        // Born from this far before the flash wall to the front wall, m; carried back toward the flash wall.
        private const float PanFromFlashWall = 0.3f;
        private const float PanBack = 1.1f;
        private static readonly Color PanClean = new Color(1f, 0.93f, 0.8f);
        private static readonly Color PanStarved = new Color(1f, 0.5f, 0.2f);
        // The box's steel shell in the cab's material glowed evenly pale and drew grey; sooted and glowing low red.
        private const float WallTone = 0.3f;
        private static readonly Color WallGlow = new Color(1f, 0.36f, 0.08f);
        private const float WallGlowStrength = 0.25f;
        // The fire's light in the S282's box, clean: orange, as the footage's brick and walls take it (its
        // yellowish Clean drew the sooted walls mustard, job 3369).
        private static readonly Color PanLight = new Color(1f, 0.62f, 0.32f);
        private static Texture2D tongues;
        private bool panFlame;
        private bool darkWalls;

        private RealisticOilFirebox firebox;
        private Fire fire;
        private FireboxSimController controller;
        private ParticleSystem flames;
        private ParticleSystem sheet;
        private ParticleSystem sparks;
        private float sparksRate;
        private AudioSource roar;
        private float seed;

        public static string Attach(Fire fire, TrainCar car, RealisticOilFirebox firebox)
        {
            if (fire == null || car == null || firebox == null || fire.GetComponent<OilFireLook>() != null)
                return null;
            var look = fire.gameObject.AddComponent<OilFireLook>();
            look.firebox = firebox;
            look.fire = fire;
            look.controller = car.SimController != null ? car.SimController.firebox : null;
            look.flames = fire.fireObj != null ? fire.fireObj.GetComponentInChildren<ParticleSystem>(true) : null;
            look.sparks = fire.sparksObj != null ? fire.sparksObj.GetComponentInChildren<ParticleSystem>(true) : null;
            if (look.sparks != null && S282OilConversion.IsOil(car))
            {
                look.sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var emission = look.sparks.emission;
                emission.enabled = false;
                look.sparks = null;
            }
            if (look.sparks != null)
                look.sparksRate = look.sparks.emission.rateOverTimeMultiplier;
            var peep = S282OilCab.PeepholeWorld(car);
            if (peep != null && fire.fillLight != null)
            {
                var toDoor = Vector3.Distance(fire.fillLight.transform.position, peep.Value) - 0.03f;
                if (toDoor > 0.1f && fire.fillLight.range > toDoor)
                    fire.fillLight.range = toDoor;
            }
            look.seed = Random.value * 100f;
            look.roar = look.BuildRoar(car);
            if (S282OilConversion.IsOil(car))
            {
                look.darkWalls = true;
                foreach (var renderer in fire.emissiveRenderersAffectedByFire ?? new Renderer[0])
                    if (renderer != null && renderer.material.HasProperty(ColorId))
                        renderer.material.SetColor(ColorId, renderer.material.GetColor(ColorId) * WallTone);
            }
            var sheet = look.BuildPanFlame(car) ?? look.BuildSheet(car);
            return $"oil fire look: {sheet ?? "no flame sheet (no peep hole or flame material); the cab's flames"}" +
                   $", sparks {(look.sparks != null ? "found" : "none")}, " +
                   $"glow {fire.emissiveRenderersAffectedByFire?.Length ?? 0}, light {(fire.fireLight != null ? "found" : "none")}, " +
                   $"roar {(look.roar != null ? $"'{RoarClip}'" : "not found")}";
        }

        // The flame sheet before the peep hole, drawn with the cab's own flame
        // material. Returns a note for the log, or null without one.
        private string BuildSheet(TrainCar car)
        {
            var interior = car.loadedInterior != null ? car.loadedInterior.transform : null;
            var peepHole = interior != null
                ? interior.GetComponentsInChildren<DV.Simulation.Ports.InteractablePortFeeder>(true).FirstOrDefault(f => f.portId == OilBurnerFit.PeepHolePortId)
                : null;
            var source = flames != null ? flames.GetComponent<ParticleSystemRenderer>() : null;
            // An S282 converted to oil: its peephole is in the blanked door drawn with its cab (S282OilCab).
            var drawnPeep = peepHole == null ? S282OilCab.PeepholeWorld(car) : null;
            if ((peepHole == null && drawnPeep == null) || source == null || source.sharedMaterial == null)
                return null;
            var frame = car.transform;
            var peep = frame.InverseTransformPoint(peepHole != null ? peepHole.transform.position : drawnPeep.Value);
            var pan = frame.InverseTransformPoint(flames.transform.position).y;
            var height = Mathf.Max(0.6f, peep.y + SheetAbovePeepHole - pan);
            var center = new Vector3(peep.x, pan + height / 2f, peep.z + SheetFromPeepHole);

            var holder = new GameObject("[RealisticFiring oil flame sheet]");
            holder.transform.SetParent(transform, false);
            holder.transform.SetPositionAndRotation(frame.TransformPoint(center), frame.rotation);
            var ps = holder.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 600;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            // Born over the lower half of the sheet, streaming up through the rest.
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(SheetWidth, height * 0.5f, 0.12f);
            shape.position = new Vector3(0f, -height * 0.25f, 0f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 2f;
            noise.scrollSpeed = 3f;
            noise.damping = true;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.3f)));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            // The flame material is a sheet of frames the cab's flames play one at
            // a time; without that each piece showed the whole grid of them.
            var frames = flames.textureSheetAnimation;
            if (frames.enabled)
            {
                var play = ps.textureSheetAnimation;
                play.enabled = true;
                play.mode = frames.mode;
                play.numTilesX = frames.numTilesX;
                play.numTilesY = frames.numTilesY;
                play.animation = frames.animation;
                play.rowMode = frames.rowMode;
                play.rowIndex = frames.rowIndex;
                play.frameOverTime = frames.frameOverTime;
                play.frameOverTimeMultiplier = frames.frameOverTimeMultiplier;
                play.cycleCount = frames.cycleCount;
                // Each piece starts on its own frame, so the sheet does not pulse in step.
                play.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.99f);
            }
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = source.sharedMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            ps.Play();
            sheet = ps;
            return $"a flame sheet {SheetWidth:F1} m wide and {height:F2} m high, {SheetFromPeepHole:F2} m in from the peep hole, in place of the cab's flames" +
                   (frames.enabled ? $" (playing its {frames.numTilesX}×{frames.numTilesY} frames)" : " (no frames to play)");
        }

        // The S282's flame laid over its firepan (S282OilTenderData.PanShape), drawn with the cab's flame
        // material's shader and the tongues below. Returns a note for the log, or null where it has no firepan.
        private string BuildPanFlame(TrainCar car)
        {
            var pan = S282OilConversion.IsOil(car) ? S282OilTender.Data?.Firebox?.Pan : null;
            var source = flames != null ? flames.GetComponent<ParticleSystemRenderer>() : null;
            if (pan == null || source == null || source.sharedMaterial == null || car.loadedInterior == null || pan.Front <= pan.Back + PanFromFlashWall)
                return null;
            var cab = CabFrame.Of(car);
            var start = pan.Back + PanFromFlashWall;
            var yAt = new System.Func<float, float>(z => Mathf.Lerp(pan.BackY, pan.FrontY, Mathf.InverseLerp(pan.Back, pan.Front, z)));
            var middle = new Vector3(0f, (yAt(start) + pan.FrontY) / 2f, (start + pan.Front) / 2f);
            var tilt = Mathf.Atan2(pan.BackY - pan.FrontY, pan.Front - pan.Back) * Mathf.Rad2Deg;

            var holder = new GameObject("[RealisticFiring oil flame over the firepan]");
            holder.transform.SetParent(transform, false);
            holder.transform.SetPositionAndRotation(cab.TransformPoint(middle), cab.rotation * Quaternion.Euler(tilt, 0f, 0f));
            var ps = holder.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 1200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(2f * pan.HalfWidth * 0.85f, 0.04f, pan.Front - start);
            // Back toward the flash wall, slowing as they turn up; rising faster as they go.
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            AnimationCurve Curve(params (float T, float V)[] keys) => new AnimationCurve(keys.Select(k => new Keyframe(k.T, k.V)).ToArray());
            velocity.x = new ParticleSystem.MinMaxCurve(0.25f, Curve((0f, -1f), (1f, -1f)), Curve((0f, 1f), (1f, 1f)));
            velocity.y = new ParticleSystem.MinMaxCurve(PanRiseLight, Curve((0f, 0.25f), (0.4f, 0.7f), (1f, 1f)), Curve((0f, 0.45f), (0.4f, 0.95f), (1f, 1.3f)));
            velocity.z = new ParticleSystem.MinMaxCurve(PanBack, Curve((0f, -1.2f), (0.6f, -0.3f), (1f, 0f)), Curve((0f, -0.7f), (0.6f, -0.1f), (1f, 0f)));
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.6f;
            noise.scrollSpeed = 2.5f;
            noise.octaveCount = 2;
            noise.damping = true;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, Curve((0f, 0.5f), (0.3f, 1f), (1f, 1.35f)));
            var fade = ps.colorOverLifetime;
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.96f, 0.84f), 0f), new GradientColorKey(new Color(1f, 0.86f, 0.55f), 0.2f),
                        new GradientColorKey(new Color(1f, 0.58f, 0.18f), 0.55f), new GradientColorKey(new Color(0.85f, 0.28f, 0.04f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0.35f, 0.8f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;
            var frames = ps.textureSheetAnimation;
            frames.enabled = true;
            frames.mode = ParticleSystemAnimationMode.Grid;
            frames.numTilesX = TongueTiles;
            frames.numTilesY = TongueTiles;
            frames.animation = ParticleSystemAnimationType.WholeSheet;
            frames.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
            frames.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.999f);
            frames.cycleCount = 1;
            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            var material = new Material(source.sharedMaterial) { name = source.sharedMaterial.name + " (oil tongues)" };
            if (material.HasProperty(MainTexId))
            {
                material.SetTexture(MainTexId, Tongues());
                material.SetTextureScale(MainTexId, Vector2.one);
                material.SetTextureOffset(MainTexId, Vector2.zero);
            }
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingLayerID = source.sortingLayerID;
            renderer.sortingOrder = source.sortingOrder;
            ps.Play();
            sheet = ps;
            panFlame = true;
            return $"a flame over the firepan, {2f * pan.HalfWidth:F2} by {pan.Front - start:F2} m, {tilt:F1} degrees down to the front, in broad tongues ({material.shader.name})";
        }

        private const int TongueTiles = 4;
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Sixteen tongues of flame, each wide and soft at its root and ragged toward its tip, in white (the
        // particles' colours tint them): broad lobes, where the cab's flame frames are thin wisps.
        private static Texture2D Tongues()
        {
            if (tongues != null)
                return tongues;
            const int tile = 64;
            const int n = tile * TongueTiles;
            var pixels = new Color32[n * n];
            for (var ty = 0; ty < TongueTiles; ty++)
            for (var tx = 0; tx < TongueTiles; tx++)
            {
                var k = ty * TongueTiles + tx;
                var lean = (k % 5 - 2) * 0.03f;
                for (var y = 0; y < tile; y++)
                for (var x = 0; x < tile; x++)
                {
                    var u = (x + 0.5f) / tile;
                    var v = (y + 0.5f) / tile;
                    var wobble = 0.12f * (Mathf.PerlinNoise(k * 3.1f + v * 2.5f, k * 1.7f) - 0.5f) * v + lean * v;
                    var width = 0.42f * Mathf.Pow(Mathf.Max(0f, 1f - v), 1.1f) * (0.8f + 0.4f * Mathf.PerlinNoise(k * 2.3f + v * 4f, 7.1f));
                    var across = Mathf.Abs(u - 0.5f - wobble);
                    var body = 1f - Edge(width * 0.15f, width, across);
                    var root = Edge(0f, 0.18f, v);
                    var ragged = Mathf.PerlinNoise(k * 5.3f + u * 7f, k * 2.9f + v * 5f);
                    var tip = 1f - Edge(0.3f, 0.85f, v + 0.4f * (ragged - 0.5f));
                    var inner = 0.5f + 0.5f * Mathf.PerlinNoise(k * 7.7f + u * 9f, k * 4.1f + v * 7f);
                    // White, its shape in its alpha only, with a strong core: the additive shader draws a piece by
                    // colour and alpha together, and the shape in both drew it at its square, all but clear (job 3362).
                    var a = Mathf.Clamp01(1.8f * body * root * tip * inner * (0.8f + 0.5f * (1f - v)));
                    pixels[(ty * tile + y) * n + tx * tile + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a));
                }
            }
            tongues = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "RealisticFiring oil flame tongues", wrapMode = TextureWrapMode.Clamp };
            tongues.SetPixels32(pixels);
            tongues.Apply(true, true);
            return tongues;
        }

        // 0 below e0, 1 above e1, smooth between (a shader's smoothstep; Unity's Mathf.SmoothStep interpolates
        // between its first two arguments instead, and drew the tongues all but clear).
        private static float Edge(float e0, float e1, float x)
        {
            var t = Mathf.Clamp01((x - e0) / (e1 - e0));
            return t * t * (3f - 2f * t);
        }

        private AudioSource BuildRoar(TrainCar car)
        {
            if (roarClip == null)
                roarClip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == RoarClip);
            if (roarClip == null)
                return null;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = roarClip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0.8f;
            source.minDistance = 6f;
            source.maxDistance = 30f;
            source.volume = 0f;
            var interior = car.loadedInterior != null ? car.loadedInterior : null;
            source.outputAudioMixerGroup = interior != null
                ? interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            return source;
        }

        private void LateUpdate()
        {
#if !PUBLIC_RELEASE
            var start = PerfMeter.Start();
#endif
            Step();
#if !PUBLIC_RELEASE
            PerfMeter.Stop(PerfSection.CoalBed, start);
#endif
        }

        private void Step()
        {
            var model = firebox.Model;
            var t = Time.time;
            var burning = model.MaxFeed > 0f ? Mathf.Clamp01(model.BurnRate / model.MaxFeed) : 0f;
            var drum = model.FireOn ? Mathf.Clamp01(model.Drumming) : 0f;
            var throb = drum > 0f ? Mathf.Sin(2f * Mathf.PI * DrumRate * t) : 0f;
            Sound(model.FireOn, burning, drum, throb);
            // The cab's own flames give way to the sheet (the game sets their rate each frame).
            if (sheet != null && flames != null)
            {
                var stock = flames.emission;
                stock.rateOverTimeMultiplier = 0f;
            }
            if (!model.FireOn)
            {
                if (sheet != null)
                {
                    var off = sheet.emission;
                    off.rateOverTimeMultiplier = 0f;
                }
                return;
            }

            var clean = Mathf.InverseLerp(StarvedBelow, CleanFrom, model.Atomizing);
            var color = Color.Lerp(Starved, darkWalls ? PanLight : Clean, clean);
            // A starved fire flickers slowly and unevenly; a clean one burns steady.
            var flicker = Mathf.Lerp(0.6f + 0.4f * Mathf.PerlinNoise(t * 2.3f, seed), 0.93f + 0.07f * Mathf.PerlinNoise(t * 6f, seed), clean);
            var brightness = Mathf.Lerp(0.55f, 1.15f, clean) * flicker * (1f + DrumDepth * drum * throb);

            if (fire.fireLight != null && fire.fireLight.enabled)
            {
                fire.fireLight.color = color;
                fire.fireLight.intensity *= brightness;
            }
            if (fire.fillLight != null && fire.fillLight.enabled)
            {
                fire.fillLight.color = color;
                fire.fillLight.intensity *= brightness;
            }
            if (fire.bounceLight != null)
            {
                fire.bounceLight.color = color;
                if (darkWalls && fire.bounceLight.enabled)
                    fire.bounceLight.intensity *= Mathf.Clamp01(firebox.DoorwayOpening);
            }

            // The glow over the firepan, which the game whitens by the fire's rate.
            var percent = controller != null ? controller.CombustionRateNormalized : burning;
            var glow = darkWalls ? WallGlow * (percent * WallGlowStrength * brightness) : color * (percent * 2f * brightness);
            foreach (var renderer in fire.emissiveRenderersAffectedByFire ?? new Renderer[0])
            {
                if (renderer != null)
                    renderer.material.SetColor(EmissionColorId, glow);
            }

            if (sheet != null && panFlame)
            {
                // Low on the floor when light, filling the box when hard; yellow-white when clean, orange and thin
                // when starved; surging when it drums.
                // By the square root of the burning: a light or standing fire's tongues still stand up off the
                // floor (standing on the blower the S282 burns about an eighth of its most, and its flame lay
                // flat, job 3369).
                var shown = Mathf.Sqrt(burning);
                var main = sheet.main;
                var tint = Color.Lerp(PanStarved, PanClean, clean);
                main.startColor = new Color(tint.r, tint.g, tint.b, Mathf.Lerp(0.65f, 1f, clean) * Mathf.Clamp01(flicker));
                main.simulationSpeed = Mathf.Lerp(0.8f, 1.2f, clean) * (1f + 0.3f * drum * throb);
                var velocity = sheet.velocityOverLifetime;
                velocity.yMultiplier = Mathf.Lerp(PanRiseLight, PanRiseHard, shown) * (1f + 0.3f * drum * throb);
                var rate = sheet.emission;
                rate.rateOverTimeMultiplier = Mathf.Lerp(PanRateLight, PanRateHard, shown) * Mathf.Lerp(0.6f, 1f, clean) * (1f + 0.4f * drum * throb);
            }
            else if (sheet != null)
            {
                // Dense, fast, and bright when clean; thin, slower, and orange when starved; surging when it drums.
                var main = sheet.main;
                var tint = color * Mathf.Lerp(0.6f, 1.1f, clean);
                main.startColor = new Color(Mathf.Clamp01(tint.r), Mathf.Clamp01(tint.g), Mathf.Clamp01(tint.b), Mathf.Lerp(0.55f, 0.95f, clean));
                main.simulationSpeed = Mathf.Lerp(0.9f, 1.4f, burning) * Mathf.Lerp(0.7f, 1f, clean) * (1f + 0.3f * drum * throb);
                var rate = sheet.emission;
                rate.rateOverTimeMultiplier = Mathf.Lerp(SheetRateLight, SheetRateHard, burning) * Mathf.Lerp(0.5f, 1f, clean) * (1f + 0.4f * drum * throb);
            }
            else if (flames != null)
            {
                var main = flames.main;
                main.startColor = new Color(color.r, color.g, color.b, Mathf.Clamp01(0.6f + 0.4f * clean));
            }
            if (sparks != null)
            {
                // Starved, burning oil spits from the burner.
                var emission = sparks.emission;
                emission.rateOverTimeMultiplier = sparksRate * (0.3f + 3f * (1f - clean));
            }
        }

        private void Sound(bool on, float burning, float drum, float throb)
        {
            if (roar == null)
                return;
            var scale = Main.Settings.OilBurnerVolume;
            var volume = on ? scale * (0.8f * Mathf.Sqrt(burning) + 0.9f * drum * (0.65f + 0.35f * throb)) : 0f;
            roar.volume = Mathf.Clamp01(volume);
            roar.pitch = 0.85f + 0.25f * burning - 0.3f * drum;
            if (volume > 0.001f && !roar.isPlaying)
                roar.Play();
            else if (volume <= 0.001f && roar.isPlaying)
                roar.Stop();
        }

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    }
}
