// Altered C# compound polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        private readonly float polygonInitialMass;
        private readonly float polygonInverseInertia;
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
            get;
        }

        /// <summary>Gets the authored body origin in world meters.</summary>
        /// <value>The center of mass minus its rotated local offset.</value>
        public Vector2 Origin => Position - PhysicsMath.Rotate(LocalCenter, Angle);

        /// <summary>Gets whether angular integration is disabled.</summary>
        /// <value>True for a fixed-rotation polygon.</value>
        public bool FixedRotation
        {
            get;
        }

        /// <summary>Creates a compound body and combines fixture mass data in fixture order.</summary>
        /// <param name="fixtures">Owned immutable local convex geometry.</param>
        /// <param name="origin">Initial authored body origin in meters.</param>
        /// <param name="density">Positive fixture density.</param>
        /// <param name="gravityScale">World gravity multiplier.</param>
        /// <param name="angle">Initial clockwise radians.</param>
        /// <param name="fixedRotation">Whether to disable angular motion.</param>
        internal PhysicsBody(IReadOnlyList<ConvexPolygon> fixtures, Vector2 origin, float density, float gravityScale, float angle, bool fixedRotation)
            : this(origin, 0, 0, gravityScale)
        {
            PolygonFixtures = fixtures;
            Vector2 weightedCenter = Vector2.Zero;
            // ResetMassData traverses Box2D's prepended fixture list.
            for (int index = fixtures.Count - 1; index >= 0; index--)
            {
                PolygonMass data = fixtures[index].ComputeMass(density);
                Mass += data.Mass;
                weightedCenter += data.Mass * data.Center;
            }
            LocalCenter = weightedCenter * (1 / Mass);
            // ResetMassData sums origin moments in fixture-list order, then shifts once.
            float inertia = 0;
            for (int index = fixtures.Count - 1; index >= 0; index--)
            {
                inertia += fixtures[index].ComputeMass(density).OriginInertia;
            }
            inertia -= Mass * Vector2.Dot(LocalCenter, LocalCenter);
            if (inertia <= 0)
            {
                // Native asserts here when large origin moments cancel. Retain the managed
                // valid-small-shape extension without changing ordinary native float arithmetic.
                double centered = 0;
                foreach (ConvexPolygon fixture in fixtures)
                {
                    PolygonMass data = fixture.ComputeMass(density);
                    double dx = (double)data.Center.X - LocalCenter.X, dy = (double)data.Center.Y - LocalCenter.Y;
                    centered += data.CenterInertia + (data.Mass * ((dx * dx) + (dy * dy)));
                }
                inertia = (float)centered;
            }
            if (!float.IsFinite(Mass) || Mass < 1e-12f || !float.IsFinite(inertia) || inertia <= 0)
            {
                throw new ArgumentException("Invalid compound polygon mass or inertia.");
            }
            polygonInitialMass = Mass;
            polygonInverseInertia = 1 / inertia;
            FixedRotation = fixedRotation;
            Angle = angle;
            Position = origin + PhysicsMath.Rotate(LocalCenter, angle);
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Total owned dynamic polygon fixture count.</summary>
        private int polygonFixtureCount;

        /// <summary>Adds a dynamic convex outline, normally triangulated into a compound body.</summary>
        /// <param name="shape">Immutable convex local outline in meters.</param>
        /// <param name="origin">Initial authored body origin in world meters.</param>
        /// <param name="density">Positive fixture density no greater than one million.</param>
        /// <param name="gravityScale">Finite bounded world gravity multiplier.</param>
        /// <param name="angle">Initial clockwise radians.</param>
        /// <param name="fixedRotation">Whether to disable angular motion.</param>
        /// <param name="compound">Whether to create cyclic triangle fixtures instead of one convex fixture.</param>
        /// <returns>The owned dynamic body with its mass-weighted center.</returns>
        /// <exception cref="ArgumentException">Body data, mass or fixture budgets are invalid.</exception>
        /// <remarks>Polygon CCD and body-to-body polygon/polygon contacts are not implemented in this subset.</remarks>
        public PhysicsBody AddPolygon(ConvexPolygon shape, Vector2 origin, float density, float gravityScale, float angle = 0, bool fixedRotation = false, bool compound = true)
        {
            ArgumentNullException.ThrowIfNull(shape);
            int count = compound ? shape.Vertices.Count - 2 : 1;
            if (bodies.Count >= 4096 || polygonFixtureCount + count > 8192 ||
                !float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || MathF.Abs(origin.X) > 10000 || MathF.Abs(origin.Y) > 10000 ||
                !float.IsFinite(density) || density <= 0 || density > 1000000 || !float.IsFinite(gravityScale) || MathF.Abs(gravityScale) > 1000000 ||
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
            PhysicsBody body = new(fixtures.AsReadOnly(), origin, density, gravityScale, angle, fixedRotation);
            RegisterBodyProxies(body);
            bodies.Add(body);
            polygonFixtureCount += count;
            return body;
        }
    }
}
