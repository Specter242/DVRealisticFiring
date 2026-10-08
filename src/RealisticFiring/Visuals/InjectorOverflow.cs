using System.Collections.Generic;
using System.Linq;
using DV.HUD;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// What a fireman watches and listens for: water and steam spilling from
    /// the injector's overflow pipe under the cab while it picks up and after
    /// it knocks off, with the game's own water-dump and steam-release sounds.
    /// A singing injector spills nothing. Hidden just under the cab's deck
    /// plate on the injector lever's (the fireman's) side, spilling out and
    /// down from under the cab.
    /// </summary>
    internal sealed class InjectorOverflow : MonoBehaviour
    {
        // Tucked under the deck plate: this far below the deck, and at least
        // this far out from the centerline (a little outboard of the lever).
        private const float BelowDeck = 0.15f;
        private const float MinOutboard = 0.8f;
        private const float OutboardOfLever = 0.3f;

        // How far below the lever to look for the deck, in m.
        private const float DeckSearch = 3f;

        // Overflow steam rises gently, so it billows out from under the cab
        // instead of drifting back up through the deck into it.
        private const float SteamRise = -0.05f;

        // Particles per second per kg/s spilled, and the most of each.
        private const float WaterParticlesPerKg = 25f;
        private const float MaxWaterParticles = 250f;
        private const float SteamParticlesPerKg = 40f;
        private const float MaxSteamParticles = 80f;

        private const string WaterClipName = "WaterDump_01";
        private const string SteamClipName = "SteamRelease_01";

        private InjectorRig rig;
        private ParticleSystem water;
        private ParticleSystem steam;
        private AudioSource waterSound;
        private AudioSource steamSound;

        /// <summary>
        /// Puts the overflow on <paramref name="car"/> beside its injector lever
        /// (or moves it there if the cab reloaded). Returns a line for the log,
        /// or null without the effects bundle.
        /// </summary>
        public static string Attach(TrainCar car, InjectorRig rig, IEnumerable<Transform> roots)
        {
            if (PlumeAssets.OverflowPrefab == null)
                return null;

            var lever = roots.SelectMany(r => r.GetComponentsInChildren<LocoControlsReader>(true))
                .Select(reader => reader.injector)
                .FirstOrDefault(go => go != null);
            var leverLocal = lever != null ? car.transform.InverseTransformPoint(lever.transform.position) : new Vector3(-0.6f, 2f, 0f);
            var side = leverLocal.x < 0f ? -1f : 1f;
            var foundDeck = TryFindDeck(car, car.transform.TransformPoint(leverLocal), out var deckHeight);
            if (!foundDeck)
                deckHeight = leverLocal.y - 1f;

            var overflow = car.GetComponentInChildren<InjectorOverflow>(true);
            if (overflow == null)
            {
                var fx = Instantiate(PlumeAssets.OverflowPrefab, car.transform, false);
                fx.name = PlumeAssets.OverflowPrefabName;
                overflow = fx.AddComponent<InjectorOverflow>();
                overflow.Build(car, rig);
            }
            var outboard = Mathf.Max(Mathf.Abs(leverLocal.x) + OutboardOfLever, MinOutboard);
            var position = new Vector3(side * outboard, deckHeight - BelowDeck, leverLocal.z);
            overflow.transform.localPosition = position;
            overflow.transform.localRotation = Quaternion.LookRotation(new Vector3(side, -0.6f, 0f).normalized, Vector3.up);

            return $"injector overflow under the deck on the {(side < 0f ? "left" : "right")} at ({position.x:F2}, {position.y:F2}, {position.z:F2}) m" +
                   (foundDeck ? $", deck at {deckHeight:F2} m" : ", deck not found (guessed 1 m below the lever)") +
                   (lever == null ? ", no injector lever found (guessed the side)" : "") +
                   $", sounds: water {(overflow.waterSound != null ? $"'{WaterClipName}'" : "missing")}, " +
                   $"steam {(overflow.steamSound != null ? $"'{SteamClipName}'" : "missing")}";
        }

        // The cab deck under a point: the first upward-facing surface of this
        // car straight below it, in the car's frame.
        private static bool TryFindDeck(TrainCar car, Vector3 from, out float height)
        {
            height = 0f;
            var down = -car.transform.up;
            foreach (var hit in Physics.RaycastAll(from, down, DeckSearch, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (Vector3.Dot(hit.normal, car.transform.up) < 0.7f || TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                height = car.transform.InverseTransformPoint(hit.point).y;
                return true;
            }
            return false;
        }

        private void Build(TrainCar car, InjectorRig owner)
        {
            rig = owner;
            water = transform.Find("Water")?.GetComponent<ParticleSystem>();
            steam = transform.Find("Steam")?.GetComponent<ParticleSystem>();

            // Spilled water stays where it fell as the train moves: simulate in
            // the same space as the game's own smoke (it follows the world's
            // origin shifts).
            var reference = car.GetComponentsInChildren<ParticleSystem>(true)
                .FirstOrDefault(s => s.main.simulationSpace == ParticleSystemSimulationSpace.Custom && s.main.customSimulationSpace != null);
            if (steam != null)
            {
                var steamMain = steam.main;
                steamMain.gravityModifier = SteamRise;
            }
            foreach (var system in new[] { water, steam }.Where(s => s != null))
            {
                var main = system.main;
                if (reference != null)
                {
                    main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                    main.customSimulationSpace = reference.main.customSimulationSpace;
                }
                system.Play();
            }

            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            waterSound = Sound(clips.FirstOrDefault(c => c.name == WaterClipName), mixer);
            steamSound = Sound(clips.FirstOrDefault(c => c.name == SteamClipName), mixer);
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
            source.minDistance = 2f;
            source.maxDistance = 40f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = mixer;
            return source;
        }

        private void LateUpdate()
        {
            if (rig == null)
                return;
            var flow = rig.Flow;
            var full = rig.Injector.Spec.MaxDelivery;
            SetRate(water, Mathf.Min(MaxWaterParticles, flow.Overflow * WaterParticlesPerKg));
            SetRate(steam, Mathf.Min(MaxSteamParticles, flow.OverflowSteam * SteamParticlesPerKg));
            SetVolume(waterSound, Mathf.Clamp01(flow.Overflow / (0.3f * full)));
            SetVolume(steamSound, Mathf.Clamp01(flow.OverflowSteam / (0.05f * full)));
        }

        private static void SetRate(ParticleSystem system, float rate)
        {
            if (system == null)
                return;
            var emission = system.emission;
            emission.rateOverTime = rate;
        }

        private static void SetVolume(AudioSource source, float volume)
        {
            if (source == null)
                return;
            source.volume = volume;
            if (volume > 0.01f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.01f && source.isPlaying)
                source.Stop();
        }
    }
}
