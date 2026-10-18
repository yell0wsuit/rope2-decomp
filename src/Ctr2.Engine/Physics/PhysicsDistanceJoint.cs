// Altered C# rope/distance subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A Box2D 2.2.1 rope or soft distance constraint with rotating local anchors.</summary>
    internal sealed class PhysicsDistanceJoint : PhysicsJoint
    {
        /// <summary>Positive spring frequency, or zero for a unilateral rope limit.</summary>
        private readonly float frequency;
        /// <summary>Spring damping ratio.</summary>
        private readonly float damping;
        /// <summary>Cached constraint direction.</summary>
        private Vector2 direction;
        /// <summary>Effective constraint mass.</summary>
        private float mass;
        /// <summary>Initial separation for predictive rope constraints.</summary>
        private float separation;
        /// <summary>Spring softness.</summary>
        private float gamma;
        /// <summary>Spring position bias.</summary>
        private float bias;
        /// <summary>Accumulated warm-start impulse.</summary>
        private float impulse;
        private readonly Vector2 localAnchorA;
        private readonly Vector2 localAnchorB;
        private Vector2 armA;
        private Vector2 armB;
        /// <summary>Gets the rope limit or spring rest length in meters.</summary>
        /// <value>The configured nonnegative length.</value>
        public override float Length
        {
            get;
        }
        /// <summary>Gets the reaction force on body B from the latest solver initialization.</summary>
        /// <value>A force vector in newtons, based on the fixed timestep.</value>
        public override Vector2 ReactionForce => impulse / PhysicsWorld.TimeStep * direction;

        /// <summary>Gets the current world-space first anchor in meters.</summary>
        public override Vector2 AnchorA => A.Position + Rotate(localAnchorA - A.LocalCenter, A.Angle);
        /// <summary>Gets the current world-space second anchor in meters.</summary>
        public override Vector2 AnchorB => B.Position + Rotate(localAnchorB - B.LocalCenter, B.Angle);

        /// <summary>Creates a constraint with body-origin local anchors.</summary>
        /// <param name="a">First body.</param>
        /// <param name="b">Second body.</param>
        /// <param name="length">Positive rest length or upper limit.</param>
        /// <param name="frequency">Zero for rope; positive for spring.</param>
        /// <param name="damping">Spring damping ratio.</param>
        /// <param name="localAnchorA">First body-local anchor.</param>
        /// <param name="localAnchorB">Second body-local anchor.</param>
        internal PhysicsDistanceJoint(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping, Vector2 localAnchorA, Vector2 localAnchorB) : base(a, b)
        {
            Length = length;
            this.frequency = frequency;
            this.damping = damping;
            this.localAnchorA = localAnchorA;
            this.localAnchorB = localAnchorB;
        }

        /// <summary>Computes effective mass and applies last step's accumulated impulse.</summary>
        /// <param name="firstStep">Whether previous-step impulses must be zeroed.</param>
        internal override void Initialize(bool firstStep)
        {
            armA = Rotate(localAnchorA - A.LocalCenter, A.Angle);
            armB = Rotate(localAnchorB - B.LocalCenter, B.Angle);
            direction = B.Position + armB - A.Position - armA;
            separation = direction.Length();
            if (separation > PhysicsWorld.LinearSlop)
            {
                direction *= 1 / separation;
            }
            else
            {
                direction = Vector2.Zero;
                if (frequency == 0)
                {
                    mass = impulse = 0;
                    return;
                }
            }
            float crossA = Cross(armA, direction), crossB = Cross(armB, direction);
            float inverseMass = A.InverseMass + (A.InverseInertia * crossA * crossA) +
                B.InverseMass + (B.InverseInertia * crossB * crossB);
            mass = inverseMass != 0 ? 1 / inverseMass : 0;
            gamma = bias = 0;
            if (frequency > 0)
            {
                float omega = 2 * MathF.PI * frequency;
                float d = 2 * mass * damping * omega;
                float k = mass * omega * omega;
                float h = PhysicsWorld.TimeStep;
                gamma = h * (d + (h * k));
                gamma = gamma != 0 ? 1 / gamma : 0;
                bias = (separation - Length) * h * k * gamma;
                inverseMass += gamma;
                mass = inverseMass != 0 ? 1 / inverseMass : 0;
            }
            if (firstStep)
            {
                impulse = 0;
            }
            ApplyVelocity(impulse * direction);
        }

        /// <summary>Solves the predictive rope limit or soft distance velocity constraint.</summary>
        internal override void SolveVelocity()
        {
            Vector2 velocityA = A.Velocity + (A.AngularVelocity * new Vector2(-armA.Y, armA.X));
            Vector2 velocityB = B.Velocity + (B.AngularVelocity * new Vector2(-armB.Y, armB.X));
            float speed = Vector2.Dot(direction, velocityB - velocityA);
            if (frequency == 0 && separation < Length)
            {
                speed += 1 / PhysicsWorld.TimeStep * (separation - Length);
            }
            float change = -mass * (speed + bias + (gamma * impulse));
            float previous = impulse;
            impulse = frequency == 0 ? MathF.Min(0, impulse + change) : impulse + change;
            // Rope impulses clamp the accumulated limit; native distance joints apply the raw increment.
            ApplyVelocity((frequency == 0 ? impulse - previous : change) * direction);
        }

        /// <summary>Corrects excessive rope length; soft springs defer correction to the velocity solver.</summary>
        /// <returns>Whether the uncorrected rope error is below the CTR2 linear slop.</returns>
        internal override bool SolvePosition()
        {
            if (frequency > 0)
            {
                return true;
            }
            Vector2 rA = Rotate(localAnchorA - A.LocalCenter, A.Angle);
            Vector2 rB = Rotate(localAnchorB - B.LocalCenter, B.Angle);
            Vector2 u = B.Position + rB - A.Position - rA;
            float length = u.Length();
            if (length >= 1.1920929e-7f)
            {
                u *= 1 / length;
            }
            Vector2 correction = -mass * Math.Clamp(length - Length, 0, 0.2f) * u;
            A.Position -= A.InverseMass * correction;
            A.Angle -= A.InverseInertia * Cross(rA, correction);
            B.Position += B.InverseMass * correction;
            B.Angle += B.InverseInertia * Cross(rB, correction);
            return length - Length < PhysicsWorld.LinearSlop;
        }

        /// <summary>Applies equal and opposite impulses at the rotating anchors.</summary>
        /// <param name="value">Impulse vector on body B.</param>
        private void ApplyVelocity(Vector2 value)
        {
            A.Velocity -= A.InverseMass * value;
            A.AngularVelocity -= A.InverseInertia * Cross(armA, value);
            B.Velocity += B.InverseMass * value;
            B.AngularVelocity += B.InverseInertia * Cross(armB, value);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static Vector2 Rotate(Vector2 value, float angle)
        {
            float sine = MathF.Sin(angle), cosine = MathF.Cos(angle);
            return new((cosine * value.X) - (sine * value.Y), (sine * value.X) + (cosine * value.Y));
        }
    }

}
