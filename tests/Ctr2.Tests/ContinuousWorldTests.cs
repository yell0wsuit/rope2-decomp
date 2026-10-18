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
    /// <summary>Independent native world time-of-impact comparisons, including TOI-phase events and reports.</summary>
    public sealed class ContinuousWorldTests
    {
        /// <summary>Compares every native tick of fast, rotating and kinematic sweeps with continuous physics enabled.</summary>
        /// <param name="scenario">Synthetic native continuous scene.</param>
        [Theory]
        [InlineData("tunnel")]
        [InlineData("polygon")]
        [InlineData("kinematic")]
        [InlineData("static-body")]
        [InlineData("burst")]
        public void MatchesNativeContinuousWorld(string scenario)
        {
            PhysicsWorld world = new()
            {
                RecordContactEvents = true,
                RecordPostSolve = true
            };
            Assert.True(world.ContinuousPhysics);
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
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, PhysicsBodyType type, float restitution = 0)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity, 0, type), .3f, restitution);
            }
            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, float angle, bool compound, PhysicsBodyType type, float restitution = 0)
            {
                ConvexPolygon shape = compound ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]);
                return Register(world.AddPolygon(shape, new(x, y), 1, gravity, angle, false, compound, type), .4f, restitution);
            }
            void Terrain(Vector2[] points, bool sensor = false)
            {
                world.AddTerrain(points, .3f, 0, sensor);
                label++;
            }
            const PhysicsBodyType Dynamic = PhysicsBodyType.Dynamic, Kinematic = PhysicsBodyType.Kinematic, Static = PhysicsBodyType.Static;
            switch (scenario)
            {
                case "tunnel":
                    Terrain([new(-3, 1), new(3, 1), new(3, 1.02f), new(-3, 1.02f)]);
                    Terrain([new(-3, .4f), new(3, .4f), new(3, .42f), new(-3, .42f)], true);
                    Circle(-1, -.5f, .05f, 1, 1, Dynamic, .6f).SetLinearVelocity(new(1, 60));
                    Circle(1, -.8f, .1f, 1, 4, Dynamic).SetLinearVelocity(new(-5, 45));
                    Circle(0, -3, .08f, 1, 1, Dynamic).SetLinearVelocity(new(0, 70));
                    break;
                case "polygon":
                    Terrain([new(-3, 1), new(3, 1), new(3, 1.03f), new(-3, 1.03f)]);
                    Terrain([new(1.5f, -1), new(1.53f, -1), new(1.53f, 1), new(1.5f, 1)]);
                    PhysicsBody a = Box(-1, -.3f, .2f, .05f, 1, .3f, false, Dynamic);
                    a.SetLinearVelocity(new(2, 50));
                    a.SetAngularVelocity(20);
                    PhysicsBody b = Box(0, -.2f, 0, 0, 1, 0, true, Dynamic, .4f);
                    b.SetLinearVelocity(new(60, 10));
                    b.SetAngularVelocity(-8);
                    break;
                case "kinematic":
                    Box(-2, 0, .05f, .6f, 0, 0, false, Kinematic).SetLinearVelocity(new(70, 0));
                    _ = Circle(0, .1f, .1f, 1, 0, Dynamic);
                    _ = Box(1, -.2f, .1f, .1f, 0, .3f, false, Dynamic);
                    Circle(2, -2, .15f, 0, 0, Kinematic).SetLinearVelocity(new(-40, 60));
                    break;
                case "static-body":
                    _ = Circle(0, 0, .3f, 0, 0, Static);
                    _ = Circle(.8f, .2f, .02f, 0, 0, Static);
                    Circle(-2, 0, .05f, 1, 0, Dynamic).SetLinearVelocity(new(90, 0));
                    Box(.8f, -2, .04f, .04f, 0, .2f, false, Dynamic, .8f).SetLinearVelocity(new(0, 80));
                    break;
                default:
                    Terrain([new(-3, 1), new(3, 1), new(3, 1.02f), new(-3, 1.02f)]);
                    Terrain([new(-3, -1.02f), new(3, -1.02f), new(3, -1), new(-3, -1)]);
                    for (int i = 0; i < 6; i++)
                    {
                        Circle(-2.5f + (i * .9f), 0, .04f + (.01f * i), 1, 0, Dynamic, i % 2 == 1 ? .5f : 0)
                            .SetLinearVelocity(new((i * 3f) - 7, i % 2 == 1 ? 70f : -70f));
                    }
                    Box(0, .5f, .05f, .05f, 0, 0, false, Dynamic).SetLinearVelocity(new(0, 120));
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContinuousWorldReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
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
                IReadOnlyList<PhysicsProxyState> proxies = world.CaptureProxies();
                Assert.Equal(row.GetProperty("Proxies").GetArrayLength(), proxies.Count);
                foreach ((JsonElement expected, PhysicsProxyState actual) in row.GetProperty("Proxies").EnumerateArray().Zip(proxies))
                {
                    Assert.Equal((expected[0].GetInt32(), expected[1].GetInt32()), (actual.Fixture, actual.ProxyId));
                    float[] bounds = [actual.Bounds.Lower.X, actual.Bounds.Lower.Y, actual.Bounds.Upper.X, actual.Bounds.Upper.Y];
                    for (int i = 0; i < bounds.Length; i++)
                    {
                        Near(expected[i + 2].GetSingle(), bounds[i], $"{context}, proxy {actual.Fixture}, bound {i}");
                    }
                }
                PhysicsContactEvent[] events = [.. row.GetProperty("Events").EnumerateArray().Select(e => new PhysicsContactEvent(
                    (PhysicsContactEventKind)e[0].GetInt32(), (PhysicsContactEventPhase)e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32(), e[4].GetInt64(), e[5].GetBoolean(), e[6].GetBoolean()))];
                Assert.True(events.SequenceEqual(world.DrainContactEvents()), context + ": events differ");
                IReadOnlyList<PhysicsPostSolve> reports = world.DrainPostSolve();
                Assert.Equal(row.GetProperty("Reports").GetArrayLength(), reports.Count);
                foreach ((JsonElement expected, PhysicsPostSolve actual) in row.GetProperty("Reports").EnumerateArray().Zip(reports))
                {
                    Assert.Equal((expected[0].GetInt32(), expected[1].GetInt32(), expected[2].GetInt64(), expected[3].GetInt32()),
                        (actual.FirstFixture, actual.SecondFixture, actual.CreationOrder, actual.ImpulseCount));
                    float[] impulses = [actual.FirstNormalImpulse, actual.SecondNormalImpulse, actual.FirstTangentImpulse, actual.SecondTangentImpulse];
                    for (int i = 0; i < impulses.Length; i++)
                    {
                        Near(expected[i + 4].GetSingle(), impulses[i], $"{context}, report {expected[2]}, impulse {i}");
                    }
                }
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(c =>
                    new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(100, tick);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
