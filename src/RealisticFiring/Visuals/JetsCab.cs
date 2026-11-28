using System.Collections.Generic;
using System.Linq;
using DV;
using DV.Simulation.Cars;
using DV.Simulation.Ports;
using LocoSim.Definitions;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class JetsCab
    {
        internal const string RootName = "[RealisticFiring over-fire jets]";

        private static readonly CabValve.Seat Default = new CabValve.Seat(new Vector3(0.15f, 0f, 0f), Vector3.zero);

        internal static readonly CabValve Valve = new CabValve("over-fire jets' valve", RootName, "JETS", RealisticFirebox.JetsControl,
            livery => LocomotiveProfiles.For(livery)?.Cab.JetsValveSeat ?? MakerFitRegistry.TryGetJetsSeat(livery),
            Default,
            livery => LocomotiveProfiles.For(livery)?.Cab.JetsValveOwnHandle == true || MakerFitRegistry.JetsOwnHandle(livery),
            // An S282 converted to oil burns its smoke at the burner: no over-fire jets (S282OilCab).
            car => FireboxRegistry.TryGet(car, out var firebox) && firebox.Character.Jets != null && (firebox.Character.JetsAsBuilt || Upgrades.Fittings.HasJets(car))
                   && !S282OilCab.IsConverted(car),
            (root, car, interior) =>
            {
                if (FireboxRegistry.TryGet(car, out var firebox))
                    root.AddComponent<JetsHiss>().Init(firebox, interior);
            });

#if !PUBLIC_RELEASE
        internal static Dictionary<string, CabValve.Seat> DevSeats => Valve.DevSeats;
#endif
        internal static CabValve.Seat SeatFor(string livery) => Valve.SeatFor(livery);
        internal static CabValve.Seat SeatInCar(TrainCar car, string livery) => Valve.SeatInCar(car, livery);
        public static void EnsureFitted() => Valve.EnsureFitted();
        public static void RebuildLoaded() => Valve.RebuildLoaded();
        public static void Rebuild(TrainCar car) => Valve.Rebuild(car);
        public static string Attach(TrainCar car, Transform interior) => Valve.Attach(car, interior);
    }

 internal sealed class JetsHiss : MonoBehaviour
    {
        private const string ClipName = "SteamRelease_01";
        private const float Loudest = 0.35f;

        private RealisticFirebox firebox;
        private AudioSource source;
        private float full;

        internal void Init(RealisticFirebox firebox, Transform interior)
        {
            this.firebox = firebox;
            var jets = firebox.Model.Jets;
            full = jets != null ? jets.Count * Core.OverFireJets.PerJet(jets.Tube, 200f / 14.5038f).Steam : 0f;
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
            source.maxDistance = 25f;
            source.dopplerLevel = 0f;
            source.volume = 0f;
            source.outputAudioMixerGroup = interior.GetComponentsInChildren<AudioSource>(true).Select(a => a.outputAudioMixerGroup).FirstOrDefault(g => g != null);
        }

        private void Update()
        {
            if (source == null || firebox == null)
                return;
            var share = Mathf.Clamp01(firebox.Model.JetSteamFlow / full);
            var volume = Loudest * Mathf.Sqrt(share) * Mathf.Max(0f, Main.Settings.SteamVentVolume);
            source.volume = volume;
            source.pitch = 1.1f + 0.2f * share;
            if (volume > 0.005f && !source.isPlaying)
                source.Play();
            else if (volume <= 0.005f && source.isPlaying)
                source.Stop();
        }
    }
}
