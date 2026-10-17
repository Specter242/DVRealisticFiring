using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RealisticFiring.Core;

namespace RealisticFiring.Sim
{
 internal static class ClientMirror
    {
        private const byte Firebox = 1;
        private const byte Injector = 2;
        private const byte Water = 3;
        private const byte Booster = 4;
        private const byte Oil = 5;
        private const byte InjectorCircuits = 6;

        /// <summary>
        /// Every car with something of the mod's to mirror: each part that
        /// captures a section, so a loco with only boiler water or a booster
        /// (the fireboxes or injectors turned off) is not missed.
        /// </summary>
        public static IEnumerable<TrainCar> Cars() =>
            FireboxRegistry.All.Select(f => f.Car)
                .Concat(InjectorRig.All.Select(r => r.Car))
                .Concat(BoilerWaterRig.All.Select(w => w.Car))
                .Concat(RealisticBooster.All.Select(b => b.Car))
                .Concat(RealisticOilFirebox.All.Select(o => o.Car))
                .Where(car => car != null)
                .Distinct();

        /// <summary>On the host: the car's state for clients; null with nothing to send.</summary>
        public static byte[] Capture(TrainCar car)
        {
            FireboxRegistry.TryGet(car, out var firebox);
            InjectorRig.TryGet(car, out var rig);
            BoilerWaterRig.TryGet(car, out var water);
            RealisticBooster.TryGet(car, out var booster);
            RealisticOilFirebox.TryGet(car, out var oil);
            return Capture(firebox, rig, water, booster, oil);
        }

        /// <summary>On a client: shows the host's state of <paramref name="car"/>.</summary>
        public static void Apply(TrainCar car, byte[] data)
        {
            if (car == null)
                return;
            FireboxRegistry.TryGet(car, out var firebox);
            InjectorRig.TryGet(car, out var rig);
            BoilerWaterRig.TryGet(car, out var water);
            RealisticBooster.TryGet(car, out var booster);
            RealisticOilFirebox.TryGet(car, out var oil);
            Apply(data, firebox, rig, water, booster, oil, car.ID);
        }

        /// <summary>The state of these parts (any may be null); null with nothing to send.</summary>
        internal static byte[] Capture(RealisticFirebox firebox, InjectorRig rig, BoilerWaterRig water, RealisticBooster booster, RealisticOilFirebox oil)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                if (firebox != null)
                    Section(writer, Firebox, w =>
                    {
                        var model = firebox.Model;
                        w.Write(model.FireOn);
                        foreach (var value in model.SaveZones())
                            w.Write(value);
                        w.Write(model.Ashpan);
                        w.Write(firebox.LastStoker.HasValue);
                        if (firebox.LastStoker is StokerFlow flow)
                            Codec<StokerFlow>.Write(w, flow);
                        w.Write(firebox.StokerEngineSpeed);
                    });
                if (rig != null)
                {
                    Section(writer, Injector, w =>
                    {
                        Codec<InjectorFlow>.Write(w, rig.Flow);
                        Codec<FeedPumpFlow>.Write(w, rig.PumpFlow);
                        w.Write(rig.HoseFilling);
                        w.Write(rig.HandPumping || rig.RemoteHandPumping);
                        w.Write(rig.FillRate);
                        w.Write(rig.HoseIn);
                        w.Write(rig.HoseOpening);
                        w.Write(rig.HoseHot);
                        w.Write(rig.HoseSteam);
                        w.Write(rig.SteamRate);
                        w.Write(rig.Steaming);
                        w.Write(rig.SteamFull);
                    });
                    if (rig.Circuits.Count > 1)
                        Section(writer, InjectorCircuits, w =>
                        {
                            w.Write((byte)rig.Circuits.Count);
                            foreach (var circuit in rig.Circuits)
                            {
                                w.Write(circuit.Control.id);
                                Codec<InjectorFlow>.Write(w, circuit.Flow);
                            }
                        });
                }
                if (water != null)
                    Section(writer, Water, w =>
                    {
                        w.Write(water.PlugVent);
                        w.Write(water.Plug != null && water.Plug.Melted);
                        w.Write(water.Alarm != null && water.Alarm.Sounding);
                    });
                if (booster != null)
                    Section(writer, Booster, w =>
                    {
                        Codec<BoosterFlow>.Write(w, booster.Flow);
                        w.Write(booster.Clashes);
                        w.Write(booster.Model.Warmth);
                    });
                if (oil != null)
                    Section(writer, Oil, w =>
                    {
                        Codec<OilFireState>.Write(w, oil.Model.Snapshot());
                        w.Write(oil.FlarebackSmoke);
                    });
                writer.Flush();
                return stream.Length > 0 ? stream.ToArray() : null;
            }
        }

        /// <summary>Shows captured state on these parts (any may be null).</summary>
        internal static void Apply(byte[] data, RealisticFirebox firebox, InjectorRig rig, BoilerWaterRig water, RealisticBooster booster,
            RealisticOilFirebox oil, string carName = null)
        {
            if (data == null)
                return;
            const int header = 3;
            var at = 0;
            while (at < data.Length)
            {
                if (data.Length - at < header)
                {
                    Main.Mod?.Logger.Warning($"{carName}: the host's state ends in a broken section header");
                    return;
                }
                var tag = data[at];
                var length = data[at + 1] | data[at + 2] << 8;
                at += header;
                if (length > data.Length - at)
                {
                    Main.Mod?.Logger.Warning($"{carName}: the host's state section {tag} runs past the end");
                    return;
                }
                try
                {
                    using (var reader = new BinaryReader(new MemoryStream(data, at, length, writable: false)))
                        ApplySection(tag, reader, firebox, rig, water, booster, oil);
                }
                catch (Exception e) when (e is EndOfStreamException || e is IOException || e is InvalidDataException)
                {
                    Main.Mod?.Logger.Warning($"{carName}: the host's state section {tag} did not read");
                }
                at += length;
            }
        }

        private static void ApplySection(byte tag, BinaryReader r, RealisticFirebox firebox, InjectorRig rig, BoilerWaterRig water,
            RealisticBooster booster, RealisticOilFirebox oil)
        {
            switch (tag)
            {
                // Each section is read whole before any of it is shown.
                case Firebox when firebox != null:
                {
                    var fireOn = r.ReadBoolean();
                    var zones = new float[GrateLayout.ZoneCount * 4];
                    for (var i = 0; i < zones.Length; i++)
                        zones[i] = Finite(r);
                    var ashpan = Finite(r);
                    var stoker = r.ReadBoolean() ? Codec<StokerFlow>.Read(r) : (StokerFlow?)null;
                    var speed = Finite(r);
                    firebox.Model.MirrorZones(fireOn, zones);
                    firebox.Model.RestoreAshpan(ashpan);
                    firebox.LastStoker = stoker;
                    firebox.StokerEngineSpeed = speed;
                    break;
                }
                case Injector when rig != null:
                {
                    var flow = Codec<InjectorFlow>.Read(r);
                    var pump = Codec<FeedPumpFlow>.Read(r);
                    var hose = r.ReadBoolean();
                    var pumping = r.ReadBoolean();
                    var fill = Finite(r);
                    var hoseIn = r.ReadBoolean();
                    var opening = Math.Max(0f, Math.Min(1f, Finite(r)));
                    var hot = r.ReadBoolean();
                    var steam = Math.Max(0f, Math.Min(1f, Finite(r)));
                    var steamRate = Math.Max(0f, Finite(r));
                    var steaming = r.ReadBoolean();
                    var steamFull = r.ReadBoolean();
                    rig.Flow = flow;
                    rig.PumpFlow = pump;
                    rig.HoseFilling = hose;
                    rig.HandPumping = pumping;
                    rig.FillRate = fill;
                    rig.HoseIn = hoseIn;
                    rig.HoseOpening = opening;
                    rig.HoseHot = hot;
                    rig.HoseSteam = steam;
                    rig.SteamRate = steamRate;
                    rig.Steaming = steaming;
                    rig.SteamFull = steamFull;
                    break;
                }
                case InjectorCircuits when rig != null:
                {
                    var count = r.ReadByte();
                    var flows = new List<(InjectorCircuit, InjectorFlow)>();
                    for (var i = 0; i < count; i++)
                        flows.Add((rig.CircuitFor(r.ReadString()), Codec<InjectorFlow>.Read(r)));
                    foreach (var (circuit, flow) in flows)
                        if (circuit != null)
                            circuit.Flow = flow;
                    break;
                }
                case Water when water != null:
                {
                    var vent = Finite(r);
                    var melted = r.ReadBoolean();
                    var sounding = r.ReadBoolean();
                    water.PlugVent = vent;
                    if (water.Plug != null && water.Plug.Melted != melted)
                        water.Plug.Restore(melted, water.Plug.CoreTemperature);
                    water.Alarm?.Mirror(sounding);
                    break;
                }
                case Booster when booster != null:
                {
                    var flow = Codec<BoosterFlow>.Read(r);
                    var clashes = r.ReadInt32();
                    var warmth = Finite(r);
                    booster.Flow = flow;
                    booster.Clashes = clashes;
                    booster.Model.MirrorWarmth(warmth);
                    break;
                }
                case Oil when oil != null:
                {
                    var state = Codec<OilFireState>.Read(r);
                    var smoke = Finite(r);
                    oil.Model.Mirror(state);
                    oil.MirrorFlarebackSmoke(smoke);
                    break;
                }
            }
        }

        // A number from the host that must be a number.
        private static float Finite(BinaryReader r)
        {
            var value = r.ReadSingle();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException("not a finite number");
            return value;
        }

        private static void Section(BinaryWriter writer, byte tag, Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var section = new BinaryWriter(stream))
            {
                write(section);
                section.Flush();
                writer.Write(tag);
                writer.Write((ushort)stream.Length);
                writer.Write(stream.ToArray());
            }
        }

        /// <summary>
        /// Writes and reads a struct of numbers, flags, and enums field by
        /// field, in declaration order; both sides run the same build.
        /// </summary>
        private static class Codec<T> where T : struct
        {
            private static readonly FieldInfo[] Fields = typeof(T)
                .GetFields(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(f => f.MetadataToken)
                .ToArray();

            public static void Write(BinaryWriter w, T value)
            {
                object boxed = value;
                foreach (var field in Fields)
                {
                    var type = field.FieldType;
                    var v = field.GetValue(boxed);
                    if (type == typeof(float))
                        w.Write((float)v);
                    else if (type == typeof(bool))
                        w.Write((bool)v);
                    else if (type == typeof(int))
                        w.Write((int)v);
                    else if (type.IsEnum)
                        w.Write(Convert.ToInt32(v));
                    else
                        throw new InvalidOperationException($"{typeof(T).Name}.{field.Name} is a {type.Name}, which the codec does not carry");
                }
            }

            public static T Read(BinaryReader r)
            {
                object boxed = default(T);
                foreach (var field in Fields)
                {
                    var type = field.FieldType;
                    if (type == typeof(float))
                        field.SetValue(boxed, Finite(r));
                    else if (type == typeof(bool))
                        field.SetValue(boxed, r.ReadBoolean());
                    else if (type == typeof(int))
                        field.SetValue(boxed, r.ReadInt32());
                    else if (type.IsEnum)
                    {
                        var raw = r.ReadInt32();
                        if (!Enum.IsDefined(type, raw))
                            throw new InvalidDataException($"no {type.Name} {raw}");
                        field.SetValue(boxed, Enum.ToObject(type, raw));
                    }
                    else
                        throw new InvalidDataException($"{typeof(T).Name}.{field.Name} is a {type.Name}, which the codec does not carry");
                }
                return (T)boxed;
            }
        }
    }
}
