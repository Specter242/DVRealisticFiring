#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using DV.ModularAudioCar;
using HarmonyLib;
using LocoSim.Implementations;
using RealisticFiring.Core;
using RealisticFiring.Patches;
using RealisticFiring.Sim;
using UnityEngine;
using Random = UnityEngine.Random;
using Voice = RealisticFiring.Core.ChuffVoice;

namespace RealisticFiring.Visuals
{
 internal sealed class ChuffPlayer
    {
        private static readonly Dictionary<ChuffClipsSimReader, ChuffPlayer> ByReader = new Dictionary<ChuffClipsSimReader, ChuffPlayer>();
        private static readonly Dictionary<ChuffClipsSimReader.ChuffLoop, ChuffPlayer> ByLoop = new Dictionary<ChuffClipsSimReader.ChuffLoop, ChuffPlayer>();

        private static readonly AccessTools.FieldRef<ChuffClipsSimReader, Port> ExhaustPort = AccessTools.FieldRefAccess<ChuffClipsSimReader, Port>("exhaustPressurePort");
        private static readonly AccessTools.FieldRef<ChuffClipsSimReader, Port> FrequencyPort = AccessTools.FieldRefAccess<ChuffClipsSimReader, Port>("chuffFrequencyPort");
        private static readonly AccessTools.FieldRef<ChuffClipsSimReader, Port> WaterPort = AccessTools.FieldRefAccess<ChuffClipsSimReader, Port>("cylinderWaterNormalizedPort");
        private static readonly AccessTools.FieldRef<ChuffClipsSimReader, Port> CocksPort = AccessTools.FieldRefAccess<ChuffClipsSimReader, Port>("cylinderCockControlPort");
        private static readonly AccessTools.FieldRef<ChuffClipsSimReader, Port> AshPort = AccessTools.FieldRefAccess<ChuffClipsSimReader, Port>("ashesInPipesPort");
        private static readonly AccessTools.FieldRef<ChuffClipsSimReader.ChuffLoop, bool> LoopActive = AccessTools.FieldRefAccess<ChuffClipsSimReader.ChuffLoop, bool>("isActive");
        private static readonly Action<ChuffClipsSimReader> UpdateLoops = AccessTools.MethodDelegate<Action<ChuffClipsSimReader>>(AccessTools.Method(typeof(ChuffClipsSimReader), "UpdateLoops"));

        // The game brings its single beats in over the first beat a second;
        // that rise is kept, and the fall is Voice.BeatShare's.
        private const float StockRiseEnds = 1f;
        // A beat's lateness, s, at most: past this it sounds like a fault, not a character.
        private const float LatestBeat = 0.06f;

        private readonly ChuffClipsSimReader reader;
        private readonly TrainCar car;
        private readonly ReciprocatingSteamEngine engine;
        private readonly BeatCharacter[] character;
        private readonly ChuffSpace space;

        // A drifting engine's panting now, as a share of a working beat; read at each beat.
        private float breath;

#if PUBLIC_RELEASE
        private bool SynthHeard => false;
#else
        // The synthesized exhaust on the S282 in test builds (ChuffSynth), in place of the recordings while steam is on.
        private ChuffSynth synth;

        private bool SynthHeard => synth != null && ChuffSynth.Heard;
#endif

        private ChuffPlayer(ChuffClipsSimReader reader, TrainCar car, ReciprocatingSteamEngine engine, ChuffSpace space)
        {
            this.reader = reader;
            this.car = car;
            this.engine = engine;
            this.space = space;
            character = Voice.Character(Seed(car), Math.Max(1, engine.numCylinders * 2));
        }

        private static bool On => Main.Settings != null && Main.Settings.RealisticChuffs;

        /// <summary>Takes over <paramref name="reader"/>'s beats where the mod works its engine; a line for the log, or null.</summary>
        internal static string Attach(ChuffClipsSimReader reader, TrainCar car, SimulationFlow flow)
        {
            if (reader == null || car == null || flow == null || ExhaustPort(reader) == null || AshPort(reader) == null)
                return null;
            var id = ComponentOf(reader.chuffEventPortId);
            var engine = flow.OrderedSimComps.OfType<ReciprocatingSteamEngine>().FirstOrDefault(e => e.id == id);
            if (engine == null || !EngineCylinders.Works(engine))
                return null;
            Detach(reader);
            var config = reader.regularChuffConfig;
            var stack = config != null && config.parent != null ? config.parent : reader.transform;
            var space = stack.GetComponent<ChuffSpace>();
            if (space == null)
                space = stack.gameObject.AddComponent<ChuffSpace>();
            space.Init(car, reader);
            var player = new ChuffPlayer(reader, car, engine, space);
#if !PUBLIC_RELEASE
            player.synth = ChuffSynth.For(space, car, engine, config);
#endif
            ByReader[reader] = player;
            foreach (var loop in reader.chuffLoops ?? new ChuffClipsSimReader.ChuffLoop[0])
                if (loop != null)
                    ByLoop[loop] = player;
            var latest = player.character.Max(b => b.Lateness);
            return $"chuffs by the steam each beat lets go, its beat up to {latest:P0} late, echoes off the walls round it";
        }

        internal static void Detach(ChuffClipsSimReader reader)
        {
            if (reader == null || !ByReader.TryGetValue(reader, out var player))
                return;
            ByReader.Remove(reader);
            foreach (var loop in ByLoop.Where(p => p.Value == player).Select(p => p.Key).ToList())
                ByLoop.Remove(loop);
            if (player.space != null)
                player.space.Sleep();
#if !PUBLIC_RELEASE
            if (player.synth != null)
                player.synth.Sleep();
#endif
        }

        /// <summary>A beat, in place of the game's; false leaves it to the game.</summary>
        internal static bool TryBeat(ChuffClipsSimReader reader, float chuffIndex)
        {
            if (!On || reader == null || !ByReader.TryGetValue(reader, out var player))
                return false;
            player.Beat(chuffIndex);
            return true;
        }

        private static float Volume => Main.Settings != null ? Mathf.Clamp(Main.Settings.ChuffVolume, 0f, 200f) / 100f : 1f;

        internal static bool TryLoop(ChuffClipsSimReader.ChuffLoop loop, float volumeInput, float beatsPerSecond, out bool active)
        {
            active = false;
            if (!On || loop == null || loop.chuffLoop == null || !ByLoop.TryGetValue(loop, out var player))
                return false;
            // Shut off and drifting, the loops have no steam of their own: the panting in its place.
            var drifting = !(volumeInput > 0f) && player.breath > 0f;
            var master = player.SynthHeard && !drifting ? 0f : loop.chuffFrequencyToMasterVolume.Evaluate(beatsPerSecond) * Voice.LoopShare(beatsPerSecond) * Volume;
            ref var isActive = ref LoopActive(loop);
            var was = isActive;
            active = isActive = master > 0f;
            loop.chuffLoop.MasterVolume = master;
            if (was != active)
                loop.chuffLoop.Stop();
            if (!active)
                return true;
            loop.chuffLoop.SetVolume(drifting ? player.breath * Voice.LoudestGauge : Voice.LoopLevel(volumeInput + 1f));
            loop.chuffLoop.SetPitch(drifting ? beatsPerSecond * Voice.BreathPitch : beatsPerSecond);
            return true;
        }

        private void Beat(float chuffIndex)
        {
            var exhaust = ExhaustPort(reader).Value;
            var beats = Mathf.Abs(FrequencyPort(reader).Value);
            breath = Breath();
            UpdateLoops(reader);
            var config = reader.regularChuffConfig;
            var rise = beats < StockRiseEnds ? config.chuffFrequencyToMasterVolume.Evaluate(beats) : 1f;
            var master = rise * Voice.BeatShare(beats);
            var clips = reader.lowPressureClips;
            if (master > 0f && clips != null && clips.Length > 0)
            {
                var index = Mathf.Clamp(Mathf.RoundToInt(chuffIndex), 0, clips.Length - 1);
                var beat = character[index % character.Length];
                var late = beats > 0.1f ? Mathf.Min(LatestBeat, beat.Lateness / beats) : 0f;
                var pitch = config.pitch * beat.Pitch * Voice.Sharpen(beats) * Random.Range(0.985f, 1.015f);
                if (exhaust > 1f && SynthHeard)
                {
                    // The synthesizer is sounding this beat.
                }
                else if (exhaust > 1f)
                {
                    var loudness = Voice.Loudness(exhaust) * master * beat.Volume * Volume * Random.Range(0.95f, 1.05f);
                    var tiers = Voice.Tiers(exhaust, reader.mediumPressureThreshold, reader.highPressureThreshold);
                    var soft = Clip(reader.lowPressureClips, index);
                    var medium = Clip(reader.mediumPressureClips, index);
                    var hard = Clip(reader.highPressureClips, index);
                    space.Play(soft, loudness * tiers.Soft, pitch, late);
                    space.Play(medium, loudness * tiers.Medium, pitch, late);
                    space.Play(hard, loudness * tiers.Hard, pitch, late);
                    var loudest = tiers.Hard >= tiers.Medium && tiers.Hard >= tiers.Soft ? hard : tiers.Medium >= tiers.Soft ? medium : soft;
                    space.Echo(loudest, loudness, pitch, late);
                }
                else if (breath > 0f)
                {
                    var panting = breath * master * beat.Volume * Volume;
                    var soft = Clip(reader.lowPressureClips, index);
                    space.Play(soft, panting, pitch * Voice.BreathPitch, late);
                    space.Echo(soft, panting, pitch * Voice.BreathPitch, late);
                }
            }
            WaterAndAsh(beats);
        }

        // The panting of a drifting engine: as hard as its cylinders pump (the drifting drag's mean pressure).
        private float Breath()
        {
            var state = EngineDrift.Read(car, engine);
            if (!state.Drifting)
                return 0f;
            var mep = Drifting.Mep(state, EngineDrift.Cutoff(engine), Drifting.PistonSpeed(engine.pistonStroke, engine.crankRpm.Value / 60f));
            return Voice.Breath(mep, state.SniftingOpen);
        }

        private static AudioClip Clip(ChuffClipsSimReader.OrderedChuffClips[] sets, int index)
        {
            var clips = sets != null && index < sets.Length && sets[index] != null ? sets[index].chuffVariations : null;
            return clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;
        }

        // The water and ash chuffs, as the game plays them (ChuffClipsSimReader.OnChuff).
        private void WaterAndAsh(float beats)
        {
            var frequency = FrequencyPort(reader).Value;
            var water = WaterPort(reader);
            if (water != null && water.Value > 0f && reader.waterChuffClips != null && reader.waterChuffClips.Length > 0)
            {
                var config = reader.waterChuffConfig;
                var level = config.chuffFrequencyToMasterVolume.Evaluate(frequency);
                var input = frequency > 0.1f ? Mathf.Lerp(0.5f, 1f, water.Value) * Mathf.Lerp(1f, 0.5f, CocksPort(reader).Value) : 0f;
                if (level > 0f)
                    reader.waterChuffClips.Play(config.parent.position, input * level, config.pitch, config.spread, config.minDistance, config.maxDistance,
                        config.customCurves, config.mixerGroup, config.parent);
            }
            var ash = AshPort(reader).Value;
            if (ash > 0f && reader.ashChuffClips != null && reader.ashChuffClips.Length > 0)
            {
                var config = reader.ashChuffConfig;
                var level = config.chuffFrequencyToMasterVolume.Evaluate(frequency);
                if (level > 0f)
                    reader.ashChuffClips.Play(config.parent.position, ash * level, config.pitch, config.spread, config.minDistance, config.maxDistance,
                        config.customCurves, config.mixerGroup, config.parent);
            }
        }

        // The car's ID (saved with it), so an engine sounds the same each session.
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

        private static string ComponentOf(string portId)
        {
            var dot = portId != null ? portId.IndexOf('.') : -1;
            return dot > 0 ? portId.Substring(0, dot) : portId;
        }
    }

    [HarmonyPatch(typeof(ChuffClipsSimReader))]
    internal static class ChuffPlayerPatches
    {
        [HarmonyPatch(nameof(ChuffClipsSimReader.Init))]
        [HarmonyPostfix]
        private static void AfterInit(ChuffClipsSimReader __instance, TrainCar car, SimulationFlow simFlow) =>
            SetupGuard.Logged(car, "chuffs", () => ChuffPlayer.Attach(__instance, car, simFlow));

        [HarmonyPatch(nameof(ChuffClipsSimReader.Deinit))]
        [HarmonyPrefix]
        private static void BeforeDeinit(ChuffClipsSimReader __instance) => ChuffPlayer.Detach(__instance);

        [HarmonyPatch("OnChuff")]
        [HarmonyPrefix]
        private static bool BeforeChuff(ChuffClipsSimReader __instance, float chuffIndexFloat)
        {
            try
            {
                return !ChuffPlayer.TryBeat(__instance, chuffIndexFloat);
            }
            catch (Exception e)
            {
                Faults.Report("the chuffs", null, e);
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(ChuffClipsSimReader.ChuffLoop), nameof(ChuffClipsSimReader.ChuffLoop.UpdateLoops))]
    internal static class ChuffLoopPatch
    {
        private static bool Prefix(ChuffClipsSimReader.ChuffLoop __instance, float volumeInput, float chuffFrequency, ref bool __result)
        {
            try
            {
                if (!ChuffPlayer.TryLoop(__instance, volumeInput, chuffFrequency, out var active))
                    return true;
                __result = active;
                return false;
            }
            catch (Exception e)
            {
                Faults.Report("the chuffs' loops", null, e);
                return true;
            }
        }
    }
}
#endif
