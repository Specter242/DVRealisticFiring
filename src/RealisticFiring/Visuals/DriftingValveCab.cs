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

        private static readonly Dictionary<string, CabValve.Seat> Seats = new Dictionary<string, CabValve.Seat>
        {
            ["LocoS282A"] = new CabValve.Seat(new Vector3(0.69f, 2.84f, 0.18f), new Vector3(0f, -40f, 0f), new Vector3(0f, 0f, 0f), 0f, 0f, plateSquare: false, inCar: true),
            ["LocoS060"] = new CabValve.Seat(new Vector3(0.310f, 3.206f, -1.990f), Vector3.zero, Vector3.zero, 0f, 0f, plateSquare: false, inCar: true),
        };

        // Cabs whose drifting valve is a copy of their own blower handle, as the jets' valve is.
        private static readonly HashSet<string> OwnHandle = new HashSet<string> { "LocoS060" };

        internal static readonly CabValve Valve = new CabValve("drifting valve", RootName, "DRIFTING", DriftingFittings.ValveControl, Seats, Default, OwnHandle,
            DriftingFittings.Valve,
            (root, car, interior) => root.AddComponent<DriftingHiss>().Init(car, interior),
            byThrottle: true, throttleOffset: new Vector3(0f, -0.25f, 0f));

        private static int fittedVersion = -1;

        /// <summary>Rebuilds the loaded cabs' valves as the drifting valve is bought, or the menu or the host changes it.</summary>
        public static void EnsureFitted()
        {
            if (fittedVersion == Upgrades.Fittings.Version)
                return;
            fittedVersion = Upgrades.Fittings.Version;
            Valve.RebuildLoaded();
        }

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
