using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises static level fitting, clipping and command selection without original assets.</summary>
    public sealed class LevelPreviewTests
    {
        /// <summary>Checks marker positions and centered margins in portrait and wide outputs.</summary>
        /// <param name="width">Output pixel width.</param>
        /// <param name="height">Output pixel height.</param>
        [Theory]
        [InlineData(640, 960)]
        [InlineData(1600, 960)]
        public void PreviewFitsAndClipsLogicalScene(int width, int height)
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='160' y='240'/><star x='-1' y='30'/></layer></map>"));
            using LevelPreview preview = new(layout);
            using SKBitmap bitmap = new(width, height);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, width, height, 0);
            Assert.Equal(SKColors.Coral, bitmap.GetPixel(width / 2, height / 2));
            if (width > 640)
            {
                Assert.Equal(new SKColor(24, 27, 32), bitmap.GetPixel(((width - 640) / 2) - 1, 60));
            }
            Assert.Equal(new SKColor(42, 55, 48), bitmap.GetPixel(width / 2, 100));
        }

        /// <summary>Verifies desktop pack order reaches the real offscreen command renderer.</summary>
        /// <returns>A task completing after the preview process and pixel readback.</returns>
        [Fact]
        public async Task CommandRendersDesktopSelection()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-layout-cli-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='synthetic.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "synthetic.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='160' y='240'/></layer></map>");
                string snapshot = Path.Combine(root, "snapshot.png");
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string argument in new[] { typeof(AssetPreview).Assembly.Location, "preview-level", root, "1", "1", "--width", "640", "--height", "960", "--snapshot", snapshot })
                {
                    start.ArgumentList.Add(argument);
                }
                using Process process = Process.Start(start)!;
                Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await error);
                Assert.Contains("synthetic.xml", await output);
                Assert.Contains("Static layout only", await output);
                using SKBitmap bitmap = SKBitmap.Decode(snapshot);
                Assert.Equal(SKColors.Coral, bitmap.GetPixel(320, 480));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
