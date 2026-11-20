using System.Collections.Generic;
using DV.Utils;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class AutoFireDoorSounds : MonoBehaviour
    {
        internal static float BurstVolume = 0.3f;
        internal static float HissVolume = 0.16f;

        private const float FadeIn = 0.03f;
        private const float FadeOut = 0.1f;
        internal const float LeastHiss = 0.12f;
        // A release that never came (a lost message, a player gone): the hiss stops by itself.
        private const float LongestHiss = 60f;
        private const float NearDistance = 1.5f;
        private const float FarDistance = 30f;

        private static AudioClip burstClip, hissClip;
        private static readonly Dictionary<TrainCar, AutoFireDoorSounds> Doors = new Dictionary<TrainCar, AutoFireDoorSounds>();

        private TrainCar car;
        private AudioSource burst, hiss;
        private bool held;
        private float pressedAt;
        private float level;

        /// <summary>The hiss's level now, 0 to 1 (for the door check).</summary>
        internal float HissLevel => level;

        /// <summary>Whether the pedal is down as this door hears it.</summary>
        internal bool Held => held;

        /// <summary>This game's own door: heard here and passed to the other players.</summary>
        public static void Play(TrainCar car, Vector3 doorInCar, AutoFireDoorAir air)
        {
            if (car == null || air == AutoFireDoorAir.None)
                return;
            Hear(car, doorInCar, air);
            Net.DoorAirSender?.Invoke(car, air);
        }

        /// <summary>Another player's door, from the network.</summary>
        public static void Remote(TrainCar car, AutoFireDoorAir air)
        {
            if (car == null || air == AutoFireDoorAir.None)
                return;
            Hear(car, DoorOf(car), air);
        }

        /// <summary>The door's sounds on <paramref name="car"/>, if it has any yet.</summary>
        internal static bool TryGet(TrainCar car, out AutoFireDoorSounds sounds)
        {
            sounds = null;
            return car != null && Doors.TryGetValue(car, out sounds) && sounds != null;
        }

        private static void Hear(TrainCar car, Vector3 doorInCar, AutoFireDoorAir air)
        {
            var door = For(car, doorInCar);
            if (door == null)
                return;
            if (air == AutoFireDoorAir.Admitted)
                door.Admit();
            else
                door.Exhaust();
        }

        // The door's place: where the rig found it, else over the middle of the grate.
        private static Vector3 DoorOf(TrainCar car)
        {
            if (AutoFireDoorRig.TryGetDoor(car, out var door))
                return door;
            if (FireboxRegistry.TryGet(car, out var firebox) && firebox.GrateBox is Bounds grate)
                return new Vector3(grate.center.x, grate.max.y + 0.4f, grate.center.z);
            return new Vector3(0f, 2.5f, 0f);
        }

        private static AutoFireDoorSounds For(TrainCar car, Vector3 doorInCar)
        {
            if (Doors.TryGetValue(car, out var door) && door != null)
            {
                door.transform.localPosition = doorInCar;
                return door;
            }
            if (!Clips())
                return null;
            var host = new GameObject("RF fire door air");
            host.transform.SetParent(car.transform, false);
            host.transform.localPosition = doorInCar;
            door = host.AddComponent<AutoFireDoorSounds>();
            door.car = car;
            var manager = SingletonBehaviour<AudioManager>.Instance;
            var group = manager != null ? manager.cabGroup : null;
            door.burst = door.Source(host, burstClip, false, group);
            door.hiss = door.Source(host, hissClip, true, group);
            Doors[car] = door;
            return door;
        }

        private AudioSource Source(GameObject host, AudioClip clip, bool loop, UnityEngine.Audio.AudioMixerGroup group)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = NearDistance;
            source.maxDistance = FarDistance;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = group;
            return source;
        }

        private static bool Clips()
        {
            if (burstClip != null && hissClip != null)
                return true;
            var rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var burstData = PneumaticAir.Burst(rate, 11);
            burstClip = AudioClip.Create("RF fire door burst", burstData.Length, 1, rate, false);
            burstClip.SetData(burstData, 0);
            var hissData = PneumaticAir.Hiss(rate, 12);
            hissClip = AudioClip.Create("RF fire door hiss", hissData.Length, 1, rate, false);
            hissClip.SetData(hissData, 0);
            return true;
        }

        private void Admit()
        {
            held = true;
            pressedAt = Time.time;
            burst.volume = BurstVolume;
            burst.Stop();
            burst.Play();
            if (!hiss.isPlaying)
            {
                // A new place in the loop each time, so two presses don't hiss alike.
                hiss.timeSamples = Random.Range(0, Mathf.Max(1, hiss.clip.samples - 1));
                hiss.volume = 0f;
                hiss.Play();
            }
        }

        private void Exhaust() => held = false;

        private void Update()
        {
            if (hiss == null || !hiss.isPlaying)
                return;
            var since = Time.time - pressedAt;
            if (held && since > LongestHiss)
                held = false;
            var on = held || since < LeastHiss;
            var step = Time.deltaTime / (on ? FadeIn : FadeOut);
            level = on ? Mathf.Min(1f, level + step) : Mathf.Max(0f, level - step);
            hiss.volume = level * HissVolume;
            if (level <= 0f && !on)
                hiss.Stop();
        }

        private void OnDestroy()
        {
            if (car != null && Doors.TryGetValue(car, out var door) && door == this)
                Doors.Remove(car);
        }
    }
}
