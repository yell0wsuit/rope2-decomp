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
    /// <summary>Independent native hinge, angular limit, motor and joint/contact island comparisons.</summary>
    public sealed class RevoluteJointTests
    {
        /// <summary>Compares every completed native tick and reaction/limit diagnostics.</summary>
        /// <param name="scenario">Synthetic native revolute scene.</param>
        [Theory]
        [InlineData("pendulum")]
        [InlineData("motor")]
        [InlineData("lower")]
        [InlineData("upper")]
        [InlineData("equal")]
        [InlineData("narrow")]
        [InlineData("fixed")]
        [InlineData("offset")]
        [InlineData("contacts")]
        [InlineData("topology")]
        [InlineData("slop-below")]
        [InlineData("slop-equal")]
        [InlineData("slop-above")]
        public void MatchesNativeRevoluteTrace(string scenario)
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
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, float angle = 0) =>
                Register(world.AddCircle(new(x, y), radius, density, gravity, angle), .3f);
            PhysicsBody Box(float x, float y, float gravity, float angle = 0, bool fixedRotation = false, bool compound = false, bool offset = false)
            {
                ConvexPolygon shape = offset ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
                return Register(world.AddPolygon(shape, new(x, y), 1, gravity, angle, fixedRotation, compound), .4f);
            }
            float slopWidth = 2 * ((1f / 180) * MathF.PI);
            if (scenario == "slop-below")
                slopWidth = MathF.BitDecrement(slopWidth);
            if (scenario == "slop-above")
                slopWidth = MathF.BitIncrement(slopWidth);
            PhysicsBody a, b;
            if (scenario == "contacts")
            {
                world.AddTerrain([new(-3, 1.5f), new(3, 1.5f), new(3, 2), new(-3, 2)], .3f, 0);
                label++;
                a = Circle(-.25f, .3f, .25f, 1, 1);
                b = Box(.15f, .8f, 1, .2f);
            }
            else if (scenario == "offset")
            {
                a = Box(0, 0, .3f, .1f, compound: true, offset: true);
                b = Circle(.7f, .3f, .2f, 1, 1);
            }
            else
            {
                a = Circle(0, 0, .1f, 0, 0);
                b = scenario == "fixed" ? Box(.6f, .8f, 1, .4f, true) :
                    Circle(scenario is "pendulum" or "topology" ? .6f : 0, scenario is "pendulum" or "topology" ? .8f : 0,
                        .3f, 1, scenario is "pendulum" or "topology" ? 1 : 0, scenario == "lower" ? -.7f : scenario == "upper" ? .7f : scenario == "equal" ? .4f : scenario == "slop-above" ? slopWidth : 0);
            }
            Vector2 anchorA = Vector2.Zero, anchorB = Vector2.Zero;
            float reference = 0, lower = -.3f, upper = .4f, speed = scenario == "lower" ? -2 : 2, torque = .05f;
            bool motor = scenario is not ("pendulum" or "topology" or "offset");
            bool limit = scenario is "lower" or "upper" or "equal" or "narrow" or "fixed" or "contacts" or "slop-below" or "slop-equal" or "slop-above";
            if (scenario is "pendulum" or "topology" or "fixed")
                anchorB = new(-.6f, -.8f);
            if (scenario == "offset")
            {
                anchorA = new(.25f, -.1f);
                anchorB = new(-.2f, .3f);
                reference = .2f;
            }
            if (scenario == "contacts")
            {
                anchorA = new(.2f, .2f);
                anchorB = new(-.2f, -.3f);
                lower = -.2f;
                upper = .5f;
                speed = .5f;
            }
            if (scenario == "motor")
            {
                speed = 3;
                torque = .02f;
            }
            if (scenario == "equal")
                lower = upper = .1f;
            if (scenario == "narrow")
            {
                lower = -.01f;
                upper = .01f;
            }
            if (scenario.StartsWith("slop-", StringComparison.Ordinal))
            {
                lower = 0;
                upper = slopWidth;
            }
            PhysicsRevoluteDefinition definition = new(anchorA, anchorB, reference, limit, limit ? lower : 0, limit ? upper : 0,
                motor, motor ? speed : 0, motor ? torque : 0, scenario == "contacts");
            PhysicsRevoluteJoint? joint = world.AddRevolute(a, b, definition);
            PhysicsJoint? rope = null;
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RevoluteReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "motor")
                {
                    if (tick == 60)
                        joint!.MotorSpeed = -4;
                    if (tick == 90)
                        joint!.MaxMotorTorque = .005f;
                    if (tick == 110)
                        joint!.MotorEnabled = false;
                    if (tick == 130)
                    {
                        joint!.MotorEnabled = true;
                        joint.MotorSpeed = 1;
                    }
                    if (tick == 150)
                        joint!.MaxMotorTorque = 0;
                }
                if (scenario is "lower" or "upper")
                {
                    if (tick == 60)
                        joint!.MotorSpeed = scenario == "lower" ? 2 : -2;
                    if (tick is 80 or 100)
                        joint!.SetLimits(-.1f, .1f);
                    if (tick == 120)
                        joint!.LimitEnabled = false;
                    if (tick is 140 or 160)
                        joint!.LimitEnabled = true;
                }
                if (scenario == "equal")
                {
                    if (tick == 60)
                        joint!.SetLimits(-.3f, .3f);
                    if (tick == 90)
                        joint!.MotorEnabled = false;
                    if (tick == 120)
                        joint!.MotorEnabled = true;
                }
                if (scenario == "topology")
                {
                    if (tick == 40)
                    {
                        Assert.True(world.Remove(joint!));
                        joint = null;
                    }
                    if (tick == 50)
                        joint = world.AddRevolute(a, b, definition);
                    if (tick == 120)
                    {
                        Assert.True(world.RemoveBody(b));
                        bodies.Remove(bodies.Single(e => e.Value == b).Key);
                        joint = null;
                    }
                    if (tick == 130)
                    {
                        b = Circle(.6f, .8f, .3f, 1, 1);
                        joint = world.AddRevolute(a, b, definition);
                    }
                }
                if (scenario is "topology" or "contacts")
                {
                    if (tick == 80)
                        rope = world.AddRope(a, b, 1.3f, new(-1, 0), new(1, 0));
                    if (tick == 100)
                        Assert.True(world.Remove(rope!));
                }
                world.Step();
                string context = $"{scenario}, tick {tick}";
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                        Near(state[i + 1].GetSingle(), actual[i], $"{context}, body {state[0]}, component {i}");
                }
                JsonElement expectedJoint = row.GetProperty("Joint");
                Assert.Equal(expectedJoint.ValueKind == JsonValueKind.Null, joint == null);
                if (joint != null)
                {
                    float[] actual = [joint.AnchorA.X, joint.AnchorA.Y, joint.AnchorB.X, joint.AnchorB.Y,
                        joint.ReactionForce.X, joint.ReactionForce.Y, joint.ReactionTorque, joint.MotorTorque, joint.Angle, joint.Speed];
                    for (int i = 0; i < actual.Length; i++)
                        Near(expectedJoint[i].GetSingle(), actual[i], $"{context}, joint component {i}");
                    Assert.Equal((PhysicsJointLimitState)expectedJoint[10].GetInt32(), joint.LimitState);
                    Assert.Equal(expectedJoint[11].GetSingle(), joint.LowerAngle);
                    Assert.Equal(expectedJoint[12].GetSingle(), joint.UpperAngle);
                }
                PhysicsContactEvent[] events = [.. row.GetProperty("Events").EnumerateArray().Select(e => new PhysicsContactEvent(
                    (PhysicsContactEventKind)e[0].GetInt32(), (PhysicsContactEventPhase)e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32(), e[4].GetInt64(), e[5].GetBoolean(), e[6].GetBoolean()))];
                Assert.True(events.SequenceEqual(world.DrainContactEvents()), context + ": events differ");
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(c =>
                    new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(180, tick);
        }
        /// <summary>World-anchor initialization uses body origins and captures the current relative angle.</summary>
        [Fact]
        public void WorldAnchorInitializationAndJointContractAreCoherent()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody a = world.AddCircle(new(.1f, .2f), .2f, 0, 0, .3f);
            PhysicsBody b = world.AddPolygon(new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]),
                new(.6f, .5f), 1, 0, -.2f);
            Vector2 anchor = new(.4f, .3f);
            PhysicsRevoluteJoint joint = world.AddRevolute(a, b, anchor);
            Near(anchor.X, joint.AnchorA.X, "world anchor A X");
            Near(anchor.Y, joint.AnchorA.Y, "world anchor A Y");
            Near(anchor.X, joint.AnchorB.X, "world anchor B X");
            Near(anchor.Y, joint.AnchorB.Y, "world anchor B Y");
            Assert.Equal(-.2f - .3f, joint.ReferenceAngle);
            Assert.Equal(0, joint.Angle);
            Assert.False(joint.CollideConnected);
            Assert.Throws<NotSupportedException>(() => joint.Length);
            world.Step();
            Assert.True(world.Remove(joint));
            Assert.False(world.Remove(joint));
            world.Step();
        }

        /// <summary>Setters retain or clear each cached impulse according to native changed-value rules.</summary>
        [Fact]
        public void RuntimeSettersPreserveNativeImmediateCacheSemantics()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody a = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody b = world.AddCircle(Vector2.Zero, .3f, 1, 0, -.5f);
            PhysicsRevoluteJoint joint = world.AddRevolute(a, b, new PhysicsRevoluteDefinition(Vector2.Zero, Vector2.Zero, 0,
                true, -.3f, .4f, true, -2, .05f));
            world.Step();
            Assert.Equal(PhysicsJointLimitState.AtLower, joint.LimitState);
            Assert.True(joint.ReactionTorque > 0);
            Assert.True(joint.MotorTorque < 0);
            float torque = joint.ReactionTorque, motor = joint.MotorTorque;
            Vector2 force = joint.ReactionForce;
            joint.SetLimits(joint.LowerAngle, joint.UpperAngle);
            joint.LimitEnabled = true;
            joint.MotorEnabled = true;
            joint.MotorSpeed = joint.MotorSpeed;
            joint.MaxMotorTorque = joint.MaxMotorTorque;
            Assert.Equal(torque, joint.ReactionTorque);
            Assert.Equal(motor, joint.MotorTorque);
            joint.MotorEnabled = false;
            Assert.Equal(motor, joint.MotorTorque);
            joint.LimitEnabled = false;
            Assert.Equal(0, joint.ReactionTorque);
            Assert.Equal(force, joint.ReactionForce);
            Assert.Equal(PhysicsJointLimitState.AtLower, joint.LimitState);
            world.Step();
            Assert.Equal(0, joint.MotorTorque);
            Assert.Equal(PhysicsJointLimitState.Inactive, joint.LimitState);
            joint.LimitEnabled = true;
            joint.MotorEnabled = true;
            world.Step();
            joint.SetLimits(-.2f, .2f);
            Assert.Equal(0, joint.ReactionTorque);
        }

        /// <summary>Rejected mutations preserve the existing definition and pending world ownership.</summary>
        [Fact]
        public void InvalidDefinitionsAndSettersDoNotMutateWorld()
        {
            PhysicsWorld world = new(), other = new();
            PhysicsBody a = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody b = world.AddCircle(new(1, 0), .2f, 1, 0);
            PhysicsBody foreign = other.AddCircle(Vector2.Zero, .2f, 1, 0);
            var proxies = world.CaptureProxies();
            PhysicsRevoluteDefinition definition = new(Vector2.Zero, Vector2.Zero, 0);
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, foreign, definition));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, a, definition));
            Assert.Throws<ArgumentNullException>(() => world.AddRevolute(null!, b, definition));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, b, definition with { LocalAnchorA = new(float.NaN, 0) }));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, b, definition with { ReferenceAngle = float.PositiveInfinity }));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, b, definition with { LowerAngle = 2, UpperAngle = 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.AddRevolute(a, b, definition with { MotorSpeed = 1000001 }));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, b, definition with { MaxMotorTorque = -1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.AddRevolute(a, b, new Vector2(float.NaN, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.AddCircle(Vector2.Zero, .2f, 1, 0, float.NaN));
            Assert.True(proxies.SequenceEqual(world.CaptureProxies()));
            PhysicsRevoluteJoint joint = world.AddRevolute(a, b, definition);
            Assert.Throws<ArgumentOutOfRangeException>(() => joint.SetLimits(.5f, -.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => joint.SetLimits(float.NaN, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => joint.MotorSpeed = float.NaN);
            Assert.Throws<ArgumentOutOfRangeException>(() => joint.MaxMotorTorque = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => joint.MaxMotorTorque = float.PositiveInfinity);
            Assert.Equal(0, joint.LowerAngle);
            Assert.Equal(0, joint.UpperAngle);
            Assert.Equal(0, joint.MotorSpeed);
            Assert.Equal(0, joint.MaxMotorTorque);
            Assert.True(world.Remove(joint));
            Assert.True(world.RemoveBody(b));
            Assert.Throws<ArgumentException>(() => world.AddRevolute(a, b, definition));
            world.Step();
        }

        /// <summary>Colliding hinges coexist with blocking joints without losing symmetric suppression ownership.</summary>
        [Fact]
        public void CollideConnectedAndRemovalPreserveCollisionBlocks()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            PhysicsBody a = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            PhysicsBody b = world.AddCircle(new(.5f, 0), .3f, 1, 0);
            a.IsSensor = b.IsSensor = true;
            PhysicsRevoluteDefinition definition = new(Vector2.Zero, new(-.5f, 0), 0, CollideConnected: true);
            PhysicsRevoluteJoint hinge = world.AddRevolute(a, b, definition);
            Assert.True(hinge.CollideConnected);
            world.Step();
            Assert.True(Assert.Single(world.CaptureFixtureContacts()).Touching);
            Assert.Equal(PhysicsContactEventKind.Begin, Assert.Single(world.DrainContactEvents()).Kind);
            PhysicsJoint rope = world.AddRope(a, b, 10);
            PhysicsRevoluteJoint blocker = world.AddRevolute(a, b, definition with
            {
                CollideConnected = false
            });
            world.Step();
            Assert.Empty(world.CaptureFixtureContacts());
            Assert.Equal(PhysicsContactEventKind.End, Assert.Single(world.DrainContactEvents()).Kind);
            Assert.True(world.Remove(rope));
            world.SetFixtureFilter(1, PhysicsCollisionFilter.Default);
            world.Step();
            Assert.Empty(world.CaptureFixtureContacts());
            Assert.True(world.Remove(blocker));
            world.Step();
            Assert.Empty(world.CaptureFixtureContacts());
            world.SetFixtureFilter(1, PhysicsCollisionFilter.Default);
            world.Step();
            world.Step();
            Assert.True(Assert.Single(world.CaptureFixtureContacts()).Touching);
            Assert.True(world.RemoveBody(b));
            Assert.False(world.Remove(hinge));
            Assert.Empty(world.CaptureFixtureContacts());
        }

        private static void Near(float expected, float actual, string context) =>
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f,
                $"{context}: expected {expected}, actual {actual}");
    }
}
