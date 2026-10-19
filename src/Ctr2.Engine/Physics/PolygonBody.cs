// Altered C# compound polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        /// <summary>Gets immutable local convex fixtures, in creation order.</summary>
        /// <value>Empty for circles; otherwise the compound triangle or single convex fixture geometry.</value>
        public IReadOnlyList<ConvexPolygon> PolygonFixtures
        {
            get;
        } = [];

        /// <summary>Gets the local center of mass relative to the authored body origin.</summary>
        /// <value>Zero for centered circles, or the mass-weighted polygon centroid.</value>
        public Vector2 LocalCenter
        {
            get; private set;
        }

        /// <summary>Gets the authored body origin in world meters.</summary>
        /// <value>The exact teleported origin until the next position change, otherwise the center of mass minus its rotated local offset.</value>
        public Vector2 Origin => TransformOrigin ?? ComputedOrigin;

        /// <summary>Gets the origin recomputed from the center of mass, as native <c>SynchronizeTransform</c> does.</summary>
        internal Vector2 ComputedOrigin => Position - PhysicsMath.Rotate(LocalCenter, Angle);

        /// <summary>Native cached transform origin set by <c>SetTransform</c>; cleared by any position or angle write.</summary>
        internal Vector2? TransformOrigin;

        /// <summary>Gets or sets whether the body is a static scene polygon, the game's land type for character reactions.</summary>
        /// <value>True for static circle, rectangle and polygon shapes built by the scene.</value>
        internal bool Land
        {
            get; set;
        }

        /// <summary>Gets whether angular integration is disabled.</summary>
        /// <value>True for a fixed-rotation body.</value>
        public bool FixedRotation
        {
            get;
        }

        /// <summary>Creates a compound body and combines fixture mass data in fixture order.</summary>
        /// <param name="fixtures">Owned immutable local convex geometry.</param>
        /// <param name="origin">Initial authored body origin in meters.</param>
        /// <param name="density">Nonnegative fixture density.</param>
        /// <param name="gravityScale">World gravity multiplier.</param>
        /// <param name="angle">Initial clockwise radians.</param>
        /// <param name="fixedRotation">Whether to disable angular motion.</param>
        /// <param name="type">Native body type.</param>
        internal PhysicsBody(IReadOnlyList<ConvexPolygon> fixtures, Vector2 origin, float density, float gravityScale, float angle, bool fixedRotation,
            PhysicsBodyType type) : this(fixtures, 0, density, gravityScale, type, fixedRotation)
        {
            Angle = angle;
            Position = origin + PhysicsMath.Rotate(LocalCenter, angle);
            // Native bodies cache the authored transform until their first synchronization.
            TransformOrigin = origin;
        }

        private PhysicsBody(IReadOnlyList<ConvexPolygon> fixtures, float radius, float density, float gravityScale, PhysicsBodyType type, bool fixedRotation)
        {
            PolygonFixtures = fixtures;
            Radius = radius;
            GravityScale = gravityScale;
            Type = type;
            FixedRotation = fixedRotation;
            int count = Math.Max(1, fixtures.Count);
            SensorFlags = new bool[count];
            Densities = new float[count];
            Array.Fill(Densities, density);
            if (!TryComputeMass(type, out float mass, out Vector2 center, out float inverse))
            {
                throw new ArgumentException("Invalid body mass or inertia.");
            }
            Mass = mass;
            LocalCenter = center;
            InverseInertia = inverse;
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Total owned dynamic polygon fixture count.</summary>
        private int polygonFixtureCount;

        /// <summary>Adds a body with the given convex fixtures, created in list order, like a native body built fixture by fixture.</summary>
        /// <param name="fixtures">Local convex fixtures in meters.</param>
        /// <param name="origin">Body origin in world meters.</param>
        /// <param name="density">Fixture density; static and kinematic bodies accept zero.</param>
        /// <param name="gravityScale">World gravity multiplier.</param>
        /// <param name="angle">Initial clockwise radians.</param>
        /// <param name="fixedRotation">Whether to disable angular motion.</param>
        /// <param name="type">Native body type.</param>
        /// <returns>The owned body.</returns>
        /// <exception cref="ArgumentException">Body data, mass or fixture budgets are invalid.</exception>
        public PhysicsBody AddPolygonFixtures(IReadOnlyList<ConvexPolygon> fixtures, Vector2 origin, float density, float gravityScale, float angle, bool fixedRotation,
            PhysicsBodyType type)
        {
            ThrowIfLocked();
            ArgumentNullException.ThrowIfNull(fixtures);
            if (fixtures.Count == 0 || bodies.Count >= 4096 || polygonFixtureCount + fixtures.Count > 8192 ||
                !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || MathF.Abs(origin.X) > 10000 || MathF.Abs(origin.Y) > 10000 ||
                !Enum.IsDefined(type) || !float.IsFinite(density) || density < 0 || (density == 0 && type == PhysicsBodyType.Dynamic) || density > 1000000 ||
                !float.IsFinite(gravityScale) || MathF.Abs(gravityScale) > 1000000 || !float.IsFinite(angle) || MathF.Abs(angle) > 1000000)
            {
                throw new ArgumentException("Invalid polygon body values or fixture budget.");
            }
            PhysicsBody body = new([.. fixtures], origin, density, gravityScale, angle, fixedRotation, type);
            body.Center0 = body.Position;
            body.Angle0 = angle;
            RegisterBodyProxies(body);
            bodies.Add(body);
            polygonFixtureCount += fixtures.Count;
            return body;
        }

        /// <summary>Adds a dynamic convex outline, normally triangulated into a compound body.</summary>
        /// <param name="shape">Immutable convex local outline in meters.</param>
        /// <param name="origin">Initial authored body origin in world meters.</param>
        /// <param name="density">Positive fixture density no greater than one million.</param>
        /// <param name="gravityScale">Finite bounded world gravity multiplier.</param>
        /// <param name="angle">Initial clockwise radians.</param>
        /// <param name="fixedRotation">Whether to disable angular motion.</param>
        /// <param name="compound">Whether to create cyclic triangle fixtures instead of one convex fixture.</param>
        /// <param name="type">Native body type; static and kinematic bodies accept zero density.</param>
        /// <returns>The owned body with its mass-weighted center.</returns>
        /// <exception cref="ArgumentException">Body data, mass or fixture budgets are invalid.</exception>
        /// <remarks>Polygon contacts are discrete; polygon CCD is not implemented in this subset.</remarks>
        public PhysicsBody AddPolygon(ConvexPolygon shape, Vector2 origin, float density, float gravityScale, float angle = 0, bool fixedRotation = false, bool compound = true,
            PhysicsBodyType type = PhysicsBodyType.Dynamic)
        {
            ThrowIfLocked();
            ArgumentNullException.ThrowIfNull(shape);
            int count = compound ? shape.Vertices.Count - 2 : 1;
            if (bodies.Count >= 4096 || polygonFixtureCount + count > 8192 ||
                !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || MathF.Abs(origin.X) > 10000 || MathF.Abs(origin.Y) > 10000 ||
                !Enum.IsDefined(type) || !float.IsFinite(density) || density < 0 || (density == 0 && type == PhysicsBodyType.Dynamic) || density > 1000000 || !float.IsFinite(gravityScale) || MathF.Abs(gravityScale) > 1000000 ||
                !float.IsFinite(angle) || MathF.Abs(angle) > 1000000)
            {
                throw new ArgumentException("Invalid polygon body values or fixture budget.");
            }
            List<ConvexPolygon> fixtures = [];
            if (compound)
            {
                List<Vector2> remaining = [.. shape.Vertices];
                int cursor = remaining.Count - 1;
                while (remaining.Count > 2)
                {
                    int a = cursor % remaining.Count, b = (a + 1) % remaining.Count, c = (b + 1) % remaining.Count;
                    fixtures.Add(new([remaining[a], remaining[b], remaining[c]]));
                    remaining.RemoveAt(b);
                    cursor = b;
                }
            }
            else
            {
                fixtures.Add(shape);
            }
            PhysicsBody body = new(fixtures.AsReadOnly(), origin, density, gravityScale, angle, fixedRotation, type);
            body.Center0 = body.Position;
            body.Angle0 = angle;
            RegisterBodyProxies(body);
            bodies.Add(body);
            polygonFixtureCount += count;
            return body;
        }
    }
}
