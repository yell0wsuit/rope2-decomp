// Altered C# fixture sensor subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System.Collections.Generic;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsWorld
    {
        /// <summary>Gets one active fixture's sensor flag by its stable world-local label.</summary>
        /// <param name="fixture">An active label from CaptureProxies.</param>
        /// <returns>The fixture's current sensor flag.</returns>
        /// <exception cref="KeyNotFoundException">The label is unknown or retired.</exception>
        public bool GetFixtureSensor(int fixture) => proxies[fixture].IsSensor;

        /// <summary>Changes one fixture's flag without immediately updating its contacts or proxy.</summary>
        /// <param name="fixture">An active label from CaptureProxies.</param>
        /// <param name="isSensor">Whether the fixture should use sensor overlap.</param>
        /// <exception cref="KeyNotFoundException">The label is unknown or retired.</exception>
        public void SetFixtureSensor(int fixture, bool isSensor) => proxies[fixture].IsSensor = isSensor;
    }
}
