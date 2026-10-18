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
    /// <summary>Independent native polygon-pair state, lifecycle and clipped feature verification.</summary>
    public sealed class PolygonBodyContactTests
    {
        /// <summary>Matches every tick's native state, candidate lifetime and packed manifold features.</summary>
        /// <param name="scenario">Synthetic native reference scene.</param>
        [Theory]
        [InlineData("flat")]
        [InlineData("tilted")]
        [InlineData("reverse")]
        [InlineData("bounce")]
        [InlineData("fixed")]
        [InlineData("compound")]
        [InlineData("offset")]
        [InlineData("stack")]
        [InlineData("topology")]
        [InlineData("narrow")]
        public void MatchesNativePolygonBodyTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            PhysicsBody Box(float x, float y, float hx, float hy, float density, float gravity, float angle = 0,
                bool fixedRotation = false, bool compound = false, bool offset = false, float restitution = 0)
            {
                ConvexPolygon shape = offset ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]);
                PhysicsBody body = world.AddPolygon(shape, new(x, y), density, gravity, angle, fixedRotation, compound);
                body.Friction = .4f;
                body.Restitution = restitution;
                bodies.Add(++label, body);
                label += body.PolygonFixtures.Count - 1;
                return body;
            }
            if (scenario is "stack" or "compound" or "offset")
            {
                world.AddTerrain([new(-3, 2), new(3, 2), new(3, 3), new(-3, 3)], .3f, 0);
                label++;
            }
            if (scenario == "topology")
            {
                _ = Box(0, 0, .3f, .2f, 1, 0);
                _ = Box(.5f, 0, .3f, .2f, 1, 0);
            }
            else if (scenario == "stack")
            {
                _ = Box(0, 1.4f, .3f, .2f, 1, .3f);
                _ = Box(0, .95f, .3f, .2f, 1, .6f);
                _ = Box(0, .5f, .3f, .2f, 1, 1);
            }
            else
            {
                void Bottom()
                {
                    _ = Box(0, 1, .6f, .2f, 2, 0, scenario == "tilted" ? -.1f : 0,
                        fixedRotation: scenario == "fixed", compound: scenario == "compound", restitution: scenario == "bounce" ? .8f : 0);
                }

                void Top()
                {
                    _ = Box(scenario == "tilted" ? .15f : 0, 0, scenario == "narrow" ? .001f : .25f, .2f, .5f, 1,
                        scenario is "tilted" or "offset" or "fixed" ? .3f : 0,
                        compound: scenario is "compound" or "offset", offset: scenario == "offset");
                }

                if (scenario == "reverse")
                {
                    Top();
                    Bottom();
                }
                else
                {
                    Bottom();
                    Top();
                }
            }
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PolygonBodyReference.json")));
            int tick = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "topology" && tick == 31)
                {
                    Assert.True(world.RemoveBody(bodies[1]));
                    _ = bodies.Remove(1);
                    _ = Box(0, 0, .3f, .2f, 1, 0);
                }
                world.Step();
                PhysicsBodyContactState[] pairs = [.. row.GetProperty("BodyContacts").EnumerateArray().Select(c =>
                    new PhysicsBodyContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(pairs.SequenceEqual(world.CaptureBodyContacts()), $"{scenario}, tick {tick}: body candidates differ");
                PhysicsContactFeatureState[] features = [.. row.GetProperty("Features").EnumerateArray().Select(c =>
                    new PhysicsContactFeatureState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt32(), c[3].GetInt32()))];
                Assert.True(features.SequenceEqual(world.CaptureBodyContactFeatures()), $"{scenario}, tick {tick}: clipped feature IDs/order differ");
                PhysicsContactState[] terrain = [.. row.GetProperty("Terrain").EnumerateArray().Select(c =>
                    new PhysicsContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(terrain.SequenceEqual(world.CaptureTerrainContacts()), $"{scenario}, tick {tick}: terrain candidates differ");
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    int tag = state[0].GetInt32();
                    PhysicsBody body = bodies[tag];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        float expected = state[i + 1].GetSingle();
                        Assert.True(float.IsFinite(expected) && float.IsFinite(actual[i]) && MathF.Abs(expected - actual[i]) <= .00002f,
                            $"{scenario}, tick {tick}, body {tag}, component {i}: expected {expected}, actual {actual[i]}");
                    }
                }
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Both polygon skins contribute to physical face overlap beyond their core outlines.</summary>
        /// <param name="centerY">Second box center.</param>
        /// <param name="touching">Expected solid overlap.</param>
        [Theory]
        [InlineData(.4039f, true)]
        [InlineData(.4041f, false)]
        public void CombinedSkinsControlOverlap(float centerY, bool touching)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            ConvexPolygon shape = new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
            _ = world.AddPolygon(shape, Vector2.Zero, 1, 0, compound: false);
            _ = world.AddPolygon(shape, new(0, centerY), 1, 0, compound: false);
            world.Step();
            Assert.Equal(touching, world.CaptureBodyContacts().Any(c => c.Touching));
            Assert.Equal(touching ? 2 : 0, world.CaptureBodyContactFeatures().Count);
        }

        /// <summary>A compound body cannot form contacts between its own triangle fixtures.</summary>
        [Fact]
        public void CompoundFixturesDoNotCollideWithTheirOwnBody()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            _ = world.AddPolygon(new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]), Vector2.Zero, 1, 0);
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Empty(world.CaptureBodyContactFeatures());
        }

        /// <summary>Filtering and removal preserve native same-type orientation and detached feature snapshots.</summary>
        /// <param name="sensorOnFirst">Whether fixture A initially acts as the sensor.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SensorsJointsAndRemovalPreserveCandidateOwnership(bool sensorOnFirst)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            ConvexPolygon shape = new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
            PhysicsBody first = world.AddPolygon(shape, Vector2.Zero, 1, 0, compound: false);
            PhysicsBody second = world.AddPolygon(shape, new(.5f, 0), 1, 0, compound: false);
            PhysicsBody sensor = sensorOnFirst ? first : second;
            sensor.IsSensor = true;
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Empty(world.CaptureBodyContactFeatures());
            sensor.IsSensor = false;
            PhysicsJoint joint = world.AddRope(first, second, 10);
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.True(world.Remove(joint));
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.True(world.RemoveBody(first));
            _ = world.AddPolygon(shape, Vector2.Zero, 1, 0, compound: false);
            world.Step();
            Assert.Equal(new PhysicsBodyContactState(3, 2, 2, true), Assert.Single(world.CaptureBodyContacts()));
            IReadOnlyList<PhysicsContactFeatureState> features = world.CaptureBodyContactFeatures();
            Assert.Equal(2, features.Count);
            Assert.True(world.RemoveBody(second));
            Assert.Empty(world.CaptureBodyContactFeatures());
            Assert.Equal(2, features.Count);
        }
    }
}
