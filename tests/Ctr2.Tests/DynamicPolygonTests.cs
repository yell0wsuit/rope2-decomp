using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks dynamic polygon body centers, integration and compound topology.</summary>
    public sealed class DynamicPolygonTests
    {
        /// <summary>Small valid fixtures retain positive centroid inertia far from their local origin.</summary>
        /// <param name="compound">Whether to split the square into triangles.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SmallOffsetPolygonRetainsCentroidInertia(bool compound)
        {
            ConvexPolygon shape = new([new(10, 10), new(10.01f, 10), new(10.01f, 10.01f), new(10, 10.01f)]);
            PhysicsWorld world = new();
            PhysicsBody body = world.AddPolygon(shape, new(-10, -10), 1, 1, compound: compound);
            Assert.InRange(MathF.Abs(body.Mass - shape.ComputeMass(1).Mass), 0, 1e-8f);
            world.Step();
            Assert.True(float.IsFinite(body.Position.Y));
        }

        /// <summary>Offset polygon motion integrates its center while artwork follows the authored origin.</summary>
        [Fact]
        public void IntegratesOffsetCenterAndPreservesOrigin()
        {
            PhysicsWorld world = new();
            ConvexPolygon shape = new([new(0, 0), new(.4f, 0), new(.4f, .4f), new(0, .4f)]);
            PhysicsBody body = world.AddPolygon(shape, new(1, 2), .5f, .7f, MathF.PI / 2);
            Assert.Equal(2, body.PolygonFixtures.Count);
            Assert.InRange(Vector2.Distance(body.Position, new(.8f, 2.2f)), 0, .00001f);
            Assert.InRange(Vector2.Distance(body.Origin, new(1, 2)), 0, .00001f);
            Assert.Equal(.08f, body.Mass, 6);
            world.Step();
            Assert.InRange(MathF.Abs(body.Origin.Y - (2 + (7 * PhysicsWorld.TimeStep * PhysicsWorld.TimeStep))), 0, .00001f);
        }

        /// <summary>The cyclic ear order is stable for a five-vertex convex outline.</summary>
        [Fact]
        public void CompoundTrianglesPreserveCyclicEarOrder()
        {
            Vector2[] vertices = [new(0, 0), new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)];
            PhysicsBody body = new PhysicsWorld().AddPolygon(new(vertices), Vector2.Zero, .5f, 1);
            Assert.Equal(3, body.PolygonFixtures.Count);
            Assert.Equal([vertices[4], vertices[0], vertices[1]], body.PolygonFixtures[0].Vertices);
            Assert.Equal([vertices[1], vertices[2], vertices[3]], body.PolygonFixtures[1].Vertices);
            Assert.Equal([vertices[3], vertices[4], vertices[1]], body.PolygonFixtures[2].Vertices);
        }

        /// <summary>A polygon falling through a static floor must acquire a support contact.</summary>
        [Fact]
        public void PolygonLandsOnFloor()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 2), new(2, 2), new(2, 3), new(-2, 3)], .3f, 0);
            PhysicsBody body = world.AddPolygon(new([new(-.2f, -.2f), new(.2f, -.2f), new(.2f, .2f), new(-.2f, .2f)]), Vector2.Zero, .5f, .7f);
            for (int i = 0; i < 180; i++)
            {
                world.Step();
            }
            Assert.InRange(body.Position.Y, 1.79f, 1.81f);
            Assert.InRange(MathF.Abs(body.Velocity.Y), 0, .001f);
        }

        /// <summary>Unsupported polygon anchors fail without preventing supported circle constraints.</summary>
        [Fact]
        public void PolygonCenterJointsFailExplicitly()
        {
            PhysicsWorld world = new();
            PhysicsBody polygon = world.AddPolygon(new([new(0, 0), new(.4f, 0), new(0, .4f)]), Vector2.Zero, 1, 1);
            PhysicsBody circle = world.AddCircle(Vector2.One, .1f, 1, 1);
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddRope(polygon, anchor, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddSpring(circle, polygon, 1, 10, .3f));
            Assert.NotNull(world.AddRope(circle, anchor, 1));
            world.Step();
            Assert.True(float.IsFinite(polygon.Position.Y));
        }

        /// <summary>Compound fixture limits reject an oversized addition without consuming remaining capacity.</summary>
        [Fact]
        public void CompoundBudgetFailureIsAtomic()
        {
            PhysicsWorld world = new();
            ConvexPolygon hexagon = new([new(-1, 0), new(-.5f, -1), new(.5f, -1), new(1, 0), new(.5f, 1), new(-.5f, 1)]);
            ConvexPolygon triangle = new([new(0, 0), new(1, 0), new(0, 1)]);
            for (int i = 0; i < 2047; i++)
            {
                _ = world.AddPolygon(hexagon, Vector2.Zero, 1, 1);
            }
            _ = world.AddPolygon(triangle, Vector2.Zero, 1, 1);
            _ = Assert.Throws<ArgumentException>(() => world.AddPolygon(hexagon, Vector2.Zero, 1, 1));
            for (int i = 0; i < 3; i++)
            {
                Assert.NotNull(world.AddPolygon(triangle, Vector2.Zero, 1, 1));
            }
            _ = Assert.Throws<ArgumentException>(() => world.AddPolygon(triangle, Vector2.Zero, 1, 1));
            Assert.NotNull(world.AddCircle(Vector2.Zero, .1f, 1, 1));
        }

        /// <summary>Checks polygon/terrain trajectories against synthetic upstream single and compound bodies.</summary>
        /// <param name="scenario">Numerical trajectory scenario.</param>
        [Theory]
        [InlineData("floor")]
        [InlineData("tilted")]
        [InlineData("bounce")]
        [InlineData("offset")]
        [InlineData("compound")]
        [InlineData("fixed")]
        [InlineData("slope")]
        [InlineData("free")]
        [InlineData("triangles")]
        [InlineData("shared-left")]
        [InlineData("shared-right")]
        public void MatchesNumericalPolygonContacts(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            if (scenario != "free")
            {
                Vector2[] terrain = scenario == "slope" ? [new(-2, 1), new(2, 2), new(2, 3), new(-2, 2)] : [new(-2, 2), new(2, 2), new(2, 3), new(-2, 3)];
                if (scenario == "triangles")
                {
                    world.AddTerrain([terrain[0], terrain[1], terrain[2]], .3f, 0);
                    world.AddTerrain([terrain[0], terrain[2], terrain[3]], .3f, 0);
                }
                else
                {
                    world.AddTerrain(terrain, .3f, scenario == "bounce" ? .6f : 0);
                }
            }
            bool compound = scenario is "compound" or "free";
            ConvexPolygon shape = new(compound ? [new(0, 0), new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)] :
                scenario == "offset" ? [new(0, 0), new(.4f, 0), new(.4f, .4f), new(0, .4f)] :
                [new(-.2f, -.2f), new(.2f, -.2f), new(.2f, .2f), new(-.2f, .2f)]);
            bool shared = scenario.StartsWith("shared-", StringComparison.Ordinal);
            PhysicsBody body = world.AddPolygon(shape, shared ? new(-.8f, 0) : Vector2.Zero, .5f, .7f, shared || scenario is "tilted" or "fixed" ? .3f : 0, scenario == "fixed", compound);
            body.AngularDamping = .1f;
            body.Friction = .3f;
            if (shared)
            {
                PhysicsBody other = world.AddPolygon(shape, new(.8f, -.2f), .5f, .7f, -.2f, compound: false);
                other.AngularDamping = .1f;
                other.Friction = .3f;
                if (scenario == "shared-right")
                {
                    body = other;
                }
            }
            string[][] samples = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PolygonContactReference.csv")).Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.Equal(19, samples.Length);
            int tick = 0;
            foreach (string[] row in samples)
            {
                int target = int.Parse(row[1], CultureInfo.InvariantCulture);
                while (tick < target)
                {
                    world.Step();
                    tick++;
                }
                Assert.Equal(target, tick);
                float[] actual = [body.Origin.X, body.Origin.Y, body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity, body.Mass, body.LocalCenter.X, body.LocalCenter.Y];
                for (int component = 0; component < actual.Length; component++)
                {
                    float expected = float.Parse(row[component + 2], CultureInfo.InvariantCulture);
                    Assert.True(MathF.Abs(actual[component] - expected) < .00002f,
                        $"{scenario} tick {tick} component {component}: expected {expected:R}, actual {actual[component]:R}");
                }
            }
        }
    }
}
