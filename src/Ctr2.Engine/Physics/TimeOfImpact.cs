// Altered C# time-of-impact subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native <c>b2Sweep</c>: center and angle at the step start and end, plus the start fraction.</summary>
    internal struct PhysicsSweep
    {
        internal Vector2 LocalCenter;
        internal Vector2 Center0;
        internal Vector2 Center;
        internal float Angle0;
        internal float Angle;
        internal float Alpha0;

        /// <summary>Native <c>GetTransform</c>: interpolates the center and angle, then shifts to the body origin.</summary>
        internal readonly PhysicsDistanceTransform Transform(float beta)
        {
            Vector2 p = ((1 - beta) * Center0) + (beta * Center);
            float angle = ((1 - beta) * Angle0) + (beta * Angle);
            return new(p - PhysicsMath.Rotate(LocalCenter, angle), angle);
        }

        /// <summary>Native <c>Advance</c>: moves the start forward to <paramref name="alpha"/>.</summary>
        internal void Advance(float alpha)
        {
            float beta = (alpha - Alpha0) / (1 - Alpha0);
            Center0 = ((1 - beta) * Center0) + (beta * Center);
            Angle0 = ((1 - beta) * Angle0) + (beta * Angle);
            Alpha0 = alpha;
        }

        /// <summary>Native <c>Normalize</c>: shifts both angles by whole turns so the start lies in [0, 2pi).</summary>
        internal void Normalize()
        {
            float twoPi = 2 * MathF.PI;
            float d = twoPi * MathF.Floor(Angle0 / twoPi);
            Angle0 -= d;
            Angle -= d;
        }
    }

    /// <summary>Native <c>b2TOIOutput::State</c>.</summary>
    internal enum PhysicsToiState
    {
        Unknown,
        Failed,
        Overlapped,
        Touching,
        Separated
    }

    /// <summary>Native conservative-advancement time of impact with separating-axis root finding.</summary>
    internal static class PhysicsTimeOfImpact
    {
        private const int MaxIterations = 20;
        private const int MaxPolygonVertices = 8;

        /// <summary>Native <c>b2TimeOfImpact</c> over [0, <paramref name="tMax"/>].</summary>
        internal static (PhysicsToiState State, float T) Compute(PhysicsDistanceProxy proxyA, PhysicsSweep sweepA, PhysicsDistanceProxy proxyB,
            PhysicsSweep sweepB, float tMax)
        {
            sweepA.Normalize();
            sweepB.Normalize();
            float totalRadius = proxyA.Radius + proxyB.Radius;
            float target = MathF.Max(PhysicsWorld.LinearSlop, totalRadius - (3 * PhysicsWorld.LinearSlop));
            float tolerance = .25f * PhysicsWorld.LinearSlop;
            float t1 = 0;
            int iteration = 0;
            PhysicsSimplexCache cache = new();
            while (true)
            {
                PhysicsDistanceResult distance = PhysicsDistance.Measure(proxyA, sweepA.Transform(t1), proxyB, sweepB.Transform(t1), false, cache);
                if (distance.Distance <= 0)
                {
                    return (PhysicsToiState.Overlapped, 0);
                }
                if (distance.Distance < target + tolerance)
                {
                    return (PhysicsToiState.Touching, t1);
                }
                Separation function = new(cache, proxyA, sweepA, proxyB, sweepB, t1);
                bool done = false;
                PhysicsToiState state = PhysicsToiState.Unknown;
                float result = tMax;
                float t2 = tMax;
                int pushBack = 0;
                while (true)
                {
                    float s2 = function.FindMinSeparation(out int indexA, out int indexB, t2);
                    if (s2 > target + tolerance)
                    {
                        (state, result, done) = (PhysicsToiState.Separated, tMax, true);
                        break;
                    }
                    if (s2 > target - tolerance)
                    {
                        t1 = t2;
                        break;
                    }
                    float s1 = function.Evaluate(indexA, indexB, t1);
                    if (s1 < target - tolerance)
                    {
                        (state, result, done) = (PhysicsToiState.Failed, t1, true);
                        break;
                    }
                    if (s1 <= target + tolerance)
                    {
                        (state, result, done) = (PhysicsToiState.Touching, t1, true);
                        break;
                    }
                    int rootIterations = 0;
                    float a1 = t1, a2 = t2;
                    while (true)
                    {
                        // Alternate bisection for progress with the secant rule for convergence.
                        float t = (rootIterations & 1) != 0 ? a1 + ((target - s1) * (a2 - a1) / (s2 - s1)) : .5f * (a1 + a2);
                        float s = function.Evaluate(indexA, indexB, t);
                        if (MathF.Abs(s - target) < tolerance)
                        {
                            t2 = t;
                            break;
                        }
                        if (s > target)
                        {
                            a1 = t;
                            s1 = s;
                        }
                        else
                        {
                            a2 = t;
                            s2 = s;
                        }
                        if (++rootIterations == 50)
                        {
                            break;
                        }
                    }
                    if (++pushBack == MaxPolygonVertices)
                    {
                        break;
                    }
                }
                ++iteration;
                if (done)
                {
                    return (state, result);
                }
                if (iteration == MaxIterations)
                {
                    return (PhysicsToiState.Failed, t1);
                }
            }
        }

        private static int Support(IReadOnlyList<Vector2> vertices, Vector2 direction)
        {
            int best = 0;
            float bestValue = Vector2.Dot(vertices[0], direction);
            for (int i = 1; i < vertices.Count; i++)
            {
                float value = Vector2.Dot(vertices[i], direction);
                if (value > bestValue)
                {
                    best = i;
                    bestValue = value;
                }
            }
            return best;
        }

        private static Vector2 Mul(PhysicsDistanceTransform transform, Vector2 point)
        {
            return transform.Position + PhysicsMath.Rotate(point, transform.Angle);
        }

        private static Vector2 Cross(Vector2 value)
        {
            return new(value.Y, -value.X);
        }

        /// <summary>Native <c>b2SeparationFunction</c>.</summary>
        private sealed class Separation
        {
            private enum Kind
            {
                Points,
                FaceA,
                FaceB
            }

            private readonly PhysicsDistanceProxy proxyA, proxyB;
            private readonly PhysicsSweep sweepA, sweepB;
            private readonly Kind kind;
            private readonly Vector2 localPoint;
            private readonly Vector2 axis;

            internal Separation(PhysicsSimplexCache cache, PhysicsDistanceProxy proxyA, PhysicsSweep sweepA, PhysicsDistanceProxy proxyB,
                PhysicsSweep sweepB, float t1)
            {
                this.proxyA = proxyA;
                this.proxyB = proxyB;
                this.sweepA = sweepA;
                this.sweepB = sweepB;
                PhysicsDistanceTransform xfA = sweepA.Transform(t1), xfB = sweepB.Transform(t1);
                PhysicsSupportPair first = cache.Pairs[0];
                if (cache.Count == 1)
                {
                    kind = Kind.Points;
                    axis = PhysicsMath.Normalize(Mul(xfB, proxyB.Vertices[first.B]) - Mul(xfA, proxyA.Vertices[first.A]));
                }
                else if (first.A == cache.Pairs[1].A)
                {
                    // Two points on B and one on A.
                    kind = Kind.FaceB;
                    Vector2 b1 = proxyB.Vertices[first.B], b2 = proxyB.Vertices[cache.Pairs[1].B];
                    axis = PhysicsMath.Normalize(Cross(b2 - b1));
                    Vector2 normal = PhysicsMath.Rotate(axis, xfB.Angle);
                    localPoint = .5f * (b1 + b2);
                    Vector2 pointB = Mul(xfB, localPoint), pointA = Mul(xfA, proxyA.Vertices[first.A]);
                    if (Vector2.Dot(pointA - pointB, normal) < 0)
                    {
                        axis = -axis;
                    }
                }
                else
                {
                    // Two points on A and one or two points on B.
                    kind = Kind.FaceA;
                    Vector2 a1 = proxyA.Vertices[first.A], a2 = proxyA.Vertices[cache.Pairs[1].A];
                    axis = PhysicsMath.Normalize(Cross(a2 - a1));
                    Vector2 normal = PhysicsMath.Rotate(axis, xfA.Angle);
                    localPoint = .5f * (a1 + a2);
                    Vector2 pointA = Mul(xfA, localPoint), pointB = Mul(xfB, proxyB.Vertices[first.B]);
                    if (Vector2.Dot(pointB - pointA, normal) < 0)
                    {
                        axis = -axis;
                    }
                }
            }

            internal float FindMinSeparation(out int indexA, out int indexB, float t)
            {
                PhysicsDistanceTransform xfA = sweepA.Transform(t), xfB = sweepB.Transform(t);
                switch (kind)
                {
                    case Kind.Points:
                        indexA = Support(proxyA.Vertices, PhysicsMath.InverseRotate(axis, xfA.Angle));
                        indexB = Support(proxyB.Vertices, PhysicsMath.InverseRotate(-axis, xfB.Angle));
                        return Vector2.Dot(Mul(xfB, proxyB.Vertices[indexB]) - Mul(xfA, proxyA.Vertices[indexA]), axis);
                    case Kind.FaceA:
                        {
                            Vector2 normal = PhysicsMath.Rotate(axis, xfA.Angle);
                            Vector2 pointA = Mul(xfA, localPoint);
                            indexA = -1;
                            indexB = Support(proxyB.Vertices, PhysicsMath.InverseRotate(-normal, xfB.Angle));
                            return Vector2.Dot(Mul(xfB, proxyB.Vertices[indexB]) - pointA, normal);
                        }

                    case Kind.FaceB:
                        {
                            Vector2 normal = PhysicsMath.Rotate(axis, xfB.Angle);
                            Vector2 pointB = Mul(xfB, localPoint);
                            indexB = -1;
                            indexA = Support(proxyA.Vertices, PhysicsMath.InverseRotate(-normal, xfA.Angle));
                            return Vector2.Dot(Mul(xfA, proxyA.Vertices[indexA]) - pointB, normal);
                        }
                    default:
                        throw new InvalidOperationException("Unknown separation function.");
                }
            }

            internal float Evaluate(int indexA, int indexB, float t)
            {
                PhysicsDistanceTransform xfA = sweepA.Transform(t), xfB = sweepB.Transform(t);
                switch (kind)
                {
                    case Kind.Points:
                        return Vector2.Dot(Mul(xfB, proxyB.Vertices[indexB]) - Mul(xfA, proxyA.Vertices[indexA]), axis);
                    case Kind.FaceA:
                        {
                            Vector2 normal = PhysicsMath.Rotate(axis, xfA.Angle);
                            return Vector2.Dot(Mul(xfB, proxyB.Vertices[indexB]) - Mul(xfA, localPoint), normal);
                        }

                    case Kind.FaceB:
                        {
                            Vector2 normal = PhysicsMath.Rotate(axis, xfB.Angle);
                            return Vector2.Dot(Mul(xfA, proxyA.Vertices[indexA]) - Mul(xfB, localPoint), normal);
                        }
                    default:
                        throw new InvalidOperationException("Unknown separation function.");
                }
            }
        }
    }
}
