using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Windows atlas 250 surface, traces and particles in the native rear/front draw passes.</summary>
    internal sealed class WaterSurface : IDisposable
    {
        private readonly SKBitmap bitmap;
        private readonly SKShader lightShader;
        private readonly AtlasFrame[] frames;
        private readonly WaterVisualDefinition design;
        internal float Level => design.Level;
        private int tick;
        private float frontSoft, backSoft;
        private float frontX, backX;
        private WaterEffects? staticEffects;

        internal WaterSurface(string cache, int resolution, WaterVisualDefinition design)
        {
            this.design = design;
            SelectedResource[] selected = [.. Enumerable.Range(0, 12).Select(q => ResourcePreview.Load(cache, 250, resolution, q))];
            using SKCodec codec = SKCodec.Create(selected[0].TexturePath) ?? throw new InvalidDataException("Cannot decode water texture.");
            if (codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192 ||
                (long)codec.Info.Width * codec.Info.Height * 4 > 128 * 1024 * 1024 || selected.Any(r => r.TexturePath != selected[0].TexturePath))
            {
                throw new InvalidDataException("Water texture exceeds preview bounds.");
            }
            frames = [.. selected.Select(r => r.Frame)];
            if (frames.Any(f => f.PixelX + f.PixelWidth > codec.Info.Width || f.PixelY + f.PixelHeight > codec.Info.Height))
            {
                throw new InvalidDataException("Water quad exceeds texture bounds.");
            }
            bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode water texture.");
            // RAW textures and node colors are premultiplied before native SRC_ALPHA/ONE.
            // Multiply the sampled color by its sampled alpha, after linear filtering.
            using SKShader texture = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
            lightShader = SKShader.CreateCompose(texture, texture, SKBlendMode.DstIn);
            Reset();
        }

        private void Reset()
        {
            tick = 0;
            staticEffects = null;
            frontSoft = backSoft = frontX = 0;
            backX = frames[8].LogicalOffset.X - frames[7].LogicalOffset.X;
        }

        private void Advance(int target)
        {
            if (target < tick)
            {
                Reset();
            }
            static float Scroll(float x, float speed, float width)
            {
                x += speed * PhysicsWorld.TimeStep;
                if (x > -width)
                {
                    if (x >= 0)
                    {
                        x -= width;
                    }
                }
                else
                {
                    x += width;
                }
                return x;
            }
            for (; tick < target; tick++)
            {
                frontSoft = Scroll(frontSoft, design.Speed, frames[8].LogicalWidth);
                backSoft = Scroll(backSoft, -design.Speed, frames[7].LogicalWidth);
                frontX = (int)frontSoft;
                backX = (int)backSoft;
            }
        }

        internal void Draw(SKCanvas canvas, Vector2 size, int fixedTick, bool foreground, WaterEffects? effects = null)
        {
            int previous = fixedTick < tick ? 0 : tick;
            Advance(fixedTick);
            if (effects == null)
            {
                staticEffects ??= new(size, design.Level);
                for (int i = previous; i < fixedTick; i++)
                {
                    staticEffects.Step();
                }
                effects = staticEffects;
            }
            using SKPaint paint = new()
            {
                IsAntialias = false
            };
            void Quad(int index, float x, float y, float width, float height)
            {
                if (width <= 0 || height <= 0)
                {
                    return;
                }
                AtlasFrame frame = frames[index];
                if (index is 3 or 4)
                {
                    _ = canvas.Save();
                    canvas.Translate(x, y);
                    canvas.Scale(width / frame.PixelWidth, height / frame.PixelHeight);
                    canvas.Translate(-frame.PixelX, -frame.PixelY);
                    paint.Shader = lightShader;
                    canvas.DrawRect(new SKRect(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight), paint);
                    paint.Shader = null;
                    canvas.Restore();
                    return;
                }
                canvas.DrawBitmap(bitmap, new SKRect(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight),
                    new SKRect(x, y, x + width, y + height), new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
            float scale = design.MinimumScale;
            if (design.ScaleTime > 0)
            {
                double phase = fixedTick * (double)PhysicsWorld.TimeStep % (2 * (double)design.ScaleTime);
                bool rising = phase < design.ScaleTime;
                float progress = (float)((rising ? phase : phase - design.ScaleTime) / design.ScaleTime);
                float eased = rising ? 1 - ((1 - progress) * (1 - progress)) : progress * progress;
                scale = rising ? design.MinimumScale + ((design.MaximumScale - design.MinimumScale) * eased)
                    : design.MaximumScale + ((design.MinimumScale - design.MaximumScale) * eased);
            }
            void Wave(int index, float x, float offsetY)
            {
                AtlasFrame frame = frames[index];
                float height = frame.LogicalHeight * scale;
                float y = design.Level + offsetY + ((frame.LogicalHeight - height) / 2);
                int count = (int)((size.X / frame.LogicalWidth) + 2);
                for (int i = 0; i < count; i++)
                {
                    Quad(index, x + (i * frame.LogicalWidth), y, frame.LogicalWidth, height);
                }
            }
            if (!foreground)
            {
                Wave(7, backX, frames[7].LogicalOffset.Y - frames[8].LogicalOffset.Y);
                return;
            }
            float upperY = design.Level + 11;
            Quad(6, 0, upperY, size.X, frames[6].LogicalHeight);
            float lowerY = upperY + frames[6].LogicalHeight;
            Quad(5, 0, lowerY, size.X, size.Y - lowerY);
            Wave(8, frontX, 0);
            paint.BlendMode = SKBlendMode.Plus;
            foreach (WaterLight light in effects.Lights)
            {
                AtlasFrame frame = frames[light.Quad];
                float quarter = frame.LogicalWidth / 4;
                float x = -quarter + (light.HorizontalSample % ((uint)(size.X + (quarter * 2)) + 1));
                paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(light.Opacity * light.Opacity, 0, 1) * 255));
                Quad(light.Quad, x, design.Level, frame.LogicalWidth, frame.LogicalHeight);
            }
            paint.BlendMode = SKBlendMode.SrcOver;
            foreach (WaterParticle bubble in effects.Bubbles)
            {
                DrawParticle(canvas, bubble, paint);
            }
        }

        internal void DrawDrops(SKCanvas canvas, WaterEffects? effects)
        {
            if (effects == null)
            {
                return;
            }

            using SKPaint paint = new()
            {
                IsAntialias = false
            };
            foreach (WaterParticle drop in effects.Drops)
            {
                DrawParticle(canvas, drop, paint);
            }
        }

        private void DrawParticle(SKCanvas canvas, WaterParticle particle, SKPaint paint)
        {
            AtlasFrame frame = frames[particle.Quad];
            paint.Color = SKColors.White.WithAlpha((byte)(particle.Opacity * 255));
            int saved = canvas.Save();
            try
            {
                Vector2 center = particle.Position;
                if (particle.Quad < 3)
                {
                    center += particle.Scale * new Vector2(particle.HarmonicX + (frame.LogicalWidth / 2), frame.LogicalHeight / 2);
                }
                canvas.Translate(center.X, center.Y);
                canvas.RotateRadians(particle.Rotation);
                canvas.Scale(particle.Scale.X, particle.Scale.Y * particle.BounceY);
                canvas.DrawBitmap(bitmap, new SKRect(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight),
                    new SKRect(-frame.LogicalWidth / 2, -frame.LogicalHeight / 2, frame.LogicalWidth / 2, frame.LogicalHeight / 2),
                    new SKSamplingOptions(SKFilterMode.Linear), paint);
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        public void Dispose()
        {
            lightShader.Dispose();
            bitmap.Dispose();
        }
    }
}
