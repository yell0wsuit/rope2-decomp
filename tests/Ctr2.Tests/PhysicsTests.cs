using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks contact-free Box2D integration and center-anchor constraint behavior.</summary>
    public sealed class PhysicsTests
    {
        /// <summary>Compares sampled body states with independently compiled upstream C++ Box2D 2.2.1.</summary>
        /// <param name="scenario">Synthetic oracle scene name.</param>
        [Theory]
        [InlineData("free")]
        [InlineData("slack")]
        [InlineData("taut")]
        [InlineData("chain")]
        [InlineData("spring")]
        [InlineData("multi")]
        [InlineData("islands")]
        public void MatchesCppReference(string scenario)
        {
            PhysicsWorld world = new();
            List<PhysicsBody> bodies = [];
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, .08f, 0, 0);
            PhysicsBody candy = world.AddCircle(new(.7f, .4f), .16f, .5f, .7f);
            bodies.Add(anchor);
            bodies.Add(candy);
            if (scenario == "free")
            {
                candy.LinearDamping = 2;
            }
            else if (scenario == "spring")
            {
                _ = world.AddSpring(anchor, candy, .5f, 5, .3f);
            }
            else if (scenario is "chain" or "multi" or "islands")
            {
                PhysicsBody p1 = world.AddCircle(new(.35f, .2f), .03f, .6f, .6f);
                PhysicsBody p2 = world.AddCircle(new(.7f, .4f), .03f, .6f, .6f);
                bodies.Add(p1);
                bodies.Add(p2);
                _ = world.AddRope(anchor, p1, .4f);
                _ = world.AddRope(p1, p2, .4f);
                _ = world.AddRope(p2, candy, .25f);
                if (scenario == "multi")
                {
                    PhysicsBody anchor2 = world.AddCircle(new(1.4f, 0), .08f, 0, 0);
                    bodies.Add(anchor2);
                    _ = world.AddRope(anchor2, candy, 1.2f);
                }
            }
            else
            {
                _ = world.AddRope(anchor, candy, scenario == "slack" ? 1.2f : .5f);
            }
            if (scenario == "islands")
            {
                PhysicsBody a = world.AddCircle(new(3, 0), .08f, 0, 0);
                PhysicsBody c = world.AddCircle(new(3.7f, .4f), .16f, .5f, .7f);
                bodies.Add(a);
                bodies.Add(c);
                _ = world.AddRope(a, c, .5f);
            }
            string[][] samples = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PhysicsReference.csv"))
                .Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.NotEmpty(samples);
            int tick = 0;
            foreach (string[] row in samples)
            {
                int targetTick = int.Parse(row[1], CultureInfo.InvariantCulture);
                while (tick < targetTick)
                {
                    world.Step();
                    tick++;
                }
                PhysicsBody body = bodies[int.Parse(row[2], CultureInfo.InvariantCulture)];
                float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y];
                for (int i = 0; i < 4; i++)
                {
                    float expected = float.Parse(row[i + 3], CultureInfo.InvariantCulture);
                    Assert.True(MathF.Abs(expected - actual[i]) <= 0.00002f,
                        $"{scenario} tick {tick}, body {row[2]}, component {i}: expected {expected}, actual {actual[i]}");
                }
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Disconnected chains must stop position iterations independently.</summary>
        [Fact]
        public void UnrelatedIslandCannotChangeChainTrajectory()
        {
            // Local functions cannot carry XML documentation.
            // Return the measured chain, optionally adding a slowly converging island.
            static (PhysicsWorld World, PhysicsBody Candy) Create(bool unrelated)
            {
                PhysicsWorld world = new();
                PhysicsBody anchor = world.AddCircle(Vector2.Zero, .1f, 0, 0);
                PhysicsBody point = world.AddCircle(new(.6f, .1f), .1f, .5f, .7f);
                PhysicsBody candy = world.AddCircle(new(.9f, .6f), .1f, .5f, .7f);
                _ = world.AddRope(anchor, point, .4f);
                _ = world.AddRope(point, candy, .4f);
                if (unrelated)
                {
                    PhysicsBody previous = world.AddCircle(new(5, 0), .1f, 0, 0);
                    for (int i = 1; i <= 10; i++)
                    {
                        PhysicsBody next = world.AddCircle(new(5 + (i * .8f), i * .1f), .1f, .5f, .7f);
                        _ = world.AddRope(previous, next, .4f);
                        previous = next;
                    }
                }
                return (world, candy);
            }
            (PhysicsWorld World, PhysicsBody Candy) = Create(false);
            (PhysicsWorld World, PhysicsBody Candy) together = Create(true);
            for (int tick = 0; tick < 180; tick++)
            {
                World.Step();
                together.World.Step();
                Assert.Equal(Candy.Position, together.Candy.Position);
                Assert.Equal(Candy.Velocity, together.Candy.Velocity);
            }
        }

        /// <summary>Uses the original semi-implicit integration and linear damping formula.</summary>
        [Fact]
        public void FreeFallAndDampingUseOriginalFixedStep()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddCircle(new(1, 2), 0.16f, 0.5f, 0.7f);
            body.LinearDamping = 2;
            world.Step();
            float speed = 0.0167f * 7 * (1 - (0.0167f * 2));
            Assert.Equal(speed, body.Velocity.Y, 6);
            Assert.Equal(2 + (0.0167f * speed), body.Position.Y, 6);
            Assert.Equal(0.5f * MathF.PI * 0.16f * 0.16f, body.Mass, 6);
        }

        /// <summary>A slack rope permits free fall while a taut rope only resists extension.</summary>
        [Fact]
        public void RopeDoesNotPushSlackBodiesAndKeepsStaticAnchorFixed()
        {
            PhysicsWorld world = new();
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, 0.08f, 0, 0);
            PhysicsBody candy = world.AddCircle(new(0, 0.5f), 0.16f, 0.5f, 0.7f);
            _ = world.AddRope(anchor, candy, 1);
            world.Step();
            Assert.True(candy.Position.Y > 0.5f);
            Assert.True(candy.Velocity.Y > 0);
            for (int i = 0; i < 120; i++)
            {
                world.Step();
            }
            Assert.InRange(candy.Position.Y, 0.999f, 1.001f);
            Assert.Equal(Vector2.Zero, anchor.Position);
        }

        /// <summary>Coincident rope endpoints do not introduce NaNs or an artificial direction.</summary>
        [Fact]
        public void CoincidentEndpointsStayFinite()
        {
            PhysicsWorld world = new();
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, 0.08f, 0, 0);
            PhysicsBody candy = world.AddCircle(Vector2.Zero, 0.16f, 0.5f, 0.7f);
            _ = world.AddRope(anchor, candy, 0.5f);
            for (int i = 0; i < 60; i++)
            {
                world.Step();
                Assert.True(float.IsFinite(candy.Position.X) && float.IsFinite(candy.Position.Y));
            }
        }

        /// <summary>Soft distance constraints pull toward their rest length and retain warm-start impulses.</summary>
        [Fact]
        public void DistanceSpringPullsTowardRestLength()
        {
            PhysicsWorld world = new();
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, 0.08f, 0, 0);
            PhysicsBody candy = world.AddCircle(new(0, 2), 0.16f, 0.5f, 0);
            _ = world.AddSpring(anchor, candy, 1, 5, 0.3f);
            for (int i = 0; i < 180; i++)
            {
                world.Step();
            }
            Assert.InRange(candy.Position.Y, 0.999f, 1.001f);
        }
    }
}
