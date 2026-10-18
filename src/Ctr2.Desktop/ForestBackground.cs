using System;
using System.IO;
using System.Numerics;

using Ctr2.Engine;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Owns the wide Windows forest background, fitted by height without stretching.</summary>
    internal sealed class ForestBackground : IDisposable
    {
        private readonly SKBitmap bitmap;
        private readonly AtlasFrame frame;

        internal ForestBackground(string cache)
        {
            // Windows supplies resolution 2 and a separate landscape variant, but no resolution 4.
            SelectedResource selected = ResourcePreview.Load(cache, 160, 2, 0);
            frame = selected.Frame;
            using SKCodec codec = SKCodec.Create(selected.TexturePath) ?? throw new InvalidDataException("Cannot decode forest background.");
            if (codec.Info.Width is <= 0 or > 8192 || codec.Info.Height is <= 0 or > 8192 ||
                (long)codec.Info.Width * codec.Info.Height * 4 > 128 * 1024 * 1024 ||
                frame.PixelX + frame.PixelWidth > codec.Info.Width || frame.PixelY + frame.PixelHeight > codec.Info.Height)
            {
                throw new InvalidDataException("Forest background exceeds preview bounds.");
            }
            bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode forest background.");
        }

        internal void Draw(SKCanvas canvas, Vector2 size)
        {
            float scale = size.Y / frame.LogicalHeight;
            float width = frame.LogicalWidth * scale;
            float left = (size.X - width) / 2;
            SKRect source = new(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight);
            canvas.DrawBitmap(bitmap, source, new SKRect(left, 0, left + width, size.Y), new SKSamplingOptions(SKFilterMode.Linear));
        }

        public void Dispose() => bitmap.Dispose();
    }
}
