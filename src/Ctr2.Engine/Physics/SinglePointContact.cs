// Altered C# one-point two-body subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Shared native sequential impulses for one-point circle and polygon-circle manifolds.</summary>
    internal abstract class SinglePointContact : PhysicsContact
    {
        protected const float Epsilon = 1.1920929e-7f;
        protected readonly PhysicsBody a;
        protected readonly PhysicsBody b;
        private readonly float friction;
        private readonly float restitution;
        private Vector2 normal;
        private Vector2 tangent;
        private Vector2 armA;
        private Vector2 armB;
        private float normalMass;
        private float tangentMass;
        private float bias;
        private float normalImpulse;
        private float tangentImpulse;

        internal override PhysicsBody Body => a;
        internal override PhysicsBody? OtherBody => b;
        internal override int[] CaptureFeatures() => [0];

        protected SinglePointContact(PhysicsBody first, PhysicsBody second, float mixedFriction, float mixedRestitution)
        {
            a = first;
            b = second;
            friction = mixedFriction;
            restitution = mixedRestitution;
        }

        internal override void Restore(PhysicsContact previous)
        {
            if (previous is SinglePointContact contact)
            {
                normalImpulse = contact.normalImpulse;
                tangentImpulse = contact.tangentImpulse;
            }
        }

        internal override void Initialize()
        {
            var geometry = VelocityGeometry();
            normal = geometry.Normal;
            tangent = new(normal.Y, -normal.X);
            Vector2 point = geometry.Point;
            armA = point - a.Position;
            armB = point - b.Position;
            float rnA = Cross(armA, normal), rnB = Cross(armB, normal);
            float rtA = Cross(armA, tangent), rtB = Cross(armB, tangent);
            float kNormal = a.InverseMass + b.InverseMass + (a.InverseInertia * rnA * rnA) + (b.InverseInertia * rnB * rnB);
            float kTangent = a.InverseMass + b.InverseMass + (a.InverseInertia * rtA * rtA) + (b.InverseInertia * rtB * rtB);
            normalMass = kNormal > 0 ? 1 / kNormal : 0;
            tangentMass = kTangent > 0 ? 1 / kTangent : 0;
            float speed = Vector2.Dot(RelativeVelocity(), normal);
            bias = speed < -1 ? -restitution * speed : 0;
        }

        internal override void WarmStart() => Apply((normalImpulse * normal) + (tangentImpulse * tangent));

        internal override void SolveVelocity()
        {
            float speed = Vector2.Dot(RelativeVelocity(), tangent);
            float lambda = tangentMass * -speed;
            float limit = friction * normalImpulse;
            float next = Math.Clamp(tangentImpulse + lambda, -limit, limit);
            lambda = next - tangentImpulse;
            tangentImpulse = next;
            Apply(lambda * tangent);

            speed = Vector2.Dot(RelativeVelocity(), normal);
            lambda = -normalMass * (speed - bias);
            next = MathF.Max(normalImpulse + lambda, 0);
            lambda = next - normalImpulse;
            normalImpulse = next;
            Apply(lambda * normal);
        }

        internal override bool SolvePosition(bool continuous = false)
        {
            var geometry = PositionGeometry();
            Vector2 n = geometry.Normal, point = geometry.Point;
            float separation = geometry.Separation;
            Vector2 rA = point - a.Position, rB = point - b.Position;
            float rnA = Cross(rA, n), rnB = Cross(rB, n);
            float correction = Math.Clamp((continuous ? .75f : .2f) * (separation + PhysicsWorld.LinearSlop), -.2f, 0);
            float mass = a.InverseMass + b.InverseMass + (a.InverseInertia * rnA * rnA) + (b.InverseInertia * rnB * rnB);
            float impulse = mass > 0 ? -correction / mass : 0;
            Vector2 p = impulse * n;
            a.Position -= a.InverseMass * p;
            a.Angle -= a.InverseInertia * Cross(rA, p);
            b.Position += b.InverseMass * p;
            b.Angle += b.InverseInertia * Cross(rB, p);
            return separation >= -(continuous ? 1.5f : 3) * PhysicsWorld.LinearSlop;
        }

        protected abstract (Vector2 Normal, Vector2 Point) VelocityGeometry();
        protected abstract (Vector2 Normal, Vector2 Point, float Separation) PositionGeometry();

        private Vector2 RelativeVelocity() => b.Velocity + Cross(b.AngularVelocity, armB) - a.Velocity - Cross(a.AngularVelocity, armA);

        private void Apply(Vector2 impulse)
        {
            a.Velocity -= a.InverseMass * impulse;
            a.AngularVelocity -= a.InverseInertia * Cross(armA, impulse);
            b.Velocity += b.InverseMass * impulse;
            b.AngularVelocity += b.InverseInertia * Cross(armB, impulse);
        }

        protected static Vector2 Normalize(Vector2 value)
        {
            float length = value.Length();
            return length < Epsilon ? value : (1 / length) * value;
        }

        private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);
        private static Vector2 Cross(float value, Vector2 point) => new(-value * point.Y, value * point.X);
    }
}
