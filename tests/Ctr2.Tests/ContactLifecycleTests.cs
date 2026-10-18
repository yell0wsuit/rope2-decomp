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
    /// <summary>Checks contact identity, order and every discrete tick against an independent native world.</summary>
    public sealed class ContactLifecycleTests
    {
        /// <summary>Matches native candidates, touching transitions and six-component numerical state.</summary>
        [Theory]
        [InlineData("corner")]
        [InlineData("bounce")]
        [InlineData("compound")]
        [InlineData("offset")]
        [InlineData("topology")]
        public void MatchesNativeContactLifecycle(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            PhysicsBody? body;
            switch (scenario)
            {
                case "corner":
                    Ground(world, 0, 0, 1, 1);
                    body = Circle(world, new(-.15f, -.15f));
                    break;
                case "bounce":
                    Ground(world, -2, 1, 2, 2, .8f);
                    body = Circle(world, Vector2.Zero);
                    break;
                case "compound":
                case "offset":
                    Ground(world, -2, 1, 2, 2);
                    Ground(world, .22f, 0, .4f, 2);
                    ConvexPolygon shape = scenario == "offset" ?
                        new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                        new([new(-.2f, -.2f), new(.2f, -.2f), new(.2f, .2f), new(-.2f, .2f)]);
                    body = world.AddPolygon(shape, scenario == "offset" ? new(-.3f, -.4f) : Vector2.Zero, .5f, .7f, .3f);
                    body.Friction = .3f;
                    body.AngularDamping = .1f;
                    break;
                default:
                    body = Circle(world, Vector2.Zero);
                    break;
            }
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ContactLifecycleReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "topology")
                {
                    if (tick == 31)
                    {
                        Ground(world, -2, 1.5f, 2, 2.5f);
                    }

                    if (tick == 101)
                    {
                        Assert.True(world.RemoveBody(body!));
                        Assert.False(world.RemoveBody(body!));
                        body = null;
                    }
                    if (tick == 102)
                    {
                        body = Circle(world, Vector2.Zero);
                    }
                }
                world.Step();
                JsonElement expectedProxies = row.GetProperty("Proxies");
                IReadOnlyList<PhysicsProxyState> actualProxies = world.CaptureProxies();
                Assert.Equal(expectedProxies.GetArrayLength(), actualProxies.Count);
                for (int i = 0; i < actualProxies.Count; i++)
                {
                    JsonElement p = expectedProxies[i];
                    Assert.Equal(p[0].GetInt32(), actualProxies[i].Fixture);
                    Assert.Equal(p[1].GetInt32(), actualProxies[i].ProxyId);
                    PhysicsAabb box = actualProxies[i].Bounds;
                    float[] values = [box.Lower.X, box.Lower.Y, box.Upper.X, box.Upper.Y];
                    for (int component = 0; component < values.Length; component++)
                    {
                        float expectedBound = p[component + 2].GetSingle();
                        Assert.True(float.IsFinite(expectedBound) && float.IsFinite(values[component]) && MathF.Abs(expectedBound - values[component]) <= .000002f,
                            $"{scenario}, tick {tick}, proxy {actualProxies[i].ProxyId}, bound {component}: expected {expectedBound}, actual {values[component]}");
                    }
                }
                PhysicsContactState[] expected = [.. row.GetProperty("Contacts").EnumerateArray().Select(c =>
                    new PhysicsContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(expected.SequenceEqual(world.CaptureTerrainContacts()), $"{scenario}, tick {tick}: contact identity/order/touch mismatch");
                float[] actual = body == null ? [] : [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                JsonElement state = row.GetProperty("State");
                Assert.Equal(state.GetArrayLength(), actual.Length);
                for (int i = 0; i < actual.Length; i++)
                {
                    float expectedValue = state[i].GetSingle();
                    Assert.True(float.IsFinite(expectedValue) && float.IsFinite(actual[i]) && MathF.Abs(expectedValue - actual[i]) <= .00002f,
                        $"{scenario}, tick {tick}, component {i}: expected {expectedValue}, actual {actual[i]}");
                }
            }
            Assert.Equal(scenario == "bounce" ? 720 : 240, tick);
        }

        /// <summary>Old diagnostics remain immutable when the world changes ownership.</summary>
        [Fact]
        public void CapturedContactsSurviveRemovalWithoutRetainingWorldCandidates()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Ground(world, 0, 0, 1, 1);
            PhysicsBody body = Circle(world, new(-.15f, -.15f));
            world.Step();
            IReadOnlyList<PhysicsContactState> snapshot = world.CaptureTerrainContacts();
            _ = Assert.Single(snapshot);
            Assert.False(snapshot[0].Touching);
            Assert.False(new PhysicsWorld().RemoveBody(body));
            Assert.True(world.RemoveBody(body));
            Assert.Empty(world.CaptureTerrainContacts());
            _ = Assert.Single(snapshot);
        }

        /// <summary>Removing an unstepped fixture cannot leave a stale move referring to its reused node.</summary>
        [Fact]
        public void RemovingBeforeFirstStepAllowsSafeProxyReuse()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Ground(world, 0, 0, 1, 1);
            PhysicsBody removed = Circle(world, new(-.15f, -.15f));
            Assert.True(world.RemoveBody(removed));
            _ = Circle(world, new(-.15f, -.15f));
            world.Step();
            Assert.Equal([new PhysicsContactState(3, 1, 1, false)], world.CaptureTerrainContacts());
        }

        /// <summary>Sensor changes suppress solid solving without dropping a still-overlapping candidate.</summary>
        [Fact]
        public void SensorChangesRetainCandidateIdentityWithoutSolidImpulses()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Ground(world, 0, 0, 1, 1);
            PhysicsBody body = Circle(world, new(.5f, -.15f));
            world.Step();
            PhysicsContactState original = Assert.Single(world.CaptureTerrainContacts());
            Assert.True(original.Touching);
            body.IsSensor = true;
            world.Step();
            Assert.Empty(world.CaptureTerrainContacts());
            Assert.True(body.Velocity.Y > 0);
            body.IsSensor = false;
            world.Step();
            Assert.Equal(original, Assert.Single(world.CaptureTerrainContacts()));
            Assert.InRange(MathF.Abs(body.Velocity.Y), 0, .000001f);
            body.IsSensor = true;
            world.Step();
            Assert.Empty(world.CaptureTerrainContacts());
            Assert.True(body.Velocity.Y > 0);
        }

        /// <summary>Invalid bounds fail before assigning labels or exposing a new body.</summary>
        [Fact]
        public void RejectedCircleBoundsPreserveFixtureLabels()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Ground(world, 0, 0, 1, 1);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Circle(world, new(1000000, 0)));
            _ = Circle(world, new(-.15f, -.15f));
            world.Step();
            Assert.Equal([new PhysicsContactState(2, 1, 1, false)], world.CaptureTerrainContacts());
        }

        private static PhysicsBody Circle(PhysicsWorld world, Vector2 position)
        {
            PhysicsBody body = world.AddCircle(position, .16f, .5f, .7f);
            body.Friction = .3f;
            body.AngularDamping = .1f;
            return body;
        }

        private static void Ground(PhysicsWorld world, float x1, float y1, float x2, float y2, float restitution = 0)
        {
            world.AddTerrain([new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)], .3f, restitution);
        }
    }
}
