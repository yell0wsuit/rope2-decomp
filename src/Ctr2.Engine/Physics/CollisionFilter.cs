// Altered C# subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System.Collections.Generic;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native fixture categories, accepted categories and signed group override.</summary>
    /// <param name="CategoryBits">This fixture's category bits.</param>
    /// <param name="MaskBits">Categories accepted by this fixture.</param>
    /// <param name="GroupIndex">Equal nonzero groups override masks: positive accepts, negative rejects.</param>
    public readonly record struct PhysicsCollisionFilter(ushort CategoryBits, ushort MaskBits, short GroupIndex)
    {
        /// <summary>Native fixture defaults: category one, all categories accepted, no group.</summary>
        public static PhysicsCollisionFilter Default => new(1, ushort.MaxValue, 0);
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Gets one owned fixture's filter using its stable world-local proxy diagnostic label.</summary>
        /// <param name="fixture">An active fixture label from CaptureProxies.</param>
        /// <returns>The fixture's current filter value.</returns>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        public PhysicsCollisionFilter GetFixtureFilter(int fixture)
        {
            return proxies[fixture].Filter;
        }

        /// <summary>Changes one fixture's filter; contact changes take effect during later world updates.</summary>
        /// <param name="fixture">An active fixture label from CaptureProxies.</param>
        /// <param name="filter">New categories, mask and group.</param>
        /// <exception cref="KeyNotFoundException">The fixture label is unknown or retired.</exception>
        public void SetFixtureFilter(int fixture, PhysicsCollisionFilter filter)
        {
            FixtureProxy proxy = proxies[fixture];
            // Native Refilter also touches an unchanged value. Coalesce pending world
            // touches so repeated setters followed by destruction cannot leave stale IDs.
            broadPhase.TouchProxyOnce(proxy.Id);
            proxy.Filter = filter;
        }

        private static bool ShouldCollide(PhysicsCollisionFilter a, PhysicsCollisionFilter b)
        {
            return a.GroupIndex != 0 && a.GroupIndex == b.GroupIndex ? a.GroupIndex > 0 :
            (a.MaskBits & b.CategoryBits) != 0 && (b.MaskBits & a.CategoryBits) != 0;
        }
    }
}
