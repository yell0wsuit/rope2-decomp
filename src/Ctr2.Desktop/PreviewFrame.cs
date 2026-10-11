using Ctr2.Engine;

namespace Ctr2.Desktop
{
    /// <summary>Optional local sidecar produced from a matching build's ImageInfo.</summary>
    /// <param name="X">Left edge of the sample rectangle in source pixels.</param>
    /// <param name="Y">Top edge of the sample rectangle in source pixels.</param>
    /// <param name="Width">Positive trimmed width in source pixels.</param>
    /// <param name="Height">Positive trimmed height in source pixels.</param>
    /// <param name="OffsetX">Horizontal trim offset in source pixels; negative offsets are permitted.</param>
    /// <param name="OffsetY">Vertical trim offset in source pixels; negative offsets are permitted.</param>
    /// <param name="PreCutWidth">Nonnegative original canvas width in source pixels.</param>
    /// <param name="PreCutHeight">Nonnegative original canvas height in source pixels.</param>
    /// <param name="PixelsPerLogicalUnit">Positive explicit resource density used to convert layout dimensions.</param>
    public sealed record PreviewFrame(float X, float Y, float Width, float Height, float OffsetX,
        float OffsetY, float PreCutWidth, float PreCutHeight, float PixelsPerLogicalUnit)
    {
        /// <summary>Validates the sidecar values and creates density-aware frame geometry.</summary>
        /// <returns>
        /// An atlas frame preserving source pixel coordinates and converting layout to logical units.
        /// </returns>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// The sidecar contains invalid dimensions, density, coordinates, or nonfinite values.
        /// </exception>
        public AtlasFrame ToFrame()
        {
            return new(X, Y, Width, Height, OffsetX, OffsetY, PreCutWidth, PreCutHeight, PixelsPerLogicalUnit);
        }
    }
}
