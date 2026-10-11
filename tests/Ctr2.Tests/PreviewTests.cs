using System.Numerics;
using System.Text.Json;

using Ctr2.Desktop;
using Ctr2.Engine;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks sidecar geometry and rendered atlas samples with pixel readback.</summary>
    public sealed class PreviewTests
    {
        /// <summary>Verifies JSON sidecar values retain pixel sampling coordinates and explicit density.</summary>
        [Fact]
        public void SidecarPreservesSourcePixelRectAndDensity()
        {
            const string json = """{"X":1,"Y":0,"Width":2,"Height":2,"OffsetX":2,"OffsetY":2,"PreCutWidth":8,"PreCutHeight":8,"PixelsPerLogicalUnit":2}""";
            AtlasFrame frame = JsonSerializer.Deserialize<PreviewFrame>(json)!.ToFrame();
            Assert.Equal(1, frame.PixelX);
            Assert.Equal(1, frame.LogicalWidth);
            Assert.Equal(1, frame.LogicalOffset.X);
            Assert.Equal(4, frame.LogicalCanvas.X);
        }

        /// <summary>
        /// Verifies rendering samples only the requested atlas rectangle and applies the logical trim offset.
        /// </summary>
        /// <param name="width">Preview output width in pixels.</param>
        /// <param name="height">Preview output height in pixels.</param>
        [Theory]
        [InlineData(80, 80)]
        [InlineData(160, 80)]
        [InlineData(80, 160)]
        public void DrawUsesQuadPixelsAndTrimOffset(int width, int height)
        {
            using SKBitmap atlas = new(4, 2);
            atlas.Erase(SKColors.Blue);
            atlas.SetPixel(1, 0, SKColors.Red);
            atlas.SetPixel(2, 0, SKColors.Red);
            atlas.SetPixel(1, 1, SKColors.Red);
            atlas.SetPixel(2, 1, SKColors.Red);
            using SKBitmap pixels = new(width, height);
            using SKCanvas canvas = new(pixels);
            AtlasFrame frame = new(1, 0, 2, 2, 2, 2, 8, 8, 2);
            AssetPreview.Draw(canvas, atlas, width, height, frame);
            Viewport viewport = Viewport.Fit(width, height, 4, 4);
            Vector2 red = viewport.ToScreen(new(1.5f, 1.5f));
            Vector2 empty = viewport.ToScreen(new(.5f, .5f));
            Assert.Equal(SKColors.Red, pixels.GetPixel((int)red.X, (int)red.Y));
            Assert.Equal(new SKColor(68, 73, 81), pixels.GetPixel((int)empty.X, (int)empty.Y));
            Assert.DoesNotContain(SKColors.Blue, pixels.Pixels);
        }
    }
}
