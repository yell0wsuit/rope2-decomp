using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;


namespace Ctr2.Engine.Physics
{
    /// <summary>An accepted cut suitable for replay before the same fixed tick.</summary>
    /// <param name="Tick">Completed ticks before the cut.</param>
    /// <param name="Rope">Zero-based authored rope index.</param>
    /// <param name="Segment">Zero-based pre-cut bone index.</param>
    /// <param name="Point">Crossing in logical scene units.</param>
    /// <param name="Impulse">Impulse applied to each new tip in kilogram-meters per second.</param>
    public sealed record RopeCut(int Tick, int Rope, int Segment, Vector2 Point, Vector2 Impulse);

    public sealed partial class CandySimulation
    {
        /// <summary>Matching master definition used to create physical cut tips.</summary>
        private readonly IReadOnlyDictionary<string, string> pointDesign;

        /// <summary>Splits one active bone into two disconnected physical fragments.</summary>
        /// <param name="ropeIndex">Zero-based authored rope.</param>
        /// <param name="segment">Zero-based pre-cut bone.</param>
        /// <param name="point">Finite crossing in logical scene units.</param>
        /// <param name="impulse">Optional finite impulse on each new tip; replay defaults to zero.</param>
        /// <returns>True if this call cut an intact rope.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An index or vector is invalid.</exception>
        /// <exception cref="InvalidDataException">The topology change exceeds world budgets.</exception>
        public bool Cut(int ropeIndex, int segment, Vector2 point, Vector2 impulse = default)
        {
            if ((uint)ropeIndex >= ropes.Count || !ValidVector(point) || !ValidVector(impulse))
            {
                throw new ArgumentOutOfRangeException(nameof(ropeIndex));
            }
            RopeChain rope = ropes[ropeIndex];
            if (rope.CutGap >= 0 || rope.Released)
            {
                return false;
            }
            if ((uint)segment >= rope.Joints.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(segment));
            }
            if (!World.CanAdd(2, 2))
            {
                throw new InvalidDataException("Cut fragments exceed physics budgets.");
            }
            // Like the native replay cut: drop the stretch spring and the cut joint, then add two tips at the crossing.
            if (rope.Stretch != null)
            {
                _ = World.Remove(rope.Stretch);
                rope.Stretch = null;
            }
            _ = World.Remove(rope.Joints[segment]);
            PhysicsBody a = rope.Points[segment], b = rope.Points[segment + 1];
            Vector2 first = BodyPosition(rope, segment), second = BodyPosition(rope, segment + 1);
            Vector2 position = Meters(point);
            PhysicsBody left = BodyFactory.Create(World, pointDesign, position, 0), right = BodyFactory.Create(World, pointDesign, position, 0);
            left.ApplyLinearImpulse(impulse, position);
            right.ApplyLinearImpulse(impulse, position);
            bool fromHead = segment == 0, toTail = segment + 1 == rope.Points.Count - 1;
            rope.Joints[segment] = World.AddRope(a, left, new PhysicsRopeDefinition(fromHead ? Meters(rope.HeadShift) : Vector2.Zero, Vector2.Zero,
                Meters(point - first).Length()));
            rope.Joints.Insert(segment + 1, World.AddRope(right, b, new PhysicsRopeDefinition(Vector2.Zero, toTail ? Meters(rope.TailShift) : Vector2.Zero,
                Meters(second - point).Length())));
            rope.Points.InsertRange(segment + 1, [left, right]);
            rope.CutGap = segment + 1;
            // Native divides each interior fixture density by ten, then resets mass data.
            foreach (PhysicsBody body in rope.Points.Skip(1).SkipLast(1))
            {
                body.Densities[0] /= 10;
                body.ResetMassData();
            }
            NotifyAttached(rope);
            return true;
        }

        /// <summary>Cuts the first crossed active bone of each intact rope in authored order.</summary>
        /// <param name="start">Previous pointer position in logical units.</param>
        /// <param name="end">Current pointer position in logical units.</param>
        /// <returns>Accepted tick-indexed records, including tip impulse for exact replay.</returns>
        /// <exception cref="ArgumentOutOfRangeException">A pointer vector is invalid.</exception>
        public IReadOnlyList<RopeCut> Swipe(Vector2 start, Vector2 end)
        {
            if (!ValidVector(start) || !ValidVector(end))
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            List<RopeCut> accepted = [];
            Vector2 drag = end - start;
            if (drag.LengthSquared() < 1e-12f)
            {
                return accepted.AsReadOnly();
            }
            Vector2 impulse = Vector2.Normalize(drag) * .001f;
            for (int r = 0; r < ropes.Count; r++)
            {
                RopeChain rope = ropes[r];
                if (rope.CutGap >= 0 || rope.Released)
                {
                    continue;
                }
                for (int i = 0; i < rope.Points.Count - 1; i++)
                {
                    Vector2 a = BodyPosition(rope, i), bone = BodyPosition(rope, i + 1) - a;
                    float denominator = Cross(drag, bone);
                    if (MathF.Abs(denominator) < 1e-8f)
                    {
                        continue;
                    }
                    float t = Cross(a - start, bone) / denominator, u = Cross(a - start, drag) / denominator;
                    if (t < 0 || t > 1 || u < 0 || u > 1)
                    {
                        continue;
                    }
                    // The original crossing search selects a midpoint from twenty bone intervals.
                    float fraction = (Math.Clamp(MathF.Ceiling(u * 20), 1, 20) - .5f) / 20;
                    Vector2 point = a + (fraction * bone);
                    if (Cut(r, i, point, impulse))
                    {
                        accepted.Add(new(Tick, r, i, point, impulse));
                    }
                    break;
                }
            }
            SwipeBalloons(start, end);
            return accepted.AsReadOnly();
        }

        /// <summary>Snapshots drawable fragments without connecting a cut gap or released candy.</summary>
        /// <param name="index">Zero-based authored rope index.</param>
        /// <returns>One intact polyline or two independent cut polylines, in logical units.</returns>
        public IReadOnlyList<IReadOnlyList<Vector2>> RopeSegments(int index)
        {
            RopeChain rope = ropes[index];
            Vector2[] points = [.. Enumerable.Range(0, rope.Released ? rope.Points.Count - 1 : rope.Points.Count).Select(i => BodyPosition(rope, i))];
            return rope.CutGap >= 0
                ? Array.AsReadOnly<IReadOnlyList<Vector2>>([Array.AsReadOnly(points[..(rope.CutGap + 1)]), Array.AsReadOnly(points[(rope.CutGap + 1)..])])
                : Array.AsReadOnly<IReadOnlyList<Vector2>>([Array.AsReadOnly(points)]);
        }

        /// <summary>Native 1024-entry sine table, filled in double precision and narrowed.</summary>
        private static readonly float[] Sines = [.. Enumerable.Range(0, 1024).Select(i => (float)Math.Sin(2 * i * 3.14159265 / 1024.0))];
        /// <summary>Native 1024-entry cosine table, filled in double precision and narrowed.</summary>
        private static readonly float[] Cosines = [.. Enumerable.Range(0, 1024).Select(i => (float)Math.Cos(2 * i * 3.14159265 / 1024.0))];

        /// <summary>Rotates a logical vector with the framework's table sine and cosine, as rope body positions do.</summary>
        /// <param name="value">Logical vector.</param>
        /// <param name="angle">Clockwise radians.</param>
        /// <returns>The rotated vector; the table quantizes the angle to 1/1024 of a turn.</returns>
        private static Vector2 TableRotate(Vector2 value, float angle)
        {
            int index = (int)((float)(angle * 1024.0) / 3.14159265 / 2.0) & 0x3FF;
            float cosine = Cosines[index], sine = Sines[index];
            return new((value.X * cosine) - (value.Y * sine), (value.X * sine) + (value.Y * cosine));
        }

        /// <summary>Validates finite bounded public cut vectors.</summary>
        /// <param name="value">Coordinate or impulse vector.</param>
        /// <returns>Whether each component has magnitude at most one million.</returns>
        private static bool ValidVector(Vector2 value)
        {
            return float.IsFinite(value.X) && float.IsFinite(value.Y) && MathF.Abs(value.X) <= 1000000 && MathF.Abs(value.Y) <= 1000000;
        }

        /// <summary>Computes the signed two-dimensional cross product.</summary>
        /// <param name="a">First vector.</param>
        /// <param name="b">Second vector.</param>
        /// <returns>The scalar determinant.</returns>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }
    }
}
