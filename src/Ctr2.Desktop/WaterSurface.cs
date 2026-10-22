using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Windows atlas 250 waves and tint in the native rear/front draw passes.</summary>
    internal sealed class WaterSurface : IDisposable
    {
        private readonly SKBitmap bitmap;
        private readonly AtlasFrame[] frames;
        private readonly WaterVisualDefinition design;
        private int tick;
        private float frontSoft, backSoft;
        private float frontX, backX;

        internal WaterSurface(string cache, int resolution, WaterVisualDefinition design)
        {
            this.design = design;
            SelectedResource[] selected = [.. Enumerable.Range(5, 4).Select(q => ResourcePreview.Load(cache, 250, resolution, q))];
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
            Reset();
        }

        private void Reset()
        {
            tick = 0;
            frontSoft = backSoft = frontX = 0;
            backX = frames[3].LogicalOffset.X - frames[2].LogicalOffset.X;
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
                frontSoft = Scroll(frontSoft, design.Speed, frames[3].LogicalWidth);
                backSoft = Scroll(backSoft, -design.Speed, frames[2].LogicalWidth);
                frontX = (int)frontSoft;
                backX = (int)backSoft;
            }
        }

        internal void Draw(SKCanvas canvas, Vector2 size, int fixedTick, bool foreground)
        {
            Advance(fixedTick);
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
                Wave(2, backX, frames[2].LogicalOffset.Y - frames[3].LogicalOffset.Y);
                return;
            }
            float upperY = design.Level + 11;
            Quad(1, 0, upperY, size.X, frames[1].LogicalHeight);
            float lowerY = upperY + frames[1].LogicalHeight;
            Quad(0, 0, lowerY, size.X, size.Y - lowerY);
            Wave(3, frontX, 0);
        }

        public void Dispose()
        {
            bitmap.Dispose();
        }
    }
}
