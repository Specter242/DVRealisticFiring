using System.IO;
using MPAPI.Interfaces.Packets;
using MPAPI.Util;
using UnityEngine;

namespace RealisticFiring.Multiplayer
{
    /// <summary>
    /// The host's live session rules, sent reliably on join and whenever one
    /// changes: its display rule, and whether the lighter lights a cold coal
    /// bed (the host's sim decides; a guest's lighter follows it).
    /// </summary>
    public sealed class HudPolicyPacket : ISerializablePacket
    {
        public bool Hidden;
        public bool LighterLightsCoal;

        /// <summary>
        /// One more each time the host sends its rule (wrapping): the
        /// Multiplayer mod delivers reliably but not in order, so two quick
        /// changes could land reversed (audit 2026-11-09).
        /// </summary>
        public ushort Seq;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Hidden);
            writer.Write(Seq);
            writer.Write(LighterLightsCoal);
        }

        public void Deserialize(BinaryReader reader)
        {
            Hidden = reader.ReadBoolean();
            Seq = reader.ReadUInt16();
            LighterLightsCoal = reader.ReadBoolean();
        }
    }

    /// <summary>
    /// Whether the host's engines have their brick arches, over-fire jets,
    /// and the S282's stoker, feed pump, and booster (Upgrades.Fittings),
    /// sent reliably on join and whenever it changes: a client's own setting
    /// and save do not count in the host's game.
    /// </summary>
    public sealed class FittingsPacket : ISerializablePacket
    {
        public bool Arch;

        /// <summary>One more each time the host sends (wrapping), as <see cref="HudPolicyPacket.Seq"/>.</summary>
        public ushort Seq;

        /// <summary>Whether the host's engines have their over-fire jets.</summary>
        public bool Jets;

        /// <summary>Whether the host's S282s have their stoker.</summary>
        public bool Stoker;

        /// <summary>Whether the host's S282s have their feed pump.</summary>
        public bool FeedPump;

        /// <summary>Whether the host's S282s have their booster.</summary>
        public bool Booster;

        /// <summary>Whether the host's S282s and S060s have their snifting valves.</summary>
        public bool Snifting;

        /// <summary>Whether the host's S282s and S060s have their drifting valve.</summary>
        public bool DriftingValve;

        /// <summary>Whether the host's S282s and S060s have their by-pass valves.</summary>
        public bool ByPass;

        /// <summary>Whether the host's coal-fired engines have their automatic fire door.</summary>
        public bool AutoFireDoor;

        /// <summary>Whether the host upgrades locomotives individually rather than fleet-wide.</summary>
        public bool PerLocomotive;

        /// <summary>The host's wrapping <c>Upgrades.Fittings.Version</c>, for ordering against per-car mirror snapshots.</summary>
        public ushort HostVersion;

        /// <summary>Maximum per-car fitting entries carried in one packet.</summary>
        public const int MaxCarFittings = 512;

        /// <summary>When <see cref="PerLocomotive"/> is true: each spawned upgradable locomotive's network ID and effective upgrade mask.</summary>
        public (ushort Car, ushort Mask)[] CarFittings = new (ushort, ushort)[0];

        /// <summary>Whether the host's S282s are converted to oil (fleet-wide); written last, so an older reader stops before it.</summary>
        public bool OilBurner;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Arch);
            writer.Write(Seq);
            writer.Write(Jets);
            writer.Write(Stoker);
            writer.Write(FeedPump);
            writer.Write(Booster);
            writer.Write(Snifting);
            writer.Write(DriftingValve);
            writer.Write(ByPass);
            writer.Write(AutoFireDoor);
            writer.Write(PerLocomotive);
            writer.Write(HostVersion);
            var count = PerLocomotive && CarFittings != null ? System.Math.Min(CarFittings.Length, MaxCarFittings) : 0;
            writer.Write((ushort)count);
            for (var i = 0; i < count; i++)
            {
                writer.Write(CarFittings[i].Car);
                writer.Write(CarFittings[i].Mask);
            }
            writer.Write(OilBurner);
        }

        public void Deserialize(BinaryReader reader)
        {
            Arch = reader.ReadBoolean();
            Seq = reader.ReadUInt16();
            Jets = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            Stoker = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            FeedPump = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            Booster = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            Snifting = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            DriftingValve = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            ByPass = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            AutoFireDoor = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            PerLocomotive = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
            HostVersion = reader.BaseStream.Position + 2 <= reader.BaseStream.Length ? reader.ReadUInt16() : (ushort)0;
            if (reader.BaseStream.Position + 2 <= reader.BaseStream.Length)
            {
                var count = reader.ReadUInt16();
                if (count > MaxCarFittings)
                    count = 0;
                var list = new (ushort, ushort)[count];
                var read = 0;
                for (var i = 0; i < count && reader.BaseStream.Position + 4 <= reader.BaseStream.Length; i++)
                {
                    list[i] = (reader.ReadUInt16(), reader.ReadUInt16());
                    read++;
                }
                CarFittings = read == count ? list : new (ushort, ushort)[0];
            }
            else
            {
                CarFittings = new (ushort, ushort)[0];
            }
            OilBurner = reader.BaseStream.Position < reader.BaseStream.Length && reader.ReadBoolean();
        }
    }

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

        /// <summary>The most of each the host keeps, in characters: it shows them on its screen.</summary>
        public const int MaxVersion = 32;
        public const int MaxShape = 1024;

        public void Deserialize(BinaryReader reader)
        {
            Version = Clean(reader.ReadString(), MaxVersion);
            Shape = Clean(reader.ReadString(), MaxShape);
        }

        // A guest's text, cut to length and with nothing but printable
        // characters, before the host logs it or shows it.
        internal static string Clean(string text, int most)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            var kept = new System.Text.StringBuilder(System.Math.Min(text.Length, most));
            foreach (var c in text)
            {
                if (kept.Length >= most)
                    break;
                kept.Append(char.IsControl(c) ? ' ' : c);
            }
            return kept.ToString();
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

        /// <summary>
        /// One more for each action this client sends (wrapping), so the host
        /// can tell a hand pump's On from the Off that followed it when they
        /// land the other way round (audit 2026-11-09).
        /// </summary>
        public ushort Seq;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Car);
            writer.Write(Action);
            writer.Write(Zone);
            writer.Write(Direction);
            writer.Write(Value);
            writer.Write(Seq);
        }

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            Action = reader.ReadByte();
            Zone = reader.ReadSByte();
            Direction = reader.ReadSByte();
            Value = reader.ReadSingle();
            Seq = reader.ReadUInt16();
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

        /// <summary>The most a snapshot carries, in bytes; a loco's is a few hundred.</summary>
        public const int MaxData = 16 * 1024;

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            // A length out of range, or fewer bytes than it claims, is
            // dropped whole rather than read on into what follows.
            var length = reader.ReadInt32();
            var data = length >= 0 && length <= MaxData ? reader.ReadBytes(length) : null;
            Data = data != null && data.Length == length ? data : new byte[0];
        }
    }

    /// <summary>Something the host's fire did, for the players near the loco.</summary>
    /// <summary>
    /// An automatic fire door's air (<see cref="Core.AutoFireDoorAir"/>): a
    /// guest's to the host, and the host's or a guest's from the host to every
    /// player, who hears it unless it was their own (<see cref="Source"/>).
    /// <see cref="Seq"/> counts each player's, so a press and its release
    /// landing the other way round leave no hiss on.
    /// </summary>
    public sealed class DoorAirPacket : ISerializablePacket
    {
        public ushort Car;
        public byte Air;
        public ushort Seq;
        public byte Source;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Car);
            writer.Write(Air);
            writer.Write(Seq);
            writer.Write(Source);
        }

        public void Deserialize(BinaryReader reader)
        {
            Car = reader.ReadUInt16();
            Air = reader.ReadByte();
            Seq = reader.ReadUInt16();
            Source = reader.ReadByte();
        }
    }

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

 public sealed class StandPlugPacket : ISerializablePacket
    {
        /// <summary>The longest stand key read, in characters; a stand's is under 40.</summary>
        public const int MaxStand = 128;

        public string Stand = "";
        public byte Place;
        public byte Holder;
        public ushort Car;
        public Vector3 Position;
        public Quaternion Rotation = Quaternion.identity;
        public bool Settled;
        public bool PoseOnly;

        /// <summary>The sender's count of its word on this stand's plug (<c>PlugReport.Seq</c>).</summary>
        public ushort Seq;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Stand ?? "");
            writer.Write(Seq);
            writer.Write(Place);
            writer.Write(Holder);
            writer.Write(Car);
            writer.WriteVector3(Position);
            writer.WriteQuaternion(Rotation);
            writer.Write((byte)((Settled ? 1 : 0) | (PoseOnly ? 2 : 0)));
        }

        public void Deserialize(BinaryReader reader)
        {
            Stand = reader.ReadString();
            Seq = reader.ReadUInt16();
            Place = reader.ReadByte();
            Holder = reader.ReadByte();
            Car = reader.ReadUInt16();
            Position = reader.ReadVector3();
            Rotation = reader.ReadQuaternion();
            var flags = reader.ReadByte();
            Settled = (flags & 1) != 0;
            PoseOnly = (flags & 2) != 0;
        }
    }

    /// <summary>How far a shed stand's valve is open, 0 shut to 1, from a client to the host or from the host to clients.</summary>
    public sealed class StandValvePacket : ISerializablePacket
    {
        public string Stand = "";
        public float Value;

        /// <summary>The sender's count of its word on this stand's valve, as a plug's.</summary>
        public ushort Seq;

        public void Serialize(BinaryWriter writer)
        {
            writer.Write(Stand ?? "");
            writer.Write(Seq);
            writer.Write(Value);
        }

        public void Deserialize(BinaryReader reader)
        {
            Stand = reader.ReadString();
            Seq = reader.ReadUInt16();
            Value = reader.ReadSingle();
        }
    }
}
