using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Owns the matching Windows first-level leaf quads and draws their fixed-step snapshots.</summary>
    internal sealed class ForestLeafSprites : IDisposable
    {
        private readonly SKBitmap bitmap;
        private readonly AtlasFrame[] frames;

        internal ForestLeafSprites(string cache, int resolution)
        {
            // Windows x86 1.0.0.46 uses 0xA30009..0xA3000B, not Android's 0xA20009..0xA2000B.
            SelectedResource[] selected = [.. Enumerable.Range(9, 3).Select(q => ResourcePreview.Load(cache, 163, resolution, q))];
            if (selected.Any(r => r.TexturePath != selected[0].TexturePath))
            {
                throw new InvalidDataException("Arrival leaf quads must share a texture.");
            }
            using SKCodec codec = SKCodec.Create(selected[0].TexturePath) ?? throw new InvalidDataException("Cannot decode arrival leaf texture.");
            if (codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192 || (long)codec.Info.Width * codec.Info.Height * 4 > 128 * 1024 * 1024)
            {
                throw new InvalidDataException("Arrival leaf texture exceeds preview bounds.");
            }
            frames = [.. selected.Select(r => r.Frame)];
            if (frames.Any(f => f.PixelX + f.PixelWidth > codec.Info.Width || f.PixelY + f.PixelHeight > codec.Info.Height))
            {
                throw new InvalidDataException("Arrival leaf quad exceeds texture bounds.");
            }
            bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode arrival leaf texture.");
        }

        internal void Draw(SKCanvas canvas, IReadOnlyList<ForestLeaf> particles)
        {
            using SKPaint paint = new();
            foreach (ForestLeaf particle in particles)
            {
                AtlasFrame frame = frames[particle.Variant];
                paint.Color = SKColors.White.WithAlpha((byte)(255 * particle.Opacity));
                int saved = canvas.Save();
                try
                {
                    canvas.Translate(particle.Position.X, particle.Position.Y);
                    canvas.RotateRadians(particle.Rotation);
                    canvas.Scale(particle.Scale.X, particle.Scale.Y);
                    SKRect source = new(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight);
                    float w = frame.LogicalWidth / 2, h = frame.LogicalHeight / 2;
                    // Native particles use the trimmed quad dimensions, centered on the particle position.
                    canvas.DrawBitmap(bitmap, source, new SKRect(-w, -h, w, h), new SKSamplingOptions(SKFilterMode.Linear), paint);
                }
                finally { canvas.RestoreToCount(saved); }
            }
        }

        public void Dispose()
        {
            bitmap.Dispose();
        }
    }
}
