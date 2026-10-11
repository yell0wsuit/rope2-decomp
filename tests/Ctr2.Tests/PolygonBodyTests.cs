using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks convex body geometry, mass and origin contracts without game data.</summary>
    public sealed class PolygonBodyTests
    {
        /// <summary>An offset rectangle preserves its centroid and inertia about both origins.</summary>
        [Fact]
        public void OffsetRectangleHasCorrectMassAndInertia()
        {
            ConvexPolygon shape = new([new(1, 2), new(3, 2), new(3, 4), new(1, 4)]);
            PolygonMass mass = shape.ComputeMass(3);
            Assert.Equal(12, mass.Mass, 5);
            Assert.Equal(new Vector2(2, 3), mass.Center);
            Assert.Equal(164, mass.OriginInertia, 4);
            Assert.Equal(8, mass.CenterInertia, 4);
        }
        /// <summary>Validates polygon mass and inertia against synthetic upstream shapes.</summary>
        [Fact]
        public void MassMatchesNumericalFixture()
        {
            string[][] samples = Read("PolygonMassReference.csv");
            Assert.Equal(12, samples.Length);
            foreach (string[] row in samples)
            {
                PolygonMass mass = Shape(row[0]).ComputeMass(Number(row[1]));
                float[] actual = [mass.Mass, mass.Center.X, mass.Center.Y, mass.OriginInertia, mass.CenterInertia];
                for (int i = 0; i < actual.Length; i++)
                {
                    Assert.InRange(MathF.Abs(actual[i] - Number(row[i + 2])), 0, .00002f);
                }
            }
        }

        /// <summary>Validates transformed circle sensors, polygon skin and vertex misses against upstream.</summary>
        [Fact]
        public void CircleOverlapMatchesNumericalFixture()
        {
            string[][] samples = Read("PolygonOverlapReference.csv");
            Assert.Equal(28, samples.Length);
            foreach (string[] row in samples)
            {
                bool actual = Shape(row[0]).OverlapsCircle(new(Number(row[2]), Number(row[3])), Number(row[4]), new(Number(row[5]), Number(row[6])), Number(row[7]));
                Assert.True(actual == (row[8] == "1"), $"{row[0]} circle {row[1]}: expected {row[8]}, actual {actual}");
            }
        }

        /// <summary>Shape construction copies caller storage and vertex transforms preserve the local origin.</summary>
        [Fact]
        public void GeometryIsOwnedAndTransformUsesBodyOrigin()
        {
            Vector2[] vertices = [new(1, 2), new(3, 2), new(3, 4), new(1, 4)];
            ConvexPolygon shape = new(vertices);
            vertices[0] = new(float.NaN, float.NaN);
            Assert.Equal(new Vector2(1, 2), shape.Vertices[0]);
            Assert.Equal(new Vector2(0, -1), shape.Normals[0]);
            Vector2 point = shape.WorldVertices(new(10, 20), MathF.PI / 2)[0];
            Assert.InRange(Vector2.Distance(point, new(8, 21)), 0, .00001f);
            PolygonMass zero = shape.ComputeMass(0);
            Assert.Equal(0, zero.Mass);
            Assert.Equal(new Vector2(2, 3), zero.Center);
            Assert.Equal(0, zero.CenterInertia);
        }

        /// <summary>Rejects clockwise, collinear, concave, duplicate and self-crossing outlines.</summary>
        /// <param name="vertices">Invented invalid coordinates.</param>
        [Theory]
        [InlineData("0,0;0,1;1,0")]
        [InlineData("0,0;1,0;2,0")]
        [InlineData("0,0;2,0;1,0.5;2,2;0,2")]
        [InlineData("0,0;1,0;1,0;0,1")]
        [InlineData("0,0;1,1;0,1;1,0")]
        [InlineData("0,0;1,0")]
        [InlineData("0,0;NaN,0;0,1")]
        [InlineData("0,0;10001,0;0,1")]
        public void InvalidGeometryFails(string vertices)
        {
            _ = Assert.Throws<ArgumentException>(() => new ConvexPolygon([.. vertices.Split(';').Select(p => p.Split(',')).Select(p => new Vector2(Number(p[0]), Number(p[1])))]));
        }

        /// <summary>Invalid density values cannot produce invalid mass state.</summary>
        /// <param name="density">Requested invalid density.</param>
        [Theory]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(1000001f)]
        public void InvalidDensityFails(float density)
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Shape("square").ComputeMass(density));
        }

        /// <summary>Invalid transforms and circle radii fail before geometry operations.</summary>
        [Fact]
        public void InvalidQueriesFail()
        {
            ConvexPolygon shape = Shape("square");
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => shape.WorldVertices(new(float.NaN, 0), 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => shape.WorldVertices(Vector2.Zero, float.NaN));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => shape.OverlapsCircle(Vector2.Zero, 0, Vector2.Zero, 0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => shape.OverlapsCircle(new(float.PositiveInfinity, 0), .2f, Vector2.Zero, 0));
            _ = Assert.Throws<ArgumentNullException>(() => new ConvexPolygon(null!));
        }

        /// <summary>Builds one invented reference polygon.</summary>
        /// <param name="name">Fixture scenario.</param>
        /// <returns>The immutable convex geometry.</returns>
        private static ConvexPolygon Shape(string name)
        {
            return new(name switch
            {
                "square" => [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)],
                "offset" => [new(1, 2), new(3, 2), new(3, 4), new(1, 4)],
                "triangle" => [new(0, 0), new(2, 0), new(0, 1)],
                "pentagon" => [new(0, 0), new(1.2f, .1f), new(1.8f, 1), new(1, 1.6f), new(-.3f, .8f)],
                _ => throw new InvalidDataException("Unknown synthetic polygon.")
            });
        }

        /// <summary>Reads all rows of a numerical fixture.</summary>
        /// <param name="name">Copied fixture filename.</param>
        /// <returns>CSV fields for every nonempty row.</returns>
        private static string[][] Read(string name)
        {
            return [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, name)).Select(line => line.Split(','))];
        }

        /// <summary>Parses invariant fixture coordinates.</summary>
        /// <param name="text">Numeric text.</param>
        /// <returns>The parsed value.</returns>
        private static float Number(string text)
        {
            return float.Parse(text, CultureInfo.InvariantCulture);
        }
    }
}
