using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 internal sealed class DriftSounds : MonoBehaviour
    {
        private const string LiftClip = "LeverLimit_08_MetalLight";
        private const string SeatClip = "LeverNotch_04_Tick";
        private const string BreathClip = "Steam_AirFlow_01";
        private const string ByPassClip = "LeverLimit_04_MetalMedium";

        // The chests empty this long after the throttle shuts before the valves lift, s (inferred).
        private const float LiftDelay = 0.8f;

        // The second valve a moment after the first: one on each chest.
        private const float OtherValve = 0.12f;

        private const float ClackVolume = 0.5f;
        private const float BreathLoudest = 0.3f;

        // Breathing at its fullest by this speed, m/s.
        private const float BreathFullSpeed = 18f;

        // Two double-acting cylinders: four strokes, four breaths, a turn of the wheels.
        private const float StrokesPerTurn = 4f;

        // Where the steam chests are on each engine, m behind the front
        // coupler and above the rail (inferred from the stock models); any
        // other engine takes the last.
        private static readonly Dictionary<string, Vector2> Chests = new Dictionary<string, Vector2>
        {
            ["LocoS282A"] = new Vector2(2.8f, 1.7f),
            ["LocoS060"] = new Vector2(1.5f, 1.4f),
        };
        private static readonly Vector2 AnyChest = new Vector2(2.8f, 1.7f);

        private const float ScanEvery = 2f;
        private static float nextScan;
        private static readonly ConditionalWeakTable<TrainCar, DriftSounds> Attached = new ConditionalWeakTable<TrainCar, DriftSounds>();

        private TrainCar car;
        private ReciprocatingSteamEngine engine;
        private AudioSource clacks;
        private AudioSource breath;
        private AudioClip lift;
        private AudioClip seat;
        private AudioClip byPassClip;
        private float shutFor;
        private bool lifted;
        private bool byPassOpen;
        private float otherIn = -1f;
        private AudioClip otherClip;
        private float beatPhase;

        /// <summary>Puts the sounds on every engine in the world that may have the fittings and hasn't them yet; cheap, and only every few seconds.</summary>
        public static void Scan()
        {
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + ScanEvery;
            var spawner = CarSpawner.Instance;
            if (spawner == null)
                return;
            foreach (var car in spawner.AllLocos)
            {
                if (car == null || car.carLivery == null || !DriftingFittings.MayHave(car.carLivery.id))
                    continue;
                if (Attached.TryGetValue(car, out var had) && had != null)
                    continue;
                var sounds = Attach(car);
                if (sounds == null)
                    continue;
                Attached.Remove(car);
                Attached.Add(car, sounds);
            }
        }

        private static DriftSounds Attach(TrainCar car)
        {
            var engine = EngineDrift.EngineOf(car);
            if (engine == null)
                return null;
            if (!Chests.TryGetValue(car.carLivery.id, out var chest))
                chest = AnyChest;
            var go = new GameObject("[RealisticFiring drifting fittings]");
            go.transform.SetParent(car.transform, false);
            var front = car.frontCoupler != null ? car.transform.InverseTransformPoint(car.frontCoupler.transform.position).z : 0f;
            go.transform.localPosition = new Vector3(0f, chest.y, front - chest.x);
            var sounds = go.AddComponent<DriftSounds>();
            sounds.Build(car, engine);
            return sounds;
        }

        private void Build(TrainCar car, ReciprocatingSteamEngine engine)
        {
            this.car = car;
            this.engine = engine;
            var clips = Resources.FindObjectsOfTypeAll<AudioClip>();
            lift = clips.FirstOrDefault(c => c.name == LiftClip);
            seat = clips.FirstOrDefault(c => c.name == SeatClip);
            byPassClip = clips.FirstOrDefault(c => c.name == ByPassClip);
            var mixer = car.interior != null
                ? car.interior.GetComponentsInChildren<AudioSource>(true).Select(s => s.outputAudioMixerGroup).FirstOrDefault(g => g != null)
                : null;
            clacks = Source(null, mixer, loop: false);
            breath = Source(clips.FirstOrDefault(c => c.name == BreathClip), mixer, loop: true);
        }

        private AudioSource Source(AudioClip clip, UnityEngine.Audio.AudioMixerGroup mixer, bool loop)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
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

        private void Update()
        {
            if (car == null || engine == null)
                return;
            var vents = Mathf.Max(0f, Main.Settings != null ? Main.Settings.SteamVentVolume : 1f);
            var speed = car.GetForwardSpeed();
            var state = EngineDrift.Read(car, engine);
            var open = state.SniftingOpen;
            if (state.ByPassOpen != byPassOpen)
            {
                byPassOpen = state.ByPassOpen;
                if (byPassClip != null && clacks != null)
                {
                    clacks.pitch = byPassOpen ? 0.85f : 0.95f;
                    clacks.PlayOneShot(byPassClip, ClackVolume * vents * 0.7f);
                }
            }

            // Lift once the chests have emptied; seat at once when steam comes back.
            shutFor = open ? shutFor + Time.deltaTime : 0f;
            if (!lifted && shutFor >= LiftDelay)
            {
                lifted = true;
                Clack(lift, vents);
            }
            else if (lifted && !open)
            {
                lifted = false;
                Clack(seat, vents);
            }
            if (otherIn >= 0f)
            {
                otherIn -= Time.deltaTime;
                if (otherIn < 0f && otherClip != null)
                    clacks.PlayOneShot(otherClip, ClackVolume * vents * 0.8f);
            }

            Breathe(lifted, speed, vents);
        }

        private void Clack(AudioClip clip, float vents)
        {
            if (clip == null || clacks == null)
                return;
            clacks.pitch = Random.Range(0.95f, 1.1f);
            clacks.PlayOneShot(clip, ClackVolume * vents);
            otherClip = clip;
            otherIn = OtherValve;
        }

        // Air drawn in on each stroke: louder and quicker with the speed.
        private void Breathe(bool lifted, float speed, float vents)
        {
            if (breath == null || breath.clip == null)
                return;
            var turns = Mathf.Abs(engine.crankRpm.Value) / 60f;
            var level = lifted ? BreathLoudest * Mathf.Clamp01((Mathf.Abs(speed) - Drifting.SniftingSpeed) / BreathFullSpeed) * vents : 0f;
            if (level > 0.005f)
            {
                beatPhase = Mathf.Repeat(beatPhase + turns * StrokesPerTurn * Time.deltaTime, 1f);
                var pulse = Mathf.Sin(Mathf.PI * beatPhase);
                breath.volume = level * (0.3f + 0.7f * pulse * pulse);
                breath.pitch = 0.8f + 0.3f * Mathf.Clamp01(Mathf.Abs(speed) / 25f);
                if (!breath.isPlaying)
                    breath.Play();
            }
            else if (breath.isPlaying)
            {
                breath.Stop();
                beatPhase = 0f;
            }
        }
    }
}
