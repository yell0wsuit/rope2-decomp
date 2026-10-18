// Altered C# body-control subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        /// <summary>Force accumulated since the last step, cleared after each step.</summary>
        internal Vector2 Force;
        /// <summary>Torque accumulated since the last step, cleared after each step.</summary>
        internal float Torque;

        /// <summary>Replaces the linear velocity of a dynamic or kinematic body; static bodies ignore the call.</summary>
        /// <param name="velocity">Finite center-of-mass velocity in meters per second.</param>
        /// <exception cref="ArgumentOutOfRangeException">The velocity is nonfinite.</exception>
        public void SetLinearVelocity(Vector2 velocity)
        {
            Finite(velocity);
            if (Type != PhysicsBodyType.Static)
            {
                Velocity = velocity;
            }
        }

        /// <summary>Replaces the angular velocity of a dynamic or kinematic body; static bodies ignore the call.</summary>
        /// <param name="velocity">Finite clockwise radians per second.</param>
        /// <exception cref="ArgumentOutOfRangeException">The velocity is nonfinite.</exception>
        public void SetAngularVelocity(float velocity)
        {
            Finite(velocity);
            if (Type != PhysicsBodyType.Static)
            {
                AngularVelocity = velocity;
            }
        }

        /// <summary>Accumulates a force at a world point for the next step.</summary>
        /// <param name="force">Finite force in newtons.</param>
        /// <param name="point">Finite world application point in meters.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument is nonfinite.</exception>
        public void ApplyForce(Vector2 force, Vector2 point)
        {
            Finite(force);
            Finite(point);
            if (Mass <= 0)
            {
                return;
            }

            Force += force;
            Torque += Cross(point - Position, force);
        }

        /// <summary>Accumulates a force through the center of mass for the next step.</summary>
        /// <param name="force">Finite force in newtons.</param>
        /// <exception cref="ArgumentOutOfRangeException">The force is nonfinite.</exception>
        public void ApplyForceToCenter(Vector2 force)
        {
            Finite(force);
            if (Mass > 0)
            {
                Force += force;
            }
        }

        /// <summary>Accumulates a torque for the next step.</summary>
        /// <param name="torque">Finite torque in newton meters.</param>
        /// <exception cref="ArgumentOutOfRangeException">The torque is nonfinite.</exception>
        public void ApplyTorque(float torque)
        {
            Finite(torque);
            if (Mass > 0)
            {
                Torque += torque;
            }
        }

        /// <summary>Immediately changes velocity by an impulse at a world point.</summary>
        /// <param name="impulse">Finite impulse in newton seconds.</param>
        /// <param name="point">Finite world application point in meters.</param>
        /// <exception cref="ArgumentOutOfRangeException">An argument is nonfinite.</exception>
        public void ApplyLinearImpulse(Vector2 impulse, Vector2 point)
        {
            Finite(impulse);
            Finite(point);
            if (Mass <= 0)
            {
                return;
            }

            Velocity += InverseMass * impulse;
            AngularVelocity += InverseInertia * Cross(point - Position, impulse);
        }

        /// <summary>Immediately changes angular velocity by an angular impulse.</summary>
        /// <param name="impulse">Finite angular impulse in newton meter seconds.</param>
        /// <exception cref="ArgumentOutOfRangeException">The impulse is nonfinite.</exception>
        public void ApplyAngularImpulse(float impulse)
        {
            Finite(impulse);
            if (Mass > 0)
            {
                AngularVelocity += InverseInertia * impulse;
            }
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static void Finite(Vector2 value)
        {
            if (!float.IsFinite(value.X) || !float.IsFinite(value.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        private static void Finite(float value)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Teleports an owned body's origin and angle, then immediately discovers new proxy pairs.</summary>
        /// <param name="body">Owned static or dynamic body.</param>
        /// <param name="origin">Authored body origin in meters, within +/-10,000.</param>
        /// <param name="angle">Finite clockwise radians within +/-1,000,000.</param>
        /// <exception cref="ArgumentException">The body is not owned or the transform is invalid.</exception>
        /// <remarks>Velocities and contact manifolds are retained, as in native <c>SetTransform</c>.</remarks>
        public void SetTransform(PhysicsBody body, Vector2 origin, float angle)
        {
            ArgumentNullException.ThrowIfNull(body);
            if (!bodies.Contains(body) || !ValidAnchor(origin) || !float.IsFinite(angle) || MathF.Abs(angle) > 1000000)
            {
                throw new ArgumentException("Invalid transform or unowned body.", nameof(body));
            }

            body.Angle = angle;
            body.Position = origin + PhysicsMath.Rotate(body.LocalCenter, angle);
            body.TransformOrigin = origin;
            List<FixtureProxy> fixtures = bodyProxies[body];
            // Native body fixture lists prepend creation.
            for (int index = fixtures.Count - 1; index >= 0; index--)
            {
                _ = broadPhase.MoveProxy(fixtures[index].Id, BodyBounds(body, index, origin, angle), Vector2.Zero);
            }

            FindNewContacts();
        }
    }
}
