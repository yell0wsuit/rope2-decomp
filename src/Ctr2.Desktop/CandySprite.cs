using System;
using System.IO;
using System.Numerics;

using Ctr2.Engine;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Owns the two restored-canvas classic candy layers; animated flare/masking are pending.</summary>
    internal sealed class CandySprite : IDisposable
    {
        /// <summary>Owned decoded atlas.</summary>
        private readonly SKBitmap bitmap;
        /// <summary>Validated bottom and main quads.</summary>
        private readonly AtlasFrame[] frames;

        /// <summary>Loads the supported Windows classic candy layers by original resource ID.</summary>
        /// <param name="cache">Imported matching Windows cache.</param>
        /// <param name="resolution">Known atlas density enum.</param>
        /// <exception cref="InvalidDataException">The texture or selected quads are invalid.</exception>
        public CandySprite(string cache, int resolution)
        {
            SelectedResource bottom = ResourcePreview.Load(cache, 133, resolution, 0);
            SelectedResource main = ResourcePreview.Load(cache, 133, resolution, 1);
            using SKCodec codec = SKCodec.Create(bottom.TexturePath) ?? throw new InvalidDataException("Cannot decode candy texture.");
            if (main.TexturePath != bottom.TexturePath || codec.Info.Width <= 0 || codec.Info.Height <= 0 || codec.Info.Width > 8192 || codec.Info.Height > 8192 ||
                (long)codec.Info.Width * codec.Info.Height * 4 > 128 * 1024 * 1024)
            {
                throw new InvalidDataException("Candy texture exceeds preview bounds.");
            }
            frames = [bottom.Frame, main.Frame];
            foreach (AtlasFrame frame in frames)
            {
                if (frame.PixelX + frame.PixelWidth > codec.Info.Width || frame.PixelY + frame.PixelHeight > codec.Info.Height)
                {
                    throw new InvalidDataException("Candy quad exceeds texture bounds.");
                }
            }
            bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode candy texture.");
        }

        /// <summary>Draws the layered candy at a logical scene position, preserving density and trim offsets.</summary>
        /// <param name="canvas">Scene-transformed caller-owned canvas.</param>
        /// <param name="position">Logical body center.</param>
        /// <param name="angle">Clockwise body rotation in radians.</param>
        public void Draw(SKCanvas canvas, Vector2 position, float angle = 0)
        {
            int saved = canvas.Save();
            try
            {
                canvas.Translate(position.X, position.Y);
                canvas.RotateRadians(angle);
                position = Vector2.Zero;
                const float scale = 0.92f * 0.92f;
                foreach (AtlasFrame frame in frames)
                {
                    Vector2 offset = (frame.LogicalOffset - (frame.LogicalCanvas / 2)) * scale;
                    SKRect source = new(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight);
                    SKRect destination = new(position.X + offset.X, position.Y + offset.Y,
                        position.X + offset.X + (frame.LogicalWidth * scale), position.Y + offset.Y + (frame.LogicalHeight * scale));
                    canvas.DrawBitmap(bitmap, source, destination, new SKSamplingOptions(SKFilterMode.Linear));
                }
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        /// <summary>Releases the owned native atlas.</summary>
        public void Dispose()
        {
            bitmap.Dispose();
        }
    }
}
