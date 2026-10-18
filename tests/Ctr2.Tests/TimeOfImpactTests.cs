using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Independent standalone native time-of-impact comparisons.</summary>
    public sealed class TimeOfImpactTests
    {
        /// <summary>Compares native state and time for random and deterministic sweeps of circles and polygons.</summary>
        [Fact]
        public void MatchesNativeTimeOfImpact()
        {
            PhysicsDistanceProxy[] proxies = [new([Vector2.Zero], .2f), new([Vector2.Zero], .05f),
                new([new(-.3f, -.2f), new(.3f, -.2f), new(.3f, .2f), new(-.3f, .2f)], .002f),
                new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f)], .002f), new([new(-2, 0), new(2, 0), new(2, .5f), new(-2, .5f)], .002f),
                new([new(-.05f, -.4f), new(.05f, -.4f), new(.05f, .4f), new(-.05f, .4f)], .002f),
                new([Vector2.Zero, new(.3f, .1f), new(.4f, .35f), new(.1f, .5f), new(-.2f, .2f)], .002f)];
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ToiReference.json")));
            int index = 0;
            foreach (JsonElement row in fixture.RootElement.EnumerateArray())
            {
                index++;
                (PhysicsToiState state, float t) = PhysicsTimeOfImpact.Compute(proxies[row[0].GetInt32()], Sweep(row[2]), proxies[row[1].GetInt32()], Sweep(row[3]),
                    row[4].GetSingle());
                Assert.True((PhysicsToiState)row[5].GetInt32() == state, $"case {index}: expected {(PhysicsToiState)row[5].GetInt32()}, actual {state}");
                float expected = row[6].GetSingle();
                Assert.True(MathF.Abs(expected - t) <= .00002f, $"case {index}: expected t {expected}, actual {t}");
            }
            Assert.Equal(2636, index);
        }

        private static PhysicsSweep Sweep(JsonElement values)
        {
            float[] v = [.. values.EnumerateArray().Select(e => e.GetSingle())];
            return new()
            {
                LocalCenter = new(v[0], v[1]),
                Center0 = new(v[2], v[3]),
                Center = new(v[4], v[5]),
                Angle0 = v[6],
                Angle = v[7],
                Alpha0 = v[8]
            };
        }
    }
}
