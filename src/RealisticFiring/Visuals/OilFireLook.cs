using System.Linq;
using DV.Simulation.Cars;
using RealisticFiring.Diagnostics;
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
            if (look.sparks != null)
                look.sparksRate = look.sparks.emission.rateOverTimeMultiplier;
            look.seed = Random.value * 100f;
            look.roar = look.BuildRoar(car);
            var sheet = look.BuildSheet(car);
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
            if (peepHole == null || source == null || source.sharedMaterial == null)
                return null;
            var frame = car.transform;
            var peep = frame.InverseTransformPoint(peepHole.transform.position);
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
            var start = PerfMeter.Start();
            Step();
            PerfMeter.Stop(PerfSection.CoalBed, start);
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
            var color = Color.Lerp(Starved, Clean, clean);
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
                fire.bounceLight.color = color;

            // The glow over the firepan, which the game whitens by the fire's rate.
            var percent = controller != null ? controller.CombustionRateNormalized : burning;
            var glow = color * (percent * 2f * brightness);
            foreach (var renderer in fire.emissiveRenderersAffectedByFire ?? new Renderer[0])
            {
                if (renderer != null)
                    renderer.material.SetColor(EmissionColorId, glow);
            }

            if (sheet != null)
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
