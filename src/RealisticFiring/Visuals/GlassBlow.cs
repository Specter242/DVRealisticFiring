using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Ports;
using HarmonyLib;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class GlassBlow : MonoBehaviour
    {
        private static readonly int IndicatorValue = Shader.PropertyToID("_IndicatorValue");
        private static readonly Dictionary<IndicatorShaderValue, GlassBlow> ByGlass = new Dictionary<IndicatorShaderValue, GlassBlow>();

        // The bottom cock wide open drains the glass this many times over (so
        // cracked a third it draws it right down); the top cock, letting the
        // steam side out, this share as hard.
        private const float DrawDown = 3f;
        private const float TopShare = 0.5f;

        // Surface boil amplitude while blowing through or rebounding. Bubbles
        // drawn in the water never showed: the gauge's cover glass hides
        // whatever is behind it (jobs 2346 to 2350), so the surface boils instead.
        // Fall and rebound time constants come from Core.GaugeGlassChoke so
        // clear glasses snap back briskly and choked glasses creep sluggishly.
        private const float Jiggle = 0.06f;

        private const string HissClip = "SteamRelease_01";
        private const float Loudest = 0.25f;

        private TryCockRig rig;
        private IndicatorShaderValue glass;
        private MaterialPropertyBlock block;
        private float shown = float.NaN;
        private float agitation;
        private bool overriding;
        private float seed;
        private float height;
        private Transform[] cocks;
        private AudioSource[] hisses;
        private static readonly float FullFlow = Core.TryCock.WaterFlow(14f);

        /// <summary>Sets up the glass of a newly loaded cab to be blown through by its cocks. Returns a line for the log.</summary>
        internal static string Attach(TrainCar car, Transform interior, TryCockRig rig, Transform root, Transform[] cocks)
        {
            foreach (var gone in ByGlass.Keys.Where(g => g == null).ToList())
                ByGlass.Remove(gone);
            var glass = interior.GetComponentsInChildren<IndicatorPortReader>(true)
                .Where(r => r.portId == rig.WaterLevelPortId)
                .Select(r => r.GetComponent<IndicatorShaderValue>())
                .FirstOrDefault(g => g != null && g.renderer != null);
            if (glass == null)
                return "no shader glass to blow through";
            var holder = new GameObject("glass blow").transform;
            holder.SetParent(root, false);
            var blow = holder.gameObject.AddComponent<GlassBlow>();
            blow.Init(car, rig, glass, cocks);
            ByGlass[glass] = blow;
            return $"the glass '{glass.name}' ({blow.height * 100f:F0} cm) blown through by its cocks";
        }

        private void Init(TrainCar car, TryCockRig owner, IndicatorShaderValue indicator, Transform[] glassCocks)
        {
            rig = owner;
            glass = indicator;
            cocks = glassCocks;
            seed = Random.value * 100f;
            block = new MaterialPropertyBlock();

            // The glass's drawn water's height, for the log.
            var renderer = glass.renderer;
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            var b = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one * 0.1f);
            var frame = car.transform;
            var lo = Vector3.positiveInfinity;
            var hi = Vector3.negativeInfinity;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var p = frame.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                lo = Vector3.Min(lo, p);
                hi = Vector3.Max(hi, p);
            }
            height = Mathf.Max(0.01f, hi.y - lo.y);
            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(a => a.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            var clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == HissClip);
            hisses = cocks.Select(cock => cock != null && clip != null ? Sound(cock, clip, mixer) : null).ToArray();
        }

        private static AudioSource Sound(Transform at, AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer)
        {
            var source = at.gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 15f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        /// <summary>
        /// Where the cab's glass shows the water now, 0 empty to 1 full,
        /// while it is blown through or settling; NaN when it shows the
        /// boiler's level as the game does.
        /// </summary>
        internal float Shown => overriding ? Mathf.Clamp01(shown + Jitter()) : float.NaN;

        // Two beats, a slow surge and a quick boil, so it jumps unevenly.
        private float Jitter() => agitation * Jiggle * 2f *
            (0.6f * (Mathf.PerlinNoise(Time.time * 6f, seed) - 0.5f) + 0.4f * (Mathf.PerlinNoise(Time.time * 15f, seed + 7f) - 0.5f));

        private void LateUpdate()
        {
            if (rig == null || glass == null)
                return;
            var dt = Time.deltaTime;
            var level = glass.NormalizeValue(glass.Value, false);
            var bottom = rig.Opening(TryCockRig.GlassBottom);
            var top = rig.Opening(TryCockRig.GlassTop);
            // Shut and settled, the glass is the game's: it follows the standing
            // column level (which already carries choking lag/sticking).
            if (!overriding && bottom <= 0f && top <= 0f)
            {
                shown = level;
                agitation = 0f;
                UpdateSound();
                return;
            }
            var glassChoke = rig.Water?.GlassChoke;
            var choke = glassChoke != null ? glassChoke.Choke : 0f;
            var fallTime = glassChoke != null ? glassChoke.FallTimeNow : Core.GaugeGlassChoke.ClearFallTime;
            var riseTime = glassChoke != null ? glassChoke.ReboundTimeNow : Core.GaugeGlassChoke.ClearReboundTime;
            var target = level;
            var drawn = Mathf.Clamp01((bottom + TopShare * top) * DrawDown);
            if (drawn > 0f)
                target = Mathf.Min(target, Mathf.Lerp(Mathf.Min(level, 1.05f), -0.05f, drawn));
            if (float.IsNaN(shown))
                shown = level;
            var before = shown;
            shown += (target - shown) * Mathf.Min(1f, dt / (target < shown ? fallTime : riseTime));
            var moving = dt > 0f ? Mathf.Abs(shown - before) / dt : 0f;
            var inGlass = Mathf.Clamp01(shown) > 0.02f;
            var lively = 1f - 0.7f * Mathf.Clamp01(choke);
            var stirred = inGlass ? Mathf.Clamp01((3f * (bottom + top) + 2f * moving) * lively) : 0f;
            agitation += (stirred - agitation) * Mathf.Min(1f, dt / 0.3f);

            var settled = bottom <= 0f && top <= 0f && Mathf.Abs(shown - level) < 0.01f && agitation < 0.02f;
            if (settled)
            {
                shown = level;
                agitation = 0f;
                if (overriding)
                {
                    overriding = false;
                    Apply(glass.GetNormalizedValue());
                }
            }
            else
            {
                overriding = true;
                Apply(Shown);
            }

            UpdateSound();
        }

        private void UpdateSound()
        {
            var volume = Mathf.Max(0f, Main.Settings.SteamVentVolume);
            for (var i = 0; i < hisses.Length; i++)
            {
                var source = hisses[i];
                if (source == null)
                    continue;
                var flow = rig.Flow(TryCockRig.TryCocks + i);
                var share = Mathf.Clamp01((flow.Water + flow.Steam * 10f) / FullFlow);
                source.volume = volume * Loudest * Mathf.Sqrt(share);
                source.pitch = Mathf.Lerp(1.5f, 0.95f, flow.Wet);
                if (source.volume > 0.002f && !source.isPlaying)
                    source.Play();
                else if (source.volume <= 0.002f && source.isPlaying)
                    source.Stop();
            }
        }

        private void Apply(float value)
        {
            if (glass == null || glass.renderer == null)
                return;
            glass.renderer.GetPropertyBlock(block);
            block.SetFloat(IndicatorValue, value);
            glass.renderer.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (glass != null)
                ByGlass.Remove(glass);
        }

        /// <summary>The stock glass setting its water anew: blown through, it shows this instead.</summary>
        internal static void AfterValueSet(IndicatorShaderValue indicator)
        {
            if (indicator != null && ByGlass.TryGetValue(indicator, out var blow) && blow != null && blow.overriding)
                blow.Apply(blow.Shown);
        }
    }

    /// <summary>A glass being blown through keeps showing its moving water when the game sets its level anew (<see cref="GlassBlow"/>).</summary>
    [HarmonyPatch(typeof(IndicatorShaderValue), "OnValueSet")]
    internal static class GlassBlowPatch
    {
        private static void Postfix(IndicatorShaderValue __instance) => GlassBlow.AfterValueSet(__instance);
    }
}
