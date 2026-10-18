using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Independent native discrete-circle and mixed contact/joint island verification.</summary>
    public sealed class CircleContactTests
    {
        /// <summary>Compares every completed tick, including contact creation and refilter timing.</summary>
        [Theory]
        [InlineData("static")]
        [InlineData("dynamic")]
        [InlineData("glancing")]
        [InlineData("bounce")]
        [InlineData("coincident")]
        [InlineData("tangent")]
        [InlineData("stack")]
        [InlineData("joint")]
        [InlineData("refilter")]
        [InlineData("material")]
        public void MatchesNativeCircleAndIslandTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            PhysicsBody Add(float x, float y, float radius, float density, float gravity, float friction = .3f, float restitution = 0)
            {
                PhysicsBody body = world.AddCircle(new(x, y), radius, density, gravity);
                body.Friction = friction;
                body.Restitution = restitution;
                bodies.Add(++label, body);
                return body;
            }
            PhysicsJoint? joint = null;
            if (scenario is "static" or "bounce" or "glancing" or "material")
            {
                _ = Add(0, 1, .6f, 0, 0, .6f, scenario == "bounce" ? .8f : 0);
                _ = Add(scenario is "glancing" or "material" ? .3f : 0, -.4f, .25f, .5f, 1, .6f);
            }
            if (scenario == "dynamic")
            {
                _ = Add(0, .6f, .3f, 2, 0);
                _ = Add(.1f, -.3f, .25f, .5f, 1);
            }
            if (scenario is "coincident" or "tangent" or "refilter")
            {
                _ = Add(0, 0, .3f, 1, 0);
                _ = Add(scenario == "coincident" ? 0 : scenario == "tangent" ? .6f : .4f, 0, .3f, 2, 0);
            }
            if (scenario is "stack" or "joint")
            {
                world.AddTerrain([new(-2, 1.6f), new(2, 1.6f), new(2, 2), new(-2, 2)], .3f, 0);
                label++;
                if (scenario == "stack")
                {
                    _ = Add(0, 1, .25f, 1, .3f);
                    _ = Add(0, .5f, .25f, 1, .6f);
                    _ = Add(0, 0, .25f, 1, 1);
                }
                else
                {
                    PhysicsBody a = Add(-.2f, .8f, .3f, 1, .3f);
                    PhysicsBody b = Add(.2f, .8f, .3f, 1, .3f);
                    _ = Add(0, 0, .25f, .5f, 1);
                    joint = world.AddRope(a, b, .5f);
                }
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CircleContactReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "material" && tick == 20)
                {
                    bodies[1].Friction = bodies[2].Friction = 0;
                    bodies[1].Restitution = bodies[2].Restitution = 1;
                }
                if (scenario == "refilter")
                {
                    if (tick == 21)
                    {
                        joint = world.AddRope(bodies[1], bodies[2], 10);
                    }

                    if (tick == 41)
                    {
                        Assert.True(world.Remove(joint!));
                    }

                    if (tick == 61)
                    {
                        Assert.True(world.RemoveBody(bodies[2]));
                        _ = bodies.Remove(2);
                        _ = Add(.4f, 0, .3f, 2, 0);
                    }
                }
                world.Step();
                PhysicsBodyContactState[] expectedCircles = [.. row.GetProperty("Circles").EnumerateArray().Select(c =>
                    new PhysicsBodyContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(expectedCircles.SequenceEqual(world.CaptureBodyContacts()), $"{scenario}, tick {tick}: circle candidate mismatch");
                PhysicsContactState[] expectedTerrain = [.. row.GetProperty("Terrain").EnumerateArray().Select(c =>
                    new PhysicsContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(expectedTerrain.SequenceEqual(world.CaptureTerrainContacts()), $"{scenario}, tick {tick}: terrain candidate mismatch");
                JsonElement expectedBodies = row.GetProperty("Bodies");
                Assert.Equal(bodies.Count, expectedBodies.GetArrayLength());
                foreach (JsonElement state in expectedBodies.EnumerateArray())
                {
                    int bodyLabel = state[0].GetInt32();
                    PhysicsBody body = bodies[bodyLabel];
                    float[] values = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < values.Length; i++)
                    {
                        float expected = state[i + 1].GetSingle();
                        Assert.True(float.IsFinite(expected) && float.IsFinite(values[i]) && MathF.Abs(expected - values[i]) <= .00002f,
                            $"{scenario}, tick {tick}, body {bodyLabel}, component {i}: expected {expected}, actual {values[i]}");
                    }
                }
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Static pairs never enter the contact graph, while sensors can resume solid solving.</summary>
        [Fact]
        public void StaticAndSensorPairsRespectSolidContactOwnership()
        {
            PhysicsWorld statics = new()
            {
                ContinuousPhysics = false
            };
            _ = statics.AddCircle(Vector2.Zero, .6f, 0, 0);
            _ = statics.AddCircle(new(.7f, 0), .25f, 0, 0);
            statics.Step();
            Assert.Empty(statics.CaptureBodyContacts());

            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            _ = world.AddCircle(Vector2.Zero, .6f, 0, 0);
            PhysicsBody body = world.AddCircle(new(.7f, 0), .25f, 1, 0);
            body.IsSensor = true;
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Equal(new Vector2(.7f, 0), body.Position);
            body.IsSensor = false;
            world.Step();
            IReadOnlyList<PhysicsBodyContactState> snapshot = world.CaptureBodyContacts();
            Assert.True(Assert.Single(snapshot).Touching);
            Assert.True(body.Position.X > .7f);
            Assert.True(world.RemoveBody(body));
            Assert.Empty(world.CaptureBodyContacts());
            _ = Assert.Single(snapshot);
        }

        /// <summary>A shared static circle must not force independent moving bodies to share convergence.</summary>
        [Fact]
        public void SharedStaticCircleDoesNotMergeIndependentIslands()
        {
            static (PhysicsWorld World, PhysicsBody Measured) Create(bool difficult)
            {
                PhysicsWorld world = new()
                {
                    ContinuousPhysics = false
                };
                _ = world.AddCircle(new(0, 1), .6f, 0, 0);
                PhysicsBody measured = world.AddCircle(new(.4f, .3f), .25f, 1, .7f);
                if (difficult)
                {
                    _ = world.AddCircle(new(0, 1), .25f, 1, 0);
                }

                return (world, measured);
            }
            (PhysicsWorld World, PhysicsBody Measured) = Create(false);
            (PhysicsWorld World, PhysicsBody Measured) together = Create(true);
            for (int tick = 0; tick < 180; tick++)
            {
                World.Step();
                together.World.Step();
                Assert.Equal(Measured.Position, together.Measured.Position);
                Assert.Equal(Measured.Velocity, together.Measured.Velocity);
                Assert.Equal(Measured.Angle, together.Measured.Angle);
                Assert.Equal(Measured.AngularVelocity, together.Measured.AngularVelocity);
            }
        }

        /// <summary>All collision-blocking joints between a pair must be removed before discovery accepts it.</summary>
        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        public void MultipleJointsKeepCollisionSuppression(int removals, bool touching)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody a = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            PhysicsBody b = world.AddCircle(new(.4f, 0), .3f, 1, 0);
            PhysicsJoint first = world.AddRope(a, b, 10);
            PhysicsJoint second = world.AddRope(b, a, 10);
            Assert.True(world.Remove(first));
            Assert.False(world.Remove(first));
            if (removals == 2)
            {
                Assert.True(world.Remove(second));
            }

            world.Step();
            Assert.Equal(touching, world.CaptureBodyContacts().Any(c => c.Touching));
            if (!touching)
            {
                Assert.Equal(new Vector2(.4f, 0), b.Position);
            }
        }
    }
}
