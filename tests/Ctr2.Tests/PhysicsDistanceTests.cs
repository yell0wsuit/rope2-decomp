using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Independent native GJK witness, simplex and sensor overlap comparisons.</summary>
    public sealed class PhysicsDistanceTests
    {
        /// <summary>Matches synthetic native distance outputs and exact simplex/support termination.</summary>
        /// <param name="group">Fresh, warm or threshold-focused native query group.</param>
        /// <param name="rows">Expected independent query count.</param>
        [Theory]
        [InlineData("fresh", 864)]
        [InlineData("warm", 864)]
        [InlineData("threshold", 12)]
        [InlineData("tiny", 6)]
        [InlineData("cache-jumps", 6)]
        public void MatchesNativeDistanceQueries(string group, int rows)
        {
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GjkReference.json")));
            PhysicsSimplexCache cache = new();
            int series = -1, count = 0;
            foreach (JsonElement row in reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Group").GetString() == group))
            {
                int currentSeries = row.GetProperty("Series").GetInt32();
                if ((group != "warm" && group != "cache-jumps") || currentSeries != series)
                {
                    cache.Reset();
                }

                series = currentSeries;
                Vector2[] Vertices(string name)
                {
                    return [.. row.GetProperty(name).EnumerateArray().Select(v => new Vector2(v[0].GetSingle(), v[1].GetSingle()))];
                }

                PhysicsDistanceTransform Transform(string name)
                {
                    JsonElement t = row.GetProperty(name);
                    return new(new(t[0].GetSingle(), t[1].GetSingle()), t[2].GetSingle());
                }
                PhysicsDistanceProxy a = new(Vertices("A"), row.GetProperty("Radii")[0].GetSingle());
                PhysicsDistanceProxy b = new(Vertices("B"), row.GetProperty("Radii")[1].GetSingle());
                PhysicsDistanceTransform ta = Transform("TransformA"), tb = Transform("TransformB");
                PhysicsDistanceResult actual = PhysicsDistance.Measure(a, ta, b, tb, row.GetProperty("UseRadii").GetBoolean(), cache);
                string context = $"{group}, series {series}, step {row.GetProperty("Step").GetInt32()}";
                JsonElement expected = row.GetProperty("Output");
                float[] components = [actual.PointA.X, actual.PointA.Y, actual.PointB.X, actual.PointB.Y, actual.Distance];
                for (int i = 0; i < components.Length; i++)
                {
                    Near(expected[i].GetSingle(), components[i], $"{context}, component {i}");
                }

                Assert.Equal(expected[5].GetInt32(), actual.Iterations);
                PhysicsSimplexState state = cache.Capture();
                Near(row.GetProperty("Metric").GetSingle(), state.Metric, $"{context}, cache metric");
                PhysicsSupportPair[] pairs = [.. row.GetProperty("Indices").EnumerateArray().Select(v => new PhysicsSupportPair(v[0].GetInt32(), v[1].GetInt32()))];
                Assert.True(pairs.SequenceEqual(state.Vertices), $"{context}: simplex indices/order differ");
                Assert.Equal(row.GetProperty("Overlap").GetBoolean(), PhysicsDistance.Overlaps(a, ta, b, tb));
                count++;
            }
            Assert.Equal(rows, count);
        }

        /// <summary>Copied proxies and detached cache snapshots retain ownership across later queries.</summary>
        [Fact]
        public void ProxiesAndSnapshotsAreImmutable()
        {
            Vector2[] vertices = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
            PhysicsDistanceProxy polygon = new(vertices, .002f);
            vertices[0] = new(99, 99);
            Assert.Equal(new(-1, -1), polygon.Vertices[0]);
            _ = Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<Vector2>)polygon.Vertices)[0] = Vector2.Zero);
            PhysicsDistanceProxy point = new([Vector2.Zero], .2f);
            PhysicsSimplexCache cache = new();
            _ = PhysicsDistance.Measure(polygon, default, point, new(new(3, 0), 0), cache: cache);
            PhysicsSimplexState snapshot = cache.Capture();
            PhysicsSupportPair[] indices = [.. snapshot.Vertices];
            Assert.NotEmpty(indices);
            _ = Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<PhysicsSupportPair>)snapshot.Vertices)[0] = default);
            cache.Reset();
            Assert.Empty(cache.Capture().Vertices);
            Assert.Equal(0, cache.Capture().Metric);
            _ = PhysicsDistance.Measure(polygon, default, point, new(new(-3, 2), .5f), cache: cache);
            Assert.True(indices.SequenceEqual(snapshot.Vertices));
        }

        /// <summary>Invalid transforms are rejected before any warm-start cache changes.</summary>
        /// <param name="kind">Invalid transform component.</param>
        /// <param name="first">Whether to invalidate the first or second transform.</param>
        [Theory]
        [InlineData(0, false)]
        [InlineData(0, true)]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void InvalidTransformsPreserveCache(int kind, bool first)
        {
            PhysicsDistanceProxy point = new([Vector2.Zero], .1f);
            PhysicsSimplexCache cache = new();
            _ = PhysicsDistance.Measure(point, default, point, new(new(1, 0), 0), cache: cache);
            PhysicsSimplexState before = cache.Capture();
            PhysicsDistanceTransform invalid = kind switch
            {
                0 => new(new(float.NaN, 0), 0),
                1 => new(Vector2.Zero, float.PositiveInfinity),
                _ => new(new(1000001, 0), 0)
            };
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => PhysicsDistance.Measure(point, first ? invalid : default,
                point, first ? default : invalid, cache: cache));
            Assert.Equal(before.Metric, cache.Capture().Metric);
            Assert.True(before.Vertices.SequenceEqual(cache.Capture().Vertices));
        }

        /// <summary>Changing to smaller support sets requires resetting incompatible cached indices.</summary>
        [Fact]
        public void IncompatibleCachedIndicesRejectWithoutMutation()
        {
            PhysicsDistanceProxy polygon = new([new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)], .002f);
            PhysicsDistanceProxy point = new([Vector2.Zero], .1f);
            PhysicsSimplexCache cache = new();
            _ = PhysicsDistance.Measure(polygon, default, point, new(new(3, 0), 0), cache: cache);
            PhysicsSimplexState before = cache.Capture();
            Assert.Contains(before.Vertices, p => p.A > 0);
            _ = Assert.Throws<ArgumentException>(() => PhysicsDistance.Measure(point, default, point, default, cache: cache));
            _ = Assert.Throws<ArgumentNullException>(() => PhysicsDistance.Measure(null!, default, point, default, cache: cache));
            _ = Assert.Throws<ArgumentNullException>(() => PhysicsDistance.Measure(point, default, null!, default, cache: cache));
            Assert.Equal(before.Metric, cache.Capture().Metric);
            Assert.True(before.Vertices.SequenceEqual(cache.Capture().Vertices));
            cache.Reset();
            Assert.Equal(0, PhysicsDistance.Measure(point, default, point, default, cache: cache).Distance);
        }

        /// <summary>Radius correction does not alter the native core simplex cache.</summary>
        [Fact]
        public void RadiusCorrectionLeavesCoreCacheUnchanged()
        {
            PhysicsDistanceProxy polygon = new([new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)], .002f);
            PhysicsDistanceProxy point = new([Vector2.Zero], .2f);
            PhysicsSimplexCache core = new(), rounded = new();
            PhysicsDistanceTransform transform = new(new(3, 0), 0);
            PhysicsDistanceResult a = PhysicsDistance.Measure(polygon, default, point, transform, false, core);
            PhysicsDistanceResult b = PhysicsDistance.Measure(polygon, default, point, transform, true, rounded);
            Near(.202f, a.Distance - b.Distance, "radius subtraction");
            Assert.Equal(a.Iterations, b.Iterations);
            Assert.Equal(core.Capture().Metric, rounded.Capture().Metric);
            Assert.True(core.Capture().Vertices.SequenceEqual(rounded.Capture().Vertices));
        }

        /// <summary>Invalid support sets and radii are rejected at the immutable boundary.</summary>
        [Fact]
        public void InvalidProxiesAreRejected()
        {
            _ = Assert.Throws<ArgumentNullException>(() => new PhysicsDistanceProxy(null!, 0));
            _ = Assert.Throws<ArgumentException>(() => new PhysicsDistanceProxy([], 0));
            _ = Assert.Throws<ArgumentException>(() => new PhysicsDistanceProxy(new Vector2[9], 0));
            _ = Assert.Throws<ArgumentException>(() => new PhysicsDistanceProxy([new(float.NaN, 0)], 0));
            _ = Assert.Throws<ArgumentException>(() => new PhysicsDistanceProxy([new(0, 1000001)], 0));
            foreach (float radius in new[] { -1, float.NaN, float.PositiveInfinity, 1000001 })
            {
                _ = Assert.Throws<ArgumentException>(() => new PhysicsDistanceProxy([Vector2.Zero], radius));
            }
        }

        private static void Near(float expected, float actual, string location)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .000002f,
                $"{location}: expected {expected}, actual {actual}");
        }
    }
}
