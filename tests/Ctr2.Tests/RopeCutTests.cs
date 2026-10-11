using System;
using System.Collections.Generic;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks physical cut fragments and deterministic crossing selection.</summary>
    public sealed class RopeCutTests
    {
        /// <summary>A replay cut adds two tips, leaves a gap, and cannot cut the same rope twice.</summary>
        [Fact]
        public void CutCreatesTwoIndependentFragments()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            Vector2 crossing = new(400, 138);
            Assert.True(scene.Cut(0, 0, crossing));
            Assert.Equal(6, scene.RopePoints(0).Count);
            Assert.Equal(crossing, scene.RopePoints(0)[1]);
            Assert.Equal(crossing, scene.RopePoints(0)[2]);
            Assert.Equal(2, scene.RopeSegments(0).Count);
            Assert.Equal(2, scene.RopeSegments(0)[0].Count);
            Assert.Equal(4, scene.RopeSegments(0)[1].Count);
            Assert.False(scene.Cut(0, 0, crossing));
            for (int i = 0; i < 120; i++)
            {
                scene.Step();
            }
            Assert.True(scene.Candies[0].Position.Y > 4);
            Assert.True(scene.RopePoints(0)[2].Y > scene.RopePoints(0)[1].Y + 100);
        }

        /// <summary>A swipe misses without changing topology and crosses once in authored segment order.</summary>
        [Fact]
        public void SwipeCutsCrossingSegmentOnlyOnce()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            Assert.Empty(scene.Swipe(new(0, 120), new(100, 120)));
            Assert.Equal(4, scene.RopePoints(0).Count);
            IReadOnlyList<RopeCut> cuts = scene.Swipe(new(350, 138), new(450, 138));
            RopeCut cut = Assert.Single(cuts);
            Assert.Equal(0, cut.Tick);
            Assert.Equal(0, cut.Rope);
            Assert.Equal(0, cut.Segment);
            Assert.InRange(cut.Point.Y, 135, 141);
            Assert.Empty(scene.Swipe(new(350, 138), new(450, 138)));
        }

        /// <summary>Invalid replay coordinates are rejected before topology changes.</summary>
        [Fact]
        public void InvalidCutCannotMutateTopology()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => scene.Cut(0, 10, Vector2.Zero));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => scene.Cut(0, 0, new(float.NaN, 0)));
            Assert.Equal(4, scene.RopePoints(0).Count);
        }
    }
}
