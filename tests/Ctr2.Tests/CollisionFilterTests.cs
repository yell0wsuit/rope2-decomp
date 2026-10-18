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
    /// <summary>Independent native category/mask/group and refilter timing verification.</summary>
    public sealed class CollisionFilterTests
    {
        /// <summary>Compares every completed native tick, including contact rediscovery delays.</summary>
        /// <param name="scenario">Synthetic native filtering scene.</param>
        [Theory]
        [InlineData("mask")]
        [InlineData("positive")]
        [InlineData("negative")]
        [InlineData("asymmetric")]
        [InlineData("terrain")]
        [InlineData("compound")]
        [InlineData("joint")]
        [InlineData("early")]
        public void MatchesNativeFilterTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            Dictionary<int, PhysicsBody> bodies = [];
            PhysicsJoint? joint = null;
            if (scenario == "terrain")
            {
                world.AddTerrain([new(-3, .2f), new(3, .2f), new(3, 1), new(-3, 1)], .3f, 0);
                bodies.Add(2, world.AddCircle(Vector2.Zero, .3f, 1, 1));
            }
            else if (scenario == "compound")
            {
                ConvexPolygon shape = new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]);
                bodies.Add(1, world.AddPolygon(shape, Vector2.Zero, 1, 0));
                bodies.Add(3, world.AddPolygon(shape, new(.5f, 0), 1, 0));
            }
            else
            {
                bodies.Add(1, world.AddCircle(Vector2.Zero, .3f, 1, 0));
                bodies.Add(2, world.AddCircle(new(.5f, 0), .3f, 1, 0));
            }
            foreach (PhysicsBody body in bodies.Values)
            {
                body.Friction = scenario == "compound" ? .4f : .3f;
            }

            int[] fixtures = [.. world.CaptureProxies().Select(p => p.Fixture)];
            void Set(int label, ushort category, ushort mask, short group)
            {
                world.SetFixtureFilter(label, new(category, mask, group));
            }

            void All(ushort mask, short group = 0)
            {
                foreach (int fixture in fixtures)
                {
                    Set(fixture, 1, mask, group);
                }
            }
            All(0);
            if (scenario is "positive" or "negative")
            {
                All(scenario == "negative" ? ushort.MaxValue : (ushort)0, scenario == "negative" ? (short)-7 : (short)7);
            }

            if (scenario == "asymmetric")
            {
                Set(1, 2, 4, 0);
                Set(2, 4, 0, 0);
            }
            if (scenario == "joint")
            {
                joint = world.AddRope(bodies[1], bodies[2], 10);
            }

            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "CollisionFilterReference.json")));
            int tick = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (tick == 10)
                {
                    if (scenario == "compound")
                    {
                        Set(1, 1, ushort.MaxValue, 0);
                        Set(3, 1, ushort.MaxValue, 0);
                    }
                    else if (scenario == "asymmetric")
                    {
                        Set(2, 4, 2, 0);
                    }
                    else
                    {
                        All(ushort.MaxValue);
                    }

                    if (scenario == "early")
                    {
                        PhysicsBody extra = world.AddCircle(new(10, 0), .1f, 0, 0);
                        extra.Friction = .3f;
                        bodies.Add(3, extra);
                        fixtures = [.. world.CaptureProxies().Select(p => p.Fixture)];
                    }
                }
                if (tick == 20)
                {
                    foreach (int fixture in fixtures)
                    {
                        world.SetFixtureFilter(fixture, world.GetFixtureFilter(fixture));
                    }
                }

                if (tick == 25 && joint != null)
                {
                    Assert.True(world.Remove(joint));
                }

                if (tick == 30)
                {
                    All(0);
                }

                if (tick == 40)
                {
                    All(ushort.MaxValue);
                }

                if (tick == 60)
                {
                    All(ushort.MaxValue, -5);
                }

                if (tick == 70)
                {
                    All(0, 5);
                }

                world.Step();
                PhysicsBodyContactState[] pairs = [.. row.GetProperty("BodyContacts").EnumerateArray().Select(c =>
                    new PhysicsBodyContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(pairs.SequenceEqual(world.CaptureBodyContacts()), $"{scenario}, tick {tick}: body candidates differ");
                PhysicsContactState[] terrain = [.. row.GetProperty("Terrain").EnumerateArray().Select(c =>
                    new PhysicsContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean()))];
                Assert.True(terrain.SequenceEqual(world.CaptureTerrainContacts()), $"{scenario}, tick {tick}: terrain candidates differ");
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    int label = state[0].GetInt32();
                    PhysicsBody body = bodies[label];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 1].GetSingle(), actual[i], .00002f, $"{scenario}, tick {tick}, body {label}, component {i}");
                    }
                }
                IReadOnlyList<PhysicsProxyState> proxies = world.CaptureProxies();
                Assert.Equal(proxies.Count, row.GetProperty("Proxies").GetArrayLength());
                int proxyIndex = 0;
                foreach (JsonElement expected in row.GetProperty("Proxies").EnumerateArray())
                {
                    PhysicsProxyState proxy = proxies[proxyIndex++];
                    Assert.Equal(expected[0].GetInt32(), proxy.Fixture);
                    Assert.Equal(expected[1].GetInt32(), proxy.ProxyId);
                    float[] bounds = [proxy.Bounds.Lower.X, proxy.Bounds.Lower.Y, proxy.Bounds.Upper.X, proxy.Bounds.Upper.Y];
                    for (int i = 0; i < 4; i++)
                    {
                        Near(expected[i + 2].GetSingle(), bounds[i], .000001f, $"{scenario}, tick {tick}, proxy {proxy.Fixture}, bound {i}");
                    }
                }
            }
            Assert.Equal(90, tick);
        }

        /// <summary>Signed groups override masks only when both nonzero groups match.</summary>
        /// <param name="categoryA">First category.</param>
        /// <param name="maskA">First accepted categories.</param>
        /// <param name="groupA">First signed group.</param>
        /// <param name="categoryB">Second category.</param>
        /// <param name="maskB">Second accepted categories.</param>
        /// <param name="groupB">Second signed group.</param>
        /// <param name="allowed">Expected candidate acceptance.</param>
        [Theory]
        [InlineData(1, 65535, 0, 1, 65535, 0, true)]
        [InlineData(1, 0, 0, 1, 65535, 0, false)]
        [InlineData(1, 65535, 0, 1, 0, 0, false)]
        [InlineData(0, 65535, 0, 1, 65535, 0, false)]
        [InlineData(32768, 2, 0, 2, 32768, 0, true)]
        [InlineData(32768, 1, 0, 2, 32768, 0, false)]
        [InlineData(0, 0, 32767, 0, 0, 32767, true)]
        [InlineData(1, 65535, -32768, 1, 65535, -32768, false)]
        [InlineData(1, 65535, 1, 1, 65535, -1, true)]
        [InlineData(1, 0, 1, 1, 0, 2, false)]
        [InlineData(1, 65535, -1, 1, 65535, -2, true)]
        public void CategoryMaskAndSignedGroupBoundaries(int categoryA, int maskA, int groupA,
            int categoryB, int maskB, int groupB, bool allowed)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            _ = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            _ = world.AddCircle(new(.5f, 0), .3f, 1, 0);
            world.SetFixtureFilter(1, new((ushort)categoryA, (ushort)maskA, (short)groupA));
            world.SetFixtureFilter(2, new((ushort)categoryB, (ushort)maskB, (short)groupB));
            world.Step();
            Assert.Equal(allowed, world.CaptureBodyContacts().Any(c => c.Touching));
        }

        /// <summary>Positive groups never override body-level static or joint exclusions.</summary>
        /// <param name="staticBodies">Whether both bodies are static rather than joint-connected.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GroupsDoNotOverrideBodyExclusions(bool staticBodies)
        {
            PhysicsWorld world = new();
            PhysicsBody a = world.AddCircle(Vector2.Zero, .3f, staticBodies ? 0 : 1, 0);
            PhysicsBody b = world.AddCircle(new(.5f, 0), .3f, staticBodies ? 0 : 1, 0);
            if (!staticBodies)
            {
                _ = world.AddRope(a, b, 10);
            }

            world.SetFixtureFilter(1, new(1, 0, 7));
            world.SetFixtureFilter(2, new(1, 0, 7));
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
        }

        /// <summary>A new-fixture pass can consume refilter touches before this step's Collide.</summary>
        /// <param name="newFixture">Whether an unrelated fixture triggers early discovery.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void EnablingUsesNativeDiscoveryPhase(bool newFixture)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            _ = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            _ = world.AddCircle(new(.5f, 0), .3f, 1, 0);
            world.SetFixtureFilter(1, new(1, 0, 0));
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
            world.SetFixtureFilter(1, PhysicsCollisionFilter.Default);
            Assert.Empty(world.CaptureBodyContacts());
            if (newFixture)
            {
                _ = world.AddCircle(new(10, 0), .1f, 0, 0);
            }

            world.Step();
            Assert.Equal(newFixture, Assert.Single(world.CaptureBodyContacts()).Touching);
            world.Step();
            Assert.True(Assert.Single(world.CaptureBodyContacts()).Touching);
            world.SetFixtureFilter(1, new(1, 0, 0));
            Assert.True(Assert.Single(world.CaptureBodyContacts()).Touching);
            world.Step();
            Assert.Empty(world.CaptureBodyContacts());
        }

        /// <summary>Default values and fixture ownership survive repeated setters and body retirement.</summary>
        [Fact]
        public void FixtureOwnershipAndRepeatedTouchesAreBounded()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            Assert.Equal(new PhysicsCollisionFilter(1, ushort.MaxValue, 0), world.GetFixtureFilter(1));
            for (int i = 0; i < 100; i++)
            {
                world.SetFixtureFilter(1, PhysicsCollisionFilter.Default);
            }

            Assert.True(world.RemoveBody(body));
            _ = Assert.Throws<KeyNotFoundException>(() => world.GetFixtureFilter(1));
            _ = Assert.Throws<KeyNotFoundException>(() => world.SetFixtureFilter(1, PhysicsCollisionFilter.Default));
            world.Step();
            PhysicsBody replacement = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            Assert.Equal(PhysicsCollisionFilter.Default, world.GetFixtureFilter(2));
            world.Step();
            world.SetFixtureFilter(2, new(8, 4, -2));
            world.SetFixtureFilter(2, new(8, 4, -2));
            Assert.True(world.RemoveBody(replacement));
            world.Step();
            Assert.Empty(world.CaptureProxies());
        }

        /// <summary>Excluded terrain is ignored by native continuous sweeps.</summary>
        /// <param name="filterBody">Whether the rejection is set on the circle.</param>
        /// <param name="excludedY">Excluded terrain height, including a nearby secondary manifold.</param>
        [Theory]
        [InlineData(false, .6f)]
        [InlineData(false, .99f)]
        [InlineData(true, .6f)]
        public void ContinuousTerrainSweepsRespectFilters(bool filterBody, float excludedY)
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, excludedY), new(2, excludedY), new(2, excludedY + .02f), new(-2, excludedY + .02f)], 0, .9f);
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.SetFixtureFilter(filterBody ? 3 : 1, new(1, 0, 0));
            world.Step();
            if (filterBody)
            {
                Assert.True(body.Position.Y > 1.5f);
            }
            else
            {
                Assert.InRange(body.Position.Y, .83f, .85f);
                Assert.Equal(0, body.Velocity.Y, 5);
            }
        }

        private static void Near(float expected, float actual, float tolerance, string location)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= tolerance,
                $"{location}: expected {expected}, actual {actual}");
        }
    }
}
