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
    /// <summary>Headless rotational joint checks against independent native Box2D trajectories.</summary>
    public sealed class JointAnchorTests(ITestOutputHelper output)
    {
        /// <summary>Compares every tick, including angular state, reaction force and world anchors.</summary>
        /// <param name="scenario">Native synthetic scenario.</param>
        [Theory]
        [InlineData("rope_circle")]
        [InlineData("spring_circle")]
        [InlineData("rope_polygon")]
        [InlineData("spring_polygon")]
        [InlineData("rope_fixed")]
        [InlineData("spring_fixed")]
        [InlineData("rope_pair")]
        [InlineData("spring_pair")]
        public void MatchesNativeRotationalJointStates(string scenario)
        {
            bool pair = scenario.EndsWith("pair", StringComparison.Ordinal);
            bool fixedRotation = scenario.EndsWith("fixed", StringComparison.Ordinal);
            bool circle = scenario.EndsWith("circle", StringComparison.Ordinal);
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            List<PhysicsBody> bodies = [];
            PhysicsBody Make(bool polygon, bool dynamic, Vector2 position, float angle, float scale)
            {
                PhysicsBody body = polygon
                    ? world.AddPolygon(new ConvexPolygon([new(-.3f, -.1f), new(.5f, -.1f), new(.5f, .1f), new(-.3f, .1f)]),
                        position, .5f, scale, angle, fixedRotation, compound: false)
                    : world.AddCircle(position, .16f, dynamic ? .5f : 0, scale);
                body.LinearDamping = .1f;
                body.AngularDamping = .2f;
                body.IsSensor = true;
                bodies.Add(body);
                return body;
            }
            PhysicsBody a = Make(pair, pair, Vector2.Zero, pair ? -.3f : 0, .4f);
            PhysicsBody b = Make(!circle, true, new(.7f, .5f), .4f, .7f);
            PhysicsJoint joint = scenario.StartsWith("spring", StringComparison.Ordinal)
                ? world.AddSpring(a, b, .5f, 5, .3f, new(.12f, .04f), new(-.25f, .03f))
                : world.AddRope(a, b, .5f, new(.12f, .04f), new(-.25f, .03f));
            string[][] rows = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "JointAnchorReference.csv"))
                .Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.Equal(360, rows.Length);
            float[] errors = new float[10];
            string[] worst = new string[10];
            int tick = 0;
            foreach (string[] row in rows)
            {
                int target = int.Parse(row[1], CultureInfo.InvariantCulture);
                while (tick < target)
                {
                    world.Step();
                    tick++;
                }
                int index = int.Parse(row[2], CultureInfo.InvariantCulture);
                PhysicsBody body = bodies[index];
                Vector2 anchor = index == 0 ? joint.AnchorA : joint.AnchorB;
                Vector2 force = joint.ReactionForce;
                float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y,
                    body.Angle, body.AngularVelocity, anchor.X, anchor.Y, force.X, force.Y];
                for (int component = 0; component < actual.Length; component++)
                {
                    float expected = float.Parse(row[component + 3], CultureInfo.InvariantCulture);
                    Assert.True(float.IsFinite(expected) && float.IsFinite(actual[component]),
                        $"{scenario} tick {tick} body {index} component {component}: nonfinite state");
                    float error = MathF.Abs(expected - actual[component]);
                    if (error > errors[component])
                    {
                        errors[component] = error;
                        worst[component] = $"{scenario} tick {tick} body {index} component {component}: native {expected:R}, managed {actual[component]:R}";
                    }
                }
            }
            output.WriteLine("Max differences: " + string.Join(", ", errors));
            for (int component = 0; component < errors.Length; component++)
            {
                Assert.True(errors[component] <= (component >= 8 ? .0002f : .00002f), worst[component]);
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Both joint types reject nonfinite or excessive local anchors before world mutation.</summary>
        /// <param name="invalid">Invalid component in meters.</param>
        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(10001f)]
        public void InvalidLocalAnchorsAreRejected(float invalid)
        {
            PhysicsWorld world = new();
            PhysicsBody a = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody b = world.AddCircle(Vector2.One, .1f, 1, 1);
            Vector2 bad = new(invalid, 0);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddRope(a, b, 1, bad, Vector2.Zero));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddRope(a, b, 1, Vector2.Zero, bad));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddSpring(a, b, 1, 5, .3f, bad, Vector2.Zero));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddSpring(a, b, 1, 5, .3f, Vector2.Zero, bad));
            Assert.NotNull(world.AddRope(a, b, 1, Vector2.Zero, Vector2.Zero));
            world.Step();
            Assert.True(float.IsFinite(b.Position.Y));
        }
    }
}
