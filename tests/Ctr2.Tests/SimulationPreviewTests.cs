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
    /// <summary>Checks moving candy rendering from entirely generated cached assets and master data.</summary>
    public sealed class SimulationPreviewTests
    {
        /// <summary>Checks simulation attachment, tick advancement and density-independent candy placement.</summary>
        /// <returns>A task completing after the real CLI snapshots and readback checks.</returns>
        [Fact]
        public async Task SimulationMovesCandyAtBothDensities()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-simulation-preview-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='test.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "test.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='200' y='147'/></layer></map>");
                File.WriteAllText(Path.Combine(maps, "%21_MasterGameDesignDocument.xml"), CandySimulationTests.Design);
                string texture = Path.Combine(root, "assets", "synthetic.png");
                byte[] pixels = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    pixels[i * 4] = 255;
                    pixels[(i * 4) + 3] = 255;
                }
                using (FileStream output = File.Create(texture))
                {
                    PngWriter.Write(output, 8, 8, pixels);
                }
                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(texture)));
                ResourceCatalog catalog = new(1, "synthetic", "", [
                    new(133, "synthetic.png", 0, 2, 1, 4, 4, [new(0, 0, 4, 4, 0, 0), new(0, 0, 4, 4, 0, 0)], hash),
                    new(133, "synthetic.png", 0, 4, 2, 8, 8, [new(0, 0, 8, 8, 0, 0), new(0, 0, 8, 8, 0, 0)], hash)]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                foreach (int resolution in new[] { 2, 4 })
                {
                    (LevelDefinition Definition, LevelPreview Preview) = LevelPreview.Load(root, 1, 1, resolution, simulate: true);
                    using LevelPreview preview = Preview;
                    Assert.NotNull(preview.Simulation);
                    for (int i = 0; i < 30; i++)
                    {
                        preview.Simulation.Step();
                    }
                    using SKBitmap bitmap = new(640, 960);
                    using SKCanvas canvas = new(bitmap);
                    preview.Draw(canvas, 640, 960, 0);
                    int y = (int)(preview.Simulation.Candies[0].Position.Y * 200);
                    Assert.True(y > 400);
                    Assert.Equal(SKColors.Red, bitmap.GetPixel(400, y));
                    Assert.Equal(new SKColor(42, 55, 48), bitmap.GetPixel(400, 294));
                    Assert.Equal(new SKColor(42, 55, 48), bitmap.GetPixel(403, y));
                    string snapshot = Path.Combine(root, $"simulation-{resolution}.png");
                    ProcessStartInfo start = new("dotnet")
                    {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };
                    foreach (string argument in new[] { typeof(AssetPreview).Assembly.Location, "simulate-level", root, "1", "1", "--resolution", resolution.ToString(), "--ticks", "30", "--release-at", "10", "--width", "640", "--height", "960", "--snapshot", snapshot })
                    {
                        start.ArgumentList.Add(argument);
                    }
                    using Process process = Process.Start(start)!;
                    Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                    Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                    Assert.True(process.ExitCode == 0, await error);
                    Assert.Contains("Physics tick 30", await output);
                    using SKBitmap fromCommand = SKBitmap.Decode(snapshot);
                    Assert.Equal(SKColors.Red, fromCommand.GetPixel(400, y));
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
