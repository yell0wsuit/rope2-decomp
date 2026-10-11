using System;
using System.Numerics;

using Ctr2.Engine;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Diagnostic atlas/frame preview; this is not the gameplay renderer.</summary>
    public static class AssetPreview
    {
        /// <summary>Draws a whole texture or trimmed atlas frame in a centered, aspect-preserving preview.</summary>
        /// <param name="canvas">Destination canvas; its contents are cleared, and the caller retains ownership.</param>
        /// <param name="image">Decoded atlas bitmap; the caller retains ownership.</param>
        /// <param name="width">Positive destination width in pixels.</param>
        /// <param name="height">Positive destination height in pixels.</param>
        /// <param name="frame">
        /// Optional sample rectangle, trim offsets, canvas dimensions, and resource density. Null previews the
        /// whole image at density one.
        /// </param>
        /// <remarks>
        /// The original canvas is shaded and the sampled sprite uses nearest-neighbor filtering. Layout uses
        /// logical units, while atlas sampling uses source pixels.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Frame canvas dimensions are not positive, or the sample rectangle extends outside the bitmap.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">Destination dimensions are not positive.</exception>
        public static void Draw(SKCanvas canvas, SKBitmap image, int width, int height, AtlasFrame? frame = null)
        {
            canvas.Clear(new SKColor(24, 27, 32));
            float logicalWidth = frame?.LogicalCanvas.X ?? image.Width;
            float logicalHeight = frame?.LogicalCanvas.Y ?? image.Height;
            if (logicalWidth <= 0 || logicalHeight <= 0)
            {
                throw new ArgumentException("Frame preview requires positive pre-cut canvas dimensions.");
            }

            if (frame != null && (frame.PixelX + frame.PixelWidth > image.Width || frame.PixelY + frame.PixelHeight > image.Height))
            {
                throw new ArgumentException("Atlas frame lies outside the texture.");
            }

            Viewport viewport = Viewport.Fit(width, height, logicalWidth, logicalHeight);
            Vector2 start = viewport.ToScreen(Vector2.Zero), end = viewport.ToScreen(new(logicalWidth, logicalHeight));
            using SKPaint background = new()
            {
                Color = new SKColor(68, 73, 81)
            };
            canvas.DrawRect(new SKRect(start.X, start.Y, end.X, end.Y), background);
            Vector2 offset = frame?.LogicalOffset ?? Vector2.Zero;
            Vector2 size = frame == null ? new(image.Width, image.Height) : new(frame.LogicalWidth, frame.LogicalHeight);
            Vector2 a = viewport.ToScreen(offset), b = viewport.ToScreen(offset + size);
            SKRect src = frame == null ? new SKRect(0, 0, image.Width, image.Height) :
                new SKRect(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight);
            using SKPaint paint = new()
            {
                IsAntialias = false
            };
            canvas.DrawBitmap(image, src, new SKRect(a.X, a.Y, b.X, b.Y), new SKSamplingOptions(SKFilterMode.Nearest), paint);
        }
    }
}
