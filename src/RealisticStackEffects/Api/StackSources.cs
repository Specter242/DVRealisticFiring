using System;
using System.Collections.Generic;
using LocoSim.Implementations;

namespace RealisticStackEffects.Api
{
    /// <summary>
    /// What a fire and its draft send up the stack in a frame, as another
    /// mod reports it to Realistic Stack Effects (<see cref="IStackFireSource"/>).
    /// Fields are only ever added, never changed or removed.
    /// </summary>
    public struct StackFireReading
    {
        public bool FireOn;

        /// <summary>The fire's heat and the most it makes, W.</summary>
        public float Heat;
        public float MostHeat;

        /// <summary>Visible smoke leaving the fire, 0 to 1: about 0.5 the blackest.</summary>
        public float Soot;

        /// <summary>The firebox's gas and a shovelful's thick smoke reaching the stack, in firebox puffs a second.</summary>
        public float FireboxGas;
        public float FireboxSmokeOut;

        /// <summary>How clear the stack is between beats, 0 to 1: 0 a clean fire's haze, 1 none (too much air).</summary>
        public float ClearStack;

        /// <summary>The air drawn through the fire, kg/s.</summary>
        public float AirThrough;

        /// <summary>Whether the fire throws cinders at all: an oil burner throws none.</summary>
        public bool Cinders;

        /// <summary>
        /// Cinders bound for the stack this frame, kg: a stoker's lost fines,
        /// which go out with the next beat, and a shaken grate's live coal,
        /// which goes out over the next few.
        /// </summary>
        public float Fines;
        public float ShakenCoal;

        /// <summary>A melted fusible plug's steam finding its way out of the stack, kg/s.</summary>
        public float PlugSteam;

        /// <summary>The share of the exhaust still water as it leaves, 0 to 0.3, or NaN to keep the last.</summary>
        public float ExhaustWetness;

        /// <summary>The steam going up the stack, kg/s: the exhaust's, the blower's, and any air pump's; and the blower's and pump's alone.</summary>
        public float StackSteam;
        public float BlowerSteam;

        /// <summary>The stack's natural draft as a share of the smokebox's stock total flow; NaN without a draft to read.</summary>
        public float NaturalFlow;
    }

    /// <summary>
    /// The steam around a locomotive other than its stack's, as Realistic
    /// Stack Effects draws it by the weather (the dynamo, cylinder cocks,
    /// safety valves, and so on), for another mod to add its own to: a
    /// booster's exhaust, an injector's overflow. From contract version 2.
    /// </summary>
    public interface IEngineSteam
    {
        /// <summary>
        /// Adds a source: steam of <paramref name="kind"/> leaving the engine
        /// at <paramref name="at"/> toward <paramref name="facing"/>, both in the
        /// car's own frame, as hard as <paramref name="strength"/> says each
        /// frame, 0 to 1. Kinds are named: "CylinderCock", "BoosterExhaust",
        /// "Overflow", "PumpExhaust", "Leak", "Gland", "Weep", "FittingWeep",
        /// "Dynamo", "Whistle", "SafetyValve", "Blowdown". False for a kind
        /// this version does not know. A source lasts as long as this object:
        /// ask <see cref="StackSources.EngineSteam"/> again now and then, and
        /// add again when it answers with another.
        /// </summary>
        bool Add(string name, string kind, UnityEngine.Vector3 at, UnityEngine.Vector3 facing, Func<float> strength);
    }

    /// <summary>Another mod's fire under a locomotive's chimney.</summary>
    public interface IStackFireSource
    {
        /// <summary>
        /// Reads this frame's fire and draft, <paramref name="dt"/> s on from
        /// the last, into <paramref name="reading"/>, which starts empty. The
        /// engine's exhaust is null where the plume found none.
        /// </summary>
        void Read(float dt, SteamExhaust exhaust, ref StackFireReading reading);

        /// <summary>Whether the player is on this engine or its tender.</summary>
        bool Tended { get; }
    }

    /// <summary>
    /// Lets another mod feed its own fire to Realistic Stack Effects' plumes
    /// in place of the game's firebox. Register while loading, before any
    /// locomotive spawns; list "RealisticStackEffects" in Info.json's
    /// LoadAfter so this mod has loaded first.
    /// </summary>
    public static class StackSources
    {
        private static readonly List<KeyValuePair<string, Func<TrainCar, IStackFireSource>>> Providers =
            new List<KeyValuePair<string, Func<TrainCar, IStackFireSource>>>();

        // Providers that threw once, left out from then on.
        private static readonly HashSet<string> Failed = new HashSet<string>();

        /// <summary>The contract's version: 2 (1 had the fires alone; 2 adds <see cref="EngineSteam"/>). It only grows; check it before using anything added later.</summary>
        public static int ApiVersion => 2;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SteamVents, Steam> Steams =
            new System.Runtime.CompilerServices.ConditionalWeakTable<SteamVents, Steam>();

        /// <summary>
        /// The steam around <paramref name="car"/> as this mod draws it now,
        /// to add sources to; null where it draws none (no plume of its own
        /// on that engine, or the player has it switched off). The same
        /// object for as long as the engine's steam is the same.
        /// </summary>
        public static IEngineSteam EngineSteam(TrainCar car)
        {
            if (car == null || !ChimneyPlume.TryGet(car, out var plume) || plume.Vents == null)
                return null;
            return Steams.GetValue(plume.Vents, vents => new Steam(vents));
        }

        private sealed class Steam : IEngineSteam
        {
            private readonly SteamVents vents;

            public Steam(SteamVents vents) => this.vents = vents;

            public bool Add(string name, string kind, UnityEngine.Vector3 at, UnityEngine.Vector3 facing, Func<float> strength)
            {
                if (strength == null || !Enum.TryParse(kind, false, out Core.VentKind parsed) || facing.sqrMagnitude < 1e-6f)
                    return false;
                var failed = false;
                // Another mod's function must not stop the plume: after one throw it reads as shut.
                vents.Add(name ?? kind, parsed, vents.Place(at, facing), () =>
                {
                    if (failed)
                        return 0f;
                    try
                    {
                        return strength();
                    }
                    catch (Exception e)
                    {
                        failed = true;
                        Main.Mod?.Logger.LogException($"A steam source '{name}' failed; it is left shut", e);
                        return 0f;
                    }
                });
                return true;
            }
        }

        /// <summary>
        /// Registers <paramref name="modId"/>'s provider. As each steam
        /// locomotive's smoke is set up, it is asked for that engine's fire,
        /// and answers null to leave the game's own. The first to answer wins.
        /// </summary>
        public static void Register(string modId, Func<TrainCar, IStackFireSource> provider)
        {
            if (string.IsNullOrEmpty(modId) || provider == null)
                throw new ArgumentException("A provider needs its mod's ID and a function");
            Providers.Add(new KeyValuePair<string, Func<TrainCar, IStackFireSource>>(modId, provider));
            Main.Mod?.Logger.Log($"{modId} feeds its fires to the chimney smoke");
        }

        /// <summary>
        /// The fire a registered provider answers with for <paramref name="car"/>,
        /// falling back to <paramref name="stock"/> should it fail; null when
        /// none answers.
        /// </summary>
        internal static IStackFire For(TrainCar car, Func<IStackFire> stock, out string by)
        {
            by = null;
            foreach (var pair in Providers)
            {
                if (Failed.Contains(pair.Key))
                    continue;
                try
                {
                    var source = pair.Value(car);
                    if (source == null)
                        continue;
                    by = pair.Key;
                    return new ExternalStack(pair.Key, source, stock);
                }
                catch (Exception e)
                {
                    Failed.Add(pair.Key);
                    Main.Mod?.Logger.LogException($"{pair.Key}'s fire for the chimney smoke failed; the game's own is read instead", e);
                }
            }
            return null;
        }

        // Another mod's fire as the plume reads it; the game's own firebox
        // once it fails.
        private sealed class ExternalStack : IStackFire
        {
            private readonly string modId;
            private readonly IStackFireSource source;
            private readonly Func<IStackFire> stock;
            private IStackFire fallback;

            public ExternalStack(string modId, IStackFireSource source, Func<IStackFire> stock)
            {
                this.modId = modId;
                this.source = source;
                this.stock = stock;
            }

            public bool Tended
            {
                get
                {
                    if (fallback != null)
                        return fallback.Tended;
                    try
                    {
                        return source.Tended;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }

            public void Read(float dt, SteamExhaust exhaust, ref StackReading reading)
            {
                if (fallback != null)
                {
                    fallback.Read(dt, exhaust, ref reading);
                    return;
                }
                var from = default(StackFireReading);
                try
                {
                    source.Read(dt, exhaust, ref from);
                }
                catch (Exception e)
                {
                    // Logged once: the game's firebox from here on.
                    Main.Mod?.Logger.LogException($"{modId}'s fire could not be read; its stack reads the game's firebox instead", e);
                    fallback = stock();
                    fallback.Read(dt, exhaust, ref reading);
                    return;
                }
                reading.FireOn = from.FireOn;
                reading.Heat = from.Heat;
                reading.MostHeat = from.MostHeat;
                reading.Soot = from.Soot;
                reading.FireboxGas = from.FireboxGas;
                reading.FireboxSmokeOut = from.FireboxSmokeOut;
                reading.ClearStack = from.ClearStack;
                reading.AirThrough = from.AirThrough;
                reading.Cinders = from.Cinders;
                reading.Fines = from.Fines;
                reading.ShakenCoal = from.ShakenCoal;
                reading.PlugSteam = from.PlugSteam;
                reading.ExhaustWetness = from.ExhaustWetness;
                reading.StackSteam = from.StackSteam;
                reading.BlowerSteam = from.BlowerSteam;
                reading.NaturalFlow = from.NaturalFlow;
            }
        }
    }
}
