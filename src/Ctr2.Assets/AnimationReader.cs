using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ctr2.Assets
{
    /// <summary>Reads the original protobuf-lite animation format without executing game code.</summary>
    public static class AnimationReader
    {
        /// <summary>Decodes an animation with bounded bytes, recursion, and field counts.</summary>
        /// <param name="bytes">Uncompressed .pb contents, at most 16 MiB.</param>
        /// <returns>The decoded element tree.</returns>
        /// <exception cref="InvalidDataException">The wire data, values, or resource limits are invalid.</exception>
        public static AnimationElement Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length is 0 or > (16 * 1024 * 1024))
            {
                throw new InvalidDataException("Animation size must be 1 byte to 16 MiB.");
            }
            int budget = 4_000_000;
            return Element(bytes, 0, ref budget);
        }

        /// <summary>Decodes one bounded element and its descendants.</summary>
        /// <param name="bytes">Element message.</param>
        /// <param name="depth">Current recursive depth.</param>
        /// <param name="budget">Shared remaining field budget.</param>
        /// <returns>The decoded element.</returns>
        private static AnimationElement Element(ReadOnlySpan<byte> bytes, int depth, ref int budget)
        {
            if (depth >= 64)
            {
                throw new InvalidDataException("Animation nesting exceeds 64 elements.");
            }
            Wire wire = new(bytes);
            int type = 0, source = 0, quad = 0, anchor = 9, parent = -1;
            float? width = null, height = null;
            string name = "";
            List<AnimationElement> children = [];
            List<AnimationTimeline> timelines = [];
            while (wire.Next(ref budget))
            {
                switch (wire.Field)
                {
                    case 1:
                        type = wire.Integer();
                        break;
                    case 2:
                        name = wire.Text();
                        break;
                    case 3:
                        source = wire.Integer();
                        break;
                    case 4:
                        quad = wire.Integer();
                        break;
                    case 5:
                        anchor = wire.Integer();
                        break;
                    case 6:
                        parent = wire.Integer();
                        break;
                    case 7:
                        width = wire.Float();
                        break;
                    case 8:
                        height = wire.Float();
                        break;
                    case 9:
                        children.Add(Element(wire.Message(), depth + 1, ref budget));
                        break;
                    case 10:
                        timelines.Add(Timeline(wire.Message(), ref budget));
                        break;
                    default:
                        wire.Skip();
                        break;
                }
            }
            return type is < 0 or > 2 || source < 0 || quad < 0 || width < 0 || height < 0 ||
                anchor is < 0 or > 127 || parent is < -1 or > 127 || timelines.Select(t => t.Id).Distinct().Count() != timelines.Count
                ? throw new InvalidDataException("Invalid animation element or duplicate scene ID.")
                : new(type, name, source, quad, anchor, parent, width, height, [.. children], [.. timelines]);
        }

        /// <summary>Decodes a timeline, preserving repeated serialized property tracks.</summary>
        /// <param name="bytes">Timeline message.</param>
        /// <param name="budget">Shared field budget.</param>
        /// <returns>The decoded timeline.</returns>
        private static AnimationTimeline Timeline(ReadOnlySpan<byte> bytes, ref int budget)
        {
            Wire wire = new(bytes);
            int id = 0;
            string name = "";
            bool empty = false;
            float frameTime = 0;
            List<AnimationTrack> tracks = [];
            while (wire.Next(ref budget))
            {
                switch (wire.Field)
                {
                    case 1:
                        id = wire.Integer();
                        break;
                    case 2:
                        name = wire.Text();
                        break;
                    case 3:
                        empty = wire.Integer() != 0;
                        break;
                    case 4:
                        frameTime = wire.Float();
                        break;
                    case 5:
                        tracks.Add(Track(wire.Message(), ref budget));
                        break;
                    default:
                        wire.Skip();
                        break;
                }
            }
            return id < 0 || frameTime < 0
                ? throw new InvalidDataException("Invalid animation timeline.")
                : new(id, name, empty, frameTime, [.. tracks]);
        }

        /// <summary>Decodes a serialized property track.</summary>
        /// <param name="bytes">Track message.</param>
        /// <param name="budget">Shared field budget.</param>
        /// <returns>The decoded track.</returns>
        private static AnimationTrack Track(ReadOnlySpan<byte> bytes, ref int budget)
        {
            Wire wire = new(bytes);
            int type = 0;
            List<AnimationKey> keys = [];
            double duration = 0;
            while (wire.Next(ref budget))
            {
                switch (wire.Field)
                {
                    case 1:
                        type = wire.Integer();
                        break;
                    case 2:
                        AnimationKey key = Key(wire.Message(), ref budget);
                        duration += key.Time;
                        keys.Add(key);
                        break;
                    default:
                        wire.Skip();
                        break;
                }
            }
            return type is < 0 or > 4 || duration > 86_400
                ? throw new InvalidDataException("Unsupported animation track or duration over one day.")
                : new(type, [.. keys]);
        }

        /// <summary>Decodes a finite keyframe with validated action and transition IDs.</summary>
        /// <param name="bytes">Keyframe message.</param>
        /// <param name="budget">Shared field budget.</param>
        /// <returns>The decoded key.</returns>
        private static AnimationKey Key(ReadOnlySpan<byte> bytes, ref int budget)
        {
            Wire wire = new(bytes);
            float time = 0, p1 = 0, p2 = 0, p3 = 0, p4 = 0;
            int action = 0, transition = 0;
            while (wire.Next(ref budget))
            {
                switch (wire.Field)
                {
                    case 1:
                        time = wire.Float();
                        break;
                    case 2:
                        p1 = wire.Float();
                        break;
                    case 3:
                        p2 = wire.Float();
                        break;
                    case 4:
                        p3 = wire.Float();
                        break;
                    case 5:
                        p4 = wire.Float();
                        break;
                    case 6:
                        action = wire.Integer();
                        break;
                    case 7:
                        transition = wire.Integer();
                        break;
                    default:
                        wire.Skip();
                        break;
                }
            }
            return time < 0 || action is < 0 or > 4 || transition is < 0 or > 6
                ? throw new InvalidDataException("Invalid animation keyframe.")
                : new(time, p1, p2, p3, p4, action, transition);
        }

        /// <summary>Bounds-checks protobuf wire values within a single message.</summary>
        /// <param name="bytes">Message bytes.</param>
        private ref struct Wire(ReadOnlySpan<byte> bytes)
        {
            /// <summary>Unconsumed message bytes.</summary>
            private ReadOnlySpan<byte> remaining = bytes;
            /// <summary>Current field wire kind.</summary>
            private int kind;
            /// <summary>Gets the current positive field number.</summary>
            /// <value>The current protobuf field number.</value>
            public int Field
            {
                get; private set;
            }

            /// <summary>Reads the next field tag.</summary>
            /// <param name="budget">Shared remaining field count.</param>
            /// <returns>False at the message end.</returns>
            public bool Next(ref int budget)
            {
                if (remaining.IsEmpty)
                {
                    return false;
                }
                if (--budget < 0)
                {
                    throw new InvalidDataException("Animation field budget exceeded.");
                }
                ulong tag = Varint();
                if (tag > uint.MaxValue || tag >> 3 == 0)
                {
                    throw new InvalidDataException("Invalid protobuf tag.");
                }
                Field = (int)(tag >> 3);
                kind = (int)(tag & 7);
                return true;
            }

            /// <summary>Reads an int32 value, including sign-extended negatives.</summary>
            /// <returns>The signed value.</returns>
            public int Integer()
            {
                Require(0);
                ulong value = Varint();
                int result = unchecked((int)value);
                return value > int.MaxValue && value != unchecked((ulong)(long)result)
                    ? throw new InvalidDataException("Animation integer exceeds int32.")
                    : result;
            }

            /// <summary>Reads a finite fixed32 float.</summary>
            /// <returns>The decoded value.</returns>
            public float Float()
            {
                Require(5);
                float value = BinaryPrimitives.ReadSingleLittleEndian(Take(4));
                return float.IsFinite(value) ? value : throw new InvalidDataException("Nonfinite animation value.");
            }

            /// <summary>Reads a bounded length-delimited message.</summary>
            /// <returns>The message slice.</returns>
            public ReadOnlySpan<byte> Message()
            {
                Require(2);
                ulong length = Varint();
                return length <= (ulong)remaining.Length ? Take((int)length) : throw new InvalidDataException("Truncated protobuf message.");
            }

            /// <summary>Reads strictly valid UTF-8 text.</summary>
            /// <returns>The decoded text.</returns>
            public string Text()
            {
                try
                {
                    ReadOnlySpan<byte> text = Message();
                    return text.Length <= 4096 ? new UTF8Encoding(false, true).GetString(text) : throw new InvalidDataException("Animation name exceeds 4096 bytes.");
                }
                catch (DecoderFallbackException error)
                {
                    throw new InvalidDataException("Invalid animation UTF-8.", error);
                }
            }

            /// <summary>Skips an unknown scalar or length-delimited field.</summary>
            public void Skip()
            {
                switch (kind)
                {
                    case 0:
                        _ = Varint();
                        return;
                    case 1:
                        _ = Take(8);
                        return;
                    case 2:
                        _ = Message();
                        return;
                    case 5:
                        _ = Take(4);
                        return;
                    default:
                        throw new InvalidDataException("Unsupported protobuf wire type.");
                }
            }

            /// <summary>Requires a specific wire kind for a known field.</summary>
            /// <param name="expected">Expected wire kind.</param>
            private readonly void Require(int expected)
            {
                if (kind != expected)
                {
                    throw new InvalidDataException("Wrong protobuf wire type for animation field.");
                }
            }

            /// <summary>Consumes an exact byte count.</summary>
            /// <param name="count">Required byte count.</param>
            /// <returns>The consumed slice.</returns>
            private ReadOnlySpan<byte> Take(int count)
            {
                if (count > remaining.Length)
                {
                    throw new InvalidDataException("Truncated protobuf value.");
                }
                ReadOnlySpan<byte> result = remaining[..count];
                remaining = remaining[count..];
                return result;
            }

            /// <summary>Reads an unsigned varint in at most ten bytes.</summary>
            /// <returns>The decoded unsigned value.</returns>
            private ulong Varint()
            {
                ulong value = 0;
                for (int shift = 0; shift <= 63; shift += 7)
                {
                    byte b = Take(1)[0];
                    if (shift == 63 && b > 1)
                    {
                        throw new InvalidDataException("Overflowing protobuf varint.");
                    }
                    value |= (ulong)(b & 127) << shift;
                    if ((b & 128) == 0)
                    {
                        return value;
                    }
                }
                throw new InvalidDataException("Unterminated protobuf varint.");
            }
        }
    }
}
