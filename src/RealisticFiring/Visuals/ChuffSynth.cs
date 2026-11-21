#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using DV.ModularAudioCar;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Sim;
using UnityEngine;

namespace RealisticFiring.Visuals
{
 [DisallowMultipleComponent]
    internal sealed class ChuffSynth : MonoBehaviour
    {
        private const string S282 = "LocoS282A";

        /// <summary>Whether it is heard in place of the recordings; the game check turns it off to compare them.</summary>
        internal static bool Heard = true;

        internal static float LinesideGain = 1.3f;
        internal static float CabGain = 2.4f;

        /// <summary>For the game check, whose listener stands beside the moving engine: whether to hear as from the cab.</summary>
        internal static bool? ForceInCab;

        private static readonly List<ChuffSynth> All = new List<ChuffSynth>();
        private static AudioClip steady;

        private TrainCar car;
        private ReciprocatingSteamEngine engine;
        private EngineCylinders cylinders;
        private AudioSource source;
        private ExhaustVoice voice;
        private float reach;
        private bool awake;
        private bool told;
        private float onSince = -1f;
        // The loudest block over the working stretch Tell watches: one block between slow beats is silent.
        private double loudest;
        private volatile Exception failure;

        /// <summary>The voice for <paramref name="car"/> if it is an S282; null otherwise.</summary>
        internal static ChuffSynth For(ChuffSpace space, TrainCar car, ReciprocatingSteamEngine engine,
            ChuffClipsSimReader.IndividualChuffAudioSourceConfig config)
        {
            if (car.carLivery == null || car.carLivery.id != S282)
                return null;
            var synth = space.GetComponentInChildren<ChuffSynth>(true);
            if (synth == null)
            {
                var host = new GameObject("RF chuff synth");
                host.transform.SetParent(space.transform, false);
                // The source and this first, so the filters the space adds work on the sound.
                var source = host.AddComponent<AudioSource>();
                synth = host.AddComponent<ChuffSynth>();
                synth.source = source;
                synth.voice = new ExhaustVoice(Spec(), new ExhaustTuning(), AudioSettings.outputSampleRate, Seed(car));
                All.Add(synth);
            }
            space.Carry(synth.source, config);
            synth.source.clip = Steady();
            synth.source.loop = true;
            // Playing all the time, it must never be the voice the audio engine drops when many sounds play at once.
            synth.source.priority = 0;
            synth.car = car;
            synth.engine = engine;
            // The reader is set up before the engine's first tick, so its cylinders may not exist yet.
            synth.cylinders = EngineCylinders.Of(engine);
            synth.reach = config.maxDistance;
            synth.awake = true;
            synth.source.Play();
            return synth;
        }

        /// <summary>The reader let go of its ports: silent until it is set up again.</summary>
        internal void Sleep()
        {
            awake = false;
            voice.Open = 0f;
        }

        private static ExhaustSpec Spec() => new ExhaustSpec
        {
            Cylinders = 2,
            Bore = 0.66f,
            Stroke = 0.762f,
            Clearance = 0.09f,
            NozzleDiameter = 0.15f,
            ChimneyLength = 1.6f,
            ChimneyDiameter = 0.48f,
            Superheated = true,
            SteamTemperature = 330f,
            Gear = new ValveGear { Lap = 1.5f, Lead = 0.25f, ExhaustLap = 0f, PortWidth = 2f },
        };

        private static int Seed(TrainCar car)
        {
            unchecked
            {
                var hash = (int)2166136261;
                foreach (var c in car.ID ?? "")
                    hash = (hash ^ c) * 16777619;
                return hash;
            }
        }

        // A second of a steady 1: what the audio engine scales for distance and pans, for the sound to multiply.
        private static AudioClip Steady()
        {
            if (steady != null)
                return steady;
            var rate = AudioSettings.outputSampleRate;
            steady = AudioClip.Create("RF chuff steady", rate, 1, rate, false);
            var ones = new float[rate];
            for (var i = 0; i < ones.Length; i++)
                ones[i] = 1f;
            steady.SetData(ones, 0);
            return steady;
        }

        private void Update()
        {
            if (failure != null)
            {
                Faults.Report("the chuff synth", car, failure);
                failure = null;
                awake = false;
            }
            if (!awake || car == null || engine == null)
            {
                voice.Open = 0f;
                return;
            }
            var turns = engine.crankRpm.Value / 60f;
            voice.Post(new ExhaustDrive
            {
                Crank = engine.crankRotationReadOut.Value,
                TurnsPerSecond = turns,
                Cutoff = EngineDrift.Cutoff(engine),
                Chest = cylinders.Chest,
                Time = AudioSettings.dspTime,
            });
            var listener = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera.transform : null;
            var near = listener == null || (listener.position - transform.position).sqrMagnitude < reach * reach;
            voice.Open = near && Heard && Main.Settings != null && Main.Settings.RealisticChuffs ? 1f : 0f;
            voice.InCab = (ForceInCab ?? PlayerManager.Car == car) ? 1f : 0f;
            // The player's "Chuff volume, %", at once.
            var volume = Main.Settings != null ? Mathf.Clamp(Main.Settings.ChuffVolume, 0f, 200f) / 100f : 1f;
            voice.LinesideGain = LinesideGain * volume;
            voice.CabGain = CabGain * volume;
            if (voice.Open > 0f && source.enabled && source.gameObject.activeInHierarchy && !source.isPlaying)
                source.Play();
            Tell(turns);
        }

        // Once a session for each engine the player works under steam: whether its voice is heard, for a report's log.
        private void Tell(float turns)
        {
            var working = PlayerManager.Car == car && Mathf.Abs(turns) * 4f > 0.5f && cylinders.Chest > 2f;
            if (!working || told)
            {
                onSince = working ? onSince : -1f;
                loudest = 0.0;
                return;
            }
            if (onSince < 0f)
                onSince = Time.time;
            loudest = Math.Max(loudest, voice.LastLevel);
            if (Time.time - onSince < 3f)
                return;
            told = true;
            var listener = PlayerManager.ActiveCamera != null ? PlayerManager.ActiveCamera.transform.position : transform.position;
            var quiet = !source.isPlaying || source.isVirtual || loudest < 1e-4;
            var line = $"{car.ID}: chuff synth at {Mathf.Abs(turns) * 4f:0.0} beats a second, chest {cylinders.Chest - 1f:0.0} bar: " +
                       $"{(source.isPlaying ? "playing" : "NOT playing")}{(source.isVirtual ? ", virtual (dropped by the audio engine)" : "")}, " +
                       $"open {voice.Open:0}, loudest {loudest:0.000} over 3 s before distance, lift {voice.Lift:0.0}, {Vector3.Distance(listener, transform.position):0} m from the listener, " +
                       $"volume {source.volume:0.00}, mixer {(source.outputAudioMixerGroup != null ? source.outputAudioMixerGroup.name : "none")}";
            if (quiet)
                Main.Mod.Logger.Warning(line);
            else
                Main.Mod.Logger.Log(line);
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (voice == null)
                return;
            try
            {
                voice.Fill(data, channels, AudioSettings.dspTime);
            }
            catch (Exception e)
            {
                failure = e;
                Array.Clear(data, 0, data.Length);
            }
        }

        private void OnDestroy() => All.Remove(this);
    }
}
#endif
