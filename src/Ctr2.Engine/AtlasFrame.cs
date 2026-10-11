using System;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine
{
    /// <summary>ImageInfo geometry: sample in pixels, lay out trimmed sprites in logical units.</summary>
    public sealed class AtlasFrame
    {
        /// <summary>Gets the left edge of the atlas sample rectangle in source pixels.</summary>
        /// <value>The left edge of the atlas sample rectangle in source pixels.</value>
        public float PixelX
        {
            get;
        }

        /// <summary>Gets the top edge of the atlas sample rectangle in source pixels.</summary>
        /// <value>The top edge of the atlas sample rectangle in source pixels.</value>
        public float PixelY
        {
            get;
        }

        /// <summary>Gets the width of the atlas sample rectangle in source pixels.</summary>
        /// <value>The width of the atlas sample rectangle in source pixels.</value>
        public float PixelWidth
        {
            get;
        }

        /// <summary>Gets the height of the atlas sample rectangle in source pixels.</summary>
        /// <value>The height of the atlas sample rectangle in source pixels.</value>
        public float PixelHeight
        {
            get;
        }

        /// <summary>Gets the trimmed sprite width in logical units.</summary>
        /// <value>The trimmed sprite width in logical units.</value>
        public float LogicalWidth
        {
            get;
        }

        /// <summary>Gets the trimmed sprite height in logical units.</summary>
        /// <value>The trimmed sprite height in logical units.</value>
        public float LogicalHeight
        {
            get;
        }

        /// <summary>Gets the trimmed sprite offset within its original canvas in logical units.</summary>
        /// <value>The trimmed sprite offset within its original canvas in logical units.</value>
        public Vector2 LogicalOffset
        {
            get;
        }

        /// <summary>Gets the untrimmed canvas dimensions in logical units.</summary>
        /// <value>The untrimmed canvas dimensions in logical units.</value>
        public Vector2 LogicalCanvas
        {
            get;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AtlasFrame"/> class with explicit pixel geometry and
        /// density.
        /// </summary>
        /// <param name="x">Left edge of the sample rectangle in source pixels.</param>
        /// <param name="y">Top edge of the sample rectangle in source pixels.</param>
        /// <param name="width">Positive trimmed width in source pixels.</param>
        /// <param name="height">Positive trimmed height in source pixels.</param>
        /// <param name="offsetX">Horizontal trim offset in source pixels; negative offsets are permitted.</param>
        /// <param name="offsetY">Vertical trim offset in source pixels; negative offsets are permitted.</param>
        /// <param name="preCutWidth">Nonnegative original canvas width in source pixels.</param>
        /// <param name="preCutHeight">Nonnegative original canvas height in source pixels.</param>
        /// <param name="pixelsPerLogicalUnit">
        /// Positive explicit resource density used to convert layout dimensions.
        /// </param>
        /// <remarks>
        /// Sample coordinates remain in pixels. Layout dimensions, offsets, and canvas size are divided by
        /// density. Texture bounds are checked by the consumer; a zero canvas dimension represents unavailable
        /// canvas metadata.
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">
        /// A value is nonfinite, the sample origin or canvas size is negative, or a sample dimension or density
        /// is not positive.
        /// </exception>
        public AtlasFrame(float x, float y, float width, float height, float offsetX, float offsetY,
            float preCutWidth, float preCutHeight, float pixelsPerLogicalUnit)
        {
            float[] values = [x, y, width, height, offsetX, offsetY, preCutWidth, preCutHeight, pixelsPerLogicalUnit];
            if (values.Any(v => !float.IsFinite(v)) || width <= 0 || height <= 0 || x < 0 || y < 0 ||
                preCutWidth < 0 || preCutHeight < 0 || pixelsPerLogicalUnit <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pixelsPerLogicalUnit));
            }

            PixelX = x;
            PixelY = y;
            PixelWidth = width;
            PixelHeight = height;
            LogicalWidth = width / pixelsPerLogicalUnit;
            LogicalHeight = height / pixelsPerLogicalUnit;
            LogicalOffset = new(offsetX / pixelsPerLogicalUnit, offsetY / pixelsPerLogicalUnit);
            LogicalCanvas = new(preCutWidth / pixelsPerLogicalUnit, preCutHeight / pixelsPerLogicalUnit);
        }
    }
}
