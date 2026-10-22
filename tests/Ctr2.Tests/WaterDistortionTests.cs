using System;
using System.Numerics;

using Ctr2.Desktop;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Windows vertex-grid distortion contracts.</summary>
    public sealed class WaterDistortionTests
    {
        /// <summary>The renderer supplies the native water mesh pass.</summary>
        [Fact]
        public void SuppliesWaterDrawerPass()
        {
            Assert.NotNull(typeof(LevelPreview).Assembly.GetType("Ctr2.Desktop.WaterDistortion"));
        }

        /// <summary>Windows emits X-major cells with the native triangle diagonal and position-only overscan.</summary>
        [Fact]
        public void MeshUsesNativeGridOrderAndOverscan()
        {
            WaterMesh mesh = WaterDistortion.Build(new(640, 960), Vector2.Zero, 200, 0, true, Vector2.One);
            Assert.Equal(2400, mesh.Positions.Length);
            Assert.Equal(new[] { new SKPoint(-2, -2), new(32, -2), new(32, 48), new(-2, -2), new(-2, 48), new(32, 48) }, mesh.Positions[..6]);
            Assert.Equal(new SKPoint(-2, 48), mesh.Positions[6]);
            Assert.Equal(new SKPoint(32, -2), mesh.Positions[120]);
            Assert.Contains(new SKPoint(642, 962), mesh.Positions);
            Assert.Equal(new SKPoint(0, 1), mesh.Coordinates[0]);
            Assert.All(mesh.Coordinates, uv => { Assert.InRange(uv.X, 0, 1); Assert.InRange(uv.Y, 0, 1); });
        }

        /// <summary>Independent C++ float reference values for the embedded Windows shader at time1.</summary>
        [Theory]
        [InlineData(320, 288, .5000114441f, .6994385719f)]
        [InlineData(160, 672, .2499860376f, .3056143224f)]
        [InlineData(642, 962, .999999404f, .0002792247687f)]
        [InlineData(320, 240, .5f, .75f)]
        public void VertexCoordinatesMatchShaderReference(float x, float y, float u, float v)
        {
            WaterMesh mesh = WaterDistortion.Build(new(640, 960), Vector2.Zero, 200, 1, true, Vector2.One);
            int index = Array.FindIndex(mesh.Positions, p => p.X == x && p.Y == y);
            Assert.True(index >= 0);
            Assert.InRange(MathF.Abs(mesh.Coordinates[index].X - u), 0, 1e-7f);
            Assert.InRange(MathF.Abs(mesh.Coordinates[index].Y - v), 0, 1e-7f);
        }

        /// <summary>The strict threshold acts at vertices and framebuffer/image UVs retain their respective Y direction.</summary>
        [Fact]
        public void ThresholdAndBackgroundDirectionFollowNativeUniforms()
        {
            WaterMesh mesh = WaterDistortion.Build(new(640, 960), Vector2.Zero, 223, 1, true, Vector2.One);
            int index = Array.FindIndex(mesh.Positions, p => p.X == 320 && p.Y == 288);
            Assert.Equal(new SKPoint(.5f, .7f), mesh.Coordinates[index]);
            WaterMesh background = WaterDistortion.Build(new(640, 960), Vector2.Zero, 200, 1, false, Vector2.One);
            index = Array.FindIndex(background.Positions, p => p.X == 160 && p.Y == 672);
            Assert.InRange(background.Coordinates[index].Y, .7056142f, .7056145f);
        }

        /// <summary>Time accumulates native fixed floats; entering image fallback adds pi once and resuming retains it.</summary>
        [Fact]
        public void ClockPreservesPhaseAcrossFallbackAndResetsOnRewind()
        {
            WaterDistortion effect = new();
            effect.Advance(60);
            Assert.Equal(1.002000809f, effect.Time);
            effect.UseFramebuffer();
            float before = effect.Time;
            effect.UseBackground(Vector2.One);
            Assert.Equal(before + 3.1415926f, effect.Time);
            float paused = effect.Time;
            effect.UseBackground(new(.5f, 1)); // Source rebuild test ignores maxX.
            effect.Advance(60);
            Assert.Equal(paused, effect.Time);
            effect.UseFramebuffer();
            Assert.Equal(paused, effect.Time);
            effect.Advance(61);
            Assert.Equal(paused + PhysicsWorld.TimeStep, effect.Time);
            effect.Advance(0);
            Assert.Equal(0, effect.Time);
        }

        /// <summary>Independent native-expression/triangle reference predicts physical readback, including wide fitting and density.</summary>
        [Theory]
        [InlineData(640, 960, 172, 692, 174)]
        [InlineData(1280, 480, 605, 350, 92)]
        [InlineData(1280, 1920, 345, 1385, 93)]
        public void RasterPassInterpolatesVertexUvs(int width, int height, int x, int y, int green)
        {
            using SKBitmap source = new(width, height);
            using (SKCanvas content = new(source))
            using (SKPaint paint = new())
            {
                for (int row = 0; row < height; row++)
                {
                    paint.Color = new(123, (byte)(row % 256), 0);
                    content.DrawRect(0, row, width, 1, paint);
                }
            }
            using SKImage image = SKImage.FromBitmap(source);
            using SKBitmap output = new(width, height);
            using SKCanvas canvas = new(output);
            WaterDistortion effect = new();
            effect.DrawFrame(canvas, image, new(640, 960), width, height, 200, 60);
            SKColor pixel = output.GetPixel(x, y);
            Assert.Equal(123, pixel.Red);
            Assert.InRange(pixel.Green, green - 1, green + 1);
            effect.DrawFrame(canvas, image, new(640, 960), width, height, 200, 60);
            Assert.Equal(pixel, output.GetPixel(x, y));
            if (width == 1280 && height == 480)
            {
                Assert.Equal(new SKColor(24, 27, 32), output.GetPixel(5, 350));
            }
        }

        /// <summary>Frozen backgrounds use native zero-origin cropped UV bounds over FULL_SCREEN, with no height-fit.</summary>
        [Fact]
        public void BackgroundFallbackUsesNativeUvRange()
        {
            using SKBitmap source = new(16, 16);
            source.Erase(SKColors.Lime);
            using (SKCanvas content = new(source))
            using (SKPaint paint = new()
            {
                Color = SKColors.Red
            })
            {
                content.DrawRect(0, 0, 8, 8, paint);
            }

            using SKBitmap output = new(640, 960);
            using SKCanvas canvas = new(output);
            WaterDistortion effect = new();
            effect.DrawBackground(canvas, source, new AtlasFrame(4, 4, 8, 8, 0, 0, 16, 16, 1), new(640, 960), 640, 960, 200, 0);
            Assert.Equal(SKColors.Red, output.GetPixel(300, 300));
            Assert.Equal(SKColors.Red, output.GetPixel(500, 700));
            Assert.Equal(3.1415926f, effect.Time);
        }
    }
}
