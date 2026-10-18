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
    /// <summary>Independent native island sleeping and wake-up comparisons.</summary>
    public sealed class SleepTests
    {
        /// <summary>Compares every native tick, including awake flags and sleep timers.</summary>
        /// <param name="scenario">Synthetic native sleep scene.</param>
        [Theory]
        [InlineData("settle")]
        [InlineData("intro")]
        [InlineData("wakes")]
        [InlineData("toi")]
        public void MatchesNativeSleepTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                AllowSleeping = true,
                RecordContactEvents = true
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            PhysicsBody Register(PhysicsBody body)
            {
                body.Friction = .6f;
                bodies.Add(++label, body);
                return body;
            }
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, PhysicsBodyType type)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity, 0, type));
            }
            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, float angle, PhysicsBodyType type)
            {
                return Register(world.AddPolygon(new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]), new(x, y), 1, gravity, angle, false, false, type));
            }
            void Ground()
            {
                world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .6f, 0);
                label++;
            }
            const PhysicsBodyType Dynamic = PhysicsBodyType.Dynamic, Kinematic = PhysicsBodyType.Kinematic, Static = PhysicsBodyType.Static;
            PhysicsBody a, b, c = null!, k = null!;
            PhysicsJoint? rope = null;
            PhysicsRevoluteJoint? hinge = null;
            switch (scenario)
            {
                case "settle":
                    Ground();
                    a = Box(0, .75f, .3f, .2f, 1, 0, Dynamic);
                    b = Box(.05f, .3f, .2f, .2f, 1, 0, Dynamic);
                    c = Circle(-1, .6f, .2f, 1, 1, Dynamic);
                    break;
                case "intro":
                    k = Box(0, -1, .3f, .1f, 0, 0, Kinematic);
                    a = Box(0, -.6f, .15f, .15f, 1, 0, Dynamic);
                    b = Circle(0, 0, .1f, 1, 1, Dynamic);
                    foreach (PhysicsBody body in new[] { a, b })
                    {
                        body.LinearDamping = 10;
                        body.AngularDamping = 10;
                    }
                    _ = world.AddRevolute(k, a, new Vector2(0, -.8f));
                    _ = world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, .6f));
                    break;
                case "wakes":
                    Ground();
                    a = Box(-1, .75f, .3f, .2f, 1, 0, Dynamic);
                    b = Circle(0, .8f, .2f, 1, 1, Dynamic);
                    c = Box(1, .75f, .2f, .2f, 1, 0, Dynamic);
                    k = Circle(2, -.5f, .1f, 0, 0, Static);
                    Vector2 anchor = new(1.9f, .75f);
                    hinge = world.AddRevolute(k, c, new PhysicsRevoluteDefinition(k.GetLocalPoint(anchor), c.GetLocalPoint(anchor), c.Angle - k.Angle,
                        EnableMotor: true, MaxMotorTorque: .0001f));
                    rope = world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, 1.2f));
                    break;
                default:
                    Ground();
                    a = Box(0, .85f, .2f, .15f, 1, 0, Dynamic);
                    b = Circle(.8f, .8f, .2f, 1, 1, Dynamic);
                    k = Box(-2.5f, .6f, .05f, .3f, 0, 0, Kinematic);
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SleepReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                switch (scenario)
                {
                    case "settle":
                        if (tick == 120)
                        {
                            _ = Circle(.05f, -1, .15f, 1, 1, Dynamic);
                        }
                        break;
                    case "intro":
                        if (tick <= 40)
                        {
                            world.SetTransform(k, new(.001f * tick, -1), 0);
                        }
                        if (tick is > 170 and <= 220)
                        {
                            world.SetTransform(k, new(.01f * tick, -1 + (.004f * tick)), .002f * tick);
                        }
                        if (tick == 225)
                        {
                            k.SetLinearVelocity(new(.5f, 0));
                        }
                        if (tick == 235)
                        {
                            k.SetLinearVelocity(Vector2.Zero);
                        }
                        break;
                    case "wakes":
                        if (tick == 80)
                        {
                            a.ApplyLinearImpulse(new(.02f, 0), a.Position);
                        }
                        if (tick == 120)
                        {
                            hinge!.MotorSpeed = .5f;
                        }
                        if (tick == 150)
                        {
                            Assert.True(world.Remove(rope!));
                        }
                        if (tick == 175)
                        {
                            world.SetFixtureSensor(3, false);
                        }
                        if (tick == 180)
                        {
                            world.SetFixtureSensor(3, true);
                        }
                        if (tick == 181)
                        {
                            world.SetFixtureSensor(3, false);
                        }
                        if (tick == 190)
                        {
                            world.SetBodyType(c, Kinematic);
                            world.SetBodyType(c, Dynamic);
                        }
                        if (tick == 200)
                        {
                            a.SleepingAllowed = false;
                        }
                        if (tick == 215)
                        {
                            a.SetAwake(false);
                        }
                        if (tick == 230)
                        {
                            world.AllowSleeping = false;
                        }
                        break;
                    default:
                        if (tick == 100)
                        {
                            k.SetLinearVelocity(new(90, 0));
                        }
                        break;
                }
                world.Step();
                string context = $"{scenario}, tick {tick}";
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 1].GetSingle(), actual[i], $"{context}, body {state[0]}, component {i}");
                    }
                    Assert.True(state[7].GetBoolean() == body.IsAwake, $"{context}, body {state[0]}: awake differs");
                    Near(state[8].GetSingle(), body.SleepTime, $"{context}, body {state[0]}, sleep time");
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
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(e =>
                    new PhysicsFixtureContactState(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt64(), e[3].GetBoolean(), e[4].GetBoolean(), e[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(240, tick);
        }

        /// <summary>Manual sleep clears motion, and waking resets the timer only for a sleeping body.</summary>
        [Fact]
        public void ManualSleepClearsMotionAndWakeResetsTimer()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddCircle(Vector2.Zero, .2f, 1, 0);
            body.SetLinearVelocity(Vector2.One);
            body.ApplyForceToCenter(Vector2.One);
            body.SetAwake(false);
            Assert.False(body.IsAwake);
            Assert.Equal(Vector2.Zero, body.Velocity);
            world.Step();
            Assert.Equal(Vector2.Zero, body.Position);
            body.SetLinearVelocity(Vector2.Zero);
            Assert.False(body.IsAwake);
            body.SetLinearVelocity(Vector2.UnitX);
            Assert.True(body.IsAwake);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
