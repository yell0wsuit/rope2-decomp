using System;
using System.Globalization;
using System.IO;
using System.Linq;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises stationary candy/star sensor timing and deterministic objective state.</summary>
    public sealed class StarCollectionTests
    {
        /// <summary>Synthetic stationary sensor master record.</summary>
        private const string StarDesign = "<circle id='star' body_type='kinematic' radius='13' isSensor='true' linearVelocityX='0' linearVelocityY='0' angularVelocity='0'/>";

        /// <summary>Checks sensor timing and untouched candy trajectories against synthetic upstream Box2D.</summary>
        /// <param name="scenario">Falling, fast passage, exact tangent or separated circles.</param>
        [Theory]
        [InlineData("fall")]
        [InlineData("fast")]
        [InlineData("tangent")]
        [InlineData("outside")]
        public void MatchesNumericalSensorFixture(string scenario)
        {
            bool boundary = scenario is "tangent" or "outside";
            string candy = scenario switch
            {
                "tangent" => "<candy id='c' x='-29' y='0'/>",
                "outside" => "<candy id='c' x='-29' y='0'/>",
                _ => "<candy id='c' x='0' y='0'/>"
            };
            string stars = boundary ? "<star x='0' y='0'/><star x='200' y='0'/><star x='200' y='0'/>" : "<star x='0' y='100'/><star x='0' y='200'/><star x='60' y='200'/>";
            string design = CandySimulationTests.Design.Replace("</layer></map>", StarDesign + "</layer></map>");
            if (scenario == "fast")
            {
                design = design.Replace("gravityScale=\"0.7\"", "gravityScale=\"800\"");
            }
            if (boundary)
            {
                design = design.Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"");
            }
            if (scenario == "outside")
            {
                stars = stars.Replace("<star x='0' y='0'/>", "<star x='0' y='0' radius='12.999'/>");
            }
            CandySimulation scene = Create(candy + stars, design);
            string[][] samples = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "StarReference.csv")).Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.Equal(120, samples.Length);
            foreach (string[] row in samples)
            {
                Assert.Equal(scene.Tick, int.Parse(row[1], CultureInfo.InvariantCulture));
                scene.Step();
                int mask = scene.Stars.Select((star, index) => star.CollectedAt.HasValue ? 1 << index : 0).Aggregate(0, (a, b) => a | b);
                Assert.Equal(int.Parse(row[2], CultureInfo.InvariantCulture), mask);
                float expectedX = float.Parse(row[3], CultureInfo.InvariantCulture);
                Assert.InRange(MathF.Abs(scene.Candies[0].Position.X - expectedX), 0, .00002f);
                Assert.InRange(MathF.Abs(scene.Candies[0].Position.Y - float.Parse(row[4], CultureInfo.InvariantCulture)), 0, .00002f);
            }
            Assert.Equal(scene.Stars.Count(s => s.CollectedAt.HasValue), scene.StarCollections.Count);
        }

        /// <summary>Overlapping candies create one immutable collection record per star.</summary>
        [Fact]
        public void CollectionIsOnceAndSnapshotsRemainImmutable()
        {
            CandySimulation scene = Create("<candy id='a' x='0' y='0'/><candy id='b' x='0' y='0'/><star x='0' y='0'/>");
            SimulationStar initial = Assert.Single(scene.Stars);
            scene.Step();
            Assert.Null(initial.CollectedAt);
            Assert.Equal(0, Assert.Single(scene.Stars).CollectedAt);
            Assert.Equal(new StarCollection(0, 0, 0), Assert.Single(scene.StarCollections));
            for (int i = 0; i < 60; i++)
            {
                scene.Step();
            }
            _ = Assert.Single(scene.StarCollections);
        }

        /// <summary>Grab sensors and rope material points cannot collect stars on their own.</summary>
        [Fact]
        public void OnlyCandyCollectsStars()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='100'/><grab x='0' y='0' length='200' target_id='c'/><star x='0' y='0'/><star x='25' y='25'/>");
            scene.Step();
            Assert.Empty(scene.StarCollections);
        }

        /// <summary>Rendering cadence cannot alter zero-based contact ticks.</summary>
        [Fact]
        public void CollectionReplayIsIndependentOfFrameCadence()
        {
            const string objects = "<candy id='c' x='0' y='0'/><star x='0' y='100'/><star x='0' y='200'/>";
            CandySimulation a = Create(objects), b = Create(objects);
            for (int i = 0; i < 120; i++)
            {
                a.Step();
            }
            SimulationClock clock = new();
            for (int frame = 1; frame <= 12; frame++)
            {
                _ = clock.AdvanceTo(frame * 10 * (double)PhysicsWorld.TimeStep, () => b.Step());
            }
            Assert.Equal(a.StarCollections.ToArray(), b.StarCollections.ToArray());
            Assert.Equal(2, a.StarCollections.Count);
        }

        /// <summary>A star with an open path ping-pongs along it from rest, and a cyclic one keeps circling at a constant parameter
        /// speed; both move only through the velocity their path mover sets after each step.</summary>
        [Fact]
        public void PathStarsMoveAlongTheirPaths()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='400'/><star x='50' y='50' path='60,0,' cyclePath='false' speed='1'/>" +
                "<star x='150' y='150' path='0,-30,30,-30,30,0,' cyclePath='true' speed='1'/><star x='250' y='50'/>");
            System.Numerics.Vector2[] start = [.. scene.Stars.Select(s => s.Position)];
            scene.Step();
            // The movers set velocities after the first step, so nothing has moved yet.
            Assert.Equal(start, scene.Stars.Select(s => s.Position));
            float lastX = start[0].X;
            for (int i = 0; i < 59; i++)
            {
                scene.Step();
                Assert.True(scene.Stars[0].Position.X >= lastX);
                lastX = scene.Stars[0].Position.X;
            }
            // The open path eases in for its first second and stays on its horizontal line.
            Assert.InRange(scene.Stars[0].Position.X - start[0].X, .05f, .6f);
            Assert.InRange(MathF.Abs(scene.Stars[0].Position.Y - start[0].Y), 0, 1e-4f);
            Assert.True(System.Numerics.Vector2.Distance(scene.Stars[1].Position, start[1]) > .05f);
            Assert.Equal(start[2], scene.Stars[2].Position);
        }

        /// <summary>Unsupported motion and invalid overrides fail instead of silently freezing stars.</summary>
        /// <param name="attributes">Invalid or unsupported placement overrides.</param>
        [Theory]
        [InlineData("normalizedPath='0,0,1,1'")]
        [InlineData("linearVelocityX='1'")]
        [InlineData("linearVelocityY='NaN'")]
        [InlineData("angularVelocity='1'")]
        [InlineData("radius='0'")]
        [InlineData("radius='NaN'")]
        [InlineData("body_type='dynamic'")]
        [InlineData("isSensor='false'")]
        [InlineData("filter.collideWith='none'")]
        public void UnsupportedStarsFailExplicitly(string attributes)
        {
            _ = Assert.Throws<InvalidDataException>(() => Create("<candy id='c' x='0' y='0'/><star x='0' y='0' " + attributes + "/>"));
        }

        /// <summary>Creates an invented scene and matching master design without game assets.</summary>
        /// <param name="objects">Authored objects fragment.</param>
        /// <param name="design">Optional complete design override.</param>
        /// <returns>The initialized physics scene.</returns>
        private static CandySimulation Create(string objects, string? design = null)
        {
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design ?? CandySimulationTests.Design.Replace("</layer></map>", StarDesign + "</layer></map>")));
        }
    }
}
