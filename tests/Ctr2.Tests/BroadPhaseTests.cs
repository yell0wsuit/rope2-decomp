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
    /// <summary>Replays synthetic proxy operations against native dynamic-tree snapshots.</summary>
    public sealed class BroadPhaseTests
    {
        /// <summary>Checks native node reuse, balancing, movement, query order and candidate-pair order.</summary>
        [Fact]
        public void MatchesNativeProxyReplay()
        {
            PhysicsBroadPhase broad = new();
            Dictionary<int, int> ids = [];
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BroadPhaseReference.json")));
            int step = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray())
            {
                Assert.Equal(step++, row.GetProperty("Step").GetInt32());
                int key = row.GetProperty("Key").GetInt32();
                int data = row.GetProperty("Data").GetInt32();
                PhysicsAabb bounds = Bounds(row.GetProperty("Bounds"));
                JsonElement delta = row.GetProperty("Delta");
                bool reinserted = false;
                int id = row.GetProperty("Id").GetInt32();
                switch (row.GetProperty("Op").GetString())
                {
                    case "create":
                        ids.Add(key, broad.CreateProxy(bounds, data));
                        Assert.Equal(id, ids[key]);
                        break;
                    case "move":
                        reinserted = broad.MoveProxy(ids[key], bounds, new(delta[0].GetSingle(), delta[1].GetSingle()));
                        break;
                    case "touch":
                        broad.TouchProxy(ids[key]);
                        broad.TouchProxy(ids[key]);
                        break;
                    case "destroy":
                        broad.DestroyProxy(ids[key]);
                        _ = ids.Remove(key);
                        break;
                    case "ephemeral":
                        int temporary = broad.CreateProxy(bounds, data);
                        Assert.Equal(id, temporary);
                        broad.DestroyProxy(temporary);
                        break;
                    case "reuse-pending":
                        broad.TouchProxy(ids[key]);
                        broad.TouchProxy(ids[key]);
                        broad.DestroyProxy(ids[key]);
                        ids[key] = broad.CreateProxy(bounds, data);
                        Assert.Equal(id, ids[key]);
                        break;
                    case "flush":
                        break;
                    default:
                        Assert.Fail("Unknown native fixture operation.");
                        break;
                }
                Assert.Equal(row.GetProperty("Reinserted").GetBoolean(), reinserted);
                BroadPhasePair[] expectedPairs = [.. row.GetProperty("Pairs").EnumerateArray()
                    .Select(pair => new BroadPhasePair(pair[0].GetInt32(), pair[1].GetInt32()))];
                Assert.Equal(expectedPairs, broad.UpdatePairs());
                Assert.Equal(ids.Count, broad.ProxyCount);
                Assert.Equal(row.GetProperty("Height").GetInt32(), broad.Height);
                Assert.Equal(row.GetProperty("Query").EnumerateArray().Select(p => p.GetInt32()),
                    broad.Query(new(new(-50, -50), new(50, 50))));
                Assert.Equal(row.GetProperty("LocalQuery").EnumerateArray().Select(p => p.GetInt32()),
                    broad.Query(new(Vector2.Zero, new(2, 2))));
                Assert.Equal(ids.Count, row.GetProperty("Proxies").GetArrayLength());
                foreach (JsonElement proxy in row.GetProperty("Proxies").EnumerateArray())
                {
                    int proxyId = ids[proxy.GetProperty("Key").GetInt32()];
                    Assert.Equal(proxy.GetProperty("Id").GetInt32(), proxyId);
                    Assert.Equal(proxy.GetProperty("Data").GetInt32(), broad.GetUserData(proxyId));
                    PhysicsAabb expected = Bounds(proxy.GetProperty("Bounds"));
                    PhysicsAabb actual = broad.GetBounds(proxyId);
                    Assert.True(float.IsFinite(actual.Lower.X) && float.IsFinite(actual.Lower.Y) &&
                        float.IsFinite(actual.Upper.X) && float.IsFinite(actual.Upper.Y));
                    Assert.InRange(Vector2.Distance(expected.Lower, actual.Lower), 0, .000002f);
                    Assert.InRange(Vector2.Distance(expected.Upper, actual.Upper), 0, .000002f);
                }
            }
            Assert.Equal(0, broad.ProxyCount);
            Assert.Equal(110, step);
        }

        private static PhysicsAabb Bounds(JsonElement value)
        {
            return new(
            new(value[0].GetSingle(), value[1].GetSingle()), new(value[2].GetSingle(), value[3].GetSingle()));
        }

        /// <summary>Early callback exit and rejected mutation leave the query state reusable.</summary>
        [Fact]
        public void QueryCanStopEarlyWithoutPermittingMutation()
        {
            PhysicsBroadPhase broad = new();
            PhysicsAabb box = new(Vector2.Zero, Vector2.One);
            int a = broad.CreateProxy(box, 20);
            int b = broad.CreateProxy(box, 10);
            int[] order = [.. broad.Query(box)];
            List<int> visited = [];
            broad.Query(box, id =>
            {
                visited.Add(id);
                _ = Assert.Throws<InvalidOperationException>(() => broad.DestroyProxy(a));
                return false;
            });
            Assert.Equal(new[] { order[0] }, visited);
            Assert.Equal(2, broad.ProxyCount);
            Assert.Equal([new BroadPhasePair(20, 10)], broad.UpdatePairs());
            Assert.Empty(broad.UpdatePairs());
            broad.TouchProxy(b);
            _ = Assert.Single(broad.UpdatePairs());
        }

        /// <summary>Rejects invalid bounds and retired IDs without consuming the native free list.</summary>
        [Fact]
        public void InvalidOperationsLeaveTheTreeUnchanged()
        {
            PhysicsBroadPhase broad = new();
            PhysicsAabb valid = new(Vector2.Zero, Vector2.One);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => broad.CreateProxy(new(Vector2.One, Vector2.Zero), 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => broad.CreateProxy(new(new(float.NaN, 0), Vector2.One), 1));
            int id = broad.CreateProxy(valid, 3);
            Assert.Equal(0, id);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => broad.MoveProxy(id, valid, new(float.PositiveInfinity, 0)));
            Assert.Equal(valid, broad.GetBounds(id));
            broad.DestroyProxy(id);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => broad.TouchProxy(id));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => broad.DestroyProxy(id));
            Assert.Equal(0, broad.ProxyCount);
            Assert.Empty(broad.UpdatePairs());
            Assert.Equal(id, broad.CreateProxy(valid, 4));
        }

        /// <summary>Stale duplicate native move entries fail explicitly, and safe ID reuse remains possible.</summary>
        [Fact]
        public void DestroyedPendingDuplicateIsRejectedUntilIdIsReused()
        {
            PhysicsBroadPhase broad = new();
            PhysicsAabb box = new(Vector2.Zero, Vector2.One);
            int a = broad.CreateProxy(box, 1);
            int b = broad.CreateProxy(box, 2);
            broad.TouchProxy(a);
            broad.DestroyProxy(a);
            _ = Assert.Throws<ArgumentOutOfRangeException>(broad.UpdatePairs);
            Assert.Equal(a, broad.CreateProxy(box, 3));
            Assert.Equal([new BroadPhasePair(3, 2)], broad.UpdatePairs());
            Assert.Equal(2, broad.GetUserData(b));
        }

        /// <summary>Candidate overflow must retain pending moves so callers can reduce workload and retry.</summary>
        [Fact]
        public void CandidateBudgetFailurePreservesPendingMoves()
        {
            PhysicsBroadPhase broad = new();
            PhysicsAabb box = new(Vector2.Zero, Vector2.One);
            int[] ids = [.. Enumerable.Range(0, 1025).Select(i => broad.CreateProxy(box, i))];
            _ = Assert.Throws<InvalidOperationException>(broad.UpdatePairs);
            foreach (int id in ids.Take(513))
            {
                broad.DestroyProxy(id);
            }
            Assert.Equal(512, broad.ProxyCount);
            Assert.Equal(512 * 511 / 2, broad.UpdatePairs().Count);
        }

        /// <summary>Repeated touches have a bounded queue and become usable again after a flush.</summary>
        [Fact]
        public void MoveQueueBudgetFailureDoesNotConsumePendingWork()
        {
            PhysicsBroadPhase broad = new();
            int id = broad.CreateProxy(new(Vector2.Zero, Vector2.One), 1);
            for (int i = 0; i < 65535; i++)
            {
                broad.TouchProxy(id);
            }
            _ = Assert.Throws<InvalidOperationException>(() => broad.TouchProxy(id));
            _ = Assert.Throws<InvalidOperationException>(() => broad.CreateProxy(new(new(3, 3), new(4, 4)), 2));
            _ = Assert.Throws<InvalidOperationException>(() => broad.MoveProxy(id, new(new(3, 3), new(4, 4)), Vector2.One));
            Assert.Equal(new PhysicsAabb(Vector2.Zero, Vector2.One), broad.GetBounds(id));
            Assert.False(broad.MoveProxy(id, new(Vector2.Zero, Vector2.One), Vector2.Zero));
            Assert.Empty(broad.UpdatePairs());
            broad.TouchProxy(id);
            Assert.Empty(broad.UpdatePairs());
            Assert.Equal(1, broad.CreateProxy(new(new(3, 3), new(4, 4)), 2));
        }

        /// <summary>Nested queries and callback exceptions restore the previous mutation guard.</summary>
        [Fact]
        public void NestedAndThrowingQueriesRestoreMutationGuard()
        {
            PhysicsBroadPhase broad = new();
            PhysicsAabb box = new(Vector2.Zero, Vector2.One);
            int id = broad.CreateProxy(box, 1);
            _ = Assert.Throws<InvalidOperationException>(() => broad.Query(box, visited =>
            {
                Assert.Equal([id], broad.Query(box));
                _ = Assert.Throws<InvalidOperationException>(() => broad.TouchProxy(id));
                throw new InvalidOperationException("Synthetic callback failure.");
            }));
            broad.TouchProxy(id);
            Assert.Empty(broad.UpdatePairs());
            broad.DestroyProxy(id);
            Assert.Empty(broad.UpdatePairs());
        }

        /// <summary>Rejected proxy creation preserves live leaves and native pool reuse.</summary>
        [Fact]
        public void ProxyBudgetFailurePreservesNodePool()
        {
            PhysicsBroadPhase broad = new();
            int last = -1;
            for (int i = 0; i < 16384; i++)
            {
                Vector2 lower = new(2 * (i % 128), 2 * (i / 128));
                last = broad.CreateProxy(new(lower, lower + Vector2.One), i);
            }
            _ = Assert.Throws<InvalidOperationException>(() => broad.CreateProxy(new(Vector2.Zero, Vector2.One), -1));
            Assert.Equal(16384, broad.ProxyCount);
            Assert.Equal(16384, broad.Query(new(new(-1, -1), new(300, 300))).Count);
            broad.DestroyProxy(last);
            Assert.Equal(last, broad.CreateProxy(new(new(254, 254), new(255, 255)), 16383));
            Assert.Empty(broad.UpdatePairs());
        }
    }
}
