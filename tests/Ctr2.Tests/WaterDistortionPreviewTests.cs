using System;
using System.Linq;
using System.Numerics;

using Ctr2.Desktop;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Generated scene pixels verify capture boundaries and fixed-clock presentation.</summary>
    public sealed class WaterDistortionPreviewTests
    {
        /// <summary>Active water deforms underwater objects; pause, repeated drawing, resize and restart preserve the proper clock.</summary>
        [Fact]
        public void SceneCaptureWarpsObjectsAndRestoresAfterResizeAndRestart()
        {
            using WaterSurfacePreviewTests.Cache cache = new(2, landmark: true);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2, play: true).Preview;
            GameSession session = preview.Session!;
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            SKColor[] initial = bitmap.Pixels;
            for (int i = 0; i < 60; i++)
            {
                session.Step();
            }

            Vector2 before = session.Simulation.Shapes[1].Body.Position;
            session.Paused = true;
            preview.Draw(canvas, 640, 960, 0);
            SKColor[] direct = bitmap.Pixels;
            session.Paused = false;
            preview.Draw(canvas, 640, 960, 0);
            SKColor[] warped = bitmap.Pixels;
            Assert.Contains(Enumerable.Range(300 * 640, 400 * 640), i => direct[i] != warped[i]);
            Assert.Equal(before, session.Simulation.Shapes[1].Body.Position);
            preview.Draw(canvas, 640, 960, 999);
            Assert.Equal(warped, bitmap.Pixels);
            using (SKBitmap wide = new(1280, 480))
            using (SKCanvas wideCanvas = new(wide))
            {
                preview.Draw(wideCanvas, 1280, 480, 0);
            }

            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(warped, bitmap.Pixels);
            session.Restart();
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(initial, bitmap.Pixels);
        }

        /// <summary>Native foreground splashes stay outside the full-scene postprocess pass.</summary>
        [Fact]
        public void SplashesDrawAfterDistortion()
        {
            using WaterSurfacePreviewTests.Cache cache = new(2, effects: true);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2, play: true).Preview;
            GameSession session = preview.Session!;
            session.Step();
            PhysicsBody body = session.Simulation.Candies[0];
            session.Simulation.World.SetTransform(body, new(2, 2.5f), 0);
            body.SetLinearVelocity(new(0, 2));
            session.Step();
            session.Step();
            WaterParticle[] drops = [.. session.Simulation.WaterEffects!.Drops.Where(p => p.Opacity == 1 && MathF.Abs(p.Position.X - (body.Position.X * 200)) > 12)];
            Assert.NotEmpty(drops);
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            foreach (WaterParticle drop in drops)
            {
                Assert.Equal(255, bitmap.GetPixel((int)drop.Position.X, (int)drop.Position.Y).Green);
            }
        }

        /// <summary>The frozen-scene branch actually selects native background-only UVs and restart restores full capture.</summary>
        [Fact]
        public void FrozenSceneUsesBackgroundFallback()
        {
            using WaterSurfacePreviewTests.Cache cache = new(2, forest: true);
            using LevelPreview preview = LevelPreview.Load(cache.Root, 2, 1, 2, play: true).Preview;
            using SKBitmap bitmap = new(640, 960);
            using SKCanvas canvas = new(bitmap);
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(SKColors.Lime, bitmap.GetPixel(200, 100));
            SKColor[] initial = bitmap.Pixels;
            preview.Session!.Paused = true;
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(SKColors.Blue, bitmap.GetPixel(200, 100));
            preview.Draw(canvas, 640, 960, 999);
            Assert.Equal(SKColors.Blue, bitmap.GetPixel(200, 100));
            preview.Session.Restart();
            preview.Draw(canvas, 640, 960, 0);
            Assert.Equal(initial, bitmap.Pixels);
        }
    }
}
