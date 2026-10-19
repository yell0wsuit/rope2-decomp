// Altered C# world query subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>One fixture reported by <see cref="PhysicsWorld.RayCast"/>.</summary>
    /// <param name="Fixture">Stable fixture label.</param>
    /// <param name="Body">Owning body, or null for terrain.</param>
    /// <param name="Point">World intersection point.</param>
    /// <param name="Normal">World surface normal at the intersection.</param>
    /// <param name="Fraction">Fraction along the segment from its start.</param>
    public readonly record struct PhysicsRayHit(int Fixture, PhysicsBody? Body, Vector2 Point, Vector2 Normal, float Fraction);

    public sealed partial class PhysicsWorld
    {
        /// <summary>Casts a segment against every fixture, including sensors, in native broadphase order.</summary>
        /// <param name="point1">Segment start in meters.</param>
        /// <param name="point2">Segment end in meters, distinct from the start.</param>
        /// <param name="callback">Native <c>ReportFixture</c>: return -1 to ignore the hit, 0 to stop, the hit fraction to
        /// clip to the closest hit so far, or 1 to continue unchanged.</param>
        /// <remarks>Shapes are tested against each body's cached transform. The callback must not mutate the world.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">The segment is nonfinite or degenerate.</exception>
        public void RayCast(Vector2 point1, Vector2 point2, Func<PhysicsRayHit, float> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            broadPhase.RayCast(point1, point2, (maxFraction, proxyId) =>
            {
                FixtureProxy proxy = proxies[broadPhase.GetUserData(proxyId)];
                if (!RayCastFixture(proxy, point1, point2, maxFraction, out float fraction, out Vector2 normal))
                {
                    return maxFraction;
                }
                Vector2 point = ((1 - fraction) * point1) + (fraction * point2);
                return callback(new(proxy.Label, proxy.Body, point, normal, fraction));
            });
        }

        /// <summary>Reports fixtures whose broadphase bounds overlap a box, in native tree order.</summary>
        /// <param name="bounds">World query box.</param>
        /// <param name="callback">Receives each fixture label; return false to stop.</param>
        /// <remarks>Like native <c>QueryAABB</c>, this tests predicted proxy bounds rather than exact shapes.</remarks>
        public void QueryAabb(PhysicsAabb bounds, Func<int, bool> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            broadPhase.Query(bounds, proxyId => callback(broadPhase.GetUserData(proxyId)));
        }

        /// <summary>Tests whether a world point lies inside a fixture's exact shape, as native <c>b2Fixture::TestPoint</c>.</summary>
        /// <param name="fixture">An active fixture label.</param>
        /// <param name="point">World point in meters.</param>
        /// <returns>Whether the point is inside or on the shape.</returns>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        public bool TestPoint(int fixture, Vector2 point)
        {
            FixtureProxy proxy = proxies[fixture];
            if (proxy.Body is PhysicsBody body && body.PolygonFixtures.Count == 0)
            {
                Vector2 d = point - body.Origin;
                return Vector2.Dot(d, d) <= body.Radius * body.Radius;
            }
            (ConvexPolygon shape, Vector2 origin, float angle) = Frame(proxy);
            Vector2 local = PhysicsMath.InverseRotate(point - origin, angle);
            for (int i = 0; i < shape.Vertices.Count; i++)
            {
                if (Vector2.Dot(shape.Normals[i], local - shape.Vertices[i]) > 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Gets a fixture's swept tight bounds from its latest synchronization, as native <c>b2Fixture::GetAABB</c>.</summary>
        /// <param name="fixture">An active fixture label.</param>
        /// <returns>Bounds without the broadphase displacement prediction.</returns>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        public PhysicsAabb GetFixtureAabb(int fixture)
        {
            return proxies[fixture].Bounds;
        }

        /// <summary>Gets the body owning a fixture.</summary>
        /// <param name="fixture">An active fixture label.</param>
        /// <returns>The owning body, or null for terrain.</returns>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        public PhysicsBody? GetFixtureBody(int fixture)
        {
            return proxies[fixture].Body;
        }

        /// <summary>Gets the body owning a fixture that may since have been retired.</summary>
        /// <param name="fixture">A fixture label from a recorded event.</param>
        /// <returns>The owning body, or null for terrain or a retired fixture.</returns>
        internal PhysicsBody? TryGetFixtureBody(int fixture)
        {
            return proxies.TryGetValue(fixture, out FixtureProxy? proxy) ? proxy.Body : null;
        }

        /// <summary>Rejects structural world changes from inside query callbacks before anything mutates.</summary>
        /// <exception cref="InvalidOperationException">A broadphase query or pair update is in progress.</exception>
        private void ThrowIfLocked()
        {
            if (broadPhase.Locked)
            {
                throw new InvalidOperationException("The physics world cannot change during a query callback.");
            }
        }

        private static (ConvexPolygon Shape, Vector2 Origin, float Angle) Frame(FixtureProxy proxy)
        {
            return proxy.Body is PhysicsBody body ? (body.PolygonFixtures[proxy.LocalIndex], body.Origin, body.Angle) : (proxy.Terrain!.Shape, Vector2.Zero, 0);
        }

        /// <summary>Native circle and polygon shape ray casts.</summary>
        private static bool RayCastFixture(FixtureProxy proxy, Vector2 p1, Vector2 p2, float maxFraction, out float fraction, out Vector2 normal)
        {
            fraction = 0;
            normal = Vector2.Zero;
            if (proxy.Body is PhysicsBody circle && circle.PolygonFixtures.Count == 0)
            {
                Vector2 s = p1 - circle.Origin;
                float b = Vector2.Dot(s, s) - (circle.Radius * circle.Radius);
                Vector2 r = p2 - p1;
                float c = Vector2.Dot(s, r), rr = Vector2.Dot(r, r);
                float sigma = (c * c) - (rr * b);
                if (sigma < 0 || rr < 1.1920929e-7f)
                {
                    return false;
                }
                float a = -(c + MathF.Sqrt(sigma));
                if (0 <= a && a <= maxFraction * rr)
                {
                    a /= rr;
                    fraction = a;
                    normal = PhysicsMath.Normalize(s + (a * r));
                    return true;
                }
                return false;
            }
            (ConvexPolygon shape, Vector2 origin, float angle) = Frame(proxy);
            Vector2 local1 = PhysicsMath.InverseRotate(p1 - origin, angle), local2 = PhysicsMath.InverseRotate(p2 - origin, angle);
            Vector2 d = local2 - local1;
            float lower = 0, upper = maxFraction;
            int index = -1;
            for (int i = 0; i < shape.Vertices.Count; i++)
            {
                float numerator = Vector2.Dot(shape.Normals[i], shape.Vertices[i] - local1);
                float denominator = Vector2.Dot(shape.Normals[i], d);
                if (denominator == 0)
                {
                    if (numerator < 0)
                    {
                        return false;
                    }
                }
                else if (denominator < 0 && numerator < lower * denominator)
                {
                    lower = numerator / denominator;
                    index = i;
                }
                else if (denominator > 0 && numerator < upper * denominator)
                {
                    upper = numerator / denominator;
                }
                if (upper < lower)
                {
                    return false;
                }
            }
            if (index < 0)
            {
                return false;
            }
            fraction = lower;
            normal = PhysicsMath.Rotate(shape.Normals[index], angle);
            return true;
        }
    }
}
