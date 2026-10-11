using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Desktop;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks command-line rendering and user-facing failures in a separate desktop-host process.</summary>
    public sealed class CommandLineTests
    {
        /// <summary>
        /// Verifies a missing dump reports a readable error, exits unsuccessfully, and leaves no cache.
        /// </summary>
        /// <returns>A task that completes after the child process exits and the assertions finish.</returns>
        [Fact]
        public async Task InvalidDumpReportsErrorWithoutCrashing()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-cli-" + Guid.NewGuid());
            _ = Directory.CreateDirectory(root);
            try
            {
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                start.ArgumentList.Add(typeof(AssetPreview).Assembly.Location);
                start.ArgumentList.Add("import");
                start.ArgumentList.Add(Path.Combine(root, "missing-dump"));
                start.ArgumentList.Add(Path.Combine(root, "cache"));
                using Process process = Process.Start(start)!;
                string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(1, process.ExitCode);
                Assert.Contains("Expected a Windows package root", error);
                Assert.DoesNotContain("Unhandled exception", error);
                Assert.False(Directory.Exists(Path.Combine(root, "cache")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
        /// <summary>Verifies resource selection reaches the offscreen renderer with the selected trim geometry.</summary>
        /// <returns>A task that completes after rendering and pixel readback assertions.</returns>
        [Fact]
        public async Task ResourceCommandRendersSelectedQuad()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-resource-cli-" + Guid.NewGuid());
            _ = Directory.CreateDirectory(Path.Combine(root, "assets"));
            try
            {
                string texture = Path.Combine(root, "assets", "synthetic.png");
                byte[] rgba = new byte[4 * 4 * 4];
                for (int pixel = 0; pixel < 16; pixel++)
                {
                    rgba[(pixel * 4) + 2] = 255;
                    rgba[(pixel * 4) + 3] = 255;
                }
                rgba[20] = 255;
                rgba[22] = 0;
                using (FileStream output = File.Create(texture))
                {
                    PngWriter.Write(output, 4, 4, rgba);
                }

                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(texture)));
                ResourceEntry resource = new(7, "synthetic.png", 0, 2, 1, 4, 4, [new(1, 1, 1, 1, 1, 1)], hash);
                ResourceCatalog catalog = new(1, "synthetic", new string('a', 64), [resource]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                string snapshot = Path.Combine(root, "snapshot.png");
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string argument in new[] { typeof(AssetPreview).Assembly.Location, "preview-resource", root, "7", "2", "0", "--width", "40", "--height", "40", "--snapshot", snapshot })
                {
                    start.ArgumentList.Add(argument);
                }
                using Process process = Process.Start(start)!;
                string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, error);
                using SKBitmap pixels = SKBitmap.Decode(snapshot);
                Assert.Equal(SKColors.Red, pixels.GetPixel(15, 15));
                Assert.Equal(new SKColor(68, 73, 81), pixels.GetPixel(5, 5));
                Assert.DoesNotContain(SKColors.Blue, pixels.Pixels);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
