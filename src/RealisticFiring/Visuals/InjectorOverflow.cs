using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.HUD;
using DV.Simulation.Ports;
using RealisticFiring.Core;
using RealisticFiring.Diagnostics;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// What a fireman watches and listens for: water and steam spilling from
    /// the injector's overflow pipe under the cab while it picks up and after
    /// it knocks off, with the game's own water-dump and steam-release sounds.
    /// A singing injector spills nothing. Hidden just under the cab's deck
    /// plate on the injector lever's side, spilling out and down from under
    /// the cab. A loco with an injector lever on each side (the Hudson's work
    /// the same injector) gets one under each, and the one under the lever
    /// last moved spills.
    /// </summary>
    internal sealed class InjectorOverflow : MonoBehaviour
    {
        // Tucked under the deck plate: this far below the deck (the S484-840's
        // is 16 cm thick), and at least this far out from the centerline (a
        // little outboard of the lever).
        private const float BelowDeck = 0.2f;
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
        private Transform leverTransform;
        private ControlImplBase lever;
        private float side;
        private InjectorOverflow[] group = new InjectorOverflow[0];
        private bool wasSpilling;
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
            roots = roots.ToList();

            // Every lever working the injector, one a side; else the one the
            // game's controls reader names.
            var levers = roots.SelectMany(r => r.GetComponentsInChildren<InteractablePortFeeder>(true))
                .Where(f => f.portId == rig.LeverPortId)
                .Select(f => f.transform)
                .ToList();
            if (levers.Count == 0)
            {
                var named = roots.SelectMany(r => r.GetComponentsInChildren<LocoControlsReader>(true))
                    .Select(reader => reader.injector)
                    .FirstOrDefault(go => go != null);
                if (named != null)
                    levers.Add(named.transform);
            }
            var bySide = levers
                .GroupBy(t => car.transform.InverseTransformPoint(t.position).x < 0f ? -1f : 1f)
                .Select(g => g.First())
                .OrderBy(t => car.transform.InverseTransformPoint(t.position).x)
                .ToList();
            if (bySide.Count == 0)
                bySide.Add(null);

            var existing = car.GetComponentsInChildren<InjectorOverflow>(true).ToList();
            var lines = new List<string>();
            var group = new List<InjectorOverflow>();
            foreach (var lever in bySide)
            {
                lines.Add(Place(car, rig, lever, existing, out var overflow));
                group.Add(overflow);
            }
            foreach (var stale in existing.Except(group))
                Destroy(stale.gameObject);
            foreach (var overflow in group)
                overflow.group = group.ToArray();
            return string.Join("; ", lines);
        }

        private static string Place(TrainCar car, InjectorRig rig, Transform lever, List<InjectorOverflow> existing, out InjectorOverflow overflow)
        {
            var leverLocal = lever != null ? car.transform.InverseTransformPoint(lever.position) : new Vector3(-0.6f, 2f, 0f);
            var side = leverLocal.x < 0f ? -1f : 1f;
            var leverWorld = car.transform.TransformPoint(leverLocal);
            var deck = "the cab's teleport point";
            var foundDeck = TryFindCabFloor(car, out var deckHeight);
            if (!foundDeck)
            {
                foundDeck = TryFindDeck(car, leverWorld, WalkableLayers, out deckHeight, out deck)
                            || TryFindDeck(car, leverWorld, ~0, out deckHeight, out deck);
            }
            if (!foundDeck)
                deckHeight = leverLocal.y - 1f;

            overflow = existing.FirstOrDefault(o => o.side == side);
            if (overflow == null)
            {
                var fx = Instantiate(PlumeAssets.OverflowPrefab, car.transform, false);
                fx.name = PlumeAssets.OverflowPrefabName + (side < 0f ? " left" : " right");
                overflow = fx.AddComponent<InjectorOverflow>();
                overflow.side = side;
                overflow.Build(car, rig);
            }
            overflow.leverTransform = lever;
            overflow.lever = null;
            var outboard = Mathf.Max(Mathf.Abs(leverLocal.x) + OutboardOfLever, MinOutboard);
            var position = new Vector3(side * outboard, deckHeight - BelowDeck, leverLocal.z);
            overflow.transform.localPosition = position;
            overflow.transform.localRotation = Quaternion.LookRotation(new Vector3(side, -0.6f, 0f).normalized, Vector3.up);

            return $"injector overflow under the deck on the {(side < 0f ? "left" : "right")} at ({position.x:F2}, {position.y:F2}, {position.z:F2}) m" +
                   (lever != null ? $" under '{lever.name}'" : "") +
                   (foundDeck ? $", deck ({deck}) at {deckHeight:F2} m" : ", deck not found (guessed 1 m below the lever)") +
                   (lever == null ? ", no injector lever found (guessed the side)" : "") +
                   $", sounds: water {(overflow.waterSound != null ? $"'{WaterClipName}'" : "missing")}, " +
                   $"steam {(overflow.steamSound != null ? $"'{SteamClipName}'" : "missing")}";
        }

        // The cab floor's height in the car's frame: where the game stands a
        // player teleported into the cab, the [cab] point every loco has (the
        // S282's 1.37 m, the S060's 1.66 m, the Hudson's 1.99 m, the
        // S484-840's 2.18 m, each on its floor). A ray down from the lever can
        // hit a custom cab's fittings first: on the S484-840 one 0.7 m up.
        internal static bool TryFindCabFloor(TrainCar car, out float height)
        {
            var cab = car.transform.Find(TrainCarColliders.CAB_NAME);
            if (cab == null && car.interior != null)
                cab = car.interior.Find(TrainCarColliders.CAB_NAME);
            height = cab != null ? car.transform.InverseTransformPoint(cab.position).y : 0f;
            return cab != null;
        }

        // The floor the crew walk on is on this layer.
        private static int WalkableLayers => LayerMask.GetMask("Train_Walkable");

        // Without a [cab] point: the first upward-facing surface of this car on
        // these layers straight below a point, in the car's frame.
        private static bool TryFindDeck(TrainCar car, Vector3 from, int layers, out float height, out string what)
        {
            height = 0f;
            what = null;
            var down = -car.transform.up;
            foreach (var hit in Physics.RaycastAll(from, down, DeckSearch, layers, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            {
                if (Vector3.Dot(hit.normal, car.transform.up) < 0.7f || TrainCar.Resolve(hit.collider.gameObject) != car)
                    continue;
                height = car.transform.InverseTransformPoint(hit.point).y;
                what = $"'{hit.collider.name}' on layer {LayerMask.LayerToName(hit.collider.gameObject.layer)} under the lever";
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
            var start = PerfMeter.Start();
            UpdateOverflow();
            PerfMeter.Stop(PerfSection.Overflow, start);
        }

        private void UpdateOverflow()
        {
            if (rig == null)
                return;
            var flow = rig.Flow;
            var full = rig.Injector.Spec.MaxDelivery;
            var spilling = Spilling();
            if (spilling && !wasSpilling && group.Length > 1 && Main.Settings.LogCoalDeliveries)
                Main.Mod.Logger.Log($"{rig.Car?.ID}: injector overflow spills on the {(side < 0f ? "left" : "right")} (valve {rig.LeverValue:F2}; " +
                                    string.Join(", ", group.Where(o => o != null).Select(o => $"{(o.side < 0f ? "left" : "right")} lever " +
                                        (o.Lever() != null ? o.Lever().Value.ToString("F2") : "not spawned"))) + ")");
            wasSpilling = spilling;
            var share = spilling ? 1f : 0f;
            SetRate(water, Mathf.Min(MaxWaterParticles, share * flow.Overflow * WaterParticlesPerKg));
            SetRate(steam, Mathf.Min(MaxSteamParticles, share * flow.OverflowSteam * SteamParticlesPerKg));
            SetVolume(waterSound, share * Mathf.Clamp01(flow.Overflow / (0.3f * full)));
            SetVolume(steamSound, share * Mathf.Clamp01(flow.OverflowSteam / (0.05f * full)));
        }

        // Whether this side spills: with a lever each side, the one whose lever
        // sits where the injector's valve is (the last moved), the fireman's
        // (left) when they agree or the cab is not loaded.
        private bool Spilling()
        {
            if (group.Length < 2)
                return true;
            InjectorOverflow best = null;
            var bestGap = float.MaxValue;
            foreach (var overflow in group)
            {
                if (overflow == null)
                    continue;
                var control = overflow.Lever();
                var gap = control != null ? Mathf.Abs(control.Value - rig.LeverValue) : float.MaxValue;
                if (gap < bestGap - 0.01f || (Mathf.Abs(gap - bestGap) <= 0.01f && best != null && overflow.side < best.side))
                {
                    best = overflow;
                    bestGap = gap;
                }
            }
            return best == null ? side < 0f || group.All(o => o == null || o.side >= side) : best == this;
        }

        // The lever's control, found once it has spawned: a custom cab's
        // controls can spawn after the overflow is placed.
        private ControlImplBase Lever()
        {
            if (lever == null && leverTransform != null)
                lever = leverTransform.GetComponent<ControlImplBase>();
            return lever;
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
