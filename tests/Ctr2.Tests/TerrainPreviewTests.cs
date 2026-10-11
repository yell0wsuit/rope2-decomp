using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises the real CLI cut/terrain path using only generated maps, design data and pixels.</summary>
    public sealed class TerrainPreviewTests
    {
        /// <summary>A recorded cut releases candy onto terrain at portrait and ultrawide viewport sizes.</summary>
        /// <param name="width">Output pixel width.</param>
        /// <param name="height">Output pixel height.</param>
        /// <returns>A task completing after the real process and pixel readback.</returns>
        [Theory]
        [InlineData(640, 960)]
        [InlineData(3440, 1440)]
        public async Task CliCutLandsCandyOnGeneratedTerrain(int width, int height)
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-terrain-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='test.xml'/></pack></filelist>");
                string map = "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='200' y='147'/><grab x='200' y='43' length='105' target_id='c'/><star x='200' y='194'/><star x='200' y='237'/><star x='250' y='200'/><polygon_materialized x='0' y='300' material='wood' polygon='320,0,320,40,0,40,' smoothFactor='1'/></layer></map>";
                File.WriteAllText(Path.Combine(maps, "test.xml"), map);
                File.WriteAllText(Path.Combine(maps, "%21_MasterGameDesignDocument.xml"), CandySimulationTests.Design.Replace("</layer></map>", "<circle id='star' body_type='kinematic' radius='13' isSensor='true' linearVelocityX='0' linearVelocityY='0' angularVelocity='0'/><polygon id='wood' body_type='static' friction='0.4' restitution='0.4' isSensor='false'/></layer></map>"));
                string image = Path.Combine(root, "assets", "synthetic.png");
                byte[] pixels = new byte[8 * 8 * 4];
                for (int i = 0; i < 64; i++)
                {
                    pixels[i * 4] = 255;
                    pixels[(i * 4) + 3] = 255;
                }
                using (FileStream file = File.Create(image))
                {
                    PngWriter.Write(file, 8, 8, pixels);
                }

                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(image)));
                ResourceCatalog catalog = new(1, "synthetic", "", [new(133, "synthetic.png", 0, 2, 1, 4, 4, [new(0, 0, 4, 4, 0, 0), new(0, 0, 4, 4, 0, 0)], hash)]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                string snapshot = Path.Combine(root, "cut.png");
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string argument in new[] { typeof(AssetPreview).Assembly.Location, "simulate-level", root, "1", "1", "--ticks", "180", "--cut", "30:0:0:400:138", "--width", width.ToString(), "--height", height.ToString(), "--snapshot", snapshot })
                {
                    start.ArgumentList.Add(argument);
                }

                using Process process = Process.Start(start)!;
                Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await error);
                Assert.Contains("Physics tick 180", await output);
                Assert.Contains("Stars collected: 2/3", await output);
                Assert.Contains("star 0, candy 0", await output);
                Assert.Contains("star 1, candy 0", await output);
                Assert.DoesNotContain("star 2, candy 0", await output);
                using SKBitmap bitmap = SKBitmap.Decode(snapshot);
                Viewport viewport = Viewport.Fit(width, height, 640, 960);
                Vector2 missedStar = viewport.ToScreen(new(500, 400));
                Assert.Equal(SKColors.Gold, bitmap.GetPixel((int)missedStar.X, (int)missedStar.Y));
                Vector2 collectedStar = viewport.ToScreen(new(400, 388));
                Assert.NotEqual(SKColors.Gold, bitmap.GetPixel((int)collectedStar.X, (int)collectedStar.Y));
                Vector2 sensorEdge = viewport.ToScreen(new(523, 400));
                Assert.Equal(SKColors.Gold, bitmap.GetPixel((int)sensorEdge.X, (int)sensorEdge.Y));
                bool candyFound = false;
                for (int y = 550; y <= 585; y++)
                {
                    Vector2 point = viewport.ToScreen(new(400, y));
                    candyFound |= bitmap.GetPixel((int)point.X, (int)point.Y).Red > 240;
                }
                Assert.True(candyFound, "Released candy must remain above the solid floor.");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
