// Altered C# joint traversal subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A world-owned constraint participating in the native joint/island traversal.</summary>
    public abstract partial class PhysicsJoint
    {
        internal readonly PhysicsBody A;
        internal readonly PhysicsBody B;
        /// <summary>Whether this joint allows collisions between its connected bodies.</summary>
        public bool CollideConnected
        {
            get;
        }
        /// <summary>Configured rope limit or spring rest length; unsupported for other joint types.</summary>
        /// <exception cref="NotSupportedException">This joint has no length parameter.</exception>
        public virtual float Length => throw new NotSupportedException("This joint has no length parameter.");
        /// <summary>World-space first anchor in meters.</summary>
        public abstract Vector2 AnchorA
        {
            get;
        }
        /// <summary>World-space second anchor in meters.</summary>
        public abstract Vector2 AnchorB
        {
            get;
        }
        /// <summary>Reaction force on body B in newtons at the fixed timestep.</summary>
        public abstract Vector2 ReactionForce
        {
            get;
        }
        /// <summary>Reaction torque from the angular constraint, excluding a motor, in newton meters.</summary>
        public virtual float ReactionTorque => 0;
        internal PhysicsJoint(PhysicsBody a, PhysicsBody b, bool collideConnected = false)
        {
            A = a;
            B = b;
            CollideConnected = collideConnected;
        }
        /// <summary>Set once construction finishes; native creation does not wake bodies, runtime setters do.</summary>
        private protected bool Constructed;

        /// <summary>Wakes both bodies, as native runtime joint setters do.</summary>
        private protected void Wake()
        {
            if (Constructed)
            {
                A.SetAwake(true);
                B.SetAwake(true);
            }
        }

        internal abstract void Initialize(bool firstStep);
        internal abstract void SolveVelocity();
        internal abstract bool SolvePosition();
    }
}
