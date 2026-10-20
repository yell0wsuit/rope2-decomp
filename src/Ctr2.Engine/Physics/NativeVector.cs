using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>The game's vector helpers as the iOS and Windows builds compute them.</summary>
    internal static class NativeVector
    {
        /// <summary>Native <c>vectRotate</c>: float sine and cosine of the narrowed angle. The Android build reads 1024-entry
        /// tables instead (<c>fmSin</c>/<c>fmCos</c>), which the Windows hint journals rule out.</summary>
        /// <param name="v">Vector to rotate.</param>
        /// <param name="radians">Angle in radians.</param>
        /// <returns>The rotated vector.</returns>
        internal static Vector2 Rotate(Vector2 v, double radians)
        {
            float cos = MathF.Cos((float)radians), sin = MathF.Sin((float)radians);
            return new((v.X * cos) - (v.Y * sin), (v.X * sin) + (v.Y * cos));
        }
    }
}
