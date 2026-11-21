using System.Collections.Generic;
using System.Linq;
using DV;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class TryCockCab
    {
        internal const string RootName = "[RealisticFiring try cocks]";

        // The body around the stem at the backhead, by the wheel's size: its
        // radius over the wheel's width and its length over the wheel's
        // depth off the backhead. The spout from its middle, down and a
        // little out into the cab, m.
        private const float BodyRadiusShare = 0.2f;
        private const float BodyLengthShare = 0.4f;
        private const float BodyInto = 0.003f;
        private static readonly Vector3 SpoutWay = new Vector3(0f, -1f, -0.5f).normalized;
        private const float SpoutLength = 0.03f;
        private const float SpoutRadius = 0.0065f;
        private const float NozzleRadius = 0.0085f;
        private const float NozzleLength = 0.006f;

        /// <summary>Builds the try cocks in a newly loaded cab. Returns a line for the log, or null.</summary>
        public static string Attach(TrainCar car, Transform interior)
        {
            if (car == null || interior == null || interior.Find(RootName) != null || !TryCockRig.TryGet(car, out var rig))
                return null;
            if (!Globals.G.Types.TryGetLivery(StokerCab.SourceLivery, out var livery) || livery.interiorPrefab == null)
                return $"no {StokerCab.SourceLivery} cab to take the try cocks' valves from";
            var sourceValve = livery.interiorPrefab.transform.Find(StokerCab.SourceValve);
            if (sourceValve == null || sourceValve.Find(StokerCab.SourceControl) == null)
                return $"no '{StokerCab.SourceValve}/{StokerCab.SourceControl}' in the {StokerCab.SourceLivery} cab for the try cocks";

            var root = new GameObject(RootName);
            root.SetActive(false);
            root.transform.SetParent(interior, false);
            var drip = interior.Find("[particles]/SteamIndicatorWaterDripping")?.GetComponent<ParticleSystem>();
            var notes = new List<string>();
            // The glass's own cocks, top and bottom, which blow it through to its drain.
            var glassCocks = new Transform[TryCockRig.Valves.Count - TryCockRig.TryCocks];
            for (var i = 0; i < TryCockRig.Valves.Count; i++)
            {
                var name = rig.Meshes[i];
                var loose = interior.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.transform).FirstOrDefault(t => t.name == name);
                var look = loose != null ? HandwheelLook.OfLoose(loose, car.transform) : null;
                if (look == null)
                {
                    notes.Add($"no '{name}' in the cab");
                    continue;
                }
                var valve = TryCockRig.Valves[i];
                var spot = new GameObject(valve.Id + " frame").transform;
                spot.SetParent(root.transform, false);
                // On the wheel's own pivot, at the backhead, turning about the loco's length.
                spot.SetPositionAndRotation(loose.position, Quaternion.LookRotation(car.transform.forward, car.transform.up));
                var depth = Depth(loose, spot);

                var control = StokerCab.PlaceWheel(sourceValve, spot, Vector3.zero, valve, rig.PortOf(i));
                look.Dress(control, car.transform, lookSize: true);
                // The blower's grip spheres are 8 cm across: no wider than the
                // wheel here, or a click takes the cock beside it.
                foreach (var sphere in control.parent.GetComponentsInChildren<SphereCollider>(true))
                {
                    var scale = sphere.transform.lossyScale;
                    var world = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    if (world > 0f)
                        sphere.radius = look.Diameter * 0.55f / world;
                }
                loose.GetComponent<MeshRenderer>().enabled = false;
                var index = i;
                control.GetComponent<HoverName>().Source = () => Hover(rig, index);

                if (i < TryCockRig.TryCocks)
                {
                    var tip = BuildSpout(spot, look.Diameter, depth);
                    var spout = tip.gameObject.AddComponent<TryCockSpout>();
                    spout.Init(car, rig, index, drip, interior);
                    if (i == 0)
                        notes.Add(spout.Drawn);
                }
                else
                    glassCocks[i - TryCockRig.TryCocks] = spot;
                var at = car.transform.InverseTransformPoint(spot.position);
                notes.Add($"{valve.Label.ToLowerInvariant()} on '{name}' at ({at.x:F2}, {at.y:F2}, {at.z:F2}) m, level {rig.Levels[i]:F2}");
            }
            notes.Add(GlassBlow.Attach(car, interior, rig, root.transform, glassCocks));
            root.SetActive(true);
            return $"try cocks: {string.Join("; ", notes)}" + (drip == null ? "; no stock drip to copy for the spray" : "");
        }

        // How far the wheel stands off the backhead, m: its mesh's front in
        // the spot's frame (z into the backhead), from its pivot at the back.
        private static float Depth(Transform wheel, Transform spot)
        {
            var bounds = wheel.GetComponent<MeshFilter>().sharedMesh.bounds;
            var front = 0f;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? bounds.min.x : bounds.max.x, (i & 2) == 0 ? bounds.min.y : bounds.max.y, (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                front = Mathf.Min(front, spot.InverseTransformPoint(wheel.TransformPoint(corner)).z);
            }
            return Mathf.Max(0.01f, -front);
        }

        // The valve's body around its stem at the backhead and the spout
        // turned down from it; returns the spout's mouth, facing out of it.
        private static Transform BuildSpout(Transform spot, float diameter, float depth)
        {
            var radius = BodyRadiusShare * diameter;
            var length = BodyLengthShare * depth;
            StokerCab.Pipe(spot, "try cock body", new Vector3(0f, 0f, BodyInto), new Vector3(0f, 0f, -length), radius);
            var from = new Vector3(0f, 0f, -length / 2f);
            var mouth = from + SpoutWay * SpoutLength;
            StokerCab.Pipe(spot, "try cock spout", from, mouth, SpoutRadius);
            StokerCab.Pipe(spot, "try cock nozzle", mouth - SpoutWay * NozzleLength, mouth, NozzleRadius);
            var tip = new GameObject("try cock mouth").transform;
            tip.SetParent(spot, false);
            tip.localPosition = mouth;
            tip.localRotation = Quaternion.LookRotation(SpoutWay, Vector3.forward);
            return tip;
        }

        private static string Hover(TryCockRig rig, int index)
        {
            var label = TryCockRig.Valves[index].Label;
            var opening = rig.Opening(index);
            if (!(opening > 0f))
                return $"{label}: shut";
            if (index >= TryCockRig.TryCocks)
            {
                var sluggish = rig.Water != null && rig.Water.GlassChoke.IsSluggish ? " (sluggish)" : "";
                return $"{label}: open {opening * 100f:F0} %, blowing the glass through to its drain{sluggish}";
            }
            var flow = rig.Flow(index);
            string what;
            if (!flow.Flowing)
                what = "nothing";
            else if (flow.Steam <= 0f && flow.Flashed <= 0f)
                what = "water dribbling";
            else if (flow.Wet >= 0.9f)
                what = flow.Froth > 0.3f ? "frothy water" : "water";
            else if (flow.Wet <= 0.1f)
                what = "steam";
            else
                what = flow.Froth > 0.3f ? "froth and steam" : "water and steam by turns";
            return $"{label}: open {opening * 100f:F0} %, {what}";
        }
    }

    /// <summary>
    /// What a try cock lets out, at its spout: dry steam as a small hard jet,
    /// clear for a few centimetres before it whitens; water as a wet white
    /// cloud from the spout, the water flashing to steam, with spray falling
    /// from it, milky brown when frothy; and its hiss, higher for dry steam
    /// and duller for water, with the spray's spatter. At the surface it
    /// spits water and steam by turns, each as often as its share under the
    /// water, so the same on average. Copies of the game's own small steam
    /// (the whistle's) and the cab's drip, scaled to a small cock and kept
    /// in the cab's frame.
    /// </summary>
    internal sealed class TryCockSpout : MonoBehaviour
    {
        private const string HissClip = "SteamRelease_01";
        private const string SpatterClip = "WaterDump_01";
        private const float Loudest = 0.3f;
        private const float SpatterLoudest = 0.15f;

        private const float ClearCore = 0.03f;

        // Particles a second for each kg/s, and the most of each.
        private const float SteamPerKg = 2200f;
        private const float FlashPerKg = 900f;
        private const float SprayPerKg = 600f;
        private const float MaxSteam = 90f;
        private const float MaxFlash = 70f;
        private const float MaxSpray = 300f;

        private const string SteamSource = "GasWispy";

        private TrainCar car;
        private TryCockRig rig;
        private int index;
        private ParticleSystem steam;
        private ParticleSystem flash;
        private ParticleSystem spray;
        private AudioSource hiss;
        private AudioSource spatter;
        private float seed;
        private static readonly float SteamFull = TryCock.SteamFlow(14f);
        private static readonly float FlashFull = TryCock.WaterFlow(14f) * TryCock.FlashShare(195f);
        private static readonly float SprayFull = TryCock.WaterFlow(14f) * (1f - TryCock.FlashShare(195f));

        /// <summary>A line for the log: what the spout draws with.</summary>
        internal string Drawn { get; private set; }

        internal void Init(TrainCar car, TryCockRig rig, int index, ParticleSystem drip, Transform interior)
        {
            this.car = car;
            this.rig = rig;
            this.index = index;
            seed = index * 7.31f;
            // The game's own small steam, the whistle's (its renderer left
            // off where the mod draws the engine's steam itself): the mod's
            // plume shader shows nothing in the cab (job 2337).
            var steamSource = StockSteam(car);
            if (steamSource != null)
            {
                // Dry steam: fast, narrow, slowed by the air, clear at the spout.
                steam = Copy(steamSource, transform, "try cock steam", speed: (2f, 4f), size: (0.05f, 0.08f), life: (0.7f, 1.2f), angle: 7f, gravity: -0.1f, offset: ClearCore);
                Slow(steam, 4f);
                Grow(steam, 2.5f);
                var main = steam.main;
                main.startColor = new Color(1f, 1f, 1f, 0.9f);
                // Flashed water: slower, thicker, wetter, from the spout itself.
                flash = Copy(steamSource, transform, "try cock flash", speed: (1.5f, 3f), size: (0.06f, 0.1f), life: (0.8f, 1.4f), angle: 18f, gravity: -0.05f, offset: 0.005f);
                Slow(flash, 3f);
                Grow(flash, 3f);
                main = flash.main;
                main.startColor = new Color(1f, 1f, 1f, 0.95f);
            }
            if (drip != null)
                spray = Copy(drip, transform, "try cock spray", speed: (1f, 2.5f), size: (0.004f, 0.008f), life: (0.25f, 0.4f), angle: 15f, gravity: 1f, offset: 0.004f);
            Drawn = (steam != null ? $"steam from the game's '{Path(steamSource.transform)}'" : "no steam (no stock steam to copy)") +
                    (spray != null ? ", spray from the cab's drip" : ", no spray");
            var mixer = interior.GetComponentsInChildren<AudioSource>(true).Select(a => a.outputAudioMixerGroup).FirstOrDefault(g => g != null);
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            hiss = Sound(clips.FirstOrDefault(c => c.name == HissClip), mixer);
            spatter = Sound(clips.FirstOrDefault(c => c.name == SpatterClip), mixer);
        }

        // A copy of a particle system at the spout, emitting along it in the
        // cab's frame, every motion of its own but gravity taken off.
        /// <summary>The game's own small steam on <paramref name="car"/>, the whistle's where it has one; null without.</summary>
        internal static ParticleSystem StockSteam(TrainCar car)
        {
            var wisps = car.GetComponentsInChildren<ParticleSystem>(true).Where(p => p.name == SteamSource).ToList();
            return wisps.FirstOrDefault(p => p.transform.parent != null && p.transform.parent.parent != null && p.transform.parent.parent.name == "Whistle")
                   ?? wisps.FirstOrDefault();
        }

        internal static ParticleSystem Copy(ParticleSystem source, Transform parent, string name, (float Min, float Max) speed, (float Min, float Max) size,
            (float Min, float Max) life, float angle, float gravity, float offset)
        {
            var copy = Instantiate(source.gameObject, parent, false);
            copy.name = name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            foreach (var child in copy.GetComponentsInChildren<ParticleSystem>(true).Where(s => s.gameObject != copy))
                DestroyImmediate(child.gameObject);
            // The game's own helpers on its systems (one puts them back in
            // the world's space as they wake; another drives the drip from
            // the glass) would undo these settings.
            foreach (var helper in copy.GetComponents<MonoBehaviour>())
                DestroyImmediate(helper);
            var system = copy.GetComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = true;
            main.prewarm = false;
            main.duration = 1f;
            main.startDelay = 0f;
            main.simulationSpeed = 1f;
            main.useUnscaledTime = false;
            // An empty system's bounds are nothing, so one left to pause
            // offscreen never starts: always simulated (job 2340).
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.gravityModifier = gravity;
            main.startSize = new ParticleSystem.MinMaxCurve(size.Min, size.Max);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.Min, life.Max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.Min, speed.Max);
            main.maxParticles = 300;
            var velocity = system.velocityOverLifetime;
            velocity.enabled = false;
            var force = system.forceOverLifetime;
            force.enabled = false;
            var inherit = system.inheritVelocity;
            inherit.enabled = false;
            var external = system.externalForces;
            external.enabled = false;
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = false;
            var collision = system.collision;
            collision.enabled = false;
            var sub = system.subEmitters;
            sub.enabled = false;
            var trails = system.trails;
            trails.enabled = false;
            var shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.002f;
            shape.rotation = Vector3.zero;
            shape.position = new Vector3(0f, 0f, offset);
            shape.scale = Vector3.one;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            copy.layer = 0;
            // The game keeps its own steam's object off where the mod draws the engine's steam.
            copy.SetActive(true);
            var renderer = copy.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
            }
            system.Play();
            return system;
        }

        // A puff spreads as the air mixes in: this many times its size by the
        // end of its life. It shows from its start (a jet's fast parcels
        // faded in over even a twentieth of their life left the dry steam
        // clear for 15 cm, job 2343); the dry steam's clear core is its
        // start beyond the spout.
        private static void Grow(ParticleSystem system, float by)
        {
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f / by, 1f, 1f));
            var color = system.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
        }

        private string Path(Transform t)
        {
            var names = new List<string>();
            for (var at = t; at != null && at != car.transform; at = at.parent)
                names.Add(at.name);
            names.Reverse();
            return string.Join("/", names);
        }

        // The air slows a small jet within a few tenths of a second: its
        // speed held to what it left at, falling to a drift by a third of
        // its life.
        private static void Slow(ParticleSystem system, float fastest)
        {
            var limit = system.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = new ParticleSystem.MinMaxCurve(fastest, new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(0.3f, 0.15f), new Keyframe(1f, 0.05f)));
            limit.dampen = 0.15f;
        }

        private AudioSource Sound(AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer)
        {
            if (clip == null)
                return null;
            var source = gameObject.AddComponent<AudioSource>();
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

        private void LateUpdate()
        {
            if (rig == null || car == null)
                return;
            var flow = rig.Opening(index) > 0f ? rig.Flow(index) : default;
            // At the surface the cock spits: water and steam by turns.
            var wet = flow.Wet;
            var now = wet;
            if (wet > 0.02f && wet < 0.98f)
            {
                var noise = Mathf.PerlinNoise(Time.time * 7f, seed);
                now = Mathf.Clamp01((wet - noise) * 5f + 0.5f);
            }
            var waterBy = wet > 0.001f ? now / wet : 0f;
            var steamBy = wet < 0.999f ? (1f - now) / (1f - wet) : 0f;
            var steamNow = flow.Steam * steamBy;
            var flashNow = flow.Flashed * waterBy;
            var sprayNow = flow.Spray * waterBy;

            SetRate(steam, Mathf.Min(MaxSteam, steamNow * SteamPerKg));
            SetRate(flash, Mathf.Min(MaxFlash, flashNow * FlashPerKg));
            SetRate(spray, Mathf.Min(MaxSpray, sprayNow * SprayPerKg));
            if (spray != null)
            {
                // Foaming water comes out milky and brown.
                var main = spray.main;
                main.startColor = Color.Lerp(new Color(0.85f, 0.92f, 1f, 0.85f), new Color(0.75f, 0.68f, 0.55f, 0.9f), flow.Froth);
            }

            var volume = Mathf.Max(0f, Main.Settings.SteamVentVolume);
            var steamShare = Mathf.Clamp01(steamNow / SteamFull);
            var flashShare = Mathf.Clamp01(flashNow / FlashFull);
            SetVolume(hiss, volume * Loudest * Mathf.Sqrt(Mathf.Max(steamShare, flashShare)),
                Mathf.Lerp(1.6f, 0.95f, flashShare / Mathf.Max(1e-4f, steamShare + flashShare)));
            SetVolume(spatter, volume * SpatterLoudest * Mathf.Sqrt(Mathf.Clamp01(sprayNow / SprayFull)), 1.3f);
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
                return;
            var emission = system.emission;
            emission.rateOverTime = rate;
        }

        private static void SetVolume(AudioSource source, float volume, float pitch)
        {
            if (source == null)
                return;
            source.volume = volume;
            source.pitch = pitch;
            if (volume > 0.002f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.002f && source.isPlaying)
                source.Stop();
        }
    }
}
