using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ctr2.Assets
{
    /// <summary>Portable RGBA8 PNG encoding, with no native dependency in the import pipeline.</summary>
    public static class PngWriter
    {
        /// <summary>Writes a lossless, eight-bit RGBA PNG to a caller-owned stream.</summary>
        /// <param name="output">Writable destination stream, left open after encoding.</param>
        /// <param name="width">Positive image width in pixels.</param>
        /// <param name="height">Positive image height in pixels.</param>
        /// <param name="rgba">
        /// Tightly packed straight-alpha RGBA8 pixels, ordered from the top row to the bottom row.
        /// </param>
        /// <remarks>
        /// Each scanline uses filter zero. Output is written directly; callers needing atomic publication must
        /// stage the destination themselves.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Dimensions are not positive or the buffer length is not width times height times four.
        /// </exception>
        /// <exception cref="IOException">Writing the PNG to the stream fails.</exception>
        public static void Write(Stream output, int width, int height, ReadOnlySpan<byte> rgba)
        {
            if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            {
                throw new ArgumentException("RGBA length must match positive image dimensions.");
            }

            output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
            Span<byte> header = stackalloc byte[13];
            header.Clear();
            BinaryPrimitives.WriteInt32BigEndian(header, width);
            BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
            header[8] = 8;
            header[9] = 6;
            Chunk(output, "IHDR", header);
            using MemoryStream packed = new();
            using (ZLibStream zlib = new(packed, CompressionLevel.Optimal, true))
            {
                for (int row = 0; row < height; row++)
                {
                    zlib.WriteByte(0);
                    zlib.Write(rgba.Slice(row * width * 4, width * 4));
                }
            }

            Chunk(output, "IDAT", packed.GetBuffer().AsSpan(0, (int)packed.Length));
            Chunk(output, "IEND", []);
        }

        /// <summary>Writes a PNG chunk with its big-endian length and CRC-32.</summary>
        /// <param name="output">Writable destination stream, left open.</param>
        /// <param name="name">Four-character ASCII chunk type.</param>
        /// <param name="data">Chunk payload bytes; the type and payload are covered by the checksum.</param>
        private static void Chunk(Stream output, string name, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            output.Write(number);
            byte[] type = Encoding.ASCII.GetBytes(name);
            output.Write(type);
            output.Write(data);
            uint crc = uint.MaxValue;
            foreach (byte b in type)
            {
                crc = Update(crc, b);
            }

            foreach (byte b in data)
            {
                crc = Update(crc, b);
            }

            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
            output.Write(number);
        }

        /// <summary>Folds one byte into an uncomplemented PNG CRC-32 accumulator.</summary>
        /// <param name="crc">Running CRC, initially uint.MaxValue for a new chunk.</param>
        /// <param name="b">Next chunk type or payload byte.</param>
        /// <returns>Updated running CRC; the caller complements the final value before writing it.</returns>
        private static uint Update(uint crc, byte b)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            }

            return crc;
        }
    }
}
