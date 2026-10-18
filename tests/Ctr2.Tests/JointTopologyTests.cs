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
    /// <summary>Checks singular limits, interacting constraints and warm-started topology mutation.</summary>
    public sealed class JointTopologyTests
    {
        /// <summary>Matches every body state against independently compiled native Box2D.</summary>
        /// <param name="scenario">Synthetic scenario name.</param>
        [Theory]
        [InlineData("rope_singular")]
        [InlineData("spring_singular")]
        [InlineData("rope_zero")]
        [InlineData("rope_short")]
        [InlineData("spring_short")]
        [InlineData("rope_multi")]
        [InlineData("spring_multi")]
        [InlineData("rope_islands")]
        [InlineData("spring_islands")]
        [InlineData("rope_recreate")]
        [InlineData("spring_recreate")]
        public void MatchesNativeJointTopology(string scenario)
        {
            bool spring = scenario.StartsWith("spring", StringComparison.Ordinal);
            bool singular = scenario.EndsWith("singular", StringComparison.Ordinal);
            bool zero = scenario.EndsWith("zero", StringComparison.Ordinal);
            bool tiny = scenario.EndsWith("short", StringComparison.Ordinal);
            bool multi = scenario.EndsWith("multi", StringComparison.Ordinal);
            bool islands = scenario.EndsWith("islands", StringComparison.Ordinal);
            bool recreate = scenario.EndsWith("recreate", StringComparison.Ordinal);
            bool polygon = multi || islands || recreate;
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            List<PhysicsBody> bodies = [];
            PhysicsBody Make(Vector2 position, bool dynamic, bool poly)
            {
                PhysicsBody body = poly
                    ? world.AddPolygon(new([new(-.3f, -.1f), new(.5f, -.1f), new(.5f, .1f), new(-.3f, .1f)]), position, .5f, .7f, .4f, compound: false)
                    : world.AddCircle(position, .16f, dynamic ? .5f : 0, .7f);
                body.LinearDamping = .1f;
                body.AngularDamping = .2f;
                body.IsSensor = true;
                bodies.Add(body);
                return body;
            }
            PhysicsJoint Connect(PhysicsBody first, PhysicsBody second, float length, Vector2 a, Vector2 b)
            {
                return spring
                ? world.AddSpring(first, second, length, 5, .3f, a, b)
                : world.AddRope(first, second, length, a, b);
            }

            PhysicsBody a = Make(Vector2.Zero, false, false);
            PhysicsBody b = Make(singular ? Vector2.Zero : tiny ? new(0, .0005f) : new(.7f, .5f), true, polygon);
            Vector2 localA = polygon ? new(.12f, .04f) : Vector2.Zero;
            Vector2 localB = polygon ? new(-.25f, .03f) : Vector2.Zero;
            float limit = zero ? 0 : tiny ? .0002f : multi ? .6f : .5f;
            PhysicsJoint? joint = Connect(a, b, limit, localA, localB);
            PhysicsJoint? extra = null;
            if (multi)
            {
                PhysicsBody other = Make(new(1.4f, 0), false, false);
                extra = Connect(other, b, .6f, new(-.12f, .04f), new(.35f, .03f));
            }
            if (islands)
            {
                PhysicsBody other = Make(new(3, 0), false, false);
                PhysicsBody next = Make(new(3.7f, .5f), true, true);
                extra = Connect(other, next, limit, localA, localB);
            }
            string[][] rows = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "JointTopologyReference.csv"))
                .Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.Equal(240 * bodies.Count, rows.Length);
            int tick = 0;
            int rowIndex = 0;
            foreach (string[] row in rows)
            {
                Assert.Equal(14, row.Length);
                int target = int.Parse(row[1], CultureInfo.InvariantCulture);
                Assert.Equal((rowIndex / bodies.Count) + 1, target);
                while (tick < target)
                {
                    if (recreate && tick == 60)
                    {
                        Assert.True(world.Remove(joint!));
                        Assert.False(world.Remove(joint!));
                        joint = null;
                    }
                    if (recreate && tick == 90)
                    {
                        localB = new(.25f, -.03f);
                        joint = Connect(a, b, .8f, localA, localB);
                    }
                    world.Step();
                    tick++;
                }
                int index = int.Parse(row[2], CultureInfo.InvariantCulture);
                Assert.Equal(rowIndex % bodies.Count, index);
                rowIndex++;
                PhysicsBody body = bodies[index];
                PhysicsJoint? watched = index >= 2 ? extra : joint;
                bool first = index is 0 or 2;
                // Native GetWorldPoint: scalar rotation plus the cached origin.
                Vector2 local = first ? localA : localB;
                float cosine = MathF.Cos(body.Angle), sine = MathF.Sin(body.Angle);
                Vector2 anchor = watched == null
                    ? body.Origin + new Vector2((cosine * local.X) - (sine * local.Y), (sine * local.X) + (cosine * local.Y))
                    : first ? watched.AnchorA : watched.AnchorB;
                Vector2 force = watched?.ReactionForce ?? Vector2.Zero;
                float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y,
                    body.Angle, body.AngularVelocity, anchor.X, anchor.Y, force.X, force.Y];
                for (int component = 0; component < actual.Length; component++)
                {
                    float expected = float.Parse(row[component + 3], CultureInfo.InvariantCulture);
                    Assert.True(float.IsFinite(expected) && float.IsFinite(actual[component]),
                        $"{scenario} tick {tick} body {index} component {component}: nonfinite state");
                    float tolerance = component >= 8 ? .0002f : .00002f;
                    Assert.True(MathF.Abs(expected - actual[component]) <= tolerance,
                        $"{scenario} tick {tick} body {index} component {component}: native {expected:R}, managed {actual[component]:R}");
                }
                Assert.Equal(watched == null ? 0 : 1, int.Parse(row[13], CultureInfo.InvariantCulture));
            }
            Assert.Equal(240, tick);
        }
    }
}
