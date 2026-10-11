using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises swept circle contacts against thin static terrain.</summary>
    public sealed class ContinuousContactTests
    {
        /// <summary>A circle crossing a thin floor during one tick must stop above it.</summary>
        [Fact]
        public void FastCircleCannotTunnelThroughThinFloor()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.Step();
            Assert.InRange(body.Position.Y, .83f, .85f);
            Assert.Equal(0, body.Velocity.Y, 5);
        }
        /// <summary>Checks continuous contacts against synthetic upstream Box2D states.</summary>
        /// <param name="scenario">Thin face, bounce, slope, vertex or repeated collision.</param>
        [Theory]
        [InlineData("floor")]
        [InlineData("bounce")]
        [InlineData("slope")]
        [InlineData("vertex")]
        [InlineData("repeated")]
        public void MatchesContinuousReference(string scenario)
        {
            PhysicsWorld world = new();
            Vector2[] vertices = scenario switch
            {
                "slope" => [new(-2, .5f), new(2, 1.5f), new(2, 1.52f), new(-2, .52f)],
                "vertex" => [new(0, 1), new(2, 2), new(0, 2)],
                _ => [new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)]
            };
            world.AddTerrain(vertices, .3f, scenario is "bounce" or "repeated" ? .8f : 0);
            if (scenario == "repeated")
            {
                world.AddTerrain([new(-2, -.52f), new(2, -.52f), new(2, -.5f), new(-2, -.5f)], .3f, .8f);
            }
            PhysicsBody body = world.AddCircle(scenario == "vertex" ? new(-.1f, 0) : Vector2.Zero, .16f, .5f, 800);
            body.AngularDamping = 1;
            body.Friction = .3f;
            string[][] samples = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ContinuousReference.csv")).Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.Equal(10, samples.Length);
            int tick = 0;
            foreach (string[] row in samples)
            {
                world.Step();
                Assert.Equal(++tick, int.Parse(row[1], CultureInfo.InvariantCulture));
                float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                for (int component = 0; component < actual.Length; component++)
                {
                    float expected = float.Parse(row[component + 2], CultureInfo.InvariantCulture);
                    Assert.True(MathF.Abs(actual[component] - expected) < (component is 2 or 3 or 5 ? .01f : .001f),
                        $"{scenario} tick {row[1]} component {component}: expected {expected:R}, actual {actual[component]:R}");
                }
            }
        }
        /// <summary>The discrete isolation switch preserves the intentionally tunneling baseline.</summary>
        [Fact]
        public void DiscreteSwitchIsExplicit()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.Step();
            Assert.Equal(2, body.Position.Y, 5);
        }

        /// <summary>Sensors and circles missing the finite fixture retain unconstrained motion.</summary>
        /// <param name="sensor">Whether the circle is a non-solid sensor.</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SensorsAndMissesAreNotStopped(bool sensor)
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(new(sensor ? 0 : 2.5f, 0), .16f, .5f, 800);
            body.IsSensor = sensor;
            world.Step();
            Assert.Equal(2, body.Position.Y, 5);
        }

        /// <summary>Sweeps choose the nearest obstacle independently of fixture creation order.</summary>
        /// <param name="reverse">Whether to create the closer fixture first.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NearestFixtureWins(bool reverse)
        {
            PhysicsWorld world = new();
            foreach (float y in reverse ? new[] { .6f, 1f } : [1f, .6f])
            {
                world.AddTerrain([new(-2, y), new(2, y), new(2, y + .02f), new(-2, y + .02f)], 0, 0);
            }
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.Step();
            Assert.InRange(body.Position.Y, .43f, .45f);
            Assert.Equal(0, body.Velocity.Y, 5);
        }

        /// <summary>An upward sweep resolves the underside rather than assuming a downward-facing floor.</summary>
        [Fact]
        public void UpwardSweepStopsAtUnderside()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(new(0, 2), .16f, .5f, -800);
            world.Step();
            Assert.InRange(body.Position.Y, 1.17f, 1.19f);
            Assert.Equal(0, body.Velocity.Y, 5);
        }
    }
}
