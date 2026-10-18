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
    /// <summary>Independent native checks for moving polygon/circle fixture pairs.</summary>
    public sealed class PolygonCircleTests
    {
        /// <summary>Checks every completed discrete tick, including orientation and compound contact order.</summary>
        [Theory]
        [InlineData("face")]
        [InlineData("vertex-left")]
        [InlineData("vertex-right")]
        [InlineData("inside")]
        [InlineData("rotated")]
        [InlineData("fixed")]
        [InlineData("compound")]
        [InlineData("offset")]
        [InlineData("static-circle")]
        [InlineData("circle-first")]
        public void MatchesNativePolygonCircleTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            void Circle(Vector2 origin, float radius, float density, float gravity)
            {
                PhysicsBody circle = world.AddCircle(origin, radius, density, gravity);
                circle.Friction = .4f;
                bodies.Add(++label, circle);
            }
            if (scenario is "compound" or "offset")
            {
                world.AddTerrain([new(-3, 3), new(3, 3), new(3, 4), new(-3, 4)], .3f, 0);
                label++;
            }
            if (scenario == "static-circle")
                Circle(new(0, 1), .45f, 0, 0);
            if (scenario == "circle-first")
                Circle(Vector2.Zero, .25f, .5f, 1);
            ConvexPolygon shape = scenario == "offset" ?
                new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                new([new(-.6f, -.2f), new(.6f, -.2f), new(.6f, .2f), new(-.6f, .2f)]);
            Vector2 origin = scenario == "offset" ? new(-.2f, .3f) : scenario == "static-circle" ? Vector2.Zero : new(0, 1);
            float angle = scenario is "rotated" or "fixed" or "offset" ? .3f : 0;
            PhysicsBody polygon = world.AddPolygon(shape, origin, 1, scenario == "offset" ? .7f : scenario == "static-circle" ? 1 : 0,
                angle, fixedRotation: scenario == "fixed", compound: scenario is "compound" or "offset");
            polygon.Friction = .4f;
            polygon.Restitution = scenario == "rotated" ? .6f : 0;
            bodies.Add(++label, polygon);
            label += polygon.PolygonFixtures.Count - 1;
            if (scenario is not "static-circle" and not "circle-first")
            {
                float x = scenario == "vertex-left" ? -.7f : scenario is "vertex-right" or "fixed" ? .7f : scenario == "rotated" ? .2f : scenario == "offset" ? .1f : 0;
                float y = scenario is "vertex-left" or "vertex-right" or "fixed" ? .65f : scenario == "inside" ? 1 : scenario == "offset" ? -.2f : 0;
                Circle(new(x, y), .25f, .5f, scenario == "inside" ? 0 : 1);
            }
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PolygonCircleReference.json")));
            int tick = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                world.Step();
                PhysicsBodyContactState[] expectedPairs = [.. row.GetProperty("BodyContacts").EnumerateArray().Select(c =>
                    new PhysicsBodyContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                var actualPairs = world.CaptureBodyContacts();
                Assert.True(expectedPairs.SequenceEqual(actualPairs), $"{scenario}, tick {tick}: body candidates expected {string.Join(", ", expectedPairs)}, actual {string.Join(", ", actualPairs)}");
                PhysicsContactState[] expectedTerrain = [.. row.GetProperty("Terrain").EnumerateArray().Select(c =>
                    new PhysicsContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(expectedTerrain.SequenceEqual(world.CaptureTerrainContacts()), $"{scenario}, tick {tick}: terrain candidates differ");
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

        /// <summary>The polygon skin creates real overlap beyond the unskinned face, with a bounded miss outside it.</summary>
        [Theory]
        [InlineData(.548001f, true)]
        [InlineData(.54799f, false)]
        public void PolygonSkinControlsFaceOverlap(float centerY, bool touching)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            world.AddPolygon(new([new(-.6f, -.2f), new(.6f, -.2f), new(.6f, .2f), new(-.6f, .2f)]), new(0, 1), 1, 0, compound: false);
            world.AddCircle(new(0, centerY), .25f, 1, 0);
            world.Step();
            Assert.Equal(touching, world.CaptureBodyContacts().Any(c => c.Touching));
        }

        /// <summary>Mixed candidates retain native orientation through sensors, joint filtering and proxy reuse.</summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void MixedCandidatesHandleSensorsJointsAndRemoval(bool circleFirst, bool polygonSensor)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody AddCircle() => world.AddCircle(new(0, .65f), .25f, .5f, 0);
            PhysicsBody? circle = circleFirst ? AddCircle() : null;
            PhysicsBody polygon = world.AddPolygon(new([new(-.6f, -.2f), new(.6f, -.2f), new(.6f, .2f), new(-.6f, .2f)]), new(0, 1), 1, 0, compound: false);
            circle ??= AddCircle();
            PhysicsBody sensor = polygonSensor ? polygon : circle;
            sensor.IsSensor = true;
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Equal(new Vector2(0, .65f), circle.Position);
            sensor.IsSensor = false;
            PhysicsJoint joint = world.AddRope(circle, polygon, 10);
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.True(world.Remove(joint));
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            Assert.True(world.RemoveBody(circle));
            PhysicsBody replacement = AddCircle();
            world.Step();
            var snapshot = world.CaptureBodyContacts();
            Assert.Equal(new PhysicsBodyContactState(circleFirst ? 2 : 1, 3, 2, true), Assert.Single(snapshot));
            Assert.True(world.RemoveBody(polygon));
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Single(snapshot);
            Assert.True(world.RemoveBody(replacement));
        }
    }
}
