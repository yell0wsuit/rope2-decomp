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
    /// <summary>Independent native sensor touching and ordered Begin/End snapshots.</summary>
    public sealed class SensorContactTests
    {
        /// <summary>Compares every native discrete tick, including setter and destruction events before Step.</summary>
        /// <param name="scenario">Synthetic native world and lifecycle sequence.</param>
        [Theory]
        [InlineData("circle-pass")]
        [InlineData("mixed")]
        [InlineData("reverse")]
        [InlineData("compound")]
        [InlineData("polygon-pair")]
        [InlineData("terrain")]
        [InlineData("terrain-poly")]
        [InlineData("terrain-after")]
        [InlineData("sensor-pair")]
        [InlineData("toggle")]
        [InlineData("joint")]
        [InlineData("interleaved")]
        [InlineData("skin-gap")]
        public void MatchesNativeSensorTrace(string scenario)
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
                fixtures.Add(body, [.. Enumerable.Range(tag, Math.Max(1, body.PolygonFixtures.Count))]);
                label += Math.Max(1, body.PolygonFixtures.Count);
                bodies.Add(tag, body);
                return body;
            }
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity), .3f);
            }

            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, bool compound = false)
            {
                return Register(world.AddPolygon(new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]), new(x, y), 1, gravity, compound: compound), .4f);
            }

            void Floor()
            {
                world.AddTerrain([new(-2, 0), new(2, 0), new(2, 1), new(-2, 1)], .3f, 0);
                label++;
            }
            void Mask(PhysicsBody body, ushort value)
            {
                foreach (int fixture in fixtures[body])
                {
                    world.SetFixtureFilter(fixture, new(1, value, 0));
                }
            }
            void Remove(PhysicsBody body)
            {
                int tag = bodies.Single(b => b.Value == body).Key;
                Assert.True(world.RemoveBody(body));
                _ = bodies.Remove(tag);
                _ = fixtures.Remove(body);
            }
            PhysicsBody first, second = null!, extra = null!;
            PhysicsJoint? joint = null;
            if (scenario is "terrain" or "terrain-poly" or "interleaved")
            {
                Floor();
            }

            if (scenario == "circle-pass")
            {
                second = Circle(0, 0, .35f, 0, 0);
                first = Circle(0, -.9f, .15f, 1, 1);
            }
            else if (scenario == "terrain")
            {
                first = Circle(0, -.9f, .15f, 1, 1);
            }
            else if (scenario == "terrain-poly")
            {
                first = Box(0, -.9f, .25f, .2f, 1, true);
            }
            else if (scenario == "terrain-after")
            {
                first = Box(0, -.05f, .25f, .2f, 0);
                Floor();
            }
            else if (scenario == "skin-gap")
            {
                first = Box(0, 0, .3f, .2f, 0);
                float d = (.102f + .0000006f) * .70710678f;
                second = Circle(.3f + d, .2f + d, .1f, 0, 0);
            }
            else if (scenario == "interleaved")
            {
                first = Circle(0, -.05f, .2f, 1, 0);
                second = Circle(.25f, -.05f, .2f, 1, 0);
            }
            else if (scenario == "polygon-pair")
            {
                second = Box(0, 0, .3f, .2f, 0);
                first = Box(0, -.9f, .25f, .2f, 1);
            }
            else if (scenario is "mixed" or "reverse" or "compound")
            {
                if (scenario == "reverse")
                {
                    second = Circle(0, 0, .3f, 0, 0);
                }

                first = Box(0, -.9f, .25f, .2f, 1, scenario == "compound");
                if (scenario != "reverse")
                {
                    second = Circle(0, 0, .3f, 0, 0);
                }
            }
            else
            {
                first = Circle(0, 0, .3f, 1, 0);
                second = Circle(.5f, 0, .3f, 1, 0);
            }
            first.IsSensor = true;
            if (scenario == "sensor-pair")
            {
                second.IsSensor = true;
            }

            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SensorEventsReference.json")));
            int tick = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario))
            {
                Assert.Equal(++tick, row.GetProperty("Tick").GetInt32());
                if (scenario == "skin-gap")
                {
                    if (tick == 10)
                    {
                        first.IsSensor = false;
                    }

                    if (tick == 20)
                    {
                        first.IsSensor = true;
                    }

                    if (tick == 30)
                    {
                        first.IsSensor = false;
                        first.IsSensor = true;
                    }
                    if (tick == 40)
                    {
                        first.IsSensor = false;
                        Remove(first);
                    }
                }
                if (scenario == "toggle")
                {
                    if (tick == 10)
                    {
                        first.IsSensor = false;
                    }

                    if (tick == 20)
                    {
                        first.IsSensor = true;
                    }

                    if (tick == 30)
                    {
                        Mask(first, 0);
                    }

                    if (tick == 40)
                    {
                        Mask(first, ushort.MaxValue);
                    }

                    if (tick == 50)
                    {
                        first.IsSensor = false;
                        Remove(first);
                        first = Circle(0, 0, .3f, 1, 0);
                        first.IsSensor = true;
                    }
                }
                if (scenario == "sensor-pair")
                {
                    if (tick == 10)
                    {
                        first.IsSensor = false;
                    }

                    if (tick == 20)
                    {
                        second.IsSensor = false;
                    }

                    if (tick == 30)
                    {
                        first.IsSensor = true;
                        second.IsSensor = true;
                    }
                    if (tick == 40)
                    {
                        Mask(first, 0);
                    }

                    if (tick == 50)
                    {
                        Mask(first, ushort.MaxValue);
                    }
                }
                if (scenario == "joint")
                {
                    if (tick == 10)
                    {
                        joint = world.AddRope(first, second, 10);
                    }

                    if (tick == 20)
                    {
                        Assert.True(world.Remove(joint!));
                    }

                    if (tick == 30)
                    {
                        Mask(first, ushort.MaxValue);
                    }

                    if (tick == 40)
                    {
                        Remove(first);
                        first = Circle(0, 0, .3f, 1, 0);
                        first.IsSensor = true;
                    }
                }
                if (scenario == "interleaved")
                {
                    if (tick == 10)
                    {
                        extra = Box(0, -.05f, .3f, .1f, 0);
                        extra.IsSensor = true;
                    }
                    if (tick == 20)
                    {
                        Remove(first);
                    }

                    if (tick == 30)
                    {
                        Mask(extra, 0);
                    }
                }
                if (scenario == "terrain-after" && tick == 20)
                {
                    Mask(first, 0);
                }

                if (scenario == "terrain-after" && tick == 30)
                {
                    Mask(first, ushort.MaxValue);
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
                    {
                        float expected = state[i + 1].GetSingle();
                        Assert.True(float.IsFinite(expected) && float.IsFinite(actual[i]) && MathF.Abs(expected - actual[i]) <= .00002f,
                            $"{scenario}, tick {tick}, body {state[0]}, component {i}: expected {expected}, actual {actual[i]}");
                    }
                }
            }
            Assert.Equal(90, tick);
        }
        /// <summary>Completed traversals remain chronological when several steps/removals are drained together.</summary>
        [Fact]
        public void QueuedBatchesAndSnapshotsRetainOwnershipAndOrder()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            _ = world.AddCircle(new(.5f, 0), .3f, 0, 0);
            PhysicsBody first = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            first.IsSensor = true;
            world.Step();
            IReadOnlyList<PhysicsFixtureContactState> contacts = world.CaptureFixtureContacts();
            Assert.Equal(new PhysicsFixtureContactState(1, 2, 1, true, true, 0), Assert.Single(contacts));
            PhysicsBody next = world.AddCircle(new(.25f, 0), .3f, 1, 0);
            next.IsSensor = true;
            world.Step();
            Assert.True(world.RemoveBody(first));
            IReadOnlyList<PhysicsContactEvent> events = world.DrainContactEvents();
            Assert.Equal([1, 3, 2, 3, 1], events.Select(e => e.CreationOrder));
            Assert.Equal([ PhysicsContactEventKind.Begin, PhysicsContactEventKind.Begin, PhysicsContactEventKind.Begin,
                PhysicsContactEventKind.End, PhysicsContactEventKind.End ], events.Select(e => e.Kind));
            Assert.All(events.Take(3), e => Assert.Equal(PhysicsContactEventPhase.Update, e.Phase));
            Assert.All(events.Skip(3), e => Assert.Equal(PhysicsContactEventPhase.Destroy, e.Phase));
            Assert.All(events, e => Assert.True(e.Touching && e.IsSensor));
            Assert.Empty(world.DrainContactEvents());
            _ = Assert.Single(contacts);
            _ = Assert.Throws<NotSupportedException>(() => ((IList<PhysicsFixtureContactState>)contacts)[0] = default);
            _ = Assert.Throws<NotSupportedException>(() => ((IList<PhysicsContactEvent>)events)[0] = default);
            Assert.True(world.RemoveBody(next));
            _ = Assert.Single(world.DrainContactEvents());
            Assert.Equal(5, events.Count);
        }

        /// <summary>Recording starts with future transitions and disabling preserves pending snapshots.</summary>
        [Fact]
        public void RecordingIsOptInWithoutSyntheticTransitions()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false
            };
            _ = world.AddCircle(new(.5f, 0), .3f, 0, 0);
            PhysicsBody body = world.AddCircle(Vector2.Zero, .3f, 1, 0);
            body.IsSensor = true;
            Assert.False(world.RecordContactEvents);
            world.Step();
            Assert.Empty(world.DrainContactEvents());
            world.RecordContactEvents = true;
            world.Step();
            Assert.Empty(world.DrainContactEvents());
            world.SetFixtureFilter(2, new(1, 0, 0));
            world.Step();
            world.RecordContactEvents = false;
            Assert.True(world.RemoveBody(body));
            PhysicsContactEvent end = Assert.Single(world.DrainContactEvents());
            Assert.Equal(PhysicsContactEventKind.End, end.Kind);
            Assert.Equal(PhysicsContactEventPhase.Destroy, end.Phase);
            Assert.True(end.Touching && end.IsSensor);
            Assert.Empty(world.DrainContactEvents());
        }

        /// <summary>A sensor toggle is deferred; destruction snapshots current flags and stored touching.</summary>
        [Fact]
        public void ToggleThenRemovalRetainsStoredManifoldAndTouching()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            _ = world.AddCircle(Vector2.Zero, .3f, 0, 0);
            PhysicsBody body = world.AddCircle(new(.5f, 0), .3f, 1, 0);
            world.Step();
            PhysicsContactEvent begin = Assert.Single(world.DrainContactEvents());
            Assert.False(begin.IsSensor);
            Assert.Equal(1, Assert.Single(world.CaptureFixtureContacts()).ManifoldPoints);
            body.IsSensor = true;
            PhysicsFixtureContactState before = Assert.Single(world.CaptureFixtureContacts());
            Assert.True(before.Touching && before.IsSensor);
            Assert.Equal(1, before.ManifoldPoints);
            Assert.Empty(world.DrainContactEvents());
            Assert.True(world.RemoveBody(body));
            PhysicsContactEvent end = Assert.Single(world.DrainContactEvents());
            Assert.True(end.IsSensor && end.Touching);
            Assert.Equal(PhysicsContactEventPhase.Destroy, end.Phase);
            Assert.Equal(PhysicsContactEventKind.End, end.Kind);
            Assert.False(world.RemoveBody(body));
            Assert.Empty(world.DrainContactEvents());
            Assert.Empty(world.CaptureFixtureContacts());
        }

        /// <summary>Sensor-solid toggles preserve touching until an update and never synthesize repeat Begin events.</summary>
        [Fact]
        public void SensorUpdatesNeverCreateSolidManifoldsOrFeatures()
        {
            PhysicsWorld world = new()
            {
                ContinuousPhysics = false,
                RecordContactEvents = true
            };
            _ = world.AddCircle(Vector2.Zero, .3f, 0, 0);
            PhysicsBody body = world.AddPolygon(new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)]),
                new(.45f, 0), 1, 0, compound: false);
            body.IsSensor = true;
            world.Step();
            _ = Assert.Single(world.DrainContactEvents());
            Vector2 position = body.Position;
            for (int i = 0; i < 5; i++)
            {
                world.Step();
            }

            Assert.Equal(position, body.Position);
            Assert.Equal(Vector2.Zero, body.Velocity);
            Assert.Empty(world.DrainContactEvents());
            Assert.Empty(world.CaptureBodyContacts());
            Assert.Empty(world.CaptureBodyContactFeatures());
            Assert.Equal(0, Assert.Single(world.CaptureFixtureContacts()).ManifoldPoints);
            body.IsSensor = false;
            body.IsSensor = true;
            world.Step();
            Assert.Empty(world.DrainContactEvents());
            body.IsSensor = false;
            world.Step();
            Assert.Empty(world.DrainContactEvents());
            Assert.Equal(1, Assert.Single(world.CaptureFixtureContacts()).ManifoldPoints);
        }
    }
}
