using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Desktop;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises animated draw/readback using entirely generated resources.</summary>
    public sealed class AnimationPreviewTests
    {
        /// <summary>Verifies trimmed sizes, density, scene selection, and motion through the CLI.</summary>
        /// <returns>A task that completes after both snapshots and readback checks.</returns>
        [Fact]
        public async Task RendersMovingCharacterAtBothDensities()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-animation-" + Guid.NewGuid());
            _ = Directory.CreateDirectory(Path.Combine(root, "assets"));
            try
            {
                byte[] rgba = new byte[4 * 4 * 4];
                for (int i = 0; i < 16; i++)
                {
                    rgba[i * 4] = 255;
                    rgba[(i * 4) + 3] = 255;
                }
                string texture = Path.Combine(root, "assets", "image.png");
                using (FileStream file = File.Create(texture))
                {
                    PngWriter.Write(file, 4, 4, rgba);
                }
                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(texture)));
                ResourceCatalog catalog = new(1, "synthetic", "", [new(1, "test.pb", 7, 2, null, 0, 0, []),
                    new(7, "image.png", 0, 2, 1, 40, 40, [new(0, 0, 2, 2, 30, 30)], hash),
                    new(7, "image.png", 0, 4, 2, 80, 80, [new(0, 0, 4, 4, 60, 60)], hash)]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                byte[] keys = Message((1, 0f), (2, 2f), (3, 2f), (7, 1));
                byte[] next = Message((1, 1f), (2, 6f), (3, 2f), (7, 0));
                byte[] track = Message((1, 0), (2, keys), (2, next));
                byte[] part = Message((1, 1), (3, 7), (5, 9), (6, 9), (10, Message((1, 0), (5, track))));
                File.WriteAllBytes(Path.Combine(root, "assets", "test.pb"), Message((1, 2), (7, 10f), (8, 10f), (9, part), (10, Message((1, 0), (2, "IDLE")))));
                foreach (int resolution in new[] { 2, 4 })
                {
                    string snapshot = Path.Combine(root, $"snapshot-{resolution}.png");
                    ProcessStartInfo start = new("dotnet")
                    {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    foreach (string argument in new[] { typeof(AssetPreview).Assembly.Location, "preview-animation", root, "1", "IDLE", "--resolution", resolution.ToString(), "--time", "0.5", "--width", "100", "--height", "100", "--snapshot", snapshot })
                    {
                        start.ArgumentList.Add(argument);
                    }
                    using Process process = Process.Start(start)!;
                    string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                    Assert.True(process.ExitCode == 0, error);
                    using SKBitmap bitmap = SKBitmap.Decode(snapshot);
                    Assert.Equal(SKColors.Red, bitmap.GetPixel(45, 25));
                    Assert.Equal(new SKColor(68, 73, 81), bitmap.GetPixel(25, 25));
                    Assert.Equal(new SKColor(68, 73, 81), bitmap.GetPixel(75, 25));
                }
                using AnimationPreview preview = AnimationPreview.Load(root, 1, "IDLE", 2);
                Assert.Equal(1, preview.Duration);
                using (SKBitmap transformed = new(100, 100))
                using (SKCanvas canvas = new(transformed))
                {
                    canvas.Clear(SKColors.Blue);
                    preview.DrawTransformed(canvas, Matrix4x4.CreateTranslation(20, 30, 0), 0.5);
                    Assert.Equal(new Vector2(10, 10), preview.CanvasSize);
                    Assert.Equal(SKColors.Red, transformed.GetPixel(24, 32));
                    Assert.Equal(SKColors.Blue, transformed.GetPixel(0, 0));
                }
                _ = Assert.Throws<InvalidDataException>(() => AnimationPreview.Load(root, 1, "missing", 2));
                ResourceEntry imageResource = catalog.Resources[1] with
                {
                    Quads = [new(0, 0, 1, 1, 0, 0, false), new(0, 0, 2, 2, 0, 0)]
                };
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog with
                {
                    Resources = [catalog.Resources[0], imageResource]
                }));
                byte[] actions = Message((1, 4), (2, Message((1, 0f), (2, 1f), (6, 1))));
                byte[] overridden = Message((1, 1), (3, 7), (10, Message((1, 0), (5, actions))));
                byte[] animationRoot = Message((1, 2), (7, 10f), (8, 10f), (9, overridden), (10, Message((1, 0), (2, "IDLE"))));
                File.WriteAllBytes(Path.Combine(root, "assets", "test.pb"), animationRoot);
                using (AnimationPreview replacement = AnimationPreview.Load(root, 1, "IDLE", 2))
                {
                    using SKBitmap pixels = new(100, 100);
                    using SKCanvas canvas = new(pixels);
                    replacement.Draw(canvas, 100, 100, 0);
                    Assert.Equal(SKColors.Red, pixels.GetPixel(5, 5));
                }
                File.WriteAllBytes(texture, [1, 2, 3]);
                _ = Assert.Throws<InvalidDataException>(() => AnimationPreview.Load(root, 1, "IDLE", 2));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Writes a small synthetic protobuf message.</summary>
        /// <param name="fields">Field number and typed value pairs.</param>
        /// <returns>The encoded bytes.</returns>
        internal static byte[] Message(params (int Tag, object Value)[] fields)
        {
            using MemoryStream stream = new();
            using BinaryWriter writer = new(stream);
            foreach ((int tag, object value) in fields)
            {
                int kind = value is float ? 5 : value is int ? 0 : 2;
                writer.Write((byte)((tag << 3) | kind));
                if (value is float number)
                {
                    writer.Write(number);
                }
                else if (value is int integer)
                {
                    writer.Write((byte)integer);
                }
                else
                {
                    byte[] data = value is byte[] bytes ? bytes : System.Text.Encoding.UTF8.GetBytes((string)value);
                    int length = data.Length;
                    while (length > 127)
                    {
                        writer.Write((byte)((length & 127) | 128));
                        length >>= 7;
                    }
                    writer.Write((byte)length);
                    writer.Write(data);
                }
            }
            return stream.ToArray();
        }
    }
}
