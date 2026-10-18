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
    /// <summary>Independent native rigid, zero-length, colliding and world-anchor distance/rope joint comparisons.</summary>
    public sealed class DistanceJointTests
    {
        /// <summary>Compares every native tick's bodies, joint diagnostics and contact candidates.</summary>
        /// <param name="scenario">Synthetic native joint scene.</param>
        [Theory]
        [InlineData("rigid")]
        [InlineData("springs")]
        [InlineData("collide")]
        [InlineData("initialize")]
        [InlineData("coincident")]
        public void MatchesNativeDistanceJointTrace(string scenario)
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
            static Vector2 At(PhysicsBody body, Vector2 local)
            {
                float cosine = MathF.Cos(body.Angle), sine = MathF.Sin(body.Angle);
                return body.Origin + new Vector2((cosine * local.X) - (sine * local.Y), (sine * local.X) + (cosine * local.Y));
            }
            List<PhysicsJoint> joints = [];
            PhysicsBody a, b, c;
            switch (scenario)
            {
                case "rigid":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Circle(.8f, .3f, .2f, 1, 1);
                    c = Box(-.6f, .5f, .3f, .15f, 1, .3f);
                    joints.Add(world.AddDistance(a, b, new(Vector2.Zero, new(.05f, -.1f), .7f)));
                    joints.Add(world.AddDistance(b, c, new(Vector2.Zero, new(.2f, .1f), .9f)));
                    break;
                case "springs":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Box(.3f, .4f, .2f, .1f, 1, 0, true);
                    c = Circle(-.8f, .2f, .15f, 1, 1);
                    joints.Add(world.AddDistance(a, b, new(new(.1f, 0), new(.1f, .2f), 0, 2, .1f)));
                    joints.Add(world.AddDistance(a, c, new(Vector2.Zero, Vector2.Zero, 2.675f, 2, .5f)));
                    joints.Add(world.AddDistance(b, c, new(new(.05f, .05f), new(0, .1f), .5f, 11, .4f)));
                    break;
                case "collide":
                    world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
                    label++;
                    a = Circle(-.5f, .5f, .25f, 1, 1);
                    b = Circle(-.49f, -.1f, .25f, 1, 1);
                    c = Circle(.6f, .5f, .25f, 1, 1);
                    PhysicsBody d = Circle(.605f, -.1f, .25f, 1, 1);
                    joints.Add(world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, .6f, true)));
                    joints.Add(world.AddDistance(c, d, new(Vector2.Zero, Vector2.Zero, .6f, 4, 0, true)));
                    joints.Add(world.AddRope(a, c, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, 1.3f)));
                    break;
                case "initialize":
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Box(.5f, .2f, .25f, .1f, 1, .4f, true);
                    c = Box(-.5f, .6f, .3f, .1f, 1, -.3f);
                    joints.Add(world.AddDistance(a, b, new(.05f, 0), At(b, new(.1f, .2f)), 10, .3f));
                    joints.Add(world.AddDistance(b, c, b.Position, At(c, new(-.2f, 0)), 3, 0, true));
                    joints.Add(world.AddDistance(a, c, Vector2.Zero, c.Position));
                    break;
                default:
                    a = Circle(0, 0, .1f, 0, 0);
                    b = Circle(0, 0, .15f, 1, 1);
                    c = Circle(.5f, 0, .15f, 1, 1);
                    joints.Add(world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, .4f)));
                    joints.Add(world.AddDistance(b, c, new(Vector2.Zero, Vector2.Zero, 0)));
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "DistanceJointReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "rigid" && tick == 40)
                {
                    b.ApplyLinearImpulse(new(-.6f, -.3f), b.Position);
                }
                if (scenario == "collide" && tick == 60)
                {
                    Assert.True(world.Remove(joints[0]));
                    joints[0] = world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, .6f));
                }
                if (scenario == "coincident" && tick == 30)
                {
                    c.ApplyLinearImpulse(new(0, -.2f), c.Position);
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
                foreach ((JsonElement expected, PhysicsJoint joint) in row.GetProperty("Joints").EnumerateArray().Zip(joints))
                {
                    float[] actual = [joint.AnchorA.X, joint.AnchorA.Y, joint.AnchorB.X, joint.AnchorB.Y, joint.ReactionForce.X, joint.ReactionForce.Y];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(expected[i].GetSingle(), actual[i], $"{context}, joint component {i}");
                    }
                }
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(e =>
                    new PhysicsFixtureContactState(e[0].GetInt32(), e[1].GetInt32(), e[2].GetInt64(), e[3].GetBoolean(), e[4].GetBoolean(), e[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), context + ": candidates differ");
            }
            Assert.Equal(150, tick);
        }

        /// <summary>Invalid joint definitions reject before world ownership changes.</summary>
        [Fact]
        public void InvalidDistanceDefinitionsDoNotMutate()
        {
            PhysicsWorld world = new(), other = new();
            PhysicsBody a = world.AddCircle(Vector2.Zero, .1f, 0, 0);
            PhysicsBody b = world.AddCircle(Vector2.One, .1f, 1, 0);
            PhysicsBody foreign = other.AddCircle(Vector2.Zero, .1f, 1, 0);
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, foreign, new(Vector2.Zero, Vector2.Zero, 1)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, a, new(Vector2.Zero, Vector2.Zero, 1)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, b, new(Vector2.Zero, Vector2.Zero, -1)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, b, new(Vector2.Zero, Vector2.Zero, 1, float.NaN)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, b, new(Vector2.Zero, Vector2.Zero, 1, 1, -1)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, b, new(new(float.NaN, 0), Vector2.Zero, 1)));
            _ = Assert.Throws<ArgumentException>(() => world.AddDistance(a, b, new Vector2(20000, 0), Vector2.Zero));
            _ = Assert.Throws<ArgumentException>(() => world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, float.PositiveInfinity)));
            _ = Assert.Throws<ArgumentNullException>(() => world.AddRope(null!, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, 1)));
            PhysicsJoint rigid = world.AddDistance(a, b, Vector2.Zero, Vector2.One);
            Assert.Equal(MathF.Sqrt(2), rigid.Length);
            Assert.False(rigid.CollideConnected);
            Assert.True(world.AddRope(a, b, new PhysicsRopeDefinition(Vector2.Zero, Vector2.Zero, 2, true)).CollideConnected);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f,
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
