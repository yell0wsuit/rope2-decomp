using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks supported static convex terrain contacts with dynamic candy circles.</summary>
    public sealed class TerrainContactTests
    {
        /// <summary>Matches discrete static-contact states from synthetic upstream C++ Box2D scenes.</summary>
        /// <param name="scenario">Numerical fixture scenario.</param>
        [Theory]
        [InlineData("floor")]
        [InlineData("bounce")]
        [InlineData("slope")]
        [InlineData("vertex")]
        [InlineData("rope")]
        [InlineData("corner")]
        public void MatchesNumericalContactFixture(string scenario)
        {
            PhysicsWorld world = new();
            Vector2[] polygon = scenario switch
            {
                "slope" or "corner" => [new(-2, 1), new(2, 2), new(2, 3), new(-2, 2)],
                "vertex" => [new(0, 1), new(2, 2), new(0, 2)],
                _ => [new(-2, 2), new(2, 2), new(2, 3), new(-2, 3)]
            };
            world.AddTerrain(polygon, .3f, scenario is "bounce" or "rope" ? .8f : 0);
            if (scenario == "corner")
            {
                world.AddTerrain([new(0, 0), new(.3f, 0), new(.3f, 4), new(0, 4)], .3f, 0);
            }
            Vector2 initial = scenario switch
            {
                "vertex" => new(-.1f, .89f),
                "corner" => new(-.1f, 1.35f),
                _ => Vector2.Zero
            };
            PhysicsBody body = world.AddCircle(initial, .16f, .5f, .7f);
            body.Friction = .3f;
            body.AngularDamping = 1;
            if (scenario == "rope")
            {
                PhysicsBody anchor = world.AddCircle(new(-1, 0), .08f, 0, 0);
                _ = world.AddRope(anchor, body, 1.9f);
            }
            int tick = 0;
            foreach (string[] row in File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "TerrainReference.csv")).Select(line => line.Split(',')).Where(row => row[0] == scenario))
            {
                int target = int.Parse(row[1], CultureInfo.InvariantCulture);
                while (tick < target)
                {
                    world.Step();
                    tick++;
                }
                float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                for (int i = 0; i < actual.Length; i++)
                {
                    float expected = float.Parse(row[i + 2], CultureInfo.InvariantCulture);
                    Assert.True(MathF.Abs(actual[i] - expected) <= .00002f,
                        $"{scenario}, tick {tick}, component {i}: expected {expected}, actual {actual[i]}");
                }
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Public contact coefficients reject nonfinite values before they can poison solver state.</summary>
        [Fact]
        public void BodyContactCoefficientsRejectInvalidValues()
        {
            PhysicsBody body = new PhysicsWorld().AddCircle(Vector2.Zero, .16f, .5f, .7f);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.Friction = float.NaN);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.Restitution = float.PositiveInfinity);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.AngularDamping = -1);
            Assert.Equal(.2f, body.Friction);
            Assert.Equal(0, body.Restitution);
            Assert.Equal(0, body.AngularDamping);
        }

        /// <summary>A falling circle rests on a solid floor, while sensor material points pass through.</summary>
        [Fact]
        public void FloorStopsCandyButNotSensors()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 2), new(2, 2), new(2, 3), new(-2, 3)], .3f, 0);
            PhysicsBody candy = world.AddCircle(Vector2.Zero, .16f, .5f, .7f);
            PhysicsBody sensor = world.AddCircle(new(.5f, 0), .03f, .6f, .6f);
            sensor.IsSensor = true;
            for (int i = 0; i < 180; i++)
            {
                world.Step();
            }
            Assert.InRange(candy.Position.Y, 1.83f, 1.85f);
            Assert.InRange(MathF.Abs(candy.Velocity.Y), 0, .001f);
            Assert.True(sensor.Position.Y > 10);
        }

        /// <summary>Restitution produces upward velocity after impact rather than sticking to the floor.</summary>
        [Fact]
        public void RestitutionBouncesCandy()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 2), new(-2, 2)], 0, .8f);
            PhysicsBody candy = world.AddCircle(Vector2.Zero, .16f, .5f, 1);
            bool bounced = false;
            for (int i = 0; i < 60; i++)
            {
                world.Step();
                bounced |= candy.Velocity.Y < -1;
            }
            Assert.True(bounced);
        }

        /// <summary>Malformed, concave and excessive fixtures are rejected before entering the world.</summary>
        [Fact]
        public void InvalidConvexGeometryIsRejected()
        {
            PhysicsWorld world = new();
            _ = Assert.Throws<ArgumentException>(() => world.AddTerrain([Vector2.Zero, Vector2.One], .3f, 0));
            _ = Assert.Throws<ArgumentException>(() => world.AddTerrain([Vector2.Zero, new(1, 0), new(.2f, .2f), new(0, 1)], .3f, 0));
            _ = Assert.Throws<ArgumentException>(() => world.AddTerrain([Vector2.Zero, new(float.NaN, 0), Vector2.One], .3f, 0));
        }
    }
}
