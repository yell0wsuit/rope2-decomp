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
    /// <summary>Independent native sensor flags, mixed-fixture solving and terrain lifecycle verification.</summary>
    public sealed class FixtureSensorTests
    {
        /// <summary>Matches every native completed tick and fixture-specific setter sequence.</summary>
        /// <param name="scenario">Synthetic native mixed sensor scene.</param>
        [Theory]
        [InlineData("compound-circle")]
        [InlineData("compound-polygon")]
        [InlineData("terrain-init")]
        [InlineData("terrain-toggle")]
        [InlineData("terrain-compound")]
        [InlineData("terrain-after")]
        [InlineData("two-terrains")]
        [InlineData("circle-static")]
        public void MatchesNativeFixtureSensorTrace(string scenario)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            Dictionary<int, PhysicsBody> bodies = [];
            Dictionary<PhysicsBody, int[]> fixtures = [];
            int label = 0;
            PhysicsBody Register(PhysicsBody body, float friction)
            {
                body.Friction = friction;
                int tag = label + 1;
                int count = Math.Max(1, body.PolygonFixtures.Count);
                fixtures.Add(body, Enumerable.Range(tag, count).ToArray());
                label += count;
                bodies.Add(tag, body);
                return body;
            }
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity) =>
                Register(world.AddCircle(new(x, y), radius, density, gravity), .3f);
            PhysicsBody Box(float x, float y, float gravity, bool compound = true) =>
                Register(world.AddPolygon(new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]), new(x, y), 1, gravity, compound: compound), .4f);
            int Floor(float y, float bottom, bool sensor)
            {
                world.AddTerrain([new(-2, y), new(2, y), new(2, bottom), new(-2, bottom)], .3f, 0, sensor);
                return ++label;
            }
            void Mask(int fixture, ushort mask) => world.SetFixtureFilter(fixture, new(1, mask, 0));
            void Remove(PhysicsBody body)
            {
                int tag = bodies.Single(b => b.Value == body).Key;
                Assert.True(world.RemoveBody(body));
                bodies.Remove(tag);
                fixtures.Remove(body);
            }
            PhysicsBody first, second = null!;
            int ground = 0;
            PhysicsJoint? joint = null;
            if (scenario == "terrain-after")
            {
                first = Box(0, -.1f, 0);
                ground = Floor(0, 1, true);
            }
            else if (scenario == "two-terrains")
            {
                ground = Floor(.4f, .42f, true);
                Floor(1, 1.02f, false);
                first = Circle(0, 0, .15f, 1, 1);
            }
            else if (scenario == "circle-static")
            {
                second = Circle(0, 0, .3f, 0, 0);
                second.IsSensor = true;
                first = Circle(-.5f, 0, .3f, 1, 0);
            }
            else if (scenario is "compound-circle" or "compound-polygon")
            {
                first = Box(0, 0, 0);
                second = scenario == "compound-circle" ? Circle(.1f, .05f, .25f, 0, 0) : Box(.45f, 0, 0, false);
            }
            else
            {
                ground = Floor(0, 1, scenario == "terrain-init");
                first = scenario == "terrain-compound" ? Box(0, -.1f, 1) : Circle(0, scenario == "terrain-init" ? -.8f : -.1f, .15f, 1, 1);
            }
            int firstLabel = fixtures[first][0];
            if (scenario is "compound-circle" or "compound-polygon" or "terrain-compound" or "terrain-after")
                world.SetFixtureSensor(firstLabel, true);
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FixtureSensorReference.json")));
            int tick = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario is "compound-circle" or "compound-polygon")
                {
                    if (tick == 10)
                    {
                        world.SetFixtureSensor(firstLabel, false);
                        world.SetFixtureSensor(firstLabel + 1, true);
                    }
                    if (tick == 20)
                        joint = world.AddRope(first, second, 10);
                    if (tick == 30)
                        Assert.True(world.Remove(joint!));
                    if (tick == 35)
                        world.SetFixtureFilter(firstLabel, world.GetFixtureFilter(firstLabel));
                    if (tick == 45)
                        first.IsSensor = true;
                    if (tick == 55)
                        first.IsSensor = false;
                    if (tick == 65)
                    {
                        Remove(first);
                        first = Box(0, 0, 0);
                        firstLabel = fixtures[first][0];
                        world.SetFixtureSensor(firstLabel, true);
                    }
                }
                if (scenario == "terrain-toggle")
                {
                    if (tick is 10 or 30)
                        world.SetFixtureSensor(ground, true);
                    if (tick is 20 or 40)
                        world.SetFixtureSensor(ground, false);
                    if (tick == 50)
                        Mask(ground, 0);
                    if (tick == 60)
                        Mask(ground, ushort.MaxValue);
                    if (tick == 65)
                    {
                        world.SetFixtureSensor(ground, true);
                        Remove(first);
                        first = Circle(0, -.1f, .15f, 1, 1);
                        firstLabel = fixtures[first][0];
                    }
                    if (tick == 75)
                        world.SetFixtureSensor(ground, false);
                }
                if (scenario == "terrain-compound")
                {
                    if (tick == 10)
                        world.SetFixtureSensor(firstLabel + 1, true);
                    if (tick == 20)
                        world.SetFixtureSensor(ground, true);
                    if (tick == 30)
                        first.IsSensor = false;
                    if (tick == 40)
                        world.SetFixtureSensor(ground, false);
                    if (tick == 65)
                    {
                        Remove(first);
                        first = Box(0, -.1f, 1);
                        firstLabel = fixtures[first][0];
                        world.SetFixtureSensor(firstLabel, true);
                    }
                }
                if (scenario == "terrain-after")
                {
                    if (tick is 10 or 60)
                        world.SetFixtureSensor(ground, false);
                    if (tick == 20)
                        world.SetFixtureSensor(firstLabel, false);
                    if (tick == 30)
                        world.SetFixtureSensor(ground, true);
                    if (tick == 40)
                        first.IsSensor = true;
                    if (tick == 50)
                        world.SetFixtureSensor(firstLabel, false);
                }
                if (scenario == "two-terrains")
                {
                    if (tick == 30)
                        world.SetFixtureSensor(1, false);
                    if (tick == 70)
                        first.IsSensor = true;
                }
                if (scenario == "circle-static")
                {
                    if (tick is 10 or 40)
                        second.IsSensor = false;
                    if (tick == 20)
                        second.IsSensor = true;
                    if (tick == 30)
                        first.IsSensor = true;
                    if (tick == 50)
                        first.IsSensor = false;
                    if (tick == 60)
                    {
                        second.IsSensor = true;
                        Remove(first);
                    }
                }
                world.Step();
                PhysicsContactEvent[] events = [.. row.GetProperty("Events").EnumerateArray().Select(e => new PhysicsContactEvent(
                    (PhysicsContactEventKind)e[0].GetInt32(), (PhysicsContactEventPhase)e[1].GetInt32(), e[2].GetInt32(), e[3].GetInt32(), e[4].GetInt64(), e[5].GetBoolean(), e[6].GetBoolean()))];
                Assert.True(events.SequenceEqual(world.DrainContactEvents()), $"{scenario}, tick {tick}: events differ");
                PhysicsFixtureContactState[] contacts = [.. row.GetProperty("Contacts").EnumerateArray().Select(c =>
                    new PhysicsFixtureContactState(c[0].GetInt32(), c[1].GetInt32(), c[2].GetInt64(), c[3].GetBoolean(), c[4].GetBoolean(), c[5].GetInt32()))];
                Assert.True(contacts.SequenceEqual(world.CaptureFixtureContacts()), $"{scenario}, tick {tick}: candidates differ");
                Assert.Equal(bodies.Count, row.GetProperty("Bodies").GetArrayLength());
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                        Near(state[i + 1].GetSingle(), actual[i], .00002f, $"{scenario}, tick {tick}, body {state[0]}, component {i}");
                    Assert.Equal(state[7].GetBoolean(), body.IsSensor);
                    Near(state[8].GetSingle(), body.Mass, .00002f, $"{scenario}, tick {tick}, mass");
                }
                var proxies = world.CaptureProxies();
                Assert.Equal(proxies.Count, row.GetProperty("Flags").GetArrayLength());
                foreach (JsonElement flag in row.GetProperty("Flags").EnumerateArray())
                    Assert.Equal(flag[1].GetBoolean(), world.GetFixtureSensor(flag[0].GetInt32()));
                Assert.Equal(proxies.Count, row.GetProperty("Proxies").GetArrayLength());
                int proxyIndex = 0;
                foreach (JsonElement expected in row.GetProperty("Proxies").EnumerateArray())
                {
                    PhysicsProxyState proxy = proxies[proxyIndex++];
                    Assert.Equal(expected[0].GetInt32(), proxy.Fixture);
                    Assert.Equal(expected[1].GetInt32(), proxy.ProxyId);
                    float[] bounds = [proxy.Bounds.Lower.X, proxy.Bounds.Lower.Y, proxy.Bounds.Upper.X, proxy.Bounds.Upper.Y];
                    for (int i = 0; i < 4; i++)
                        Near(expected[i + 2].GetSingle(), bounds[i], .000002f, $"{scenario}, tick {tick}, proxy {proxy.Fixture}, bound {i}");
                }
            }
            Assert.Equal(90, tick);
        }
        /// <summary>Body assignment normalizes mixed flags even when its aggregate getter already reads false.</summary>
        [Fact]
        public void MixedBodyBroadcastAndRetiredOwnershipAreCoherent()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddPolygon(new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]), Vector2.Zero, 1, 0);
            float mass = body.Mass;
            var proxies = world.CaptureProxies();
            world.SetFixtureSensor(1, true);
            Assert.False(body.IsSensor);
            Assert.True(world.GetFixtureSensor(1));
            Assert.False(world.GetFixtureSensor(2));
            body.IsSensor = false;
            Assert.False(world.GetFixtureSensor(1));
            Assert.False(world.GetFixtureSensor(2));
            body.IsSensor = true;
            world.SetFixtureSensor(1, false);
            Assert.False(body.IsSensor);
            Assert.True(world.GetFixtureSensor(2));
            body.IsSensor = true;
            Assert.True(world.GetFixtureSensor(1) && world.GetFixtureSensor(2));
            for (int i = 0; i < 100; i++)
                world.SetFixtureSensor(1, true);
            Assert.Equal(mass, body.Mass);
            Assert.True(proxies.SequenceEqual(world.CaptureProxies()));
            Assert.Throws<KeyNotFoundException>(() => world.SetFixtureSensor(0, true));
            Assert.True(world.RemoveBody(body));
            Assert.Throws<KeyNotFoundException>(() => world.GetFixtureSensor(1));
            Assert.Throws<KeyNotFoundException>(() => world.SetFixtureSensor(2, false));
            body.IsSensor = false;
            Assert.False(body.IsSensor);
            body.IsSensor = true;
            Assert.True(body.IsSensor);
            PhysicsBody next = world.AddCircle(Vector2.Zero, .2f, 1, 0);
            Assert.False(next.IsSensor);
            Assert.False(world.GetFixtureSensor(3));
            world.Step();
        }

        /// <summary>Terrain setters preserve stored contact state, proxy bounds and queued event snapshots.</summary>
        [Fact]
        public void TerrainSetterIsDeferredAndPreservesSnapshots()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            world.AddTerrain([new(-2, 0), new(2, 0), new(2, 1), new(-2, 1)], .3f, 0);
            world.AddCircle(new(0, -.1f), .15f, 1, 0);
            world.Step();
            var begin = world.DrainContactEvents();
            var before = world.CaptureFixtureContacts();
            var proxies = world.CaptureProxies();
            Assert.False(Assert.Single(before).IsSensor);
            Assert.Equal(1, Assert.Single(before).ManifoldPoints);
            world.SetFixtureSensor(1, true);
            PhysicsFixtureContactState pending = Assert.Single(world.CaptureFixtureContacts());
            Assert.True(pending.IsSensor && pending.Touching);
            Assert.Equal(1, pending.ManifoldPoints);
            Assert.Empty(world.DrainContactEvents());
            Assert.True(proxies.SequenceEqual(world.CaptureProxies()));
            world.Step();
            Assert.Equal(0, Assert.Single(world.CaptureFixtureContacts()).ManifoldPoints);
            Assert.Empty(world.CaptureTerrainContacts());
            Assert.Empty(world.DrainContactEvents());
            world.SetFixtureSensor(1, false);
            world.Step();
            Assert.Equal(1, Assert.Single(world.CaptureFixtureContacts()).ManifoldPoints);
            Assert.Empty(world.DrainContactEvents());
            Assert.False(Assert.Single(begin).IsSensor);
            Assert.False(Assert.Single(before).IsSensor);
        }

        /// <summary>Sensor changes cannot rediscover a pair whose blocking joint was removed without touching proxies.</summary>
        [Fact]
        public void SensorSettersDoNotTouchOrRefilterCandidates()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            PhysicsBody first = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            PhysicsBody second = world.AddCircle(new(.5f, 0), .3f, 1, 0);
            PhysicsJoint joint = world.AddRope(first, second, 10);
            world.Step();
            Assert.Empty(world.CaptureFixtureContacts());
            Assert.True(world.Remove(joint));
            world.SetFixtureSensor(1, true);
            world.Step();
            world.SetFixtureSensor(1, true);
            world.Step();
            Assert.Empty(world.CaptureFixtureContacts());
            Assert.Empty(world.DrainContactEvents());
            world.SetFixtureFilter(1, PhysicsCollisionFilter.Default);
            world.Step();
            Assert.False(Assert.Single(world.CaptureFixtureContacts()).Touching);
            world.Step();
            Assert.True(Assert.Single(world.DrainContactEvents()).IsSensor);
        }

        /// <summary>Both primary and nearby extra analytical contacts exclude terrain sensors.</summary>
        /// <param name="initial">Whether the first terrain starts as a sensor or is changed before stepping.</param>
        /// <param name="height">Excluded terrain height, including a nearby secondary manifold.</param>
        [Theory]
        [InlineData(false, .6f)]
        [InlineData(true, .6f)]
        [InlineData(false, .99f)]
        [InlineData(true, .99f)]
        public void AnalyticalSweepsExcludeTerrainSensors(bool initial, float height)
        {
            PhysicsWorld world = new()
            {
                RecordContactEvents = true
            };
            world.AddTerrain([new(-2, height), new(2, height), new(2, height + .02f), new(-2, height + .02f)], 0, .9f, initial);
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            if (!initial)
                world.SetFixtureSensor(1, true);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.Step();
            Assert.InRange(body.Position.Y, .83f, .85f);
            Assert.Equal(0, body.Velocity.Y, 5);
            Assert.Empty(world.DrainContactEvents());
        }

        /// <summary>A circle's individual flag excludes its own swept solid solving.</summary>
        [Fact]
        public void CircleFixtureSensorDisablesAnalyticalSweeps()
        {
            PhysicsWorld world = new();
            world.AddTerrain([new(-2, 1), new(2, 1), new(2, 1.02f), new(-2, 1.02f)], 0, 0);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .16f, .5f, 800);
            world.SetFixtureSensor(2, true);
            world.Step();
            Assert.True(body.Position.Y > 1.5f);
            Assert.True(body.IsSensor);
        }

        /// <summary>Solid diagnostics exclude only the sensor fixture in a mixed pair set.</summary>
        /// <param name="terrain">Whether the independent fixtures belong to static terrain instead of a compound body.</param>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void MixedDiagnosticsExcludeOnlyTheSensorFixture(bool terrain)
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            if (terrain)
            {
                world.AddTerrain([new(-2, 0), new(2, 0), new(2, 1), new(-2, 1)], .3f, 0, true);
                world.AddTerrain([new(-2, 0), new(2, 0), new(2, 1), new(-2, 1)], .3f, 0);
                world.AddCircle(new(0, -.1f), .15f, 1, 0);
            }
            else
            {
                world.AddPolygon(new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]), Vector2.Zero, 1, 0);
                world.AddCircle(new(.1f, .05f), .25f, 0, 0);
                world.SetFixtureSensor(1, true);
            }
            world.Step();
            Assert.Equal(2, world.CaptureFixtureContacts().Count);
            Assert.Equal(1, world.CaptureFixtureContacts().Count(c => c.IsSensor));
            if (terrain)
                Assert.Equal(2, Assert.Single(world.CaptureTerrainContacts()).TerrainFixture);
            else
            {
                Assert.Equal(2, Assert.Single(world.CaptureBodyContacts()).FirstFixture);
                Assert.Equal(2, Assert.Single(world.CaptureBodyContactFeatures()).FirstFixture);
            }
        }

        private static void Near(float expected, float actual, float tolerance, string location) =>
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= tolerance,
                $"{location}: expected {expected}, actual {actual}");
    }
}
