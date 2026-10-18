// Altered C# body type and mass subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native Box2D body simulation types.</summary>
    public enum PhysicsBodyType
    {
        /// <summary>Zero mass and zero velocity; never moves.</summary>
        Static,
        /// <summary>Zero mass; moves only by its assigned velocity and ignores forces.</summary>
        Kinematic,
        /// <summary>Positive mass; responds to gravity, forces, impulses and contacts.</summary>
        Dynamic
    }

    /// <summary>Explicit body mass properties, in the native <c>b2MassData</c> convention.</summary>
    /// <param name="Mass">Mass in kilograms; nonpositive values become one.</param>
    /// <param name="Center">Center of mass relative to the body origin, in meters.</param>
    /// <param name="RotationalInertia">Rotational inertia about the body origin.</param>
    public readonly record struct PhysicsMassData(float Mass, Vector2 Center, float RotationalInertia);

    public sealed partial class PhysicsBody
    {
        /// <summary>Gets the native simulation type.</summary>
        /// <value>Changed only through <see cref="PhysicsWorld.SetBodyType"/>.</value>
        public PhysicsBodyType Type
        {
            get; private set;
        }

        /// <summary>Gets the rotational inertia about the body origin, as native <c>GetInertia</c> reports it.</summary>
        /// <value>The centroidal inertia plus the parallel-axis term; only the parallel-axis term without rotation.</value>
        public float RotationalInertia => (InverseInertia > 0 ? 1 / InverseInertia : 0) + (Mass * Vector2.Dot(LocalCenter, LocalCenter));

        /// <summary>Per-fixture densities in creation order, used by the next mass reset.</summary>
        internal readonly float[] Densities;

        /// <summary>Recomputes mass, center of mass and inertia from the current fixture densities.</summary>
        /// <remarks>The center of mass moves with the cached transform and linear velocity gains the native
        /// <c>cross(angularVelocity, centerShift)</c> correction, as in native <c>ResetMassData</c>.</remarks>
        /// <exception cref="InvalidOperationException">The densities produce invalid mass or inertia.</exception>
        public void ResetMassData()
        {
            if (!TryComputeMass(Type, out float mass, out Vector2 center, out float inverse))
            {
                throw new InvalidOperationException("Fixture densities produce invalid mass or inertia.");
            }
            ApplyMass(mass, center, inverse);
        }

        /// <summary>Overrides a dynamic body's mass properties; other body types ignore the call.</summary>
        /// <param name="data">Finite mass data; inertia is about the body origin.</param>
        /// <exception cref="ArgumentOutOfRangeException">The data is nonfinite or its centered inertia is not positive.</exception>
        public void SetMassData(PhysicsMassData data)
        {
            if (!float.IsFinite(data.Mass) || !float.IsFinite(data.Center.X) || !float.IsFinite(data.Center.Y) ||
                MathF.Abs(data.Center.X) > 10000 || MathF.Abs(data.Center.Y) > 10000 || !float.IsFinite(data.RotationalInertia))
            {
                throw new ArgumentOutOfRangeException(nameof(data));
            }
            if (Type != PhysicsBodyType.Dynamic)
            {
                return;
            }
            float mass = data.Mass <= 0 ? 1 : data.Mass;
            float inverse = 0;
            if (data.RotationalInertia > 0 && !FixedRotation)
            {
                float inertia = data.RotationalInertia - (mass * Vector2.Dot(data.Center, data.Center));
                if (!float.IsFinite(inertia) || inertia <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(data));
                }
                inverse = 1 / inertia;
            }
            ApplyMass(mass, data.Center, inverse);
        }

        /// <summary>Changes the type and resets mass, matching native <c>SetType</c> before fixture work.</summary>
        /// <returns>Whether the type changed.</returns>
        internal bool ChangeType(PhysicsBodyType type)
        {
            if (Type == type)
            {
                return false;
            }
            if (!TryComputeMass(type, out float mass, out Vector2 center, out float inverse))
            {
                throw new InvalidOperationException("Fixture densities produce invalid mass or inertia.");
            }
            Type = type;
            ApplyMass(mass, center, inverse);
            if (type == PhysicsBodyType.Static)
            {
                Velocity = Vector2.Zero;
                AngularVelocity = 0;
            }
            Force = Vector2.Zero;
            Torque = 0;
            return true;
        }

        /// <summary>Computes native <c>ResetMassData</c> results for a type without mutating the body.</summary>
        internal bool TryComputeMass(PhysicsBodyType type, out float mass, out Vector2 localCenter, out float inverse)
        {
            mass = 0;
            localCenter = Vector2.Zero;
            inverse = 0;
            if (type != PhysicsBodyType.Dynamic)
            {
                return true;
            }
            float inertia = 0;
            bool dense = false;
            if (PolygonFixtures.Count == 0)
            {
                if (Densities[0] != 0)
                {
                    dense = true;
                    float fixtureMass = Densities[0] * MathF.PI * Radius * Radius;
                    mass += fixtureMass;
                    inertia += fixtureMass * (.5f * Radius * Radius);
                }
            }
            else
            {
                // ResetMassData traverses Box2D's prepended fixture list.
                for (int index = PolygonFixtures.Count - 1; index >= 0; index--)
                {
                    if (Densities[index] == 0)
                    {
                        continue;
                    }
                    dense = true;
                    PolygonMass data = PolygonFixtures[index].ComputeMass(Densities[index]);
                    mass += data.Mass;
                    localCenter += data.Mass * data.Center;
                    inertia += data.OriginInertia;
                }
            }
            if (dense && (!float.IsFinite(mass) || mass < 1e-12f))
            {
                return false;
            }
            if (mass > 0)
            {
                localCenter *= 1 / mass;
            }
            else
            {
                // Native forces every dynamic body to have positive mass.
                mass = 1;
            }
            if (inertia > 0 && !FixedRotation)
            {
                inertia -= mass * Vector2.Dot(localCenter, localCenter);
                if (inertia <= 0 && PolygonFixtures.Count > 0)
                {
                    // Native asserts here when large origin moments cancel. Retain the managed
                    // valid-small-shape extension without changing ordinary native float arithmetic.
                    double centered = 0;
                    for (int index = 0; index < PolygonFixtures.Count; index++)
                    {
                        if (Densities[index] == 0)
                        {
                            continue;
                        }
                        PolygonMass data = PolygonFixtures[index].ComputeMass(Densities[index]);
                        double dx = (double)data.Center.X - localCenter.X, dy = (double)data.Center.Y - localCenter.Y;
                        centered += data.CenterInertia + (data.Mass * ((dx * dx) + (dy * dy)));
                    }
                    inertia = (float)centered;
                }
                if (!float.IsFinite(inertia) || inertia <= 0)
                {
                    return false;
                }
                inverse = 1 / inertia;
            }
            return true;
        }

        /// <summary>Moves the center of mass with the cached transform and applies the native velocity correction.</summary>
        private void ApplyMass(float mass, Vector2 localCenter, float inverse)
        {
            Vector2 origin = Origin, oldCenter = Position;
            Mass = mass;
            LocalCenter = localCenter;
            InverseInertia = inverse;
            if (Type == PhysicsBodyType.Dynamic)
            {
                Position = origin + PhysicsMath.Rotate(localCenter, Angle);
                Vector2 shift = Position - oldCenter;
                Velocity += new Vector2(-AngularVelocity * shift.Y, AngularVelocity * shift.X);
            }
            else
            {
                Position = origin;
                Angle0 = Angle;
            }
            // Native keeps its cached transform; only the sweep center moves.
            TransformOrigin = origin;
            Center0 = Position;
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Changes an owned body's native type; repeating the current type does nothing.</summary>
        /// <param name="body">Owned body.</param>
        /// <param name="type">New body type.</param>
        /// <remarks>Mass is reset from fixture densities and accumulated forces clear. A body made static loses
        /// its velocity and synchronizes its proxies in place. Every fixture is refiltered, so contacts that no
        /// longer involve a dynamic body end at the next step and new pairs are discovered after it.</remarks>
        /// <exception cref="ArgumentException">The body is not owned or the type is undefined.</exception>
        /// <exception cref="InvalidOperationException">The densities produce invalid mass or inertia.</exception>
        public void SetBodyType(PhysicsBody body, PhysicsBodyType type)
        {
            ThrowIfLocked();
            ArgumentNullException.ThrowIfNull(body);
            if (!bodies.Contains(body) || !Enum.IsDefined(type))
            {
                throw new ArgumentException("Invalid body type or unowned body.", nameof(body));
            }
            if (!body.ChangeType(type))
            {
                return;
            }
            List<FixtureProxy> fixtures = bodyProxies[body];
            if (type == PhysicsBodyType.Static)
            {
                Vector2 origin = body.Origin;
                for (int index = fixtures.Count - 1; index >= 0; index--)
                {
                    fixtures[index].Bounds = BodyBounds(body, index, origin, body.Angle);
                    _ = broadPhase.MoveProxy(fixtures[index].Id, fixtures[index].Bounds, Vector2.Zero);
                }
            }
            for (int index = fixtures.Count - 1; index >= 0; index--)
            {
                broadPhase.TouchProxyOnce(fixtures[index].Id);
            }
        }

        /// <summary>Gets an owned body fixture's density.</summary>
        /// <param name="fixture">An active body fixture label from CaptureProxies.</param>
        /// <returns>The density used by the next mass reset.</returns>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        /// <exception cref="ArgumentException">The label names terrain.</exception>
        public float GetFixtureDensity(int fixture)
        {
            FixtureProxy proxy = proxies[fixture];
            return proxy.Body?.Densities[proxy.LocalIndex] ?? throw new ArgumentException("Terrain fixtures have no density.", nameof(fixture));
        }

        /// <summary>Changes a body fixture's density without changing mass until <see cref="PhysicsBody.ResetMassData"/>.</summary>
        /// <param name="fixture">An active body fixture label from CaptureProxies.</param>
        /// <param name="density">Finite density from zero to one million.</param>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        /// <exception cref="ArgumentException">The label names terrain or the density is invalid.</exception>
        public void SetFixtureDensity(int fixture, float density)
        {
            FixtureProxy proxy = proxies[fixture];
            if (proxy.Body == null || !float.IsFinite(density) || density < 0 || density > 1000000)
            {
                throw new ArgumentException("Invalid density or terrain fixture.", nameof(density));
            }
            proxy.Body.Densities[proxy.LocalIndex] = density;
        }
    }
}
