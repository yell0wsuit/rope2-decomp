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
    /// <summary>Independent native prismatic limit, motor, runtime setter and topology comparisons.</summary>
    public sealed class PrismaticJointTests
    {
        /// <summary>Compares every native tick's bodies, joint diagnostics and candidates.</summary>
        /// <param name="scenario">Synthetic native prismatic scene.</param>
        [Theory]
        [InlineData("banner")]
        [InlineData("slider")]
        [InlineData("equal")]
        [InlineData("free")]
        [InlineData("contacts")]
        [InlineData("lower")]
        public void MatchesNativePrismaticTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
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
            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, float angle = 0, bool compound = false)
            {
                ConvexPolygon shape = compound ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]);
                return Register(world.AddPolygon(shape, new(x, y), 1, gravity, angle, compound: compound), .4f);
            }
            PhysicsBody a, b;
            PhysicsPrismaticJoint? joint;
            PhysicsPrismaticDefinition definition = default;
            Func<PhysicsPrismaticJoint> create;
            switch (scenario)
            {
                case "banner":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Box(0, -.5f, .6f, .3f, 1);
                    definition = new(default, default, default, 0, true, 0, 2.675f, true, 30, -10, true);
                    create = () => world.AddPrismatic(a, b, b.Position, new(0, 1), definition);
                    break;
                case "slider":
                    a = Box(0, 0, .4f, .1f, .5f, .3f);
                    b = Box(.5f, .3f, 0, 0, 1, -.2f, true);
                    definition = new(new(.1f, .05f), new(.05f, .1f), new(2, 1), -.5f, true, -.3f, .4f, true, 2, .5f);
                    create = () => world.AddPrismatic(a, b, definition);
                    break;
                case "equal":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Box(.4f, .2f, .2f, .1f, 1, .4f);
                    definition = new(default, default, default, 0, true, .05f, .05f);
                    create = () => world.AddPrismatic(a, b, new(.2f, .1f), new(1, -1), definition);
                    break;
                case "free":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Circle(.3f, .1f, .2f, 1, 1);
                    create = () => world.AddPrismatic(a, b, Vector2.Zero, new(1, .3f));
                    break;
                case "contacts":
                    world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
                    label++;
                    a = Box(-.5f, .3f, .3f, .2f, 1);
                    b = Circle(.4f, .3f, .25f, 1, 1);
                    definition = new(default, default, default, 0, true, .2f, 1.2f, true, .6f, 2);
                    create = () => world.AddPrismatic(a, b, new(0, .3f), new(1, 0), definition);
                    break;
                default:
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Box(0, .3f, .2f, .2f, 1);
                    definition = new(default, default, default, 0, true, -.2f, .2f, true, 50, -1);
                    create = () => world.AddPrismatic(a, b, new(0, .3f), new(0, 1), definition);
                    break;
            }
            joint = create();
            // Native recreates from the same definition, so later joints keep the original local frame.
            PhysicsPrismaticDefinition initial = definition with
            {
                LocalAnchorA = joint.LocalAnchorA,
                LocalAnchorB = joint.LocalAnchorB,
                LocalAxisA = joint.LocalAxisA,
                ReferenceAngle = joint.ReferenceAngle
            };
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PrismaticReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                switch (scenario)
                {
                    case "banner":
                        if (tick == 40)
                        {
                            joint!.MotorSpeed = 10;
                        }
                        if (tick == 70)
                        {
                            joint!.SetLimits(0, 3.175f);
                            joint.MotorSpeed = -10;
                        }
                        if (tick == 90)
                        {
                            joint!.MotorEnabled = false;
                        }
                        if (tick == 110)
                        {
                            joint!.MotorEnabled = true;
                            joint.SetLimits(0, 2.675f);
                        }
                        if (tick == 130)
                        {
                            joint!.SetLimits(0, 2.675f);
                        }
                        break;
                    case "slider":
                        if (tick == 50)
                        {
                            joint!.MotorSpeed = -1.5f;
                        }
                        if (tick == 90)
                        {
                            joint!.MaxMotorForce = .1f;
                        }
                        if (tick == 120)
                        {
                            joint!.LimitEnabled = false;
                        }
                        break;
                    case "equal":
                        if (tick == 60)
                        {
                            joint!.SetLimits(-.1f, .1f);
                        }
                        if (tick == 100)
                        {
                            joint!.LimitEnabled = false;
                        }
                        break;
                    case "lower":
                        if (tick == 50)
                        {
                            joint!.MotorSpeed = 1;
                        }
                        if (tick == 100)
                        {
                            joint!.LimitEnabled = false;
                            joint.LimitEnabled = true;
                        }
                        break;
                    case "contacts":
                        if (tick == 60)
                        {
                            Assert.True(world.Remove(joint!));
                            joint = null;
                        }
                        if (tick == 80)
                        {
                            joint = world.AddPrismatic(a, b, initial with
                            {
                                CollideConnected = true
                            });
                        }
                        break;
                    default:
                        break;
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
                JsonElement expected = row.GetProperty("Joint");
                Assert.Equal(expected.ValueKind == JsonValueKind.Null, joint == null);
                if (joint != null)
                {
                    float[] actual = [joint.AnchorA.X, joint.AnchorA.Y, joint.AnchorB.X, joint.AnchorB.Y, joint.ReactionForce.X, joint.ReactionForce.Y,
                        joint.ReactionTorque, joint.MotorForce, joint.Translation, joint.Speed];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(expected[i].GetSingle(), actual[i], $"{context}, joint component {i}");
                    }
                    Assert.Equal((PhysicsJointLimitState)expected[10].GetInt32(), joint.LimitState);
                }
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(c =>
                    new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(150, tick);
        }

        /// <summary>Invalid definitions and setters reject before mutating ownership or parameters.</summary>
        [Fact]
        public void InvalidPrismaticDefinitionsAndSettersReject()
        {
            PhysicsWorld world = new();
            PhysicsBody a = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody b = world.AddCircle(Vector2.One, .1f, 1, 0);
            PhysicsPrismaticDefinition definition = new(Vector2.Zero, Vector2.Zero, Vector2.UnitX, 0);
            _ = Assert.Throws<ArgumentException>(() => world.AddPrismatic(a, a, definition));
            _ = Assert.Throws<ArgumentException>(() => world.AddPrismatic(a, b, definition with { LocalAxisA = Vector2.Zero }));
            _ = Assert.Throws<ArgumentException>(() => world.AddPrismatic(a, b, definition with { LowerTranslation = 1, UpperTranslation = 0 }));
            _ = Assert.Throws<ArgumentException>(() => world.AddPrismatic(a, b, definition with { MaxMotorForce = -1 }));
            _ = Assert.Throws<ArgumentException>(() => world.AddPrismatic(a, b, new Vector2(float.NaN, 0), Vector2.UnitY));
            PhysicsPrismaticJoint joint = world.AddPrismatic(a, b, definition with
            {
                LocalAxisA = new(3, 4)
            });
            Assert.Equal(new Vector2(.6f, .8f), joint.LocalAxisA);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => joint.SetLimits(1, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => joint.MotorSpeed = float.NaN);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => joint.MaxMotorForce = -1);
            Assert.Equal(0, joint.MaxMotorForce);
            Assert.False(joint.CollideConnected);
            _ = Assert.Throws<NotSupportedException>(() => joint.Length);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
