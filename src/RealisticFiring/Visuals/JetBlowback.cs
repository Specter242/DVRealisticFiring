using System.Collections.Generic;
using System.Linq;
using DV.Simulation.Controllers;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal static class JetBlowback
    {
        // Blowback under this, kg/s, shows nothing: a whiff at the door.
        private const float Least = 0.1f;

        // A burst at the most every this long, s, at Full kg/s of blowback; sparser below.
        private const float Shortest = 1.5f;
        private const float Full = 1f;

        // The door open at least this far, as the game's own blowback asks.
        private const float DoorOpen = 0.2f;

        private const string ClipName = "Steam_Blowback_01";
        private const string Notice = "The over-fire jets are blowing the fire back out of the door: open the blower, or shut the jets.";

        private static AudioClip clip;

        private sealed class Door
        {
            public BlowbackParticlePortReader Reader;
            public float Next;
            public bool Blowing;
        }

        private static readonly Dictionary<RealisticFirebox, Door> Doors = new Dictionary<RealisticFirebox, Door>();

        /// <summary>Plays each engine's blowback this frame.</summary>
        public static void Update()
        {
            foreach (var firebox in FireboxRegistry.All)
            {
                var car = firebox.Car;
                if (car == null)
                    continue;
                var blowback = firebox.Model.Blowback;
                var open = firebox.DoorOpening >= DoorOpen && firebox.Model.FireOn;
                if (!Doors.TryGetValue(firebox, out var door))
                {
                    if (!(blowback > Least) || !open)
                        continue;
                    door = new Door { Reader = car.GetComponentsInChildren<BlowbackParticlePortReader>(true).FirstOrDefault() };
                    Doors[firebox] = door;
                }
                if (!(blowback > Least) || !open)
                {
                    door.Blowing = false;
                    continue;
                }
                if (!door.Blowing)
                {
                    door.Blowing = true;
                    door.Next = 0f;
                    var at = door.Reader != null && door.Reader.spawnAnchor != null ? door.Reader.spawnAnchor.position : car.transform.position + car.transform.up * 2f;
                    if (clip == null)
                        clip = Resources.FindObjectsOfTypeAll<AudioClip>().FirstOrDefault(c => c.name == ClipName);
                    if (clip != null)
                        AudioSource.PlayClipAtPoint(clip, at, Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(blowback / Full)));
                    if (car == PlayerManager.Car)
                        FireController.Announce(Notice);
                    Main.Mod?.Logger.Log($"{car.ID}: the jets blowing back {blowback:F2} kg/s out of the door");
                }
                door.Next -= Time.deltaTime;
                if (door.Next > 0f)
                    continue;
                door.Next = Shortest * Full / Mathf.Clamp(blowback, Least, Full);
                Burst(door.Reader);
            }
            foreach (var gone in Doors.Keys.Where(f => f == null || f.Car == null).ToList())
                Doors.Remove(gone);
        }

        private static void Burst(BlowbackParticlePortReader reader)
        {
            if (reader == null || reader.blowbackParticlesPrefab == null || reader.spawnAnchor == null)
                return;
            var anchor = reader.spawnAnchor;
            var burst = Object.Instantiate(reader.blowbackParticlesPrefab, anchor.position, anchor.rotation, anchor);
            burst.AddComponent<LightFades>();
            Object.Destroy(burst, reader.particlesLifetime);
        }

        // A burst's light fades out with its flame, in about a second: lit for
        // its whole life, the bursts kept the cab a steady red between them, a
        // fire that stays on rather than puffs (the council, 2026-11-14).
        private sealed class LightFades : MonoBehaviour
        {
            private const float FadeTime = 1.2f;
            private Light[] lights;
            private float[] start;
            private float age;

            private void Start()
            {
                lights = GetComponentsInChildren<Light>(true);
                start = lights.Select(l => l.intensity).ToArray();
            }

            private void Update()
            {
                if (lights == null)
                    return;
                age += Time.deltaTime;
                var share = Mathf.Clamp01(1f - age / FadeTime);
                for (var i = 0; i < lights.Length; i++)
                    if (lights[i] != null)
                        lights[i].intensity = start[i] * share * share;
            }
        }
    }
}
