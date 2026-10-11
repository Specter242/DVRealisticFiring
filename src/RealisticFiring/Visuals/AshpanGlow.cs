using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The glow of the fire in the ashpan, under the engine: "When the glow of
    /// the fire can be seen in the ash pan—this indicates the absence of dead
    /// ashes on the grates, and that they do not need shaking" (Baker p.62).
    /// A soft point light under the grate, as bright as the fire is hot and
    /// the grates are clear of ash and clinker; live coal shaken or dropped
    /// into the pan flares it, dying over several seconds. It hangs from the
    /// car itself, not the cab, so it shows from the ground, placed from the
    /// grate measured when a cab of the livery first loaded.
    /// </summary>
    internal sealed class AshpanGlow : MonoBehaviour
    {
        private const string LightName = "[RealisticFiring ashpan glow]";

        // The ashpan's glow sits this far under the grate, in m, and reaches this far.
        private const float BelowGrate = 0.3f;
        private const float Range = 2.2f;

        // Light intensity of a hot fire over clean grates, and how much each
        // kg of live coal in the pan adds, dying over this time, in s.
        private const float SteadyIntensity = 0.8f;
        private const float FlarePerKg = 0.6f;
        private const float MaxFlare = 2.5f;
        private const float FlareDying = 8f;

        private static readonly Color Glow = new Color(1f, 0.42f, 0.12f);

        private RealisticFirebox firebox;
        private Light glow;
        private float flare;
        private float droppedSeen = float.NaN;
        private float dumpedSeen = float.NaN;
        private float seed;

        public static void Attach(TrainCar car, RealisticFirebox firebox)
        {
            if (car == null || firebox == null || car.GetComponent<AshpanGlow>() != null)
                return;
            var glow = car.gameObject.AddComponent<AshpanGlow>();
            glow.firebox = firebox;
            glow.seed = Random.value * 100f;
        }

        private void Update()
        {
            var start = PerfMeter.Start();
            Step();
            PerfMeter.Stop(PerfSection.CoalBed, start);
        }

        private void Step()
        {
            var model = firebox.Model;
            var dropped = model.CoalDroppedThroughGrate;
            var dumped = model.CoalDumped;
            var fallen = float.IsNaN(droppedSeen) ? 0f : Mathf.Max(0f, dropped - droppedSeen) + Mathf.Max(0f, dumped - dumpedSeen);
            droppedSeen = dropped;
            dumpedSeen = dumped;
            flare = Mathf.Min(MaxFlare, flare * Mathf.Exp(-Time.deltaTime / FlareDying) + fallen * FlarePerKg);

            var brightness = Main.Settings.AshpanGlow;
            var box = firebox.GrateBox;
            if (brightness <= 0f || !box.HasValue || (!model.FireOn && flare < 0.01f))
            {
                if (glow != null)
                    glow.enabled = false;
                return;
            }

            if (glow == null)
            {
                var holder = new GameObject(LightName).transform;
                holder.SetParent(transform, false);
                glow = holder.gameObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = Glow;
                glow.range = Range;
                glow.shadows = LightShadows.None;
            }
            var grate = box.Value;
            glow.transform.localPosition = new Vector3(grate.center.x, grate.min.y - BelowGrate, grate.center.z);

            // Hot fire, clear grates: dead ash and clinker hide the glow.
            var hot = model.FireOn ? Mathf.Clamp01((model.Temperature - 400f) / 800f) : 0f;
            var choked = 0f;
            foreach (var zone in model.Zones)
                choked += Mathf.Clamp01(1.5f * (zone.Ash + zone.Clinker) / (zone.Ash + zone.Clinker + zone.Coal + 0.1f));
            choked /= model.Zones.Count;
            var flicker = 0.85f + 0.3f * Mathf.PerlinNoise(Time.time * 1.3f, seed);
            var intensity = brightness * (SteadyIntensity * hot * (1f - choked) + flare) * flicker;
            glow.intensity = intensity;
            glow.enabled = intensity > 0.01f;
        }
    }
}
