using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks bounded terrain smoothing, triangulation, and imported-scene contact loading.</summary>
    public sealed class TerrainGeometryTests
    {
        /// <summary>Periodic chord-parameter Akima sampling includes the implicit origin and curved midpoints.</summary>
        [Fact]
        public void SmoothSquareIncludesOriginAndExpectedMidpoints()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon_materialized x='100' y='200' material='wood' polygon='20,0,20,20,0,20,' smoothFactor='2'/></layer></map>");
            IReadOnlyList<TerrainShape> shapes = TerrainGeometry.Build(level, LevelReader.Read(MaterialDesign));
            TerrainShape shape = Assert.Single(shapes);
            Assert.Equal(8, shape.Outline.Count);
            Assert.Equal(new Vector2(200, 400), shape.Outline[0]);
            Assert.InRange(Vector2.Distance(new(220, 395), shape.Outline[1]), 0, .0001f);
            Assert.Equal(6, shape.Triangles.Count);
            Assert.Equal(.4f, shape.Friction);
        }

        /// <summary>Concave outlines triangulate without filling the missing concavity.</summary>
        [Fact]
        public void ConcaveOutlinePreservesArea()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon_materialized x='0' y='0' material='wood' polygon='40,0,40,10,10,10,10,40,0,40,' smoothFactor='1'/></layer></map>");
            TerrainShape shape = Assert.Single(TerrainGeometry.Build(level, LevelReader.Read(MaterialDesign)));
            float area = shape.Triangles.Sum(t => MathF.Abs(((t[1].X - t[0].X) * (t[2].Y - t[0].Y)) - ((t[1].Y - t[0].Y) * (t[2].X - t[0].X))) / 2);
            Assert.Equal(.07f, area, 5);
        }

        /// <summary>Ear clipping continues from each clipped ear rather than restarting at the first vertex.</summary>
        [Fact]
        public void EarClippingContinuesFromClippedEar()
        {
            // Invented convex pentagon with the implicit origin as P0, in logical units.
            Vector2[] p = [new(0, 0), new(40, 0), new(60, 30), new(20, 60), new(-20, 30)];
            AssertTriangles("20,0,30,15,10,30,-10,15,", [[p[4], p[0], p[1]], [p[1], p[2], p[3]], [p[3], p[4], p[1]]]);
        }

        /// <summary>Collinear authored vertices remain contour points instead of being removed before clipping.</summary>
        [Fact]
        public void CollinearVertexRemainsInContour()
        {
            Vector2[] p = [new(0, 0), new(40, 0), new(80, 0), new(80, 40), new(0, 40)];
            AssertTriangles("20,0,40,0,40,20,0,20,", [[p[4], p[0], p[1]], [p[1], p[2], p[3]], [p[3], p[4], p[1]]]);
        }

        /// <summary>Checks a path placed at logical (200, 400) against expected local logical-unit triangles.</summary>
        /// <param name="polygon">Editor path after the implicit origin.</param>
        /// <param name="expected">Ordered local triangles in logical units.</param>
        private static void AssertTriangles(string polygon, Vector2[][] expected)
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon_materialized x='100' y='200' material='wood' polygon='" + polygon + "' smoothFactor='1'/></layer></map>");
            TerrainShape shape = Assert.Single(TerrainGeometry.Build(level, LevelReader.Read(MaterialDesign)));
            Assert.Equal(expected.Length, shape.Triangles.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    Assert.InRange(Vector2.Distance((expected[i][j] + new Vector2(200, 400)) / 200, shape.Triangles[i][j]), 0, .000001f);
                }
            }
        }

        /// <summary>Malformed crossing paths fail instead of silently creating a solid convex hull.</summary>
        [Fact]
        public void SelfIntersectionFailsExplicitly()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon_materialized x='0' y='0' material='wood' polygon='40,40,0,40,40,0,' smoothFactor='1'/></layer></map>");
            _ = Assert.Throws<InvalidDataException>(() => TerrainGeometry.Build(level, LevelReader.Read(MaterialDesign)));
        }

        /// <summary>A generated material definition; no original game document is embedded.</summary>
        internal const string MaterialDesign = "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon id='wood' body_type='static' friction='0.4' restitution='0.4' isSensor='false'/></layer></map>";
    }
}
