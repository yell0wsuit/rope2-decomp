// Altered C# world traversal subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        /// <summary>Native <c>GetWorldPoint</c>: maps a body-origin local point through the cached transform.</summary>
        /// <param name="localPoint">Point relative to the body origin.</param>
        /// <returns>The world point.</returns>
        public Vector2 GetWorldPoint(Vector2 localPoint)
        {
            return Origin + PhysicsMath.Rotate(localPoint, Angle);
        }

        /// <summary>Native <c>GetLocalPoint</c>: maps a world point into the body-origin frame.</summary>
        /// <param name="worldPoint">World point.</param>
        /// <returns>The point relative to the body origin.</returns>
        public Vector2 GetLocalPoint(Vector2 worldPoint)
        {
            return PhysicsMath.InverseRotate(worldPoint - Origin, Angle);
        }

        /// <summary>Native <c>GetWorldVector</c>: rotates a body-frame vector into world space.</summary>
        /// <param name="localVector">Body-frame vector.</param>
        /// <returns>The world vector.</returns>
        public Vector2 GetWorldVector(Vector2 localVector)
        {
            return PhysicsMath.Rotate(localVector, Angle);
        }
    }

    public abstract partial class PhysicsJoint
    {
        /// <summary>Gets the joint's first body.</summary>
        public PhysicsBody BodyA => A;
        /// <summary>Gets the joint's second body.</summary>
        public PhysicsBody BodyB => B;
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Gets owned bodies in native body-list order, newest first.</summary>
        public IReadOnlyList<PhysicsBody> Bodies => [.. Enumerable.Reverse(bodies)];

        /// <summary>Gets a body's joints in native joint-edge order, newest first.</summary>
        /// <param name="body">Owned body.</param>
        /// <returns>Attached joints.</returns>
        /// <exception cref="ArgumentException">The body is not owned.</exception>
        public IReadOnlyList<PhysicsJoint> GetJoints(PhysicsBody body)
        {
            ArgumentNullException.ThrowIfNull(body);
            return !bodies.Contains(body) ? throw new ArgumentException("Unowned body.", nameof(body)) :
                [.. Enumerable.Reverse(joints).Where(j => j.A == body || j.B == body)];
        }

        /// <summary>Gets a body's contact candidates in native contact-edge order, newest first.</summary>
        /// <param name="body">Owned body.</param>
        /// <returns>Candidates involving any of the body's fixtures, including sensors and terrain.</returns>
        /// <exception cref="ArgumentException">The body is not owned.</exception>
        public IReadOnlyList<PhysicsFixtureContactState> GetContacts(PhysicsBody body)
        {
            ArgumentNullException.ThrowIfNull(body);
            return !bodies.Contains(body) ? throw new ArgumentException("Unowned body.", nameof(body)) :
                [.. CandidatesNewestFirst().Where(c => c.A.Body == body || c.B.Body == body).Select(c =>
                    new PhysicsFixtureContactState(c.A.Label, c.B.Label, c.CreationOrder, c.Touching, IsSensor(c.A, c.B), c.Manifold?.PointCount ?? 0))];
        }

        /// <summary>Removes an owned body, first reporting each attached joint as native <c>SayGoodbye</c> does.</summary>
        /// <param name="body">Body to retire.</param>
        /// <param name="jointRemoved">Called for each attached joint, newest first, just before it is removed.</param>
        /// <returns>Whether the body belonged to this world.</returns>
        public bool RemoveBody(PhysicsBody body, Action<PhysicsJoint> jointRemoved)
        {
            ArgumentNullException.ThrowIfNull(jointRemoved);
            ThrowIfLocked();
            if (!bodies.Contains(body))
            {
                return false;
            }
            foreach (PhysicsJoint joint in GetJoints(body))
            {
                jointRemoved(joint);
                _ = Remove(joint);
            }
            return RemoveBody(body);
        }
    }
}
