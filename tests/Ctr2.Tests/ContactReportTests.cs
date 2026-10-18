using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Independent native Begin-time world manifolds and PostSolve impulse reports.</summary>
    public sealed class ContactReportTests
    {
        /// <summary>Compares every native tick's events, manifolds and island-ordered PostSolve reports.</summary>
        /// <param name="scenario">Synthetic native contact report scene.</param>
        [Theory]
        [InlineData("terrain")]
        [InlineData("pairs")]
        [InlineData("redundant")]
        [InlineData("sensor")]
        public void MatchesNativeContactReports(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true,
                RecordContactManifolds = true,
                RecordPostSolve = true
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            PhysicsBody Register(PhysicsBody body, float friction, float restitution)
            {
                body.Friction = friction;
                body.Restitution = restitution;
                int tag = label + 1;
                label += Math.Max(1, body.PolygonFixtures.Count);
                bodies.Add(tag, body);
                return body;
            }
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, float restitution = 0)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity), .3f, restitution);
            }
            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, float angle = 0, bool compound = false, float restitution = 0)
            {
                return Register(world.AddPolygon(new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]), new(x, y), 1, gravity, angle,
                    compound: compound), .4f, restitution);
            }
            world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
            label++;
            PhysicsBody? moving = null;
            switch (scenario)
            {
                case "terrain":
                    _ = Circle(-1, .2f, .2f, 1, 1, .5f);
                    _ = Box(.5f, .3f, .3f, .2f, 1, .15f, true);
                    _ = Box(1.6f, .5f, .25f, .25f, 1, 0, false, .3f);
                    break;
                case "pairs":
                    _ = Circle(-1.5f, .7f, .25f, 1, 1);
                    _ = Circle(-1.45f, .1f, .2f, 1, 1);
                    _ = Box(0, .75f, .4f, .2f, 1);
                    _ = Circle(.1f, .2f, .2f, 1, 1);
                    _ = Box(-.05f, -.3f, .3f, .15f, 1, .3f, true);
                    _ = Box(1.8f, .7f, .3f, .3f, 1);
                    _ = Box(1.85f, .05f, .25f, .2f, 1, -.2f);
                    break;
                case "redundant":
                    _ = Box(0, .69f, .002f, .3f, 1);
                    _ = Box(1, .5f, .003f, .25f, 1, .01f);
                    _ = Box(-1, .9f, .25f, .001f, 1);
                    break;
                default:
                    PhysicsBody sensor = Circle(-1, -.2f, .2f, 1, 1);
                    sensor.IsSensor = true;
                    moving = Box(1, .2f, .3f, .2f, 0, 0, true);
                    _ = Circle(1.8f, .75f, .25f, 1, 1);
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContactReportReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (moving != null)
                {
                    if (tick == 20)
                    {
                        world.SetTransform(moving, new(1.6f, .4f), .4f);
                    }
                    if (tick == 50)
                    {
                        world.SetTransform(moving, new(1.2f, -1), -.3f);
                    }
                    if (tick == 51)
                    {
                        world.SetTransform(moving, new(1.75f, .35f), .1f);
                    }
                }
                world.Step();
                string context = $"{scenario}, tick {tick}";
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 1].GetSingle(), actual[i], $"{context}, body {state[0]}, component {i}");
                    }
                }
                IReadOnlyList<PhysicsContactEvent> events = world.DrainContactEvents();
                Assert.Equal(row.GetProperty("Events").GetArrayLength(), events.Count);
                foreach ((JsonElement expected, PhysicsContactEvent actual) in row.GetProperty("Events").EnumerateArray().Zip(events))
                {
                    Assert.Equal(new PhysicsContactEvent((PhysicsContactEventKind)expected[0].GetInt32(), (PhysicsContactEventPhase)expected[1].GetInt32(),
                        expected[2].GetInt32(), expected[3].GetInt32(), expected[4].GetInt64(), expected[5].GetBoolean(), expected[6].GetBoolean(), actual.Manifold), actual);
                    Manifold(expected[7], actual.Manifold, $"{context}, event {expected[4]}");
                }
                IReadOnlyList<PhysicsPostSolve> reports = world.DrainPostSolve();
                Assert.Equal(row.GetProperty("Reports").GetArrayLength(), reports.Count);
                foreach ((JsonElement expected, PhysicsPostSolve actual) in row.GetProperty("Reports").EnumerateArray().Zip(reports))
                {
                    string where = $"{context}, report {expected[2]}";
                    Assert.Equal((expected[0].GetInt32(), expected[1].GetInt32(), expected[2].GetInt64(), expected[3].GetInt32()),
                        (actual.FirstFixture, actual.SecondFixture, actual.CreationOrder, actual.ImpulseCount));
                    float[] impulses = [actual.FirstNormalImpulse, actual.SecondNormalImpulse, actual.FirstTangentImpulse, actual.SecondTangentImpulse];
                    for (int i = 0; i < impulses.Length; i++)
                    {
                        Near(expected[i + 4].GetSingle(), impulses[i], $"{where}, impulse {i}");
                    }
                    Manifold(expected[8], actual.Manifold, where);
                }
            }
            Assert.Equal(120, tick);
        }

        /// <summary>Recording flags are opt-in and draining empties each queue.</summary>
        [Fact]
        public void ReportsAreOptInAndDrainOnce()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            world.AddTerrain([new(-1, 1), new(1, 1), new(1, 2), new(-1, 2)], .3f, 0);
            _ = world.AddCircle(new(0, .79f), .2f, 1, 1);
            IReadOnlyList<PhysicsContactEvent> events = [];
            for (int i = 0; i < 30 && events.Count == 0; i++)
            {
                world.Step();
                events = world.DrainContactEvents();
            }
            PhysicsContactEvent begin = Assert.Single(events);
            Assert.Equal(default, begin.Manifold);
            Assert.Empty(world.DrainPostSolve());
            world.RecordPostSolve = true;
            world.Step();
            PhysicsPostSolve report = Assert.Single(world.DrainPostSolve());
            Assert.Equal(1, report.ImpulseCount);
            Assert.True(report.FirstNormalImpulse > 0);
            Assert.Equal(1, report.Manifold.PointCount);
            Assert.Empty(world.DrainPostSolve());
        }

        private static void Manifold(JsonElement expected, PhysicsWorldManifold actual, string context)
        {
            Assert.Equal(expected[2].GetInt32(), actual.PointCount);
            float[] values = [actual.Normal.X, actual.Normal.Y, actual.FirstPoint.X, actual.FirstPoint.Y, actual.SecondPoint.X, actual.SecondPoint.Y];
            int[] columns = [0, 1, 3, 4, 5, 6];
            for (int i = 0; i < values.Length; i++)
            {
                Near(expected[columns[i]].GetSingle(), values[i], $"{context}, manifold {i}");
            }
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f,
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
