using System.Collections.Generic;
using System.Linq;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class DriftingValveCab
    {
        internal const string RootName = "[RealisticFiring drifting valve]";

        private static readonly CabValve.Seat Default = new CabValve.Seat(new Vector3(-0.15f, 0f, 0f), Vector3.zero);

        internal static readonly CabValve Valve = new CabValve("drifting valve", RootName, "DRIFTING", DriftingFittings.ValveControl,
            livery => LocomotiveProfiles.For(livery)?.Cab.DriftingValveSeat ?? MakerFitRegistry.TryGetDriftingValveSeat(livery),
            Default,
            livery => LocomotiveProfiles.For(livery)?.Cab.DriftingValveOwnHandle == true || MakerFitRegistry.DriftingValveOwnHandle(livery),
            DriftingFittings.Valve,
            (root, car, interior) => root.AddComponent<DriftingHiss>().Init(car, interior),
            byThrottle: true, throttleOffset: new Vector3(0f, -0.25f, 0f));

        /// <summary>Adds or removes the drifting valve in loaded cabs whose fitted state or car GUID changed.</summary>
        public static void EnsureFitted() => Valve.EnsureFitted();

        public static string Attach(TrainCar car, Transform interior) => Valve.Attach(car, interior);
    }

    /// <summary>
    /// The drifting valve's steam in the cab: the game's steam release,
    /// looped, on the valve, as loud as the square root of the steam it
    /// passes against its pipe wide open at 14 bar, quieter than the jets',
    /// scaled by the steam vents' volume setting.
    /// </summary>
    internal sealed class DriftingHiss : MonoBehaviour
    {
        private const string ClipName = "SteamRelease_01";
        private const float Loudest = 0.2f;

        private TrainCar car;
        private AudioSource source;
        private float full;

        internal void Init(TrainCar car, Transform interior)
        {
            this.car = car;
            full = Core.Drifting.ValveSteam(1f, 14f);
            var clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == ClipName);
            if (clip == null || !(full > 0f))
                return;
            source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1.5f;
            source.maxDistance = 20f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = interior.GetComponentsInChildren<AudioSource>(true).Select(a => a.outputAudioMixerGroup).FirstOrDefault(g => g != null);
        }

        private void Update()
        {
            if (source == null || car == null)
                return;
            var share = Mathf.Clamp01(EngineDrift.SteamUsed(car) / full);
            var volume = Loudest * Mathf.Sqrt(share) * Mathf.Max(0f, Main.Settings.SteamVentVolume);
            source.volume = volume;
            source.pitch = 1.3f + 0.2f * share;
            if (volume > 0.005f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.005f && source.isPlaying)
                source.Stop();
        }
    }
}
