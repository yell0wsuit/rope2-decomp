// Altered C# subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
/*
* Copyright (c) 2007-2009 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Immutable support vertices of a convex hull and its rounded radius.</summary>
    public sealed class PhysicsDistanceProxy
    {
        /// <summary>Copied local support vertices in native index order.</summary>
        public IReadOnlyList<Vector2> Vertices
        {
            get;
        }
        /// <summary>Radius added around the support hull, in meters.</summary>
        public float Radius
        {
            get;
        }
        /// <summary>Copies one to eight support vertices and validates their finite bounds.</summary>
        /// <param name="vertices">One point for a circle, two for a segment, or a convex hull's support vertices.</param>
        /// <param name="radius">Finite radius between zero and one million meters.</param>
        /// <exception cref="ArgumentException">The support set or radius is invalid.</exception>
        public PhysicsDistanceProxy(IReadOnlyList<Vector2> vertices, float radius)
        {
            ArgumentNullException.ThrowIfNull(vertices);
            if (vertices.Count is < 1 or > 8 || vertices.Any(v => !PhysicsAabb.ValidVector(v)) ||
                !float.IsFinite(radius) || radius < 0 || radius > 1000000)
            {
                throw new ArgumentException("Invalid bounded distance proxy.");
            }

            Vertices = Array.AsReadOnly(vertices.ToArray());
            Radius = radius;
        }
    }

    /// <summary>A body-origin transform used by standalone distance queries.</summary>
    /// <param name="Position">World origin in meters.</param>
    /// <param name="Angle">Clockwise rotation in radians.</param>
    public readonly record struct PhysicsDistanceTransform(Vector2 Position, float Angle);

    /// <summary>Native witness points, rounded distance and support-query iteration count.</summary>
    /// <param name="PointA">World witness on the first hull or its rounded surface.</param>
    /// <param name="PointB">World witness on the second hull or its rounded surface.</param>
    /// <param name="Distance">Nonnegative distance in meters.</param>
    /// <param name="Iterations">Number of support-pair queries, at most twenty.</param>
    public readonly record struct PhysicsDistanceResult(Vector2 PointA, Vector2 PointB, float Distance, int Iterations);

    /// <summary>One cached pair of support vertex indices.</summary>
    /// <param name="A">Index in the first proxy.</param>
    /// <param name="B">Index in the second proxy.</param>
    public readonly record struct PhysicsSupportPair(int A, int B);

    /// <summary>A detached snapshot of the native simplex metric and support pairs.</summary>
    /// <param name="Metric">Native signed triangle area, segment length or zero.</param>
    /// <param name="Vertices">Support pairs in native simplex order.</param>
    public readonly record struct PhysicsSimplexState(float Metric, IReadOnlyList<PhysicsSupportPair> Vertices);

    /// <summary>Reusable simplex state; queries mutate it, so do not share it concurrently.</summary>
    public sealed class PhysicsSimplexCache
    {
        internal readonly PhysicsSupportPair[] Pairs = new PhysicsSupportPair[3];
        internal int Count;
        internal float Metric;
        /// <summary>Discards warm-start state before the next query.</summary>
        public void Reset()
        {
            Count = 0;
            Metric = 0;
        }
        /// <summary>Copies the current metric and support pairs into an immutable diagnostic.</summary>
        public PhysicsSimplexState Capture()
        {
            return new(Metric, Array.AsReadOnly(Pairs.Take(Count).ToArray()));
        }
    }

    /// <summary>Bounded standalone native-style GJK queries, separate from world collision solving.</summary>
    public static class PhysicsDistance
    {
        /// <summary>Computes native witness points and optionally applies rounded shape radii.</summary>
        /// <param name="a">First immutable distance proxy.</param>
        /// <param name="transformA">First world origin and orientation.</param>
        /// <param name="b">Second immutable distance proxy.</param>
        /// <param name="transformB">Second world origin and orientation.</param>
        /// <param name="useRadii">Whether to shift witnesses to the rounded surfaces.</param>
        /// <param name="cache">Optional warm-start state, initially empty.</param>
        /// <returns>The closest witnesses, distance and native support iteration count.</returns>
        /// <exception cref="ArgumentNullException">A proxy is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A transform has nonfinite or out-of-bounds coordinates.</exception>
        /// <exception cref="ArgumentException">Active cached indices do not fit the supplied proxies; reset the cache first.</exception>
        public static PhysicsDistanceResult Measure(PhysicsDistanceProxy a, PhysicsDistanceTransform transformA,
            PhysicsDistanceProxy b, PhysicsDistanceTransform transformB, bool useRadii = true, PhysicsSimplexCache? cache = null)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            Validate(transformA, nameof(transformA));
            Validate(transformB, nameof(transformB));
            cache ??= new();
            for (int i = 0; i < cache.Count; i++)
            {
                if (cache.Pairs[i].A >= a.Vertices.Count || cache.Pairs[i].B >= b.Vertices.Count)
                {
                    throw new ArgumentException("Cached support indices do not fit these proxies. Reset the cache first.", nameof(cache));
                }
            }

            Span<Vertex> storage = stackalloc Vertex[3];
            storage.Clear();
            Simplex simplex = new(storage)
            {
                Count = cache.Count
            };
            for (int i = 0; i < simplex.Count; i++)
            {
                storage[i] = SupportVertex(a, transformA, cache.Pairs[i].A, b, transformB, cache.Pairs[i].B);
            }

            if (simplex.Count > 1)
            {
                float oldMetric = cache.Metric, metric = simplex.Metric();
                if (metric < .5f * oldMetric || 2 * oldMetric < metric || metric < Epsilon)
                {
                    simplex.Count = 0;
                }
            }
            if (simplex.Count == 0)
            {
                storage[0] = SupportVertex(a, transformA, 0, b, transformB, 0);
                simplex.Count = 1;
            }

            Span<PhysicsSupportPair> saved = stackalloc PhysicsSupportPair[3];
            int iterations = 0;
            while (iterations < 20)
            {
                int savedCount = simplex.Count;
                for (int i = 0; i < savedCount; i++)
                {
                    saved[i] = new(storage[i].IndexA, storage[i].IndexB);
                }

                if (simplex.Count == 2)
                {
                    simplex.Solve2();
                }
                else if (simplex.Count == 3)
                {
                    simplex.Solve3();
                }

                if (simplex.Count == 3)
                {
                    break;
                }
                // The native distance-progress check is disabled. Do not terminate here
                // merely because the closest distance stopped decreasing.
                Vector2 direction = simplex.SearchDirection();
                if (Dot(direction, direction) < Epsilon * Epsilon)
                {
                    break;
                }

                int indexA = Support(a, PhysicsMath.InverseRotate(-direction, transformA.Angle));
                int indexB = Support(b, PhysicsMath.InverseRotate(direction, transformB.Angle));
                // Native support queries overwrite geometry and indices, retaining the
                // slot's previous barycentric weight until the next simplex reduction.
                float previousWeight = storage[simplex.Count].Weight;
                storage[simplex.Count] = SupportVertex(a, transformA, indexA, b, transformB, indexB);
                storage[simplex.Count].Weight = previousWeight;
                iterations++;
                bool duplicate = false;
                for (int i = 0; i < savedCount; i++)
                {
                    if (indexA == saved[i].A && indexB == saved[i].B)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (duplicate)
                {
                    break;
                }

                simplex.Count++;
            }
            simplex.Witnesses(out Vector2 pointA, out Vector2 pointB);
            float distance = Length(pointB - pointA);
            cache.Metric = simplex.Metric();
            cache.Count = simplex.Count;
            for (int i = 0; i < simplex.Count; i++)
            {
                cache.Pairs[i] = new(storage[i].IndexA, storage[i].IndexB);
            }

            if (useRadii)
            {
                float radii = a.Radius + b.Radius;
                if (distance > radii && distance > Epsilon)
                {
                    distance -= radii;
                    Vector2 normal = pointB - pointA;
                    normal *= 1 / Length(normal);
                    pointA += a.Radius * normal;
                    pointB -= b.Radius * normal;
                }
                else
                {
                    pointA = .5f * (pointA + pointB);
                    pointB = pointA;
                    distance = 0;
                }
            }
            return new(pointA, pointB, distance, iterations);
        }

        private const float Epsilon = 1.1920929e-7f;
        private static float Dot(Vector2 a, Vector2 b)
        {
            return (a.X * b.X) + (a.Y * b.Y);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static float Length(Vector2 v)
        {
            return MathF.Sqrt(Dot(v, v));
        }

        private static void Validate(PhysicsDistanceTransform transform, string name)
        {
            if (!PhysicsAabb.ValidVector(transform.Position) || !float.IsFinite(transform.Angle))
            {
                throw new ArgumentOutOfRangeException(name, "Invalid bounded distance transform.");
            }
        }
        private static int Support(PhysicsDistanceProxy proxy, Vector2 direction)
        {
            int best = 0;
            float value = Dot(proxy.Vertices[0], direction);
            for (int i = 1; i < proxy.Vertices.Count; i++)
            {
                float candidate = Dot(proxy.Vertices[i], direction);
                if (candidate > value)
                {
                    best = i;
                    value = candidate;
                }
            }
            return best;
        }
        private static Vertex SupportVertex(PhysicsDistanceProxy a, PhysicsDistanceTransform ta, int ia,
            PhysicsDistanceProxy b, PhysicsDistanceTransform tb, int ib)
        {
            Vector2 pointA = ta.Position + PhysicsMath.Rotate(a.Vertices[ia], ta.Angle);
            Vector2 pointB = tb.Position + PhysicsMath.Rotate(b.Vertices[ib], tb.Angle);
            return new()
            {
                A = pointA,
                B = pointB,
                W = pointB - pointA,
                IndexA = ia,
                IndexB = ib
            };
        }
        private struct Vertex
        {
            internal Vector2 A, B, W;
            internal float Weight;
            internal int IndexA, IndexB;
        }
        private ref struct Simplex(Span<Vertex> vertices)
        {
            internal Span<Vertex> Vertices = vertices;
            internal int Count;
            internal readonly float Metric()
            {
                return Count switch
                {
                    1 => 0,
                    2 => Length(Vertices[0].W - Vertices[1].W),
                    3 => Cross(Vertices[1].W - Vertices[0].W, Vertices[2].W - Vertices[0].W),
                    _ => throw new InvalidOperationException("Invalid simplex count.")
                };
            }

            internal readonly Vector2 SearchDirection()
            {
                if (Count == 1)
                {
                    return -Vertices[0].W;
                }

                Vector2 edge = Vertices[1].W - Vertices[0].W;
                return Cross(edge, -Vertices[0].W) > 0 ? new(-edge.Y, edge.X) : new(edge.Y, -edge.X);
            }
            internal readonly void Witnesses(out Vector2 a, out Vector2 b)
            {
                if (Count == 1)
                {
                    a = Vertices[0].A;
                    b = Vertices[0].B;
                    return;
                }
                a = (Vertices[0].Weight * Vertices[0].A) + (Vertices[1].Weight * Vertices[1].A);
                if (Count == 3)
                {
                    a += Vertices[2].Weight * Vertices[2].A;
                    b = a;
                }
                else
                {
                    b = (Vertices[0].Weight * Vertices[0].B) + (Vertices[1].Weight * Vertices[1].B);
                }
            }
            internal void Solve2()
            {
                Vector2 w1 = Vertices[0].W, w2 = Vertices[1].W, edge = w2 - w1;
                float d2 = -Dot(w1, edge);
                if (d2 <= 0)
                {
                    Vertices[0].Weight = 1;
                    Count = 1;
                    return;
                }
                float d1 = Dot(w2, edge);
                if (d1 <= 0)
                {
                    Vertices[1].Weight = 1;
                    Vertices[0] = Vertices[1];
                    Count = 1;
                    return;
                }
                float inverse = 1 / (d1 + d2);
                Vertices[0].Weight = d1 * inverse;
                Vertices[1].Weight = d2 * inverse;
                Count = 2;
            }
            internal void Solve3()
            {
                Vector2 w1 = Vertices[0].W, w2 = Vertices[1].W, w3 = Vertices[2].W;
                Vector2 e12 = w2 - w1, e13 = w3 - w1, e23 = w3 - w2;
                float d12_1 = Dot(w2, e12), d12_2 = -Dot(w1, e12);
                float d13_1 = Dot(w3, e13), d13_2 = -Dot(w1, e13);
                float d23_1 = Dot(w3, e23), d23_2 = -Dot(w2, e23);
                float n123 = Cross(e12, e13);
                float d123_1 = n123 * Cross(w2, w3), d123_2 = n123 * Cross(w3, w1), d123_3 = n123 * Cross(w1, w2);
                if (d12_2 <= 0 && d13_2 <= 0)
                {
                    Vertices[0].Weight = 1;
                    Count = 1;
                    return;
                }
                if (d12_1 > 0 && d12_2 > 0 && d123_3 <= 0)
                {
                    float inverse = 1 / (d12_1 + d12_2);
                    Vertices[0].Weight = d12_1 * inverse;
                    Vertices[1].Weight = d12_2 * inverse;
                    Count = 2;
                    return;
                }
                if (d13_1 > 0 && d13_2 > 0 && d123_2 <= 0)
                {
                    float inverse = 1 / (d13_1 + d13_2);
                    Vertices[0].Weight = d13_1 * inverse;
                    Vertices[2].Weight = d13_2 * inverse;
                    Vertices[1] = Vertices[2];
                    Count = 2;
                    return;
                }
                if (d12_1 <= 0 && d23_2 <= 0)
                {
                    Vertices[1].Weight = 1;
                    Vertices[0] = Vertices[1];
                    Count = 1;
                    return;
                }
                if (d13_1 <= 0 && d23_1 <= 0)
                {
                    Vertices[2].Weight = 1;
                    Vertices[0] = Vertices[2];
                    Count = 1;
                    return;
                }
                if (d23_1 > 0 && d23_2 > 0 && d123_1 <= 0)
                {
                    float inverse = 1 / (d23_1 + d23_2);
                    Vertices[1].Weight = d23_1 * inverse;
                    Vertices[2].Weight = d23_2 * inverse;
                    Vertices[0] = Vertices[2];
                    Count = 2;
                    return;
                }
                float invTriangle = 1 / (d123_1 + d123_2 + d123_3);
                Vertices[0].Weight = d123_1 * invTriangle;
                Vertices[1].Weight = d123_2 * invTriangle;
                Vertices[2].Weight = d123_3 * invTriangle;
                Count = 3;
            }
        }

        /// <summary>Tests fresh-cache native overlap with radii and the strict ten-epsilon threshold.</summary>
        /// <param name="a">First immutable proxy.</param>
        /// <param name="transformA">First world transform.</param>
        /// <param name="b">Second immutable proxy.</param>
        /// <param name="transformB">Second world transform.</param>
        /// <returns>Whether the native rounded distance is less than ten float epsilons.</returns>
        public static bool Overlaps(PhysicsDistanceProxy a, PhysicsDistanceTransform transformA,
            PhysicsDistanceProxy b, PhysicsDistanceTransform transformB)
        {
            return Measure(a, transformA, b, transformB).Distance < 10 * Epsilon;
        }
    }
}
