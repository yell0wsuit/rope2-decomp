using System;
using System.Numerics;

using Ctr2.Engine.Framework;

namespace Ctr2.Engine
{
    /// <summary>Uniform fit and inverse input mapping through DX's original transform stack.</summary>
    public sealed class Viewport
    {
        /// <summary>Gets the transform from logical coordinates to output pixels.</summary>
        /// <value>The uniform scale and centered translation used for drawing.</value>
        public Matrix4x4 Transform
        {
            get;
        }

        /// <summary>Inverse drawing transform used to map pointer positions into logical coordinates.</summary>
        private readonly Matrix4x4 inverse;

        /// <summary>
        /// Initializes a new instance of the <see cref="Viewport"/> class from an invertible drawing transform.
        /// </summary>
        /// <param name="transform">Matrix mapping logical coordinates to output pixels.</param>
        /// <exception cref="ArgumentException">The transform cannot be inverted.</exception>
        private Viewport(Matrix4x4 transform)
        {
            Transform = transform;
            if (!Matrix4x4.Invert(transform, out inverse))
            {
                throw new ArgumentException("Singular viewport transform.");
            }
        }

        /// <summary>
        /// Fits a logical canvas into the output while preserving aspect ratio and centering any margins.
        /// </summary>
        /// <param name="width">Positive output width in pixels.</param>
        /// <param name="height">Positive output height in pixels.</param>
        /// <param name="logicalWidth">Positive finite logical canvas width.</param>
        /// <param name="logicalHeight">Positive finite logical canvas height.</param>
        /// <returns>The drawing transform and its inverse for input mapping.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An output dimension is not positive or a logical dimension is not positive and finite.
        /// </exception>
        /// <exception cref="ArgumentException">The resulting transform cannot be inverted.</exception>
        public static Viewport Fit(int width, int height, float logicalWidth, float logicalHeight)
        {
            if (width <= 0 || height <= 0 || !float.IsFinite(logicalWidth) || !float.IsFinite(logicalHeight) ||
                logicalWidth <= 0 || logicalHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            float scale = Math.Min(width / logicalWidth, height / logicalHeight);
            MatrixStack stack = new();
            stack.Translate((width - (logicalWidth * scale)) / 2, (height - (logicalHeight * scale)) / 2);
            stack.Scale(scale, scale);
            return new(stack.ModelView);
        }

        /// <summary>Maps a logical position into output pixels.</summary>
        /// <param name="logical">Position in logical canvas units.</param>
        /// <returns>The corresponding output pixel position.</returns>
        public Vector2 ToScreen(Vector2 logical)
        {
            return Vector2.Transform(logical, Transform);
        }

        /// <summary>Maps an output pixel position back into logical canvas units.</summary>
        /// <param name="screen">Position in output pixels, including any letterbox margins.</param>
        /// <returns>The corresponding logical position; positions outside the canvas are not clamped.</returns>
        public Vector2 ToLogical(Vector2 screen)
        {
            return Vector2.Transform(screen, inverse);
        }
    }
}
