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

        /// <summary>Every car with something of the mod's to mirror.</summary>
        public static IEnumerable<TrainCar> Cars() =>
            FireboxRegistry.All.Select(f => f.Car)
                .Concat(InjectorRig.All.Select(r => r.Car))
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
                    Section(writer, Injector, w =>
                    {
                        Codec<InjectorFlow>.Write(w, rig.Flow);
                        Codec<FeedPumpFlow>.Write(w, rig.PumpFlow);
                        w.Write(rig.HydrantFilling);
                        w.Write(rig.HandPumping || rig.RemoteHandPumping);
                        w.Write(rig.FillRate);
                    });
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
            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                while (reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    var tag = reader.ReadByte();
                    var length = reader.ReadUInt16();
                    var end = reader.BaseStream.Position + length;
                    try
                    {
                        ApplySection(tag, reader, firebox, rig, water, booster, oil);
                    }
                    catch (Exception e) when (e is EndOfStreamException || e is IOException)
                    {
                        Main.Mod?.Logger.Warning($"{carName}: the host's state section {tag} did not read");
                    }
                    reader.BaseStream.Position = end;
                }
            }
        }

        private static void ApplySection(byte tag, BinaryReader r, RealisticFirebox firebox, InjectorRig rig, BoilerWaterRig water,
            RealisticBooster booster, RealisticOilFirebox oil)
        {
            switch (tag)
            {
                case Firebox when firebox != null:
                {
                    var fireOn = r.ReadBoolean();
                    var zones = new float[GrateLayout.ZoneCount * 4];
                    for (var i = 0; i < zones.Length; i++)
                        zones[i] = r.ReadSingle();
                    var ashpan = r.ReadSingle();
                    firebox.Model.MirrorZones(fireOn, zones);
                    firebox.Model.RestoreAshpan(ashpan);
                    firebox.LastStoker = r.ReadBoolean() ? Codec<StokerFlow>.Read(r) : (StokerFlow?)null;
                    firebox.StokerEngineSpeed = r.ReadSingle();
                    break;
                }
                case Injector when rig != null:
                    rig.Flow = Codec<InjectorFlow>.Read(r);
                    rig.PumpFlow = Codec<FeedPumpFlow>.Read(r);
                    rig.HydrantFilling = r.ReadBoolean();
                    rig.HandPumping = r.ReadBoolean();
                    rig.FillRate = r.ReadSingle();
                    break;
                case Water when water != null:
                {
                    water.PlugVent = r.ReadSingle();
                    var melted = r.ReadBoolean();
                    var sounding = r.ReadBoolean();
                    if (water.Plug != null && water.Plug.Melted != melted)
                        water.Plug.Restore(melted, water.Plug.CoreTemperature);
                    water.Alarm?.Mirror(sounding);
                    break;
                }
                case Booster when booster != null:
                    booster.Flow = Codec<BoosterFlow>.Read(r);
                    booster.Clashes = r.ReadInt32();
                    booster.Model.MirrorWarmth(r.ReadSingle());
                    break;
                case Oil when oil != null:
                    oil.Model.Mirror(Codec<OilFireState>.Read(r));
                    oil.MirrorFlarebackSmoke(r.ReadSingle());
                    break;
            }
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
                }
            }

            public static T Read(BinaryReader r)
            {
                object boxed = default(T);
                foreach (var field in Fields)
                {
                    var type = field.FieldType;
                    if (type == typeof(float))
                        field.SetValue(boxed, r.ReadSingle());
                    else if (type == typeof(bool))
                        field.SetValue(boxed, r.ReadBoolean());
                    else if (type == typeof(int))
                        field.SetValue(boxed, r.ReadInt32());
                    else if (type.IsEnum)
                        field.SetValue(boxed, Enum.ToObject(type, r.ReadInt32()));
                }
                return (T)boxed;
            }
        }
    }
}
