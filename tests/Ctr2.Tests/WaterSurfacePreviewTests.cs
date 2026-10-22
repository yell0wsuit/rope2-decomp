using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Generated Windows atlas pixels verify water geometry and ordering without game assets.</summary>
    public sealed class WaterSurfacePreviewTests
    {
        internal sealed class Cache : IDisposable
        {
            internal string Root { get; } = Path.Combine(Path.GetTempPath(), "ctr2-water-surface-" + Guid.NewGuid());
            internal Cache(int resolution, bool translucent = false, string maximumScale = "1", bool effects = false, byte effectAlpha = 255, bool landmark = false, bool forest = false)
            {
                string assets = Path.Combine(Root, "assets"), maps = Path.Combine(assets, "maps");
                _ = Directory.CreateDirectory(maps);
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='2'><file short='water.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "water.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='20' y='20'/><omnom x='250' y='108'/><circle_materialized x='100' y='99' radius='12' material='wood_dynamic'/><water x='160' y='100'/></layer></map>");
                if (forest)
                {
                    string levelPath = Path.Combine(maps, "water.xml");
                    File.WriteAllText(levelPath, File.ReadAllText(levelPath).Replace("height='480'/>", "height='480'/><gameDesign location='forest'/>", StringComparison.Ordinal));
                }
                if (landmark)
                {
                    string levelPath = Path.Combine(maps, "water.xml");
                    File.WriteAllText(levelPath, File.ReadAllText(levelPath).Replace("<water x=", "<circle_materialized x='80' y='250' radius='12' material='wood_dynamic'/><water x=", StringComparison.Ordinal));
                }
                string design = RopeTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                    "<water_design id='water_design' gravity_multiplier='-0.6' water_damping='3' water_angular_damping='1' gfx_wave_speed='60' gfx_wave_scale_y_min='1' gfx_wave_scale_y_max='1' gfx_wave_scale_time='0.6'/></layer></map>", StringComparison.Ordinal)
                    .Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"", StringComparison.Ordinal)
                    .Replace("gravityScale='0.7'", "gravityScale='0'", StringComparison.Ordinal).Replace("gravityScale=\"0.9\"", "gravityScale=\"0\"", StringComparison.Ordinal);
                design = design.Replace("gfx_wave_scale_y_max='1'", "gfx_wave_scale_y_max='" + maximumScale + "'", StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(maps, "!_MasterGameDesignDocument.xml"), design);
                int density = resolution == 2 ? 1 : 2;
                using (SKBitmap texture = new(40 * density, 16 * density))
                {
                    texture.Erase(SKColors.Transparent);
                    using SKCanvas canvas = new(texture);
                    canvas.Scale(density);
                    using SKPaint paint = new()
                    {
                        Color = translucent ? SKColors.Blue.WithAlpha(128) : SKColors.Blue
                    };
                    // Windows atlases extrude the edge texel into the padding used by linear sampling.
                    canvas.DrawRect(0, 0, 4, 5, paint);
                    paint.Color = translucent ? SKColors.Cyan.WithAlpha(128) : SKColors.Cyan;
                    canvas.DrawRect(4, 0, 4, 8, paint);
                    paint.Color = SKColors.Red;
                    canvas.DrawRect(8, 0, 8, 4, paint);
                    paint.Color = SKColors.Yellow;
                    canvas.DrawRect(16, 0, 8, 4, paint);
                    paint.Color = SKColors.Purple;
                    canvas.DrawRect(8, 7, 8, 6, paint);
                    paint.Color = SKColors.HotPink;
                    canvas.DrawRect(16, 7, 8, 6, paint);
                    paint.Color = SKColors.Lime.WithAlpha(effectAlpha);
                    canvas.DrawRect(24, 0, 8, 8, paint);
                    using SKData png = texture.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(assets, "surface.png"), png.ToArray());
                }
                string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets, "surface.png"))));
                ResourceQuad Quad(int x, int y, int width, int height, int offsetX = 0, int offsetY = 0)
                {
                    return new(x * density, y * density, width * density, height * density, offsetX * density, offsetY * density);
                }
                ResourceQuad green = Quad(24, 0, 8, 8);
                ResourceQuad clear = effects ? green : Quad(34, 0, 4, 4);
                ResourceQuad[] water = [clear, clear, clear, clear, clear, Quad(0, 0, 4, 4), Quad(4, 0, 4, 8), Quad(8, 0, 16, 4, 4, 10), Quad(8, 8, 16, 4, 8, 12), clear, clear, clear];
                ResourceCatalog catalog = new(1, "synthetic", "", [new(85, "character.pb", 7, resolution, null, 0, 0, []),
                    new(133, "surface.png", 0, resolution, density, 40 * density, 16 * density, [green, green], hash),
                    new(160, "surface.png", 0, 2, density, 40 * density, 16 * density, [green], hash),
                    new(7, "surface.png", 0, resolution, density, 40 * density, 16 * density, [green], hash),
                    new(250, "surface.png", 0, resolution, density, 40 * density, 16 * density, water, hash)]);
                File.WriteAllText(Path.Combine(Root, "resources.json"), JsonSerializer.Serialize(catalog));
                string[] names = ["IDLE", "MOUTH_OPEN", "MOUTH_CLOSE", "CHEWING", "PUZZLED", "FAIL", "FALLING", "FALLING_MOUTH_OPEN",
                    "FALLING_MOUTH_CLOSE", "FALLING_CHEWING", "ROLLING", "STAND_UP", "WATER_DIVE", "WATER_SWIM", "WATER_MOUTH_OPEN",
                    "WATER_MOUTH_CLOSE", "WATER_CHEWING", "WATER_FAIL"];
                byte[] track = AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                    (2, AnimationPreviewTests.Message((1, 1f), (2, 0f), (3, 0f))));
                byte[] part = AnimationPreviewTests.Message([(1, 1), (3, 7), .. names.Select((_, i) => (10, (object)AnimationPreviewTests.Message((1, i), (5, track))))]);
                File.WriteAllBytes(Path.Combine(assets, "character.pb"), AnimationPreviewTests.Message([(1, 2), (7, 8f), (8, 8f), (9, part),
                    .. names.Select((name, i) => (10, (object)AnimationPreviewTests.Message((1, i), (2, name))))]));
            }
            public void Dispose()
            {
                Directory.Delete(Root, true);
            }
        }

        /// <summary>Rear waves lie behind objects, front waves cover them, and tint fills from the native eleven-unit shift.</summary>
        [Theory]
        [InlineData(2, 640, 960)]
        [InlineData(4, 640, 960)]
        [InlineData(2, 1280, 480)]
        public void UsesWindowsQuadsAndNativeLayerOrder(int resolution, int width, int height)
        {
            using Cache cache = new(resolution);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, resolution, play: true).Preview;
            using SKBitmap bitmap = new(width, height);
            using SKCanvas canvas = new(bitmap);
            preview.Session!.Paused = true; // Isolate native wave/tint placement from postprocess resampling.
            preview.Draw(canvas, width, height, 0);
            Viewport viewport = Viewport.Fit(width, height, 640, 960);
            SKColor Pixel(float x, float y)
            {
                Vector2 pixel = viewport.ToScreen(new(x, y));
                return bitmap.GetPixel((int)pixel.X, (int)pixel.Y);
            }
            Assert.Equal(new SKColor(196, 150, 96), Pixel(200, 199));
            Assert.Equal(SKColors.HotPink, Pixel(200, 201));
            Assert.Equal(SKColors.Yellow, Pixel(320, 198));
            Assert.Equal(SKColors.Cyan, Pixel(320, 214));
            Assert.Equal(SKColors.Blue, Pixel(320, 230));
            Assert.Equal(SKColors.Blue, Pixel(320, 958));
            for (int x = 1; x < 640; x += 16)
            {
                Assert.Equal(SKColors.Purple, Pixel(x, 202));
            }
        }

        /// <summary>The source atlas alpha blends with the scene; the tint is not replaced by an opaque fill.</summary>
        [Fact]
        public void TintPreservesAtlasAlpha()
        {
            using Cache cache = new(2, translucent: true);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2, play: true).Preview;
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(new SKColor(21, 155, 152), bitmap.GetPixel(320, 214));
            Assert.Equal(new SKColor(21, 27, 152), bitmap.GetPixel(320, 500));
        }

        /// <summary>Static previews also render water, using the supplied clock and source ease-out wave scaling.</summary>
        [Fact]
        public void StaticPreviewUsesClockAndEasedScale()
        {
            using Cache cache = new(2, maximumScale: "2");
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2).Preview;
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(new SKColor(42, 55, 48), bitmap.GetPixel(320, 205));
            preview.Draw(canvas, 640, 960, .301);
            // Tick18: ease-out gives scale1.751 and covers y205; a linear scale1.501 would leave it dry.
            Assert.Contains(bitmap.GetPixel(320, 205), new[] { SKColors.Purple, SKColors.HotPink });
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(new SKColor(42, 55, 48), bitmap.GetPixel(320, 205));
        }

        /// <summary>Drawing uses fixed tick phase, repeats wave strips, preserves simulation, and resets on restart.</summary>
        [Fact]
        public void WaveScrollUsesFixedTicksWithoutChangingPhysics()
        {
            using Cache cache = new(2);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2, play: true).Preview;
            GameSession session = preview.Session!;
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            SKColor Surface(double seconds)
            {
                preview.Draw(canvas, 640, 960, seconds);
                return bitmap.GetPixel(320, 202);
            }
            Assert.Equal(SKColors.Purple, Surface(0));
            Assert.Equal(SKColors.Yellow, bitmap.GetPixel(320, 198));
            Vector2 before = session.Simulation.Candies[0].Position;
            Assert.Equal(SKColors.Purple, Surface(999));
            Assert.Equal(before, session.Simulation.Candies[0].Position);
            for (int i = 0; i < 4; i++)
            {
                session.Step();
            }
            Assert.Equal(SKColors.HotPink, Surface(0));
            Assert.Equal(SKColors.Red, bitmap.GetPixel(320, 198));
            session.Paused = true;
            session.Step();
            Assert.Equal(SKColors.HotPink, Surface(10));
            session.Paused = false;
            for (int i = 0; i < 20; i++)
            {
                session.Step();
            }
            Assert.Equal(SKColors.Purple, Surface(0));
            Assert.Equal(SKColors.Yellow, bitmap.GetPixel(320, 198));
            session.Restart();
            Assert.Equal(SKColors.Purple, Surface(0));
        }

        /// <summary>Additive traces and centered bubbles sample their Windows quads at both densities, without advancing in Draw.</summary>
        [Theory]
        [InlineData(2, 255)]
        [InlineData(4, 255)]
        [InlineData(2, 128)]
        public void EffectQuadsRenderAtLogicalPositionsAndPause(int resolution, byte alpha)
        {
            using Cache cache = new(resolution, effects: true, effectAlpha: alpha);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, resolution, play: true).Preview;
            GameSession session = preview.Session!;
            for (int i = 0; i < 60; i++)
            {
                session.Step();
            }

            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            WaterLight light = session.Simulation.WaterEffects!.Lights.First(l => l.Opacity > .1f);
            int x = (int)(-2 + (light.HorizontalSample % 645)) + 4;
            SKColor pixel = bitmap.GetPixel(x, 206);
            Assert.Equal(42, pixel.Red);
            int contribution = session.Simulation.WaterEffects.Lights.Where(l =>
            {
                float left = -2 + (l.HorizontalSample % 645);
                return x >= left && x < left + 8;
            }).Sum(l => (((byte)(l.Opacity * l.Opacity * 255) * alpha * alpha) + 32512) / 65025);
            Assert.InRange(pixel.Green, 55 + contribution - 1, 55 + contribution + 1);
            Assert.False(session.Simulation.Tap(new(300, 500)));
            for (int i = 0; i < 20; i++)
            {
                session.Step();
            }

            WaterParticle bubble = session.Simulation.WaterEffects.Bubbles[0];
            session.Paused = true;
            preview.Draw(canvas, 640, 960, 0);
            // Native left/top wrapper has zero dimensions; its scale also scales the child harmonic shift.
            Vector2 center = bubble.Position + (bubble.Scale * new Vector2(bubble.HarmonicX + 4, 4));
            SKColor bubblePixel = bitmap.GetPixel((int)center.X, (int)center.Y);
            Assert.True(bubblePixel.Green > 0);
            WaterParticle[] before = [.. session.Simulation.WaterEffects.Bubbles];
            preview.Draw(canvas, 640, 960, 999);
            Assert.Equal(bubblePixel, bitmap.GetPixel((int)center.X, (int)center.Y));
            Assert.Equal(before, session.Simulation.WaterEffects.Bubbles.ToArray());
            session.Restart();
            Assert.Empty(session.Simulation.WaterEffects!.Bubbles);
            Assert.All(session.Simulation.WaterEffects.Lights, l => Assert.Equal(0, l.Opacity));
        }
    }
}
