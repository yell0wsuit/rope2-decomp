using System;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading;

using Ctr2.Assets;
using Ctr2.Engine;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>
    /// Checks RAW decoding, PNG encoding, viewport geometry, and staged Windows imports using synthetic assets.
    /// </summary>
    public sealed class FoundationTests : IDisposable
    {
        /// <summary>Unique temporary directory owned by this test instance.</summary>
        private readonly string root = Path.Combine(Path.GetTempPath(), "ctr2-tests-" + Guid.NewGuid());

        /// <summary>
        /// Initializes a new instance of the <see cref="FoundationTests"/> class and creates its temporary
        /// directory.
        /// </summary>
        public FoundationTests()
        {
            _ = Directory.CreateDirectory(root);
        }

        /// <summary>Deletes this test instance's temporary inputs, caches, and archives.</summary>
        public void Dispose()
        {
            Directory.Delete(root, true);
        }

        /// <summary>Writes a synthetic one-row RAW fixture, optionally splitting its compressed payload.</summary>
        /// <param name="format">
        /// Original pixel format identifier, including unsupported values for negative tests.
        /// </param>
        /// <param name="pixels">
        /// Uncompressed source bytes; copied without validating their length against the dimensions.
        /// </param>
        /// <param name="width">Header width in pixels; the header height is always one.</param>
        /// <param name="split">
        /// Whether to put all but the first three compressed bytes into a .raw1 continuation.
        /// </param>
        /// <returns>The base RAW file path inside the temporary directory.</returns>
        private string Raw(byte format, byte[] pixels, int width = 1, bool split = false)
        {
            using MemoryStream packed = new();
            using (ZLibStream z = new(packed, CompressionLevel.SmallestSize, true))
            {
                z.Write(pixels);
            }

            byte[] payload = packed.ToArray();
            string path = Path.Combine(root, "test.raw");
            using (BinaryWriter writer = new(File.Create(path)))
            {
                writer.Write((ushort)1981);
                writer.Write((ushort)width);
                writer.Write((ushort)1);
                writer.Write(format);
                writer.Write(pixels.Length);
                writer.Write(payload.Length);
                writer.Write(payload, 0, split ? 3 : payload.Length);
            }
            if (split)
            {
                File.WriteAllBytes(path + "1", payload[3..]);
            }

            return path;
        }

        /// <summary>Verifies each supported packed pixel format expands to the expected straight RGBA bytes.</summary>
        /// <param name="format">Source format identifier.</param>
        /// <param name="input">Packed bytes for one source pixel.</param>
        /// <param name="expected">Expected four decoded RGBA bytes.</param>
        [Theory]
        [InlineData(0, new byte[] { 17, 34, 51, 68 }, new byte[] { 17, 34, 51, 68 })]
        [InlineData(1, new byte[] { 0, 248 }, new byte[] { 255, 0, 0, 255 })]
        [InlineData(2, new byte[] { 0x48, 0xf2 }, new byte[] { 255, 34, 68, 136 })]
        [InlineData(3, new byte[] { 1, 248 }, new byte[] { 255, 0, 0, 255 })]
        [InlineData(4, new byte[] { 128 }, new byte[] { 255, 255, 255, 128 })]
        public void DecodesOriginalPixelFormats(byte format, byte[] input, byte[] expected)
        {
            Assert.Equal(expected, RawTexture.Load(Raw(format, input)).Rgba);
        }

        /// <summary>Verifies a split compressed payload is joined before decoding.</summary>
        [Fact]
        public void JoinsContinuationFiles()
        {
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, RawTexture.Load(Raw(0, [1, 2, 3, 4], split: true)).Rgba);
        }

        /// <summary>Verifies a missing RAW continuation fails instead of producing a partial texture.</summary>
        [Fact]
        public void RejectsMissingContinuation()
        {
            string path = Raw(0, [1, 2, 3, 4], split: true);
            File.Delete(path + "1");
            _ = Assert.Throws<InvalidDataException>(() => RawTexture.Load(path));
        }

        /// <summary>Verifies unsupported pixel format identifiers are rejected.</summary>
        [Fact]
        public void RejectsUnknownFormat()
        {
            _ = Assert.Throws<InvalidDataException>(() => RawTexture.Load(Raw(9, [0])));
        }

        /// <summary>Verifies header dimensions must match the declared uncompressed pixel byte count.</summary>
        [Fact]
        public void RejectsMismatchedPixelLength()
        {
            _ = Assert.Throws<InvalidDataException>(() => RawTexture.Load(Raw(0, [1, 2, 3, 4], width: 2)));
        }

        /// <summary>Verifies an unrecognized RAW marker is rejected.</summary>
        [Fact]
        public void RejectsUnknownHeader()
        {
            string path = Raw(0, [1, 2, 3, 4]);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[0] = 0;
            File.WriteAllBytes(path, bytes);
            _ = Assert.Throws<InvalidDataException>(() => RawTexture.Load(path));
        }

        /// <summary>Verifies truncated compressed trailers fail even when all pixels could be decoded.</summary>
        /// <param name="trim">Number of bytes removed from the compressed payload tail.</param>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        public void RejectsTruncatedZlibTrailer(int trim)
        {
            string path = Raw(0, [1, 2, 3, 4]);
            byte[] bytes = File.ReadAllBytes(path);
            bytes = bytes[..^trim];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(11), bytes.Length - 15);
            File.WriteAllBytes(path, bytes);
            _ = Assert.Throws<InvalidDataException>(() => RawTexture.Load(path));
        }

        /// <summary>Verifies PNG output preserves semitransparent colors and source row order after decoding.</summary>
        [Fact]
        public void PngRoundtripPreservesStraightAlphaAndRowOrder()
        {
            using MemoryStream stream = new();
            PngWriter.Write(stream, 1, 2, [255, 0, 0, 255, 0, 255, 0, 136]);
            using SKBitmap decoded = SKBitmap.Decode(stream.ToArray());
            Assert.Equal(new SKColor(255, 0, 0, 255), decoded.GetPixel(0, 0));
            Assert.Equal(new SKColor(0, 255, 0, 136), decoded.GetPixel(0, 1));
        }

        /// <summary>
        /// Verifies portrait, widescreen, and ultrawide fits preserve scale and invert pointer coordinates.
        /// </summary>
        /// <param name="width">Output width in pixels.</param>
        /// <param name="height">Output height in pixels.</param>
        [Theory]
        [InlineData(640, 960)]
        [InlineData(1920, 1080)]
        [InlineData(3440, 1440)]
        public void ViewportPreservesAspectAndRoundtripsInput(int width, int height)
        {
            Viewport view = Viewport.Fit(width, height, 640, 960);
            Vector2 origin = view.ToScreen(Vector2.Zero), end = view.ToScreen(new(640, 960));
            Assert.True(origin.X >= 0 && origin.Y >= 0 && end.X <= width + .001f && end.Y <= height + .001f);
            Assert.Equal((end.X - origin.X) / 640, (end.Y - origin.Y) / 960, 5);
            Assert.True(Vector2.Distance(new(123, 456), view.ToLogical(view.ToScreen(new(123, 456)))) < .001f);
        }

        /// <summary>Verifies density converts layout geometry without changing source sampling coordinates.</summary>
        [Fact]
        public void TrimOffsetsAndCanvasUseExplicitDensity()
        {
            AtlasFrame frame = new(10, 20, 156, 78, 31.2f, 15.6f, 312, 156, 1.56f);
            Assert.Equal(100, frame.LogicalWidth, 4);
            Assert.Equal(50, frame.LogicalHeight, 4);
            Assert.Equal(20, frame.LogicalOffset.X, 4);
            Assert.Equal(10, frame.LogicalOffset.Y, 4);
            Assert.Equal(200, frame.LogicalCanvas.X, 4);
            Assert.Equal(10, frame.PixelX);
        }

        /// <summary>Verifies a zero output dimension is rejected before computing the viewport.</summary>
        [Fact]
        public void InvalidViewportIsRejected()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Viewport.Fit(0, 100, 640, 960));
        }

        /// <summary>
        /// Creates a minimal Windows package tree with one red RAW texture and a copied animation fixture.
        /// </summary>
        /// <returns>The package root containing Assets/assets.</returns>
        private string Dump()
        {
            string assets = Path.Combine(root, "dump", "Assets", "assets");
            _ = Directory.CreateDirectory(assets);
            File.Copy(Raw(0, [255, 0, 0, 255]), Path.Combine(assets, "sample_640_960.raw"));
            File.WriteAllText(Path.Combine(assets, "sample.pb"), "synthetic animation");
            return Path.Combine(root, "dump");
        }

        /// <summary>
        /// Verifies import publishes a PNG, manifest, and copied animation while retaining the source RAW file.
        /// </summary>
        [Fact]
        public void ImportPublishesConvertedCacheAndPreservesSource()
        {
            string dump = Dump(), cache = Path.Combine(root, "cache");
            ImportManifest result = GameImport.ImportWindows(dump, cache, cancellationToken: TestContext.Current.CancellationToken);
            _ = Assert.Single(result.Textures);
            Assert.True(File.Exists(Path.Combine(cache, "assets", "sample_640_960.png")));
            Assert.True(File.Exists(Path.Combine(cache, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(cache, "assets", "sample.pb")));
            Assert.True(File.Exists(Path.Combine(dump, "Assets", "assets", "sample_640_960.raw")));
            Assert.Equal(1, result.Textures[0].PixelsPerLogicalUnit);
        }

        /// <summary>Verifies an APPX-compatible archive produces a converted texture cache.</summary>
        [Fact]
        public void ImportsWindowsArchive()
        {
            string dump = Dump(), archive = Path.Combine(root, "dump.appx");
            ZipFile.CreateFromDirectory(dump, archive);
            _ = Assert.Single(GameImport.ImportWindows(archive, Path.Combine(root, "cache"), cancellationToken: TestContext.Current.CancellationToken).Textures);
        }

        /// <summary>Verifies a malformed texture prevents publication and removes staging files.</summary>
        [Fact]
        public void ImportFailureDoesNotPublishPartialCache()
        {
            string dump = Dump(), cache = Path.Combine(root, "cache");
            File.WriteAllBytes(Path.Combine(dump, "Assets", "assets", "broken.raw"), [0]);
            _ = Assert.Throws<InvalidDataException>(() => GameImport.ImportWindows(dump, cache, cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(Directory.Exists(cache));
            Assert.Empty(Directory.GetDirectories(root, ".ctr2-import-*"));
        }

        /// <summary>Verifies an archive cannot write outside its staging directory.</summary>
        [Fact]
        public void RejectsArchiveTraversal()
        {
            string archive = Path.Combine(root, "bad.appx");
            using (ZipArchive zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using StreamWriter writer = new(zip.CreateEntry("../escaped").Open());
                writer.Write("bad");
            }
            _ = Assert.Throws<InvalidDataException>(() => GameImport.ImportWindows(archive, Path.Combine(root, "cache"), cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(root, "escaped")));
        }

        /// <summary>Verifies a cache cannot be placed inside its source dump.</summary>
        [Fact]
        public void RejectsSourceCacheOverlap()
        {
            string dump = Dump();
            _ = Assert.Throws<ArgumentException>(() => GameImport.ImportWindows(dump, Path.Combine(dump, "cache"), cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>Verifies cancellation during import prevents publication and removes staging files.</summary>
        [Fact]
        public void CancellationRollsBackStaging()
        {
            string dump = Dump(), cache = Path.Combine(root, "cache");
            using CancellationTokenSource cancel = new();
            _ = Assert.Throws<OperationCanceledException>(() => GameImport.ImportWindows(dump, cache, _ => cancel.Cancel(), cancel.Token));
            Assert.False(Directory.Exists(cache));
            Assert.Empty(Directory.GetDirectories(root, ".ctr2-import-*"));
        }

        /// <summary>Verifies a linked Assets ancestor is rejected before recursive import.</summary>
        /// <remarks>
        /// Returns without checking on Windows, where creating a symbolic link may require elevated privileges.
        /// </remarks>
        [Fact]
        public void RejectsLinkedAssetsAncestor()
        {
            if (OperatingSystem.IsWindows())
            {
                return; // Creating symlinks may require extra privileges.
            }

            string dump = Dump();
            string outer = Path.Combine(root, "linked-dump");
            _ = Directory.CreateDirectory(outer);
            _ = Directory.CreateSymbolicLink(Path.Combine(outer, "Assets"), Path.Combine(dump, "Assets"));
            _ = Assert.Throws<InvalidDataException>(() => GameImport.ImportWindows(outer, Path.Combine(root, "cache"), cancellationToken: TestContext.Current.CancellationToken));
        }

        /// <summary>Verifies an existing cache is refused without modifying its files.</summary>
        [Fact]
        public void NeverOverwritesAnExistingCache()
        {
            string dump = Dump(), cache = Path.Combine(root, "cache");
            _ = Directory.CreateDirectory(cache);
            File.WriteAllText(Path.Combine(cache, "keep"), "keep");
            _ = Assert.Throws<IOException>(() => GameImport.ImportWindows(dump, cache, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(cache, "keep")));
        }
    }
}
