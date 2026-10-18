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
    /// <summary>Independent native velocity, impulse, force, gravity-scale and teleport comparisons.</summary>
    public sealed class BodyControlTests
    {
        /// <summary>Compares every native tick, including pair discovery immediately after teleports.</summary>
        /// <param name="scenario">Synthetic native body-control scene.</param>
        [Theory]
        [InlineData("velocity")]
        [InlineData("impulse")]
        [InlineData("force")]
        [InlineData("gravity")]
        [InlineData("transform")]
        public void MatchesNativeBodyControlTrace(string scenario)
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
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity), .3f);
            }

            PhysicsBody Box(float x, float y, float gravity, float angle = 0, bool fixedRotation = false, bool compound = false, bool offset = false)
            {
                ConvexPolygon shape = offset ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
                return Register(world.AddPolygon(shape, new(x, y), 1, gravity, angle, fixedRotation, compound), .4f);
            }
            void Ground()
            {
                world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
                label++;
            }
            static Vector2 At(PhysicsBody body, float x, float y)
            {
                return body.Origin + Rotate(new(x, y), body.Angle);
            }

            PhysicsBody a, b, s = null!, c = null!;
            switch (scenario)
            {
                case "velocity":
                    s = Circle(0, 0, .1f, 0, 0);
                    a = Circle(1, 0, .2f, 1, 1);
                    b = Box(-1, 0, 1, .2f);
                    b.LinearDamping = .5f;
                    b.AngularDamping = .2f;
                    _ = world.AddRope(s, a, 1.2f, new(-1, 0), new(1, 0));
                    break;
                case "impulse":
                    s = Circle(0, -2, .1f, 0, 0);
                    a = Box(0, 0, 0, .3f, false, true, true);
                    b = Circle(1, 0, .2f, 1, 0);
                    c = Box(-1, 0, 0, .1f, true);
                    b.AngularDamping = .1f;
                    break;
                case "force":
                    Ground();
                    a = Circle(0, .7f, .25f, 1, 1);
                    b = Box(1, .8f, 1);
                    s = Circle(-1, 0, .1f, 0, 0);
                    c = Box(-1.5f, -1, 0, .2f, false, true, true);
                    break;
                case "gravity":
                    s = Circle(0, 0, .1f, 0, 0);
                    a = Circle(.5f, 0, .2f, 1, 1);
                    b = Box(-.5f, 0, .5f, .4f);
                    _ = world.AddSpring(s, b, Vector2.Distance(s.Position, b.Position), 3, .2f);
                    break;
                default:
                    Ground();
                    a = Circle(0, 0, .2f, 1, 1);
                    b = Box(1, -.5f, 1, .3f, false, true, true);
                    c = Circle(-2, -1, .3f, 0, 0);
                    a.LinearDamping = .5f;
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BodyControlReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                switch (scenario)
                {
                    case "velocity":
                        if (tick == 10)
                        {
                            a.SetLinearVelocity(new(2, -3));
                        }

                        if (tick == 20)
                        {
                            b.SetAngularVelocity(4);
                        }

                        if (tick == 30)
                        {
                            s.SetLinearVelocity(new(5, 5));
                            s.SetAngularVelocity(3);
                        }
                        if (tick == 40)
                        {
                            a.SetLinearVelocity(Vector2.Zero);
                        }

                        if (tick == 50)
                        {
                            b.SetAngularVelocity(-2.5f);
                            b.SetLinearVelocity(new(-1, -4));
                        }
                        if (tick == 70)
                        {
                            a.SetLinearVelocity(new(1, 1));
                            a.SetLinearVelocity(new(-3, .5f));
                            a.SetAngularVelocity(7);
                        }
                        if (tick == 90)
                        {
                            b.SetAngularVelocity(0);
                        }

                        break;
                    case "impulse":
                        if (tick == 10)
                        {
                            a.ApplyLinearImpulse(new(.03f, -.05f), At(a, .4f, .1f));
                        }

                        if (tick == 20)
                        {
                            a.ApplyAngularImpulse(.002f);
                        }

                        if (tick == 30)
                        {
                            b.ApplyLinearImpulse(new(-.02f, 0), b.Position + new Vector2(0, .2f));
                        }

                        if (tick == 40)
                        {
                            a.ApplyLinearImpulse(new(0, .02f), At(a, -.1f, .3f));
                            a.ApplyLinearImpulse(new(-.01f, .01f), a.Position);
                            a.ApplyAngularImpulse(-.001f);
                        }
                        if (tick == 50)
                        {
                            s.ApplyLinearImpulse(new(1, 1), s.Position + new Vector2(.1f, 0));
                            s.ApplyAngularImpulse(1);
                        }
                        if (tick == 60)
                        {
                            c.ApplyLinearImpulse(new(.05f, -.02f), At(c, .3f, .2f));
                            c.ApplyAngularImpulse(.5f);
                        }
                        if (tick == 80)
                        {
                            b.ApplyAngularImpulse(-.003f);
                        }

                        break;
                    case "force":
                        if (tick <= 60)
                        {
                            a.ApplyForceToCenter(new(0, -12));
                        }

                        if (tick is >= 30 and <= 40)
                        {
                            a.ApplyForceToCenter(new(.5f, -.2f));
                        }

                        if (tick is > 60 and <= 90)
                        {
                            b.ApplyForce(new(1, 0), At(b, .1f, -.1f));
                            c.ApplyForce(new(.3f, -.1f), At(c, .4f, .1f));
                            a.ApplyForce(new(-1, -2), a.Position + new Vector2(.25f, 0));
                        }
                        if (tick is > 90 and <= 120)
                        {
                            b.ApplyTorque(.2f);
                            b.ApplyTorque(-.1f);
                            a.ApplyTorque(.05f);
                            c.ApplyTorque(-.004f);
                        }
                        if (tick == 100)
                        {
                            s.ApplyForceToCenter(new(10, 10));
                            s.ApplyForce(Vector2.One, Vector2.Zero);
                            s.ApplyTorque(5);
                        }
                        break;
                    case "gravity":
                        if (tick == 30)
                        {
                            a.GravityScale = -1;
                            b.GravityScale = 2.5f;
                        }
                        if (tick == 60)
                        {
                            a.GravityScale = 2.5f;
                            s.GravityScale = 3;
                        }
                        if (tick == 90)
                        {
                            a.GravityScale = 0;
                            b.GravityScale = -.25f;
                        }
                        if (tick == 120)
                        {
                            a.GravityScale = 1;
                        }

                        break;
                    default:
                        if (tick == 20)
                        {
                            world.SetTransform(a, new(.5f, .8f), .3f);
                        }

                        if (tick == 40)
                        {
                            world.SetTransform(b, new(.9f, .55f), 1);
                        }

                        if (tick == 60)
                        {
                            world.SetTransform(c, new(1.4f, .4f), .5f);
                        }

                        if (tick == 80)
                        {
                            world.SetTransform(a, a.Origin + new Vector2(.01f, 0), a.Angle);
                        }

                        if (tick == 100)
                        {
                            world.SetTransform(b, new(-1.5f, -.5f), -.4f);
                            world.SetTransform(a, new(-1.6f, -.2f), 0);
                            world.SetTransform(b, new(-1.4f, .7f), -.4f);
                        }
                        if (tick == 130)
                        {
                            world.SetTransform(c, new(-1.5f, 0), 0);
                        }

                        break;
                }
                string context = $"{scenario}, tick {tick}";
                Assert.True(Contacts(row.GetProperty("Pre")).SequenceEqual(world.CaptureFixtureContacts()), context + ": pre-step candidates differ");
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

        /// <summary>Forces apply only to the next step, and static bodies ignore every control.</summary>
        [Fact]
        public void ForcesClearAfterOneStepAndStaticBodiesIgnoreControls()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody anchor = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody body = world.AddCircle(new(2, 0), .2f, 1, 0);
            body.ApplyForceToCenter(new(1, 0));
            body.ApplyTorque(1);
            world.Step();
            Vector2 velocity = body.Velocity;
            float spin = body.AngularVelocity;
            Assert.True(velocity.X > 0 && spin > 0);
            world.Step();
            Assert.Equal(velocity, body.Velocity);
            Assert.Equal(spin, body.AngularVelocity);
            anchor.SetLinearVelocity(Vector2.One);
            anchor.SetAngularVelocity(1);
            anchor.ApplyLinearImpulse(Vector2.One, Vector2.One);
            anchor.ApplyAngularImpulse(1);
            anchor.ApplyForce(Vector2.One, Vector2.One);
            anchor.ApplyForceToCenter(Vector2.One);
            anchor.ApplyTorque(1);
            world.Step();
            Assert.Equal(Vector2.Zero, anchor.Position);
            Assert.Equal(Vector2.Zero, anchor.Velocity);
            Assert.Equal(0, anchor.AngularVelocity);
            Assert.Equal(0, anchor.Angle);
        }

        /// <summary>A teleported origin reads back exactly until the next step recomputes it from the center of mass.</summary>
        [Fact]
        public void TeleportedOriginIsExactUntilNextStep()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody body = world.AddPolygon(new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]),
                Vector2.Zero, 1, 0);
            int exact = 0;
            for (int i = 0; i < 64; i++)
            {
                Vector2 origin = new((.37f * i) - 5.3f, 1.1f - (.29f * i));
                float angle = (.61f * i) - 9;
                world.SetTransform(body, origin, angle);
                Assert.Equal(origin, body.Origin);
                Assert.Equal(angle, body.Angle);
                Vector2 center = origin + Rotate(body.LocalCenter, angle);
                Assert.Equal(center, body.Position);
                if (body.Position - Rotate(body.LocalCenter, angle) == origin)
                {
                    exact++;
                }
            }
            // Recomputing the origin from the center does not round-trip for every transform.
            Assert.True(exact < 64);
            world.Step();
            Assert.Equal(body.Position - Rotate(body.LocalCenter, body.Angle), body.Origin);
        }

        /// <summary>Invalid controls reject before changing body state or world ownership.</summary>
        [Fact]
        public void InvalidControlsAndForeignTeleportsDoNotMutate()
        {
            PhysicsWorld world = new(), other = new();
            PhysicsBody body = world.AddCircle(new(1, 0), .2f, 1, 1);
            PhysicsBody foreign = other.AddCircle(Vector2.Zero, .2f, 1, 1);
            IReadOnlyList<PhysicsProxyState> proxies = world.CaptureProxies();
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.SetLinearVelocity(new(float.NaN, 0)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.SetAngularVelocity(float.PositiveInfinity));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyForce(Vector2.One, new(0, float.NaN)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyForceToCenter(new(float.NegativeInfinity, 0)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyTorque(float.NaN));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyLinearImpulse(new(float.NaN, 0), Vector2.Zero));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.ApplyAngularImpulse(float.NaN));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.GravityScale = float.NaN);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => body.GravityScale = 1000001);
            _ = Assert.Throws<ArgumentException>(() => world.SetTransform(foreign, Vector2.Zero, 0));
            _ = Assert.Throws<ArgumentException>(() => world.SetTransform(body, new(20000, 0), 0));
            _ = Assert.Throws<ArgumentException>(() => world.SetTransform(body, Vector2.Zero, float.NaN));
            _ = Assert.Throws<ArgumentNullException>(() => world.SetTransform(null!, Vector2.Zero, 0));
            Assert.Equal(new Vector2(1, 0), body.Position);
            Assert.Equal(Vector2.Zero, body.Velocity);
            Assert.Equal(1, body.GravityScale);
            Assert.True(proxies.SequenceEqual(world.CaptureProxies()));
            Assert.True(world.RemoveBody(body));
            _ = Assert.Throws<ArgumentException>(() => world.SetTransform(body, Vector2.Zero, 0));
        }

        private static PhysicsFixtureContactState[] Contacts(JsonElement rows)
        {
            return [.. rows.EnumerateArray().Select(c =>
            new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
        }

        private static Vector2 Rotate(Vector2 point, float angle)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            return new((cosine * point.X) - (sine * point.Y), (sine * point.X) + (cosine * point.Y));
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f,
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
