// Altered C# polygon geometry subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Mass properties of a polygon relative to its authored local body origin.</summary>
    /// <param name="Mass">Fixture mass in kilograms.</param>
    /// <param name="Center">Local center of mass in meters.</param>
    /// <param name="OriginInertia">Moment of inertia about the authored origin in kilogram square meters.</param>
    /// <param name="CenterInertia">Moment of inertia about the center of mass in kilogram square meters.</param>
    public sealed record PolygonMass(float Mass, Vector2 Center, float OriginInertia, float CenterInertia);

    /// <summary>Immutable strictly convex counterclockwise polygon geometry in local meters.</summary>
    public sealed class ConvexPolygon
    {
        /// <summary>Owned local vertices, independent of caller mutation.</summary>
        private readonly Vector2[] points;
        /// <summary>Owned outward unit face normals.</summary>
        private readonly Vector2[] normals;

        /// <summary>Gets local vertices in counterclockwise order.</summary>
        /// <value>A read-only view that preserves the authored body origin.</value>
        public IReadOnlyList<Vector2> Vertices
        {
            get;
        }

        /// <summary>Gets outward unit normals parallel to the vertex edge order.</summary>
        /// <value>Normal at index i belongs to edge i to i plus one.</value>
        public IReadOnlyList<Vector2> Normals
        {
            get;
        }

        /// <summary>Creates local convex geometry from caller-owned vertices.</summary>
        /// <param name="vertices">Three to eight counterclockwise local vertices in meters.</param>
        /// <exception cref="ArgumentException">Geometry is degenerate, nonconvex, clockwise or outside supported bounds.</exception>
        public ConvexPolygon(IReadOnlyList<Vector2> vertices)
        {
            ArgumentNullException.ThrowIfNull(vertices);
            if (vertices.Count is < 3 or > 8)
            {
                throw new ArgumentException("Convex polygons require three to eight vertices.", nameof(vertices));
            }
            points = [.. vertices];
            normals = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 p = points[i], edge = points[(i + 1) % points.Length] - p;
                if (!ValidPoint(p) || edge.LengthSquared() < 1e-10f)
                {
                    throw new ArgumentException("Invalid convex polygon vertex.", nameof(vertices));
                }
                normals[i] = PhysicsMath.Normalize(new(edge.Y, -edge.X));
                for (int j = 0; j < points.Length; j++)
                {
                    if (j != i && j != (i + 1) % points.Length && Vector2.Dot(normals[i], points[j] - p) >= -1e-7f)
                    {
                        throw new ArgumentException("Polygon must be strictly convex and counterclockwise.", nameof(vertices));
                    }
                }
            }
            Vertices = Array.AsReadOnly(points);
            Normals = Array.AsReadOnly(normals);
        }

        /// <summary>Computes fixture mass and moments of inertia about the origin and centroid.</summary>
        /// <param name="density">Nonnegative fixture density.</param>
        /// <returns>Local mass data.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Density is nonfinite, negative or greater than one million.</exception>
        public PolygonMass ComputeMass(float density)
        {
            if (!float.IsFinite(density) || density < 0 || density > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(density));
            }
            Vector2 reference = Vector2.Zero;
            foreach (Vector2 point in points)
            {
                reference += point;
            }
            reference *= 1f / points.Length;
            Vector2 relativeCenter = Vector2.Zero;
            float area = 0, inertia = 0;
            const float third = 1f / 3;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i] - reference, b = points[(i + 1) % points.Length] - reference;
                float cross = (a.X * b.Y) - (a.Y * b.X);
                float triangle = .5f * cross;
                area += triangle;
                relativeCenter += triangle * third * (a + b);
                float xx = (a.X * a.X) + (b.X * a.X) + (b.X * b.X);
                float yy = (a.Y * a.Y) + (b.Y * a.Y) + (b.Y * b.Y);
                inertia += .25f * third * cross * (xx + yy);
            }
            float mass = density * area;
            relativeCenter *= 1 / area;
            Vector2 center = relativeCenter + reference;
            float localInertia = density * inertia;
            float centerInertia = localInertia - (mass * Vector2.Dot(relativeCenter, relativeCenter));
            float originInertia = localInertia + (mass * (Vector2.Dot(center, center) - Vector2.Dot(relativeCenter, relativeCenter)));
            return new(mass, center, originInertia, centerInertia);
        }

        /// <summary>Snapshots polygon vertices using an authored body origin and rotation.</summary>
        /// <param name="origin">World-space body origin in meters, rather than its center of mass.</param>
        /// <param name="angle">Clockwise radians in the downward-positive world.</param>
        /// <returns>A read-only transformed vertex snapshot.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The origin or angle is invalid.</exception>
        public IReadOnlyList<Vector2> WorldVertices(Vector2 origin, float angle)
        {
            ValidateTransform(origin, angle);
            Matrix3x2 transform = Matrix3x2.CreateRotation(angle) * Matrix3x2.CreateTranslation(origin);
            return Array.AsReadOnly(points.Select(p => Vector2.Transform(p, transform)).ToArray());
        }

        /// <summary>Tests a circle against the transformed polygon, including its Box2D skin.</summary>
        /// <param name="center">Circle center in world meters.</param>
        /// <param name="radius">Positive circle radius in meters.</param>
        /// <param name="origin">Polygon body origin in world meters.</param>
        /// <param name="angle">Polygon clockwise rotation in radians.</param>
        /// <returns>True for overlap or sensor-distance tolerance at tangency.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The circle or transform is invalid.</exception>
        public bool OverlapsCircle(Vector2 center, float radius, Vector2 origin, float angle)
        {
            ValidateTransform(origin, angle);
            if (!ValidPoint(center) || !float.IsFinite(radius) || radius <= 0 || radius > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            Vector2 local = Vector2.Transform(center - origin, Matrix3x2.CreateRotation(-angle));
            float closest = float.PositiveInfinity;
            bool inside = true;
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 a = points[i], edge = points[(i + 1) % points.Length] - a;
                inside &= Vector2.Dot(local - a, normals[i]) <= 0;
                float t = Math.Clamp(Vector2.Dot(local - a, edge) / edge.LengthSquared(), 0, 1);
                closest = MathF.Min(closest, Vector2.DistanceSquared(local, a + (t * edge)));
            }
            float distance = radius + (2 * PhysicsWorld.LinearSlop) + (10 * 1.1920929e-7f);
            return inside || closest <= distance * distance;
        }

        /// <summary>Validates finite bounded polygon transforms before applying matrix operations.</summary>
        /// <param name="origin">Requested world-space body origin.</param>
        /// <param name="angle">Requested clockwise radians.</param>
        /// <exception cref="ArgumentOutOfRangeException">The origin or angle is invalid.</exception>
        private static void ValidateTransform(Vector2 origin, float angle)
        {
            if (!ValidPoint(origin) || !float.IsFinite(angle) || MathF.Abs(angle) > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }
        }

        /// <summary>Tests the finite geometry range shared with static terrain.</summary>
        /// <param name="point">Requested local or world-space point.</param>
        /// <returns>True for a finite point within ten thousand meters per axis.</returns>
        private static bool ValidPoint(Vector2 point)
        {
            return float.IsFinite(point.X) && float.IsFinite(point.Y) && MathF.Abs(point.X) <= 10000 && MathF.Abs(point.Y) <= 10000;
        }
    }
}
