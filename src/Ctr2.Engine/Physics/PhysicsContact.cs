namespace Ctr2.Engine.Physics
{
    /// <summary>Shared operations for supported solid terrain and two-body constraints.</summary>
    internal abstract class PhysicsContact
    {
        /// <summary>Gets the terrain-contact body or native fixture A's body, which may be static.</summary>
        /// <value>The world-owned body.</value>
        internal abstract PhysicsBody Body
        {
            get;
        }
        /// <summary>Gets fixture B's body for a body pair, or null for implicit static terrain.</summary>
        internal virtual PhysicsBody? OtherBody => null;
        /// <summary>Snapshots native feature IDs in manifold point order.</summary>
        internal virtual int[] CaptureFeatures()
        {
            return [];
        }

        /// <summary>Stored solid manifold point count, independent of velocity conditioning.</summary>
        internal virtual int PointCount => 1;
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
