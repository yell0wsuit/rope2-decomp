using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Independent native ray cast, AABB query, point test and fixture bounds comparisons.</summary>
    public sealed class WorldQueryTests
    {
        /// <summary>Compares native query results in report order at several ticks of a moving world.</summary>
        [Fact]
        public void MatchesNativeWorldQueries()
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
            PhysicsBody Circle(float x, float y, float radius, float density, float gravity, PhysicsBodyType type)
            {
                return Register(world.AddCircle(new(x, y), radius, density, gravity, 0, type), .3f);
            }
            PhysicsBody Box(float x, float y, float hx, float hy, float gravity, float angle, bool compound, PhysicsBodyType type)
            {
                ConvexPolygon shape = compound ? new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)]) :
                    new([new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy)]);
                return Register(world.AddPolygon(shape, new(x, y), 1, gravity, angle, false, compound, type), .4f);
            }
            world.AddTerrain([new(-3, 1), new(3, 1), new(3, 1.5f), new(-3, 1.5f)], .3f, 0);
            world.AddTerrain([new(1, .2f), new(1.8f, .6f), new(1.6f, .9f), new(.9f, .7f)], .3f, 0);
            label += 2;
            _ = Circle(-1, .2f, .2f, 1, 1, PhysicsBodyType.Dynamic);
            _ = Box(-.3f, 0, .3f, .2f, 1, .4f, true, PhysicsBodyType.Dynamic);
            _ = Box(.4f, .3f, .25f, .1f, 1, -.6f, false, PhysicsBodyType.Dynamic);
            _ = Circle(-2, .6f, .3f, 0, 0, PhysicsBodyType.Static);
            PhysicsBody sensor = Circle(.2f, -.3f, .25f, 1, .5f, PhysicsBodyType.Dynamic);
            sensor.IsSensor = true;
            PhysicsBody kinematic = Box(2.2f, -.2f, .2f, .3f, 0, .3f, false, PhysicsBodyType.Kinematic);
            kinematic.SetLinearVelocity(new(-.6f, .2f));
            kinematic.SetAngularVelocity(1);
            (Vector2, Vector2)[] rays = [(new(-3, .5f), new(3, .5f)), (new(3, .7f), new(-3, .7f)), (new(-1, -1), new(-1, 2)), (new(.4f, -1), new(.45f, 2)),
                (new(-2.5f, -.5f), new(2.5f, 1.2f)), (new(2.5f, -1), new(-.5f, 1.4f)), (new(.2f, -.3f), new(.21f, -.29f)), (new(-2, .6f), new(-2, .61f))];
            PhysicsAabb[] boxes = [new(new(-3, -2), new(3, 2)), new(new(-1.2f, 0), new(-.8f, 1)), new(new(0, -1), new(1, .5f)), new(new(2, -1), new(2.5f, 0))];
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "QueryReference.json")));
            int tick = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray())
            {
                int target = row.GetProperty("Tick").GetInt32();
                while (tick < target)
                {
                    world.Step();
                    tick++;
                }
                string context = $"tick {tick}";
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32()];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 1].GetSingle(), actual[i], $"{context}, body {state[0]}, component {i}");
                    }
                }
                JsonElement[] expectedRays = [.. row.GetProperty("Rays").EnumerateArray()];
                int rayIndex = 0;
                foreach ((Vector2 start, Vector2 end) in rays)
                {
                    for (int mode = 0; mode < 4; mode++)
                    {
                        List<PhysicsRayHit> hits = [];
                        world.RayCast(start, end, hit =>
                        {
                            hits.Add(hit);
                            return mode switch
                            {
                                0 => 1,
                                1 => hit.Fraction,
                                2 => 0,
                                _ => hit.Fixture % 2 == 0 ? -1 : 1
                            };
                        });
                        JsonElement[] expected = [.. expectedRays[rayIndex++].EnumerateArray()];
                        string where = $"{context}, ray {rayIndex}";
                        Assert.Equal(expected.Length, hits.Count);
                        foreach ((JsonElement e, PhysicsRayHit hit) in expected.Zip(hits))
                        {
                            Assert.Equal(e[0].GetInt32(), hit.Fixture);
                            Assert.Same(world.GetFixtureBody(hit.Fixture), hit.Body);
                            float[] values = [hit.Point.X, hit.Point.Y, hit.Normal.X, hit.Normal.Y, hit.Fraction];
                            for (int i = 0; i < values.Length; i++)
                            {
                                Near(e[i + 1].GetSingle(), values[i], $"{where}, hit {hit.Fixture}, value {i}");
                            }
                        }
                    }
                }
                JsonElement[] expectedQueries = [.. row.GetProperty("Queries").EnumerateArray()];
                int queryIndex = 0;
                foreach (PhysicsAabb box in boxes)
                {
                    foreach (int limit in new[] { 100, 2 })
                    {
                        List<int> found = [];
                        world.QueryAabb(box, fixtureLabel =>
                        {
                            found.Add(fixtureLabel);
                            return found.Count < limit;
                        });
                        Assert.Equal(expectedQueries[queryIndex++].EnumerateArray().Select(e => e.GetInt32()), found);
                    }
                }
                int fixtureIndex = 0;
                foreach (JsonElement bits in row.GetProperty("Points").EnumerateArray())
                {
                    fixtureIndex++;
                    StringBuilder actual = new();
                    for (int j = 0; j < 8; j++)
                    {
                        for (int i = 0; i < 12; i++)
                        {
                            _ = actual.Append(world.TestPoint(fixtureIndex, new(-2.75f + (i * .47f), -.9f + (j * .32f))) ? '1' : '0');
                        }
                    }
                    Assert.True(bits.GetString() == actual.ToString(), $"{context}, fixture {fixtureIndex} point tests differ");
                }
                fixtureIndex = 0;
                foreach (JsonElement bounds in row.GetProperty("Bounds").EnumerateArray())
                {
                    PhysicsAabb actual = world.GetFixtureAabb(++fixtureIndex);
                    float[] values = [actual.Lower.X, actual.Lower.Y, actual.Upper.X, actual.Upper.Y];
                    for (int i = 0; i < values.Length; i++)
                    {
                        Near(bounds[i].GetSingle(), values[i], $"{context}, fixture {fixtureIndex} bound {i}");
                    }
                }
            }
            Assert.Equal(90, tick);
        }

        /// <summary>Invalid segments and callbacks reject, and callbacks cannot mutate the world.</summary>
        [Fact]
        public void InvalidQueriesRejectAndCallbacksCannotMutate()
        {
            PhysicsWorld world = new();
            PhysicsBody body = world.AddCircle(Vector2.Zero, .5f, 1, 0);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.RayCast(Vector2.One, Vector2.One, _ => 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => world.RayCast(new(float.NaN, 0), Vector2.One, _ => 1));
            _ = Assert.Throws<ArgumentNullException>(() => world.RayCast(-Vector2.One, Vector2.One, null!));
            _ = Assert.Throws<ArgumentNullException>(() => world.QueryAabb(new(-Vector2.One, Vector2.One), null!));
            _ = Assert.Throws<KeyNotFoundException>(() => world.TestPoint(9, Vector2.Zero));
            _ = Assert.Throws<InvalidOperationException>(() => world.QueryAabb(new(-Vector2.One, Vector2.One), _ =>
            {
                world.SetTransform(body, Vector2.One, 0);
                return true;
            }));
            Assert.Equal(Vector2.Zero, body.Position);
            Assert.True(world.TestPoint(1, new(.5f, 0)));
            Assert.False(world.TestPoint(1, new(.51f, 0)));
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f,
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
