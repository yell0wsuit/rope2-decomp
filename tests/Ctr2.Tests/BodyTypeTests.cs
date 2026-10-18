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
    /// <summary>Independent native body type, kinematic motion and mass reset comparisons.</summary>
    public sealed class BodyTypeTests
    {
        /// <summary>Compares every native tick, including mass properties before each step.</summary>
        /// <param name="scenario">Synthetic native body-type scene.</param>
        [Theory]
        [InlineData("fruit")]
        [InlineData("polygon")]
        [InlineData("kinematic")]
        [InlineData("mass")]
        [InlineData("fireball")]
        public void MatchesNativeBodyTypeTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            Dictionary<int, PhysicsBody> bodies = [];
            int label = 0;
            PhysicsBody Register(PhysicsBody body, float friction)
            {
                body.Friction = friction;
                int tag = label + 1;
                label += Math.Max(1, body.PolygonFixtures.Count);
                bodies.Add(tag, body);
                return body;
            }
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, PhysicsBodyType type)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity, 0, type), .3f);
            }

            PhysicsBody Box(float x, float y, float gravity, PhysicsBodyType type, float angle = 0, bool fixedRotation = false, bool compound = false,
                bool offset = false, float density = 1)
            {
                ConvexPolygon shape = offset ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
                return Register(world.AddPolygon(shape, new(x, y), density, gravity, angle, fixedRotation, compound, type), .4f);
            }
            void Ground()
            {
                world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
                label++;
            }
            PhysicsJoint Rope(PhysicsBody a, PhysicsBody b, float length)
            {
                return world.AddRope(a, b, length, new(-1, 0), new(1, 0));
            }

            const PhysicsBodyType Static = PhysicsBodyType.Static, Kinematic = PhysicsBodyType.Kinematic, Dynamic = PhysicsBodyType.Dynamic;
            PhysicsBody a, b, c, d = null!;
            List<PhysicsJoint> joints = [];
            switch (scenario)
            {
                case "fruit":
                    Ground();
                    a = Circle(0, .3f, .2f, .5f, 1, Static);
                    a.LinearDamping = a.AngularDamping = .3f;
                    world.SetFixtureSensor(2, true);
                    b = Circle(.1f, -.3f, .15f, 1, 1, Dynamic);
                    b.LinearDamping = .3f;
                    c = Circle(1, .5f, .2f, 1, 1, Dynamic);
                    break;
                case "polygon":
                    a = Circle(0, 1, .3f, 0, 0, Static);
                    b = Box(-.1f, .3f, 1, Dynamic, .2f, false, true, true);
                    c = Box(1.5f, 0, 0, Dynamic, 0, false, true, true);
                    c.SetAngularVelocity(3);
                    c.SetLinearVelocity(new(.2f, 0));
                    break;
                case "kinematic":
                    Ground();
                    a = Circle(-1.5f, .85f, .25f, .4f, 1, Kinematic);
                    a.SetLinearVelocity(new(1.2f, 0));
                    b = Circle(-.5f, .7f, .2f, 1, 1, Dynamic);
                    c = Box(1, -1.2f, 1, Kinematic, 0, false, true, true, .4f);
                    c.SetAngularVelocity(-1.5f);
                    c.SetLinearVelocity(new(0, .3f));
                    d = Circle(1.2f, -1.5f, .2f, 1, 1, Dynamic);
                    d.LinearDamping = d.AngularDamping = .3f;
                    _ = Rope(c, d, .8f);
                    _ = Rope(b, c, 2.5f);
                    _ = Circle(1.6f, .4f, .2f, 0, 0, Static);
                    break;
                case "mass":
                    a = Circle(0, 0, .2f, 1, 0, Dynamic);
                    a.SetAngularVelocity(2);
                    b = Circle(1, 0, .2f, 0, 1, Dynamic);
                    c = Box(-1, 0, .5f, Dynamic, .3f, true);
                    d = Circle(0, -1, .1f, 0, 0, Static);
                    _ = Rope(d, a, 1.1f);
                    break;
                default:
                    Ground();
                    a = Circle(-1, -1, .1f, 0, 0, Static);
                    b = Circle(1, -1, .1f, 0, 0, Static);
                    c = Circle(0, .7f, .3f, 1, 1, Dynamic);
                    joints.Add(Rope(a, c, 2));
                    joints.Add(Rope(b, c, 2));
                    d = Box(.9f, .7f, 1, Dynamic);
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BodyTypeReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                switch (scenario)
                {
                    case "fruit":
                        if (tick == 20)
                        {
                            world.SetFixtureSensor(2, false);
                            world.SetBodyType(a, Dynamic);
                        }
                        if (tick == 25)
                        {
                            world.SetBodyType(a, Dynamic);
                        }
                        if (tick is >= 60 and <= 80)
                        {
                            world.SetBodyType(a, Static);
                        }
                        if (tick == 100)
                        {
                            world.SetBodyType(c, Static);
                        }
                        if (tick == 120)
                        {
                            world.SetBodyType(a, Dynamic);
                            a.SetAngularVelocity(4);
                        }
                        if (tick == 140)
                        {
                            world.SetBodyType(c, Dynamic);
                        }
                        break;
                    case "polygon":
                        if (tick == 30)
                        {
                            world.SetBodyType(b, Static);
                        }
                        if (tick == 60)
                        {
                            world.SetBodyType(b, Dynamic);
                        }
                        // Native fixture lists start with the newest fixture: labels 7, 6, 5.
                        if (tick == 20)
                        {
                            world.SetFixtureDensity(7, 4);
                            c.ResetMassData();
                        }
                        if (tick == 40)
                        {
                            world.SetFixtureDensity(6, 0);
                            c.ResetMassData();
                        }
                        if (tick == 70)
                        {
                            for (int fixtureLabel = 5; fixtureLabel <= 7; fixtureLabel++)
                            {
                                world.SetFixtureDensity(fixtureLabel, 0);
                            }
                            c.ResetMassData();
                        }
                        if (tick == 90)
                        {
                            world.SetFixtureDensity(7, 2);
                            c.ResetMassData();
                        }
                        if (tick == 110)
                        {
                            world.SetBodyType(c, Kinematic);
                        }
                        if (tick == 130)
                        {
                            world.SetBodyType(c, Dynamic);
                            c.SetAngularVelocity(-2);
                        }
                        break;
                    case "kinematic":
                        if (tick == 40)
                        {
                            a.ApplyForceToCenter(new(50, 0));
                            a.ApplyLinearImpulse(new(5, 0), a.Position);
                            a.ApplyAngularImpulse(1);
                            a.ApplyTorque(1);
                        }
                        if (tick == 60)
                        {
                            a.SetLinearVelocity(new(-.8f, 0));
                        }
                        if (tick == 80)
                        {
                            a.SetAngularVelocity(2);
                            c.SetLinearVelocity(Vector2.Zero);
                        }
                        if (tick == 100)
                        {
                            world.SetBodyType(b, Kinematic);
                        }
                        if (tick == 120)
                        {
                            world.SetBodyType(b, Dynamic);
                            world.SetBodyType(a, Dynamic);
                        }
                        if (tick == 150)
                        {
                            _ = Rope(a, d, 3);
                        }
                        break;
                    case "mass":
                        if (tick == 20)
                        {
                            a.SetMassData(new(2, new(.1f, 0), .2f));
                        }
                        if (tick == 40)
                        {
                            b.SetMassData(new(-1, new(0, .05f), .01f));
                        }
                        if (tick == 60)
                        {
                            c.SetMassData(new(3, new(.05f, .02f), .5f));
                        }
                        if (tick == 70)
                        {
                            d.SetMassData(new(5, Vector2.Zero, 1));
                        }
                        if (tick == 80)
                        {
                            a.ResetMassData();
                            world.SetFixtureDensity(1, world.GetFixtureDensity(1) / 10);
                            a.ResetMassData();
                        }
                        if (tick == 100)
                        {
                            a.SetMassData(new(1.5f, Vector2.Zero, 0));
                            a.SetAngularVelocity(1);
                        }
                        if (tick == 120)
                        {
                            world.SetBodyType(a, Kinematic);
                            world.SetBodyType(a, Dynamic);
                        }
                        if (tick == 140)
                        {
                            world.SetBodyType(b, Static);
                        }
                        break;
                    default:
                        if (tick == 50)
                        {
                            world.SetBodyType(c, Static);
                        }
                        if (tick == 51)
                        {
                            // Native destroys from its newest-first joint list.
                            for (int i = joints.Count - 1; i >= 0; i--)
                            {
                                Assert.True(world.Remove(joints[i]));
                            }
                        }
                        if (tick == 90)
                        {
                            world.SetBodyType(c, Dynamic);
                        }
                        if (tick == 110)
                        {
                            world.SetBodyType(d, Static);
                        }
                        if (tick == 130)
                        {
                            world.SetBodyType(d, Kinematic);
                        }
                        if (tick == 131)
                        {
                            d.SetAngularVelocity(1);
                        }
                        break;
                }
                string context = $"{scenario}, tick {tick}";
                Assert.True(Contacts(row.GetProperty("Pre")).SequenceEqual(world.CaptureFixtureContacts()), context + ": pre-step candidates differ");
                foreach (JsonElement state in row.GetProperty("Mass").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    string where = $"{context}, mass of body {state[0]}";
                    Assert.Equal((PhysicsBodyType)state[1].GetInt32(), body.Type);
                    float[] actual = [body.Mass, body.RotationalInertia, body.LocalCenter.X, body.LocalCenter.Y, body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 2].GetSingle(), actual[i], $"{where}, component {i}");
                    }
                }
                world.Step();
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
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
                    Assert.Equal(expected[0].GetInt32(), actual.Fixture);
                    Assert.Equal(expected[1].GetInt32(), actual.ProxyId);
                    float[] bounds = [actual.Bounds.Lower.X, actual.Bounds.Lower.Y, actual.Bounds.Upper.X, actual.Bounds.Upper.Y];
                    for (int i = 0; i < bounds.Length; i++)
                    {
                        Near(expected[i + 2].GetSingle(), bounds[i], $"{context}, proxy {actual.Fixture}, bound {i}");
                    }
                }
                PhysicsContactEvent[] events = [.. row.GetProperty("Events").EnumerateArray().Select(e => new PhysicsContactEvent(
                    (PhysicsContactEventKind)e[0].GetInt32(), (PhysicsContactEventPhase)e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32(), e[4].GetInt64(), e[5].GetBoolean(), e[6].GetBoolean()))];
                Assert.True(events.SequenceEqual(world.DrainContactEvents()), context + ": events differ");
                Assert.True(Contacts(row.GetProperty("Contacts")).SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(180, tick);
        }

        /// <summary>Invalid type, density and mass requests reject before changing any body state.</summary>
        [Fact]
        public void InvalidTypeDensityAndMassRequestsDoNotMutate()
        {
            PhysicsWorld world = new(), other = new();
            PhysicsBody body = world.AddCircle(new(1, 0), .2f, 1, 1);
            PhysicsBody foreign = other.AddCircle(Vector2.Zero, .2f, 1, 1);
            world.AddTerrain([new(-1, 1), new(1, 1), new(1, 2), new(-1, 2)], .3f, 0);
            float mass = body.Mass;
            _ = Assert.Throws<ArgumentException>(() => world.SetBodyType(foreign, PhysicsBodyType.Static));
            _ = Assert.Throws<ArgumentException>(() => world.SetBodyType(body, (PhysicsBodyType)7));
            _ = Assert.Throws<ArgumentNullException>(() => world.SetBodyType(null!, PhysicsBodyType.Static));
            _ = Assert.Throws<ArgumentException>(() => world.SetFixtureDensity(1, -1));
            _ = Assert.Throws<ArgumentException>(() => world.SetFixtureDensity(1, float.NaN));
            _ = Assert.Throws<ArgumentException>(() => world.SetFixtureDensity(2, 1));
            _ = Assert.Throws<ArgumentException>(() => world.GetFixtureDensity(2));
            _ = Assert.Throws<KeyNotFoundException>(() => world.SetFixtureDensity(99, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.SetMassData(new(float.NaN, Vector2.Zero, 1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.SetMassData(new(1, new(1, 0), .5f)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.AddCircle(Vector2.Zero, .2f, 1, 1, 0, (PhysicsBodyType)9));
            _ = Assert.Throws<ArgumentException>(() => world.AddPolygon(new([new(0, 0), new(1, 0), new(0, 1)]), Vector2.Zero, 0, 1));
            Assert.Equal(mass, body.Mass);
            Assert.Equal(1, world.GetFixtureDensity(1));
            Assert.Equal(PhysicsBodyType.Dynamic, body.Type);
            PhysicsBody anchor = world.AddPolygon(new([new(0, 0), new(1, 0), new(0, 1)]), Vector2.Zero, 0, 1, type: PhysicsBodyType.Static);
            Assert.Equal(0, anchor.Mass);
            anchor.SetMassData(new(5, Vector2.One, 3));
            Assert.Equal(0, anchor.Mass);
            Assert.Equal(Vector2.Zero, anchor.LocalCenter);
        }

        private static PhysicsFixtureContactState[] Contacts(JsonElement rows)
        {
            return [.. rows.EnumerateArray().Select(c =>
            new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
