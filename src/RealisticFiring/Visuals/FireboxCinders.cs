using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class FireboxCinders : MonoBehaviour
    {
        // Cinders a second over the whole grate at a full draft, by the
        // draft's square (a drifting fire, at 0.02, throws none; a working
        // one, at 0.3, a tenth of these); and how many times that rate a
        // beat's surge reaches at its peak.
        internal static float CinderRate = 90f;
        internal static float CinderBurst = 3f;

        // Their speed up, m/s, with no draft and a full one; how long they
        // glow, s; and their size, m (the footage: 0.2 to 1 % of a door
        // about 0.4 m wide).
        internal static float CinderRiseDrifting = 0.6f;
        internal static float CinderRiseForced = 3.2f;
        internal static float CinderLife = 0.55f;
        internal static float CinderSize = 0.006f;

        private const int MostCinders = 160;

        private FireboxVolume volume;
        private FireboxFlames flames;
        private ZonedCoalBed bed;
        private ParticleSystem system;
        private ParticleSystemRenderer cinderRenderer;
        private Material material;
        private Transform space;
        private float due;
        private float room = 1f;

        /// <summary>Adds cinders over <paramref name="bed"/>. Returns a line for the log, or null.</summary>
        public static string Attach(Fire fire, ZonedCoalBed bed)
        {
            var volume = fire != null ? fire.GetComponent<FireboxVolume>() : null;
            var flames = fire != null ? fire.GetComponent<FireboxFlames>() : null;
            var source = SparkMaterial();
            if (volume == null || flames == null || flames.SmokeSpace == null || bed == null || !bed.Ready || source == null
                || fire.GetComponent<FireboxCinders>() != null)
                return null;
            var cinders = fire.gameObject.AddComponent<FireboxCinders>();
            cinders.volume = volume;
            cinders.flames = flames;
            cinders.bed = bed;
            cinders.space = flames.SmokeSpace;
            cinders.material = new Material(source) { name = source.name + " (firebox)" };
            cinders.Build();
            return "cinders rise through the firebox's flames with the draft";
        }

        // The chimney's cinder material, from its effects prefab.
        private static Material SparkMaterial()
        {
            var prefab = PlumeAssets.Prefab;
            if (prefab == null)
                return null;
            foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer.name == "Sparks" && renderer.sharedMaterial != null)
                    return renderer.sharedMaterial;
            }
            return null;
        }

        private void Build()
        {
            var holder = new GameObject("[RealisticFiring firebox cinders]");
            holder.transform.SetParent(space, false);
            holder.layer = volume.VolumeRenderer != null ? volume.VolumeRenderer.gameObject.layer : holder.layer;
            system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = MostCinders;
            main.simulationSpace = ParticleSystemSimulationSpace.Custom;
            main.customSimulationSpace = space;
            main.startLifetime = CinderLife;
            main.startSpeed = 0f;
            main.startSize = CinderSize;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;
            // Leaving the fire yellow, cooling through orange to a dull red, and out.
            var cooling = system.colorOverLifetime;
            cooling.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.5f, 0.12f), 0.45f),
                    new GradientColorKey(new Color(0.7f, 0.12f, 0.02f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            cooling.color = gradient;
            // Buffeted by the flames as they rise.
            var noise = system.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 3f;
            noise.scrollSpeed = 1.5f;
            cinderRenderer = holder.GetComponent<ParticleSystemRenderer>();
            cinderRenderer.sharedMaterial = material;
            // Streaks along their flight, short ones.
            cinderRenderer.renderMode = ParticleSystemRenderMode.Stretch;
            cinderRenderer.velocityScale = 0.012f;
            cinderRenderer.lengthScale = 1f;
            cinderRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cinderRenderer.receiveShadows = false;
            cinderRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            cinderRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            // After the fire's volume, which they fly in.
            if (volume.VolumeRenderer != null)
            {
                cinderRenderer.sortingLayerID = volume.VolumeRenderer.sortingLayerID;
                cinderRenderer.sortingOrder = volume.VolumeRenderer.sortingOrder + 1;
            }
            system.Play(true);
            // The room over the grate, m, which a cinder's flight stays within.
            room = Mathf.Max(0.2f, (bed.BedPoint(0.5f, 0.5f, 0f) - space.TransformPoint(flames.SmokeBox.center + 0.5f * flames.SmokeBox.size.y * Vector3.up)).magnitude);
        }

        private void LateUpdate()
        {
            var start = PerfMeter.Start();
            Emit();
            PerfMeter.Stop(PerfSection.FireboxVolume, start);
        }

        private void Emit()
        {
            if (system == null || volume == null || !volume.Viewed || FireboxTestBed.HidingFlames)
            {
                due = 0f;
                return;
            }
            var draft = Mathf.Clamp01(flames.Draft);
            var rate = CinderRate * draft * draft * (1f + CinderBurst * Mathf.Clamp01(flames.Pulse));
            due = Mathf.Min(due + rate * Time.deltaTime, 12f);
            if (due < 1f)
                return;
            var up = (bed.BedPoint(0.5f, 0.5f, 1f) - bed.BedPoint(0.5f, 0.5f, 0f)).normalized;
            var along = (bed.BedPoint(0.5f, 1f, 0f) - bed.BedPoint(0.5f, 0f, 0f)).normalized;
            var across = (bed.BedPoint(1f, 0.5f, 0f) - bed.BedPoint(0f, 0.5f, 0f)).normalized;
            var rise = Mathf.Lerp(CinderRiseDrifting, CinderRiseForced, Mathf.Sqrt(draft));
            for (; due >= 1f; due -= 1f)
            {
                // From a zone as hard as it burns: a few tries at a burning one.
                var x = Random.value;
                var z = Random.value;
                var strength = 0f;
                for (var tries = 0; tries < 3; tries++)
                {
                    x = Random.value;
                    z = Random.value;
                    var zone = GrateLayout.At(Mathf.Min((int)(z * GrateLayout.Rows), GrateLayout.Rows - 1), Mathf.Min((int)(x * GrateLayout.Columns), GrateLayout.Columns - 1));
                    strength = volume.ZoneStrength(zone);
                    if (Random.value < strength)
                        break;
                }
                if (strength <= 0.02f)
                    continue;
                var from = bed.BedPoint(x, z, bed.SurfaceHeight(x, z)) + up * 0.02f;
                var speed = rise * Random.Range(0.6f, 1.3f);
                // Under the brick arch a cinder is carried back toward its
                // lip with the gases, and is out before the bricks.
                var under = volume.Arch != null ? volume.Arch.Room(from) : float.PositiveInfinity;
                var carried = float.IsPositiveInfinity(under) ? 0.35f * draft : -0.5f;
                var velocity = up * speed + along * (speed * carried) + (across * Random.Range(-1f, 1f) + along * Random.Range(-1f, 1f)) * (0.25f * speed);
                // Out before the crown.
                var life = Mathf.Min(CinderLife * Random.Range(0.6f, 1.3f), 0.85f * Mathf.Min(room, under) / Mathf.Max(speed, 0.1f));
                system.Emit(new ParticleSystem.EmitParams
                {
                    position = space.InverseTransformPoint(from),
                    velocity = space.InverseTransformVector(velocity),
                    startLifetime = life,
                    startSize = CinderSize * Random.Range(0.5f, 1.6f),
                    applyShapeToPosition = false,
                }, 1);
            }
        }

        private void OnDestroy()
        {
            if (system != null)
                Destroy(system.gameObject);
            if (material != null)
                Destroy(material);
        }
    }
}
