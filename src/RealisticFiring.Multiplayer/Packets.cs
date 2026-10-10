using System.IO;
using MPAPI.Interfaces.Packets;

namespace RealisticFiring.Multiplayer
{
    /// <summary>A joining client's mod version and the settings that shape its locos.</summary>
    public sealed class HelloPacket : ISerializablePacket
    {
        public string Version = "";
        public string Shape = "";

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Version ?? "");
            writer.Write(Shape ?? "");
        }

        public void Deserialize(BinaryReader reader)
        {
            Version = reader.ReadString();
            Shape = reader.ReadString();
        }
    }

    /// <summary>Something a client does to a loco's fire, for the host to carry out.</summary>
    public sealed class FireActionPacket : ISerializablePacket
    {
        public ushort Car;
        public byte Action;
        public sbyte Zone = -1;
        public sbyte Direction = -1;
        public float Value;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Car);
            writer.Write(Action);
            writer.Write(Zone);
            writer.Write(Direction);
            writer.Write(Value);
        }

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            Action = reader.ReadByte();
            Zone = reader.ReadSByte();
            Direction = reader.ReadSByte();
            Value = reader.ReadSingle();
        }
    }

    /// <summary>A notice from the host for one player, shown under the crosshair.</summary>
    public sealed class NoticePacket : ISerializablePacket
    {
        public string Text = "";

        public void Serialize(BinaryWriter writer) => writer.Write(Text ?? "");

        public void Deserialize(BinaryReader reader) => Text = reader.ReadString();
    }

    /// <summary>The host's state of a loco's fire and boiler fittings, for clients' views of it.</summary>
    public sealed class SnapshotPacket : ISerializablePacket
    {
        public ushort Car;
        public byte[] Data = new byte[0];

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Car);
            writer.Write(Data?.Length ?? 0);
            if (Data != null)
                writer.Write(Data);
        }

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            Data = reader.ReadBytes(reader.ReadInt32());
        }
    }

    /// <summary>Something the host's fire did, for the players near the loco.</summary>
    public sealed class FireEventPacket : ISerializablePacket
    {
        public ushort Car;
        public byte Event;
        public float Value;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Car);
            writer.Write(Event);
            writer.Write(Value);
        }

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            Event = reader.ReadByte();
            Value = reader.ReadSingle();
        }
    }
}
