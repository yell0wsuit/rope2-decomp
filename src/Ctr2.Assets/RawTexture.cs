using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Ctr2.Assets
{
    /// <summary>Stores decoded texture pixels without changing source row order.</summary>
    /// <param name="Width">Texture width in pixels.</param>
    /// <param name="Height">Texture height in pixels.</param>
    /// <param name="PixelFormat">Original format identifier, from 0 through 4.</param>
    /// <param name="Rgba">Tightly packed, straight-alpha RGBA8 bytes in source row order.</param>
    public sealed record RawTexture(int Width, int Height, byte PixelFormat, byte[] Rgba)
    {
        /// <summary>Maximum permitted compressed payload or decoded RGBA buffer size in bytes.</summary>
        private const int MaxBytes = 256 * 1024 * 1024;

        /// <summary>Decodes and validates the packed Windows RAW texture format.</summary>
        /// <param name="path">
        /// Base .raw file path; split payloads are read from .raw1, .raw2, and later continuations.
        /// </param>
        /// <returns>Decoded source dimensions, original format identifier, and straight RGBA8 pixels.</returns>
        /// <remarks>
        /// Validates the 15-byte little-endian header, declared sizes, zlib envelope, and Adler-32 checksum.
        /// Supports RGBA8888, RGB565, RGBA4444, RGB5A1, and A8; A8 expands to white RGB with source alpha. Row
        /// order is preserved, and density or quad metadata is not inferred.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        /// The header, format, payload size, compression, continuation files, or checksum is invalid.
        /// </exception>
        /// <exception cref="IOException">The base file or a continuation cannot be read.</exception>
        /// <exception cref="UnauthorizedAccessException">Read access to an input file is denied.</exception>
        public static RawTexture Load(string path)
        {
            using FileStream file = File.OpenRead(path);
            Span<byte> header = stackalloc byte[15];
            try
            {
                file.ReadExactly(header);
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated RAW header: " + path, e);
            }
            ushort marker = BinaryPrimitives.ReadUInt16LittleEndian(header);
            int width = BinaryPrimitives.ReadUInt16LittleEndian(header[2..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(header[4..]);
            byte format = header[6];
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(header[7..]);
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[11..]);
            int bpp = format switch
            {
                0 => 4,
                1 or 2 or 3 => 2,
                4 => 1,
                _ => 0
            };
            long pixels = (long)width * height;
            if (marker != 1981 || bpp == 0 || pixels == 0 || pixels * 4 > MaxBytes ||
                rawSize != pixels * bpp || compressedSize == 0 || compressedSize > MaxBytes)
            {
                throw new InvalidDataException($"Unsupported or invalid RAW header: {path} (marker {marker}, format {format}).");
            }

            byte[] compressed = new byte[(int)compressedSize];
            int used = ReadPart(file, compressed, 0);
            for (int part = 1; used < compressed.Length; part++)
            {
                string continuation = path + part;
                if (!File.Exists(continuation))
                {
                    throw new InvalidDataException("Missing RAW continuation: " + continuation);
                }

                using FileStream next = File.OpenRead(continuation);
                int count = ReadPart(next, compressed, used);
                if (count == 0)
                {
                    throw new InvalidDataException("Empty RAW continuation: " + continuation);
                }

                used += count;
            }
            if (compressed.Length < 6 || (compressed[0] & 15) != 8 || (compressed[0] >> 4) > 7 ||
                ((compressed[0] << 8) + compressed[1]) % 31 != 0 || (compressed[1] & 32) != 0)
            {
                throw new InvalidDataException("Invalid or unsupported zlib envelope: " + path);
            }

            byte[] raw = new byte[(int)rawSize];
            using MemoryStream payload = new(compressed, false);
            using ZLibStream zlib = new(payload, CompressionMode.Decompress);
            try
            {
                zlib.ReadExactly(raw);
                if (zlib.ReadByte() != -1)
                {
                    throw new InvalidDataException("RAW expands past declared size: " + path);
                }
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated RAW pixels: " + path, e);
            }
            // ZLibStream can return every pixel without requiring the checksum trailer.
            // Verify it ourselves so interrupted/corrupt dumps cannot become valid caches.
            uint a = 1, b = 0;
            foreach (byte value in raw)
            {
                a = (a + value) % 65521;
                b = (b + a) % 65521;
            }
            if (((b << 16) | a) != BinaryPrimitives.ReadUInt32BigEndian(compressed.AsSpan(compressed.Length - 4)))
            {
                throw new InvalidDataException("Missing or invalid zlib checksum: " + path);
            }

            byte[] rgba = new byte[checked((int)pixels * 4)];
            for (int i = 0; i < pixels; i++)
            {
                int dst = i * 4;
                if (format == 0)
                {
                    raw.AsSpan(dst, 4).CopyTo(rgba.AsSpan(dst));
                    continue;
                }
                if (format == 4)
                {
                    rgba[dst] = rgba[dst + 1] = rgba[dst + 2] = 255;
                    rgba[dst + 3] = raw[i];
                    continue;
                }
                int p = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(i * 2));
                if (format == 1)
                {
                    rgba[dst] = Expand5(p >> 11);
                    rgba[dst + 1] = Expand6((p >> 5) & 63);
                    rgba[dst + 2] = Expand5(p & 31);
                    rgba[dst + 3] = 255;
                }
                else if (format == 2)
                {
                    rgba[dst] = (byte)((p >> 12) * 17);
                    rgba[dst + 1] = (byte)(((p >> 8) & 15) * 17);
                    rgba[dst + 2] = (byte)(((p >> 4) & 15) * 17);
                    rgba[dst + 3] = (byte)((p & 15) * 17);
                }
                else
                {
                    rgba[dst] = Expand5(p >> 11);
                    rgba[dst + 1] = Expand5((p >> 6) & 31);
                    rgba[dst + 2] = Expand5((p >> 1) & 31);
                    rgba[dst + 3] = (byte)((p & 1) * 255);
                }
            }
            return new(width, height, format, rgba);
        }

        /// <summary>Fills the remaining compressed buffer from one source part and rejects surplus bytes.</summary>
        /// <param name="stream">Readable part stream, positioned at the start of its payload; left open.</param>
        /// <param name="target">Combined compressed payload buffer.</param>
        /// <param name="offset">First free buffer index.</param>
        /// <returns>The number of bytes copied before reaching EOF or buffer capacity.</returns>
        /// <exception cref="InvalidDataException">
        /// The source contains bytes beyond the remaining declared payload size.
        /// </exception>
        private static int ReadPart(Stream stream, byte[] target, int offset)
        {
            int read, total = 0;
            while (offset + total < target.Length && (read = stream.Read(target, offset + total, target.Length - offset - total)) > 0)
            {
                total += read;
            }

            return stream.ReadByte() != -1 ? throw new InvalidDataException("RAW payload exceeds declared compressed size.") : total;
        }

        /// <summary>Expands a 5-bit channel to eight bits by replicating its high bits.</summary>
        /// <param name="p">Unsigned channel value from 0 through 31.</param>
        /// <returns>The expanded channel value from 0 through 255.</returns>
        private static byte Expand5(int p)
        {
            return (byte)((p << 3) | (p >> 2));
        }

        /// <summary>Expands a 6-bit channel to eight bits by replicating its high bits.</summary>
        /// <param name="p">Unsigned channel value from 0 through 63.</param>
        /// <returns>The expanded channel value from 0 through 255.</returns>
        private static byte Expand6(int p)
        {
            return (byte)((p << 2) | (p >> 4));
        }
    }
}
