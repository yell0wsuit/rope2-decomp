// Altered C# subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Scalar rotations preserve native multiply/add rounding rather than matrix SIMD fusion.</summary>
    internal static class PhysicsMath
    {
        internal static Vector2 Rotate(Vector2 point, float angle)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            return new((cosine * point.X) - (sine * point.Y), (sine * point.X) + (cosine * point.Y));
        }

        internal static Vector2 InverseRotate(Vector2 point, float angle)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            return new((cosine * point.X) + (sine * point.Y), (-sine * point.X) + (cosine * point.Y));
        }
    }
}
