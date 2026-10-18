namespace Ctr2.Engine.Physics
{
    /// <summary>Shared static-terrain constraint operations for a dynamic circle or polygon fixture.</summary>
    internal abstract class PhysicsContact
    {
        /// <summary>Gets the contacted dynamic body.</summary>
        /// <value>The world-owned body.</value>
        internal abstract PhysicsBody Body
        {
            get;
        }
        /// <summary>Contact creation sequence retained while broadphase proxies overlap.</summary>
        internal long CreationOrder;
        /// <summary>Restores compatible accumulated impulses.</summary>
        /// <param name="previous">Previous manifold for the same fixture pair.</param>
        internal abstract void Restore(PhysicsContact previous);
        /// <summary>Initializes velocity masses and restitution bias before warm starting.</summary>
        internal abstract void Initialize();
        /// <summary>Applies retained velocity impulses.</summary>
        internal abstract void WarmStart();
        /// <summary>Solves contact friction and unilateral normal velocity constraints.</summary>
        internal abstract void SolveVelocity();
        /// <summary>Corrects penetration using the selected position tolerance.</summary>
        /// <param name="continuous">Whether to use time-of-impact correction parameters.</param>
        /// <returns>Whether all manifold points satisfy the selected slop tolerance.</returns>
        internal abstract bool SolvePosition(bool continuous = false);
    }
}
