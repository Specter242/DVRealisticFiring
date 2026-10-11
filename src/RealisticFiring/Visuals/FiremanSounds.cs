using System.Linq;
using DV.Utils;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
    /// <summary>
    /// The fireman's shovel, heard but not seen: the game's own shovel
    /// sounds, the scoop as he loads from the tender and the coal landing as
    /// he throws, at the firebox in the cab's mix.
    /// </summary>
    internal static class FiremanSounds
    {
        private const float Volume = 1.5f;
        private const float LookAgain = 10f;

        private static AudioClip scoop;
        private static AudioClip drop;
        private static float lookedAt = float.NegativeInfinity;

        public static void Scoop(TrainCar car, RealisticFirebox firebox)
        {
            Find();
            Play(scoop, car, firebox);
        }

        public static void Throw(TrainCar car, RealisticFirebox firebox)
        {
            Find();
            Play(drop, car, firebox);
        }

        // The shovel's prefab carries the clips; it loads with the items,
        // so until it has, look again now and then.
        private static void Find()
        {
            if (drop != null || Time.time - lookedAt < LookAgain)
                return;
            lookedAt = Time.time;
            var audio = Resources.FindObjectsOfTypeAll<ShovelAudio>().FirstOrDefault(a => a.coalDrop != null);
            if (audio == null)
                return;
            scoop = audio.coalSpawn;
            drop = audio.coalDrop;
        }

        private static void Play(AudioClip clip, TrainCar car, RealisticFirebox firebox)
        {
            if (clip == null || car == null)
                return;
            var manager = SingletonBehaviour<AudioManager>.Instance;
            var at = firebox.GrateBox is Bounds box ? car.transform.TransformPoint(box.center) : car.transform.position;
            clip.Play(at, Volume, mixerGroup: manager != null ? manager.cabGroup : null);
        }
    }
}
