using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

using Ctr2.Assets;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises executable metadata extraction with an entirely synthetic x86 PE image.</summary>
    public sealed class WindowsMetadataTests
    {
        /// <summary>Verifies ID lookup, float decoding, trim offsets, resolution, and original quad ordering.</summary>
        [Fact]
        public void ReadsImageAndArrayBuildersWithoutExecutingCode()
        {
            byte[] image = Fixture();
            ResourceEntry entry = Assert.Single(WindowsMetadata.ReadCore(image, new(0x402040, 1, 0x402000, 1), TestContext.Current.CancellationToken));
            Assert.Equal(0, entry.Id);
            Assert.Equal("synthetic_640_960.png", entry.Path);
            Assert.Equal(2, entry.Resolution);
            Assert.Equal(1f, entry.PixelsPerLogicalUnit);
            Assert.Equal(40, entry.PreCutWidth);
            Assert.Equal(60, entry.PreCutHeight);
            Assert.Equal(new ResourceQuad(1, 2, 3, 4, 5, 6), Assert.Single(entry.Quads));
        }

        /// <summary>Verifies counted loops and signed indexed displacements produce a complete ordered array.</summary>
        [Fact]
        public void DecodesBoundedIndexedLoops()
        {
            // xor esi,esi; call allocator; mov [eax],12; mov ecx,2;
            // loop: mov [edi+esi*4],1f; lea esi,[esi+1]; dec ecx; jne loop; ret
            byte[] code = [0x33, 0xf6, 0xe8, 0, 0, 0, 0, 0xc7, 0, 12, 0, 0, 0,
                0xb9, 2, 0, 0, 0, 0xc7, 4, 0xb7, 0, 0, 0x80, 0x3f,
                0x8d, 0x76, 1, 0x49, 0x75, 0xf3, 0xc3];
            Assert.Equal(new float[] { 1, 1 }, WindowsMetadata.ReadArray(code, 0x401000, 2));
        }

        /// <summary>Verifies array builders that copy an allocation result without an errno path.</summary>
        [Fact]
        public void DecodesDirectAllocationBuilder()
        {
            byte[] code = [0xe8, 0, 0, 0, 0, 0x8b, 0xf0, 0xc7, 6, 0, 0, 0x80, 0x3f, 0xc3];
            Assert.Equal(new float[] { 1 }, WindowsMetadata.ReadArray(code, 0x401000, 1));
        }

        /// <summary>Verifies incomplete array initialization fails rather than supplying zero-valued missing entries.</summary>
        [Fact]
        public void RejectsArrayGaps()
        {
            byte[] code = [0xe8, 0, 0, 0, 0, 0xc7, 0, 12, 0, 0, 0, 0xc7, 7, 0, 0, 0x80, 0x3f, 0xc3];
            _ = Assert.Throws<InvalidDataException>(() => WindowsMetadata.ReadArray(code, 0x401000, 2));
        }

        /// <summary>Verifies a looping builder cannot consume unbounded time.</summary>
        [Fact]
        public void RejectsUnboundedBuilderLoop()
        {
            byte[] code = [0xe8, 0, 0, 0, 0, 0xc7, 0, 12, 0, 0, 0, 0xeb, 0xfe, 0xc3];
            _ = Assert.Throws<InvalidDataException>(() => WindowsMetadata.ReadArray(code, 0x401000, 1));
        }

        /// <summary>Verifies a malformed PE cannot be treated as a resource table.</summary>
        [Fact]
        public void RejectsTruncatedImage()
        {
            _ = Assert.Throws<InvalidDataException>(() => WindowsMetadata.ReadCore([0x4d, 0x5a], new(0x402040, 1, 0x402000, 1), TestContext.Current.CancellationToken));
        }

        /// <summary>Verifies unknown executable bytes are rejected before profile offsets are used.</summary>
        [Fact]
        public void RejectsUnsupportedExecutable()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, Fixture());
                _ = Assert.Throws<InvalidDataException>(() => WindowsMetadata.Load(path, TestContext.Current.CancellationToken));
            }
            finally
            {
                File.Delete(path);
            }
        }

        /// <summary>Creates a minimal PE containing one resource, one initializer, and two float-array builders.</summary>
        /// <returns>Synthetic PE bytes with no original game data.</returns>
        private static byte[] Fixture()
        {
            byte[] image = new byte[0xa00];
            void U16(int offset, ushort value)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(offset), value);
            }

            void U32(int offset, uint value)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(offset), value);
            }

            U16(0, 0x5a4d);
            U32(0x3c, 0x80);
            U32(0x80, 0x4550);
            U16(0x84, 0x14c);
            U16(0x86, 3);
            U16(0x94, 0xe0);
            U16(0x98, 0x10b);
            U32(0xb4, 0x400000);
            U32(0xb8, 0x1000);
            U32(0xbc, 0x200);
            U32(0xd0, 0x4000);
            U32(0xd4, 0x200);
            for (int section = 0; section < 3; section++)
            {
                int header = 0x178 + (section * 40);
                Encoding.ASCII.GetBytes(section == 0 ? ".text" : section == 1 ? ".rdata" : ".data").CopyTo(image, header);
                U32(header + 8, 0x200);
                U32(header + 12, (uint)(section + 1) * 0x1000);
                U32(header + 16, 0x200);
                U32(header + 20, (uint)(section + 1) * 0x200);
                U32(header + 36, section == 0 ? 0x60000020u : 0xc0000040u);
            }
            U32(0x400, 0x403000);
            U32(0x404, 1);
            U32(0x440, 0x401000);
            Encoding.ASCII.GetBytes("synthetic_640_960.raw\0").CopyTo(image, 0x500);
            using MemoryStream code = new();
            using BinaryWriter writer = new(code);
            writer.Write((byte)0x68);
            writer.Write(0x402100u);
            writer.Write((byte)0xa3);
            writer.Write(0x403000u);
            (uint Offset, uint Value)[] globalFields = [(4u, 0u), (8u, 2u)];
            foreach ((uint offset, uint value) in globalFields)
            {
                writer.Write([0xc7, 5]);
                writer.Write(0x403000u + offset);
                writer.Write(value);
            }

            void Call(uint target)
            {
                writer.Write((byte)0xe8);
                writer.Write(unchecked(target - (0x401000u + (uint)code.Position + 4)));
            }

            Call(0x401100);
            writer.Write([0x8b, 0xf0]);
            Call(0x401180);
            (byte Offset, uint Value)[] imageFields = [(0, uint.MaxValue), (4, uint.MaxValue),
                (24, BitConverter.SingleToUInt32Bits(40)), (28, BitConverter.SingleToUInt32Bits(60)), (32, 1u)];
            foreach ((byte offset, uint value) in imageFields)
            {
                writer.Write((byte)0xc7);
                if (offset == 0)
                {
                    writer.Write((byte)7);
                }
                else
                {
                    writer.Write((byte)0x47);
                    writer.Write(offset);
                }
                writer.Write(value);
            }
            writer.Write([0x89, 0x77, 0x24, 0x89, 0x47, 0x28, 0xc3]);
            code.ToArray().CopyTo(image, 0x200);
            (int Offset, float[] Values)[] builders = [(0x300, [1, 2, 3, 4]), (0x380, [5, 6])];
            foreach ((int offset, float[] values) in builders)
            {
                using MemoryStream builder = new();
                using BinaryWriter array = new(builder);
                array.Write([0xe8, 0, 0, 0, 0, 0xc7, 0, 12, 0, 0, 0]);
                for (byte i = 0; i < values.Length; i++)
                {
                    array.Write((byte)0xc7);
                    if (i == 0)
                    {
                        array.Write((byte)7);
                    }
                    else
                    {
                        array.Write((byte)0x47);
                        array.Write((byte)(i * 4));
                    }
                    array.Write(values[i]);
                }
                array.Write((byte)0xc3);
                builder.ToArray().CopyTo(image, offset);
            }
            return image;
        }
    }
}
