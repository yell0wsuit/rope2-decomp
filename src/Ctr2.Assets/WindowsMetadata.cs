using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Iced.Intel;

namespace Ctr2.Assets
{
    /// <summary>Locates the initializer pointer table and resource records of one verified executable layout.</summary>
    /// <param name="Initializers">Virtual address of the CRT initializer pointer table.</param>
    /// <param name="InitializerCount">Number of pointers to inspect.</param>
    /// <param name="Records">Virtual address of the eight-byte resource records.</param>
    /// <param name="ResourceCount">Number of resource records.</param>
    internal sealed record MetadataLayout(uint Initializers, int InitializerCount, uint Records, int ResourceCount);

    /// <summary>Reads Windows resource metadata statically without loading or executing the game executable.</summary>
    public static class WindowsMetadata
    {
        /// <summary>Extracts metadata from a supported Windows executable.</summary>
        /// <param name="path">Path to the user-supplied Game.exe.</param>
        /// <param name="cancellationToken">Cancellation checked between initializer functions.</param>
        /// <returns>A catalog bound to the executable profile and SHA-256.</returns>
        /// <exception cref="InvalidDataException">The executable version or metadata layout is unsupported.</exception>
        public static ResourceCatalog Load(string path, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > 64 * 1024 * 1024)
            {
                throw new InvalidDataException("Windows executable exceeds metadata size limit.");
            }
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (hash != "565f380c7fba556b5f23adc3b9ca2f693069c980a068f3b9a2c5475b3461008b")
            {
                throw new InvalidDataException("Unsupported Windows Game.exe for resource metadata: " + hash + ". Expected the verified x86 1.0.0.46 build.");
            }
            IReadOnlyList<ResourceEntry> resources = ReadCore(bytes, new(0x8e45fc, 3518, 0x8ee9e0, 383), cancellationToken);
            return resources.Count(r => r.Type == 0) != 256 || resources.Count(r => r.Type == 7) != 92
                ? throw new InvalidDataException("Windows metadata extraction coverage does not match the verified profile.")
                : new(1, "windows-x86-1.0.0.46", hash, resources);
        }

        /// <summary>Reads resource records and image geometry using explicit verified table bounds.</summary>
        /// <param name="bytes">Complete PE file bytes.</param>
        /// <param name="layout">Verified table virtual addresses and lengths.</param>
        /// <param name="token">Cancellation checked between functions.</param>
        /// <returns>Image, font, and animation variants found in the resource table.</returns>
        internal static IReadOnlyList<ResourceEntry> ReadCore(byte[] bytes, MetadataLayout layout, CancellationToken token)
        {
            try
            {
                using PortableImage image = new(bytes);
                Dictionary<uint, int> ids = [];
                for (int id = 0; id < layout.ResourceCount; id++)
                {
                    uint row = layout.Records + ((uint)id * 8);
                    uint pointer = image.UInt32(row), count = image.UInt32(row + 4);
                    if (count is 0 or > 16)
                    {
                        throw new InvalidDataException("Invalid resource variant count.");
                    }
                    for (uint variant = 0; variant < count; variant++)
                    {
                        if (!ids.TryAdd(pointer + (variant * 16), id))
                        {
                            throw new InvalidDataException("Overlapping resource records.");
                        }
                    }
                }
                List<ResourceEntry> result = [];
                HashSet<uint> visited = [];
                for (int index = 0; index < layout.InitializerCount; index++)
                {
                    token.ThrowIfCancellationRequested();
                    uint function = image.UInt32(layout.Initializers + ((uint)index * 4));
                    if (function == 0 || !visited.Add(function))
                    {
                        continue;
                    }
                    foreach (Initializer record in ReadInitializers(image, function))
                    {
                        if (!ids.TryGetValue(record.Address, out int id))
                        {
                            throw new InvalidDataException("An asset initializer has no resource ID.");
                        }
                        int type = checked((int)record.Globals[record.Address + 4]);
                        int resolution = checked((int)record.Globals[record.Address + 8]);
                        if (type is not (0 or 1 or 7))
                        {
                            throw new InvalidDataException("Unexpected image/animation resource kind.");
                        }
                        List<ResourceQuad> quads = [];
                        float preCutWidth = 0, preCutHeight = 0;
                        if (type == 0)
                        {
                            int count = checked((int)record.Fields[32]);
                            if (count is < 1 or > 16384)
                            {
                                throw new InvalidDataException("Invalid image quad count.");
                            }
                            preCutWidth = Float(record.Fields[24]);
                            preCutHeight = Float(record.Fields[28]);
                            float[] samples = ReadArray(image.Code(record.Fields[36]), record.Fields[36], checked(count * 4));
                            uint offsets = record.Fields[40];
                            float[] trims = offsets == 0 ? new float[count * 2] : ReadArray(image.Code(offsets), offsets, count * 2);
                            for (int quad = 0; quad < count; quad++)
                            {
                                ResourceQuad frame = new(samples[quad * 4], samples[(quad * 4) + 1], samples[(quad * 4) + 2], samples[(quad * 4) + 3], trims[quad * 2], trims[(quad * 2) + 1]);
                                if (frame.X < 0 || frame.Y < 0 || frame.Width < 0 || frame.Height < 0)
                                {
                                    throw new InvalidDataException("Negative atlas sample geometry.");
                                }
                                quads.Add(frame);
                            }
                        }
                        float? density = resolution switch
                        {
                            2 => 1f,
                            4 => 1.56f,
                            _ => null
                        };
                        string path = record.Name.EndsWith(".raw", StringComparison.Ordinal) ? Path.ChangeExtension(record.Name, ".png") : record.Name;
                        result.Add(new(id, path, type, resolution, density, preCutWidth, preCutHeight, quads));
                    }
                }
                return result.GroupBy(r => (r.Id, r.Resolution)).Any(g => g.Count() != 1)
                    ? throw new InvalidDataException("Duplicate resource resolution variants.")
                    : [.. result.OrderBy(r => r.Id).ThenBy(r => r.Resolution)];
            }
            catch (Exception error) when (error is BadImageFormatException or ArgumentException or OverflowException or KeyNotFoundException)
            {
                throw new InvalidDataException("Invalid or unsupported Windows resource metadata layout.", error);
            }
        }

        /// <summary>Decodes bounded constant stores and loops in an array-builder function.</summary>
        /// <param name="code">Function bytes, including its allocation preamble and return.</param>
        /// <param name="address">Virtual address of the first instruction.</param>
        /// <param name="count">Exact number of float elements required.</param>
        /// <returns>Initialized float values in source array order.</returns>
        internal static float[] ReadArray(byte[] code, uint address, int count)
        {
            if (count is < 1 or > 65536)
            {
                throw new InvalidDataException("Invalid float array length.");
            }
            List<Instruction> instructions = Decode(code, address);
            int lastCall = instructions.FindLastIndex(i => i.Mnemonic == Mnemonic.Call);
            if (lastCall < 0 || lastCall + 2 >= instructions.Count)
            {
                throw new InvalidDataException("Missing array allocation preamble.");
            }
            Dictionary<Register, uint> registers = [];
            foreach (Instruction instruction in instructions.Take(lastCall + 1))
            {
                if (instruction.Op0Kind != OpKind.Register)
                {
                    continue;
                }
                _ = registers.Remove(instruction.Op0Register);
                if (instruction.Mnemonic == Mnemonic.Xor && instruction.Op0Register == instruction.Op1Register)
                {
                    registers[instruction.Op0Register] = 0;
                }
                else if (instruction.Mnemonic == Mnemonic.Mov && instruction.Op1Kind == OpKind.Immediate32)
                {
                    registers[instruction.Op0Register] = instruction.Immediate32;
                }
            }
            // The final call is followed by an errno store or a copy of the allocation result.
            Instruction errorStore = instructions[lastCall + 1];
            bool errnoStore = errorStore.Mnemonic == Mnemonic.Mov && errorStore.Op0Kind == OpKind.Memory && errorStore.Op1Kind == OpKind.Immediate32 && errorStore.Immediate32 == 12;
            bool allocationCopy = errorStore.Mnemonic == Mnemonic.Mov && errorStore.Op0Kind == OpKind.Register && errorStore.Op1Kind == OpKind.Register && errorStore.Op1Register == Register.EAX;
            if (!errnoStore && !allocationCopy)
            {
                throw new InvalidDataException("Unsupported array allocation preamble.");
            }
            float[] values = new float[count];
            bool[] written = new bool[count];
            Dictionary<ulong, int> positions = instructions.Select((i, n) => (i.IP, n)).ToDictionary(p => p.IP, p => p.n);
            Register arrayBase = Register.None;
            bool? zero = null;
            int cursor = lastCall + 2, steps = 0;
            uint Value(Register register)
            {
                return register == Register.None ? 0 : registers.TryGetValue(register, out uint value) ? value : throw new InvalidDataException("Unknown array index register.");
            }

            while (cursor < instructions.Count)
            {
                if (++steps > 1000000)
                {
                    throw new InvalidDataException("Array builder exceeds instruction limit.");
                }
                Instruction instruction = instructions[cursor++];
                Register destination = instruction.Op0Register;
#pragma warning disable IDE0010 // Only supported builder instructions belong here; the default rejects the rest.
                switch (instruction.Mnemonic)
                {
                    case Mnemonic.Mov when instruction.Op0Kind == OpKind.Memory && instruction.Op1Kind == OpKind.Immediate32:
                        if (arrayBase == Register.None)
                        {
                            arrayBase = instruction.MemoryBase;
                        }
                        if (instruction.MemoryBase != arrayBase || arrayBase == Register.None)
                        {
                            throw new InvalidDataException("Unexpected array store base.");
                        }
                        uint offset = unchecked((uint)instruction.MemoryDisplacement64 + (Value(instruction.MemoryIndex) * (uint)instruction.MemoryIndexScale));
                        if (offset % 4 != 0 || offset / 4 >= count || written[offset / 4])
                        {
                            throw new InvalidDataException("Invalid or duplicate array store.");
                        }
                        values[offset / 4] = Float(instruction.Immediate32);
                        written[offset / 4] = true;
                        break;
                    case Mnemonic.Mov when instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Immediate32:
                        registers[destination] = instruction.Immediate32;
                        break;
                    case Mnemonic.Mov when instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Register:
                        // The return-value copy is not an index initializer.
                        if (registers.TryGetValue(instruction.Op1Register, out uint copy))
                        {
                            registers[destination] = copy;
                        }
                        else
                        {
                            _ = registers.Remove(destination);
                        }

                        break;
                    case Mnemonic.Lea when destination == Register.ESP && instruction.MemoryBase == Register.ESP && instruction.MemoryIndex == Register.None && instruction.MemoryDisplacement64 == 0:
                        break;
                    case Mnemonic.Lea:
                        registers[destination] = unchecked(Value(instruction.MemoryBase) + (Value(instruction.MemoryIndex) * (uint)instruction.MemoryIndexScale) + (uint)instruction.MemoryDisplacement64);
                        break;
                    case Mnemonic.Add when instruction.Op0Kind == OpKind.Register && instruction.Op1Kind == OpKind.Immediate8to32:
                        registers[destination] = unchecked(Value(destination) + (uint)instruction.Immediate8to32);
                        zero = registers[destination] == 0;
                        break;
                    case Mnemonic.Dec:
                    case Mnemonic.Inc:
                        registers[destination] = unchecked(Value(destination) + (instruction.Mnemonic == Mnemonic.Inc ? 1u : uint.MaxValue));
                        zero = registers[destination] == 0;
                        break;
                    case Mnemonic.Jne:
                        if (zero == null)
                        {
                            throw new InvalidDataException("Unknown loop condition.");
                        }

                        if (zero.Value)
                        {
                            break;
                        }

                        goto case Mnemonic.Jmp;
                    case Mnemonic.Jmp:
                        if (!positions.TryGetValue(instruction.NearBranchTarget, out cursor) || cursor < lastCall + 2)
                        {
                            throw new InvalidDataException("Array branch escapes its builder body.");
                        }
                        break;
                    case Mnemonic.Nop:
                    case Mnemonic.Pop:
                        break;
                    case Mnemonic.Ret:
                        if (written.Any(value => !value))
                        {
                            throw new InvalidDataException("Incomplete float array initialization.");
                        }

                        return values;
                    default:
                        throw new InvalidDataException("Unsupported array builder instruction: " + instruction.Mnemonic);
                }
#pragma warning restore IDE0010
            }
            throw new InvalidDataException("Missing array builder return.");
        }

        /// <summary>Converts an immediate bit pattern into a finite single-precision metadata value.</summary>
        /// <param name="bits">Little-endian IEEE 754 bit pattern.</param>
        /// <returns>The finite float represented by the immediate.</returns>
        private static float Float(uint bits)
        {
            float value = BitConverter.UInt32BitsToSingle(bits);
            return float.IsFinite(value) ? value : throw new InvalidDataException("Nonfinite image geometry.");
        }

        /// <summary>Decodes one bounded x86 function through its first return instruction.</summary>
        /// <param name="code">Bytes from the start of an executable section function.</param>
        /// <param name="address">Original function virtual address.</param>
        /// <returns>Instructions in address order.</returns>
        private static List<Instruction> Decode(byte[] code, uint address)
        {
            Iced.Intel.Decoder decoder = Iced.Intel.Decoder.Create(32, new ByteArrayCodeReader(code));
            decoder.IP = address;
            List<Instruction> instructions = [];
            while (instructions.Count < 20000 && decoder.IP < (ulong)address + (uint)code.Length)
            {
                decoder.Decode(out Instruction instruction);
                if (instruction.Code == Code.INVALID)
                {
                    throw new InvalidDataException("Invalid x86 initializer instruction.");
                }

                instructions.Add(instruction);
                if (instruction.Mnemonic == Mnemonic.Ret)
                {
                    return instructions;
                }
            }
            throw new InvalidDataException("Initializer exceeds function bounds.");
        }

        /// <summary>Collects source filenames, resource stores, image fields, and array-call provenance.</summary>
        /// <param name="image">Verified executable section reader.</param>
        /// <param name="address">Initializer function virtual address.</param>
        /// <returns>Asset records initialized by the function.</returns>
        private static IEnumerable<Initializer> ReadInitializers(PortableImage image, uint address)
        {
            Initializer? current = null;
            Dictionary<Register, uint> calls = [];
            Register imageBase = Register.None;
            foreach (Instruction instruction in Decode(image.Code(address), address))
            {
                if (instruction.Mnemonic == Mnemonic.Push && instruction.Op0Kind == OpKind.Immediate32 && image.AssetName(instruction.Immediate32) is string name)
                {
                    if (current != null)
                    {
                        yield return current;
                    }

                    current = new(name);
                    imageBase = Register.None;
                }
                if (current == null)
                {
                    continue;
                }

                if (instruction.Mnemonic == Mnemonic.Call)
                {
                    _ = calls.Remove(Register.ECX);
                    _ = calls.Remove(Register.EDX);
                    calls[Register.EAX] = instruction.NearBranchTarget <= uint.MaxValue ? (uint)instruction.NearBranchTarget : 0;
                }
                if (instruction.Mnemonic != Mnemonic.Mov)
                {
                    continue;
                }

                if (instruction.Op0Kind == OpKind.Register)
                {
                    if (instruction.Op1Kind == OpKind.Register && calls.TryGetValue(instruction.Op1Register, out uint call))
                    {
                        calls[instruction.Op0Register] = call;
                    }
                    else
                    {
                        _ = calls.Remove(instruction.Op0Register);
                    }
                }
                if (instruction.Op0Kind != OpKind.Memory || instruction.MemoryIndex != Register.None)
                {
                    continue;
                }

                uint offset = (uint)instruction.MemoryDisplacement64;
                if (instruction.MemoryBase == Register.None)
                {
                    if (instruction.Op1Kind == OpKind.Register && instruction.Op1Register == Register.EAX && current.Address == 0)
                    {
                        current.Address = offset;
                    }

                    if (instruction.Op1Kind == OpKind.Immediate32)
                    {
                        current.Globals[offset] = instruction.Immediate32;
                    }
                }
                else
                {
                    if (instruction.Op1Kind == OpKind.Immediate32)
                    {
                        if (offset == 0 && instruction.Immediate32 == uint.MaxValue)
                        {
                            imageBase = instruction.MemoryBase;
                        }

                        if (instruction.MemoryBase == imageBase)
                        {
                            current.Fields[offset] = instruction.Immediate32;
                        }
                    }
                    else if (instruction.Op1Kind == OpKind.Register && instruction.MemoryBase == imageBase && offset is 36 or 40)
                    {
                        current.Fields[offset] = calls.TryGetValue(instruction.Op1Register, out uint target) && target != 0 ? target : throw new InvalidDataException("Unknown image array builder.");
                    }
                }
            }
            if (current != null)
            {
                yield return current;
            }
        }

        /// <summary>Accumulates stores belonging to one source asset variant.</summary>
        /// <param name="name">Source asset filename referenced by the initializer.</param>
        private sealed class Initializer(string name)
        {
            /// <summary>Source asset filename.</summary>
            public readonly string Name = name;

            /// <summary>Virtual address of the sixteen-byte asset variant record.</summary>
            public uint Address;

            /// <summary>Absolute resource type and resolution stores.</summary>
            public readonly Dictionary<uint, uint> Globals = [];

            /// <summary>ImageInfo field offsets mapped to constants or array-builder addresses.</summary>
            public readonly Dictionary<uint, uint> Fields = [];
        }

        /// <summary>Maps bounded PE virtual addresses to file bytes without loading the image.</summary>
        private sealed class PortableImage : IDisposable
        {
            /// <summary>Original executable file bytes.</summary>
            private readonly byte[] bytes;

            /// <summary>PE headers used solely for section mapping.</summary>
            private readonly PEReader reader;

            /// <summary>Initializes a section mapper for a 32-bit x86 executable.</summary>
            /// <param name="bytes">Complete PE bytes.</param>
            public PortableImage(byte[] bytes)
            {
                this.bytes = bytes;
                reader = new(new MemoryStream(bytes, false));
                if (reader.PEHeaders.CoffHeader.Machine != Machine.I386 || reader.PEHeaders.PEHeader?.Magic != PEMagic.PE32)
                {
                    reader.Dispose();
                    throw new InvalidDataException("Expected a 32-bit x86 PE executable.");
                }
            }

            /// <summary>Maps a virtual address range to bytes physically present in a PE section.</summary>
            /// <param name="address">Original virtual address.</param>
            /// <param name="length">Number of required file bytes.</param>
            /// <returns>File offset containing the entire requested range.</returns>
            private int Offset(uint address, int length)
            {
                long rva = address - (long)reader.PEHeaders.PEHeader!.ImageBase;
                foreach (SectionHeader section in reader.PEHeaders.SectionHeaders)
                {
                    long relative = rva - section.VirtualAddress;
                    long offset = section.PointerToRawData + relative;
                    if (relative >= 0 && relative + length <= section.SizeOfRawData && offset >= 0 && offset + length <= bytes.Length)
                    {
                        return checked((int)offset);
                    }
                }
                throw new InvalidDataException("Metadata address lies outside file-backed PE sections.");
            }

            /// <summary>Reads a little-endian unsigned table field.</summary>
            /// <param name="address">Field virtual address.</param>
            /// <returns>The field's unsigned value.</returns>
            public uint UInt32(uint address)
            {
                return System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(Offset(address, 4), 4));
            }

            /// <summary>Copies a bounded function window from an executable section.</summary>
            /// <param name="address">Function start virtual address.</param>
            /// <returns>Up to 100,000 bytes, ending at the containing section's file boundary.</returns>
            public byte[] Code(uint address)
            {
                int offset = Offset(address, 1);
                SectionHeader section = reader.PEHeaders.SectionHeaders.Single(s => offset >= s.PointerToRawData && offset < (long)s.PointerToRawData + s.SizeOfRawData);
                return (section.SectionCharacteristics & SectionCharacteristics.MemExecute) == 0
                    ? throw new InvalidDataException("Initializer does not point into executable code.")
                    : bytes.AsSpan(offset, Math.Min(100000, Math.Min(bytes.Length - offset, section.PointerToRawData + section.SizeOfRawData - offset))).ToArray();
            }

            /// <summary>Recognizes a bounded printable filename referenced by a push instruction.</summary>
            /// <param name="address">Possible ASCII string virtual address.</param>
            /// <returns>
            /// A safe image/font/animation filename, or null when the immediate is not such a string.
            /// </returns>
            public string? AssetName(uint address)
            {
                int offset;
                try
                {
                    offset = Offset(address, 1);
                }
                catch (InvalidDataException) { return null; }
                int end = offset;
                while (end < bytes.Length && end - offset <= 128 && bytes[end] is >= 32 and <= 126)
                {
                    end++;
                }

                if (end == bytes.Length || bytes[end] != 0 || end == offset || end - offset > 128)
                {
                    return null;
                }

                string name = Encoding.ASCII.GetString(bytes, offset, end - offset);
                string extension = Path.GetExtension(name);
                return extension is not (".raw" or ".png" or ".jpg" or ".jpeg" or ".pb")
                    ? null
                    : name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..", StringComparison.Ordinal)
                    ? throw new InvalidDataException("Unsafe asset filename in resource metadata.")
                    : name;
            }

            /// <summary>Releases the header reader and its in-memory stream.</summary>
            public void Dispose()
            {
                reader.Dispose();
            }
        }
    }
}
