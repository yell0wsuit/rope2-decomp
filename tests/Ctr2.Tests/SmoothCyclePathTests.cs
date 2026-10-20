using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Compares the smoothed polygon path and its triangulation with an independent native reconstruction, bit for bit, using invented paths.</summary>
    public sealed class SmoothCyclePathTests
    {
        private static string Bits(IReadOnlyList<Vector2> points)
        {
            return string.Concat(points.Select(p => " " + BitConverter.SingleToUInt32Bits(p.X).ToString("x8", CultureInfo.InvariantCulture) + " " +
                BitConverter.SingleToUInt32Bits(p.Y).ToString("x8", CultureInfo.InvariantCulture)));
        }

        /// <summary>Every smooth point and triangle vertex matches the native float bits.</summary>
        [Fact]
        public void MatchesNativeReconstruction()
        {
            string[] lines = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SmoothPathReference.txt")).Where(l => !l.StartsWith('#'))];
            Assert.Equal(36, lines.Length);
            for (int i = 0; i < lines.Length; i += 3)
            {
                string[] input = lines[i][2..].Split(' ');
                SmoothCyclePath path = new(new(float.Parse(input[0], CultureInfo.InvariantCulture), float.Parse(input[1], CultureInfo.InvariantCulture)), input[5],
                    input[2] == "1", int.Parse(input[3], CultureInfo.InvariantCulture), float.Parse(input[4], CultureInfo.InvariantCulture));
                Assert.Equal(lines[i + 1], "S" + Bits(path.SmoothPoints()));
                Assert.Equal(lines[i + 2], "T" + Bits(path.TrianglePoints()));
            }
        }

        /// <summary>Open and cyclic spline values and arc-length derivatives match the native bits, including the short-path
        /// branches that add and later drop mirrored end points.</summary>
        [Fact]
        public void SplineValuesAndDerivativesMatchNativeReconstruction()
        {
            string[] lines = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "SplineReference.txt")).Where(l => !l.StartsWith('#'))];
            Assert.Equal(18, lines.Length);
            for (int i = 0; i < lines.Length; i += 2)
            {
                string[] input = lines[i][2..].Split(' ');
                bool open = input[0] == "open";
                Func<float, Vector2> value, derivative;
                float length;
                if (open)
                {
                    SmoothPath path = new(Vector2.Zero, input[1]);
                    (value, derivative, length) = (path.VectorAt, path.DerivativeAt, path.MaxRealT);
                }
                else
                {
                    SmoothCyclePath path = new(Vector2.Zero, input[1], false, 10);
                    (value, derivative, length) = (path.VectorAt, path.DerivativeAt, path.MaxRealT);
                }
                List<string> actual = [input[0], "00000000", Hex(length)];
                for (int k = 0; k <= 40; k++)
                {
                    float t = open ? k / 40f : (k / 40f) + .013f;
                    Vector2 v = value(t), d = derivative(t);
                    actual.AddRange([Hex(v.X), Hex(v.Y), Hex(d.X), Hex(d.Y)]);
                }
                Assert.Equal(lines[i + 1], string.Join(' ', actual));
            }
        }

        private static string Hex(float value)
        {
            return BitConverter.SingleToUInt32Bits(value).ToString("x8", CultureInfo.InvariantCulture);
        }

        /// <summary>The body origin is the editor position scaled to meters in double precision.</summary>
        [Fact]
        public void BodyOriginRoundTripsEditorPosition()
        {
            SmoothCyclePath path = new(new(161.4f, 403.7f), "10,0,10,10,0,10,", false, 1);
            Assert.Equal(new Vector2((float)(161.4f / 200.0), (float)(403.7f / 200.0)), path.BodyOrigin);
        }

        /// <summary>Malformed paths and smoothing factors are rejected.</summary>
        [Theory]
        [InlineData("10,0,10", 1)]
        [InlineData("10,0,x,10,0,10", 1)]
        [InlineData("10,0,10,10,0,10", 0)]
        [InlineData("10,0,10,10,0,10", 17)]
        public void RejectsInvalidPaths(string path, int factor)
        {
            _ = Assert.Throws<InvalidDataException>(() => new SmoothCyclePath(Vector2.Zero, path, false, factor));
        }
    }
}
