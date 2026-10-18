using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Native body, joint-edge and contact-edge traversal order and point helpers.</summary>
    public sealed class WorldTopologyTests
    {
        /// <summary>Lists are newest first, and body removal says goodbye to each joint before removing it.</summary>
        [Fact]
        public void TraversalFollowsNativeLinkedListOrder()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 2), new(-2, 2)], .3f, 0);
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody a = world.AddCircle(new(0, .8f), .2f, 1, 1);
            PhysicsBody b = world.AddCircle(new(.3f, .8f), .2f, 1, 1);
            Assert.Equal([b, a, anchor], world.Bodies);
            PhysicsJoint first = world.AddRope(anchor, a, 2);
            PhysicsJoint second = world.AddRevolute(b, a, new Vector2(.15f, .8f));
            PhysicsJoint third = world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, 1, true));
            Assert.Equal([third, second, first], world.GetJoints(a));
            Assert.Equal([third, second], world.GetJoints(b));
            Assert.Same(a, second.BodyB);
            Assert.Same(b, second.BodyA);
            for (int i = 0; i < 5; i++)
            {
                world.Step();
            }
            IReadOnlyList<PhysicsFixtureContactState> contacts = world.GetContacts(a);
            Assert.NotEmpty(contacts);
            Assert.Equal(world.CaptureFixtureContacts().Where(c => c.FirstFixture == 3 || c.SecondFixture == 3), contacts);
            List<PhysicsJoint> goodbye = [];
            Assert.True(world.RemoveBody(a, goodbye.Add));
            Assert.Equal([third, second, first], goodbye);
            Assert.Empty(world.GetJoints(b));
            Assert.False(world.RemoveBody(a, goodbye.Add));
            _ = Assert.Throws<ArgumentException>(() => world.GetJoints(a));
            _ = Assert.Throws<ArgumentException>(() => world.GetContacts(a));
        }

        /// <summary>World and local point helpers use the cached transform and invert each other.</summary>
        [Fact]
        public void PointHelpersUseTheCachedTransform()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddPolygon(new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]), new(.37f, -1.1f), 1, 0, .8f);
            Assert.Equal(new Vector2(.37f, -1.1f), body.GetWorldPoint(Vector2.Zero));
            Vector2 local = new(.25f, -.4f);
            Vector2 world2 = body.GetWorldPoint(local);
            Assert.True(Vector2.Distance(local, body.GetLocalPoint(world2)) < 1e-6f);
            Assert.True(Vector2.Distance(world2 - body.Origin, body.GetWorldVector(local)) < 1e-6f);
        }
    }
}
