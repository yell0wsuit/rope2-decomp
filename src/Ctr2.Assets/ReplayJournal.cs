using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Ctr2.Assets
{
    /// <summary>Lightweight journal event types, as the original recorder numbers them.</summary>
    public enum JournalEventType
    {
        /// <summary>Unknown or absent type.</summary>
        None = 0,
        /// <summary>Raw primary touch press.</summary>
        TouchDown = 1,
        /// <summary>Raw primary touch movement.</summary>
        TouchMove = 2,
        /// <summary>Raw primary touch release.</summary>
        TouchUp = 3,
        /// <summary>Hint pause showing a tap; no gameplay effect.</summary>
        PauseTap = 4,
        /// <summary>Hint pause showing a swipe; no gameplay effect.</summary>
        PauseSwipe = 5,
        /// <summary>Exact rope cut: journal rope index, segment and crossing.</summary>
        RopeCut = 6,
        /// <summary>Recorded star catch by journal star index.</summary>
        StarCatch = 7,
        /// <summary>Recorded fruit catch by journal fruit index.</summary>
        FruitCatch = 8
    }

    /// <summary>One recorded event; absent fields read zero.</summary>
    /// <param name="Frame">Completed steps when the event was recorded; it applies before the next step.</param>
    /// <param name="Type">Event type.</param>
    /// <param name="X">Logical x, for touches and cuts.</param>
    /// <param name="Y">Logical y, for touches and cuts.</param>
    /// <param name="Angle">Hint swipe angle.</param>
    /// <param name="Index">Journal object index, for cuts and catches.</param>
    /// <param name="Segment">Rope segment, for cuts.</param>
    public sealed record JournalEvent(int Frame, JournalEventType Type, float X, float Y, float Angle, int Index, int Segment);

    /// <summary>One recorded body state: origin and angle, plus velocities on full syncs.</summary>
    /// <param name="Index">Journal object index.</param>
    /// <param name="X">Body origin x in meters, if recorded.</param>
    /// <param name="Y">Body origin y in meters, if recorded.</param>
    /// <param name="Angle">Body angle in radians, if recorded.</param>
    /// <param name="VelocityX">Linear velocity x, if recorded.</param>
    /// <param name="VelocityY">Linear velocity y, if recorded.</param>
    /// <param name="AngularVelocity">Angular velocity, if recorded.</param>
    public sealed record JournalObjectState(int Index, float? X, float? Y, float? Angle, float? VelocityX, float? VelocityY, float? AngularVelocity);

    /// <summary>The recorded states after one step.</summary>
    /// <param name="Frame">Completed steps.</param>
    /// <param name="Objects">States in recording order.</param>
    public sealed record JournalSnapshot(int Frame, IReadOnlyList<JournalObjectState> Objects);

    /// <summary>An original lightweight replay journal: a developer hint playthrough.</summary>
    /// <param name="LevelHash">Hash of the level's object layer the journal was recorded on.</param>
    /// <param name="FormatVersion">Recorder format version.</param>
    /// <param name="Events">Events in recording order.</param>
    /// <param name="Snapshots">Snapshots in recording order.</param>
    public sealed record ReplayJournal(string LevelHash, int FormatVersion, IReadOnlyList<JournalEvent> Events, IReadOnlyList<JournalSnapshot> Snapshots)
    {
        private const int Limit = 16 * 1024 * 1024;

        /// <summary>Reads a bounded protobuf journal.</summary>
        /// <param name="data">Journal bytes.</param>
        /// <returns>The parsed journal.</returns>
        /// <exception cref="InvalidDataException">The data is malformed or too large.</exception>
        public static ReplayJournal Read(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length > Limit)
            {
                throw new InvalidDataException("Replay journal is too large.");
            }
            string hash = "";
            int version = 0;
            List<JournalEvent> events = [];
            List<JournalSnapshot> snapshots = [];
            foreach (Field field in Fields(data))
            {
                switch (field.Number)
                {
                    case 1:
                        hash = Encoding.UTF8.GetString(field.Bytes!);
                        break;
                    case 2:
                        version = (int)field.Varint;
                        break;
                    case 3:
                        snapshots.Add(Snapshot(field.Bytes!));
                        break;
                    case 4:
                        events.Add(Event(field.Bytes!));
                        break;
                    default:
                        break;
                }
            }
            return new(hash, version, events.AsReadOnly(), snapshots.AsReadOnly());
        }

        /// <summary>Computes the original level hash: MD5 of a breadth-first walk of the object layer.</summary>
        /// <param name="xml">Level XML, optionally preceded by a bracketed hash line.</param>
        /// <returns>The lowercase hexadecimal hash.</returns>
        /// <remarks>The walk starts from the first layer named <c>Objects</c> or <c>Layer 1</c> and appends each node's name, text,
        /// then its attribute names and values in reverse document order, as the original dictionary yields them.</remarks>
        public static string ComputeLevelHash(string xml)
        {
            ArgumentNullException.ThrowIfNull(xml);
            if (xml.StartsWith('['))
            {
                int end = xml.IndexOf(']', StringComparison.Ordinal);
                xml = end > 0 ? xml[(end + 1)..] : xml;
            }
            using StringReader reader = new(xml.Trim());
            using XmlReader xmlReader = XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = Limit });
            XDocument document = XDocument.Load(xmlReader, LoadOptions.None);
            XElement? layer = document.Root?.Elements("layer").FirstOrDefault(e => (string?)e.Attribute("name") is "Objects" or "Layer 1");
            StringBuilder text = new("old_style_level");
            if (layer != null)
            {
                Queue<XElement> queue = new([layer]);
                while (queue.TryDequeue(out XElement? node))
                {
                    _ = text.Append(node.Name.LocalName);
                    string data = string.Concat(node.Nodes().OfType<XText>().Select(t => t.Value));
                    if (!string.IsNullOrWhiteSpace(data))
                    {
                        _ = text.Append(data);
                    }
                    XAttribute[] attributes = [.. node.Attributes().Reverse()];
                    foreach (XAttribute attribute in attributes)
                    {
                        _ = text.Append(attribute.Name.LocalName);
                    }
                    foreach (XAttribute attribute in attributes)
                    {
                        _ = text.Append(attribute.Value);
                    }
                    foreach (XElement child in node.Elements())
                    {
                        queue.Enqueue(child);
                    }
                }
            }
            return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static JournalSnapshot Snapshot(byte[] data)
        {
            int frame = 0;
            List<JournalObjectState> objects = [];
            foreach (Field field in Fields(data))
            {
                if (field.Number == 1)
                {
                    frame = (int)field.Varint;
                }
                else if (field.Number == 2)
                {
                    int index = 0;
                    float? x = null, y = null, angle = null, vx = null, vy = null, vang = null;
                    foreach (Field state in Fields(field.Bytes!))
                    {
                        if (state.Number == 1)
                        {
                            index = (int)state.Varint;
                        }
                        else if (state.Number == 2)
                        {
                            foreach (Field value in Fields(state.Bytes!))
                            {
                                switch (value.Number)
                                {
                                    case 1:
                                        x = value.Float;
                                        break;
                                    case 2:
                                        y = value.Float;
                                        break;
                                    case 3:
                                        angle = value.Float;
                                        break;
                                    case 4:
                                        vx = value.Float;
                                        break;
                                    case 5:
                                        vy = value.Float;
                                        break;
                                    case 6:
                                        vang = value.Float;
                                        break;
                                    default:
                                        break;
                                }
                            }
                        }
                    }
                    objects.Add(new(index, x, y, angle, vx, vy, vang));
                }
            }
            return new(frame, objects.AsReadOnly());
        }

        private static JournalEvent Event(byte[] data)
        {
            int frame = 0, type = 0, index = 0, segment = 0;
            float x = 0, y = 0, angle = 0;
            foreach (Field field in Fields(data))
            {
                switch (field.Number)
                {
                    case 1:
                        frame = (int)field.Varint;
                        break;
                    case 2:
                        type = (int)field.Varint;
                        break;
                    case 3:
                        x = field.Float;
                        break;
                    case 4:
                        y = field.Float;
                        break;
                    case 5:
                        angle = field.Float;
                        break;
                    case 6:
                        index = (int)field.Varint;
                        break;
                    case 7:
                        segment = (int)field.Varint;
                        break;
                    default:
                        break;
                }
            }
            return new(frame, Enum.IsDefined((JournalEventType)type) ? (JournalEventType)type : JournalEventType.None, x, y, angle, index, segment);
        }

        private readonly record struct Field(int Number, ulong Varint, float Float, byte[]? Bytes);

        private static IEnumerable<Field> Fields(byte[] data)
        {
            int i = 0;
            while (i < data.Length)
            {
                ulong key = ReadVarint(data, ref i);
                int number = (int)(key >> 3);
                switch ((int)(key & 7))
                {
                    case 0:
                        yield return new(number, ReadVarint(data, ref i), 0, null);
                        break;
                    case 1:
                        Need(data, i, 8);
                        i += 8;
                        break;
                    case 2:
                        ulong length = ReadVarint(data, ref i);
                        Need(data, i, length);
                        byte[] bytes = data[i..(i + (int)length)];
                        i += (int)length;
                        yield return new(number, 0, 0, bytes);
                        break;
                    case 5:
                        Need(data, i, 4);
                        float value = BitConverter.ToSingle(data, i);
                        i += 4;
                        yield return new(number, 0, value, null);
                        break;
                    default:
                        throw new InvalidDataException("Unsupported replay wire type.");
                }
            }
        }

        private static void Need(byte[] data, int offset, ulong length)
        {
            if (length > (ulong)(data.Length - offset))
            {
                throw new InvalidDataException("Truncated replay journal.");
            }
        }

        private static ulong ReadVarint(byte[] data, ref int i)
        {
            ulong result = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (i >= data.Length)
                {
                    throw new InvalidDataException("Truncated replay varint.");
                }
                byte b = data[i++];
                result |= (ulong)(b & 0x7f) << shift;
                if (b < 0x80)
                {
                    return result;
                }
            }
            throw new InvalidDataException("Overlong replay varint.");
        }
    }
}
