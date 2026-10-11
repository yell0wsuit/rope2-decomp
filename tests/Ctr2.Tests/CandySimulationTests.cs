using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises authored rope topology and fixed-tick diagnostic replay without game assets.</summary>
    public sealed class CandySimulationTests
    {
        /// <summary>Invented master design with the supported center-body fields.</summary>
        internal const string Design = """
            <map><layer name="settings"><map width="320" height="480"/></layer><layer name="Objects">
            <circle id="candy" body_type="dynamic" radius="16" density="0.5" gravityScale="0.7" linearDamping="0" angularDamping="1" linearVelocityX="0" linearVelocityY="0" angularVelocity="0"/>
            <circle id="grab" body_type="static" radius="8" density="0.4" gravityScale="1" linearDamping="0" angularDamping="0" linearVelocityX="0" linearVelocityY="0" angularVelocity="0"/>
            <circle id="ropeMaterialPoint" body_type="dynamic" radius="3" density="0.6" gravityScale="0.6" linearDamping="0" angularDamping="0" linearVelocityX="0" linearVelocityY="0" angularVelocity="0"/>
            <rope_design id="rope_design" bone_length="40" stretch_hertz="5"/>
            </layer></map>
            """;

        /// <summary>Creates an invented map with one candy and a zero-offset grab.</summary>
        /// <param name="length">Rope length in editor units.</param>
        /// <returns>The parsed level.</returns>
        internal static LevelDefinition Map(int length = 105)
        {
            return LevelReader.Read($"<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='200' y='147'/><grab x='200' y='43' length='{length}' target_id='c' target_x='0' target_y='0'/></layer></map>");
        }

        /// <summary>Checks chain damping, stretch transitions and diagnostic release against the independent C++ reference.</summary>
        /// <param name="scenario">Reference replay with or without release at tick 60.</param>
        [Theory]
        [InlineData("game")]
        [InlineData("game_release")]
        public void GameChainMatchesIndependentReference(string scenario)
        {
            LevelDefinition map = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='70' y='40'/><grab x='0' y='0' length='105' target_id='c'/></layer></map>");
            CandySimulation scene = new(map, LevelReader.Read(Design));
            string[][] samples = [.. File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "PhysicsReference.csv"))
                .Select(line => line.Split(',')).Where(row => row[0] == scenario)];
            Assert.NotEmpty(samples);
            foreach (string[] row in samples)
            {
                int target = int.Parse(row[1], CultureInfo.InvariantCulture);
                while (scene.Tick < target)
                {
                    scene.Step(scenario == "game_release" ? 60 : -1);
                }
                int body = int.Parse(row[2], CultureInfo.InvariantCulture);
                int point = body switch
                {
                    0 => 0,
                    1 => 3,
                    2 => 1,
                    3 => 2,
                    _ => throw new InvalidDataException()
                };
                Vector2 position = scene.RopePoints(0)[point] / 200;
                Assert.InRange(MathF.Abs(position.X - float.Parse(row[3], CultureInfo.InvariantCulture)), 0, .00002f);
                Assert.InRange(MathF.Abs(position.Y - float.Parse(row[4], CultureInfo.InvariantCulture)), 0, .00002f);
                if (body == 1)
                {
                    Assert.InRange(MathF.Abs(scene.Candies[0].Velocity.X - float.Parse(row[5], CultureInfo.InvariantCulture)), 0, .00002f);
                    Assert.InRange(MathF.Abs(scene.Candies[0].Velocity.Y - float.Parse(row[6], CultureInfo.InvariantCulture)), 0, .00002f);
                }
            }
        }

        /// <summary>Preserves the native generated-point count, endpoint duplication and fractional final bone.</summary>
        [Fact]
        public void RopeTopologyMatchesNativeConstruction()
        {
            CandySimulation scene = new(Map(), LevelReader.Read(Design));
            Assert.Equal(new Vector2(400, 294), scene.Candies[0].Position * 200);
            Assert.Equal(4, scene.RopePoints(0).Count);
            Assert.Equal(scene.RopePoints(0)[2], scene.RopePoints(0)[3]);
            Assert.Equal(0.25f, scene.RopeLengths(0).Last(), 6);
            CandySimulation exact = new(Map(120), LevelReader.Read(Design));
            Assert.Equal(4, exact.RopePoints(0).Count);
            Assert.All(exact.RopeLengths(0), length => Assert.Equal(0.4f, length, 6));
        }

        /// <summary>Short ropes connect directly without evaluating the unused native division by zero.</summary>
        [Fact]
        public void ShortRopesUseOneDirectConstraint()
        {
            CandySimulation scene = new(Map(10), LevelReader.Read(Design));
            Assert.Equal(2, scene.RopePoints(0).Count);
            Assert.Equal(0.1f, Assert.Single(scene.RopeLengths(0)), 6);
        }

        /// <summary>Catch-up limits retain backlog and invalid host times cannot mutate the simulation.</summary>
        [Fact]
        public void ClockRetainsBacklogAndRejectsInvalidTime()
        {
            SimulationClock clock = new();
            int ticks = 0;
            double elapsed = 1201 * (double)PhysicsWorld.TimeStep;
            Assert.Equal(600, clock.AdvanceTo(elapsed, () => ticks++));
            Assert.Equal(600, clock.AdvanceTo(elapsed, () => ticks++));
            Assert.Equal(1, clock.AdvanceTo(elapsed, () => ticks++));
            Assert.Equal(0, clock.AdvanceTo(elapsed, () => ticks++));
            Assert.Equal(1201, ticks);
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(elapsed - 1, () => ticks++));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(double.NaN, () => ticks++));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => clock.AdvanceTo(double.PositiveInfinity, () => ticks++));
            Assert.Equal(1201, ticks);
        }

        /// <summary>Render cadence does not change tick-indexed release input or resulting state.</summary>
        [Fact]
        public void FixedTickReplayDoesNotDependOnFrameCadence()
        {
            CandySimulation a = new(Map(), LevelReader.Read(Design));
            CandySimulation b = new(Map(), LevelReader.Read(Design));
            SimulationClock clock = new();
            for (int i = 0; i < 120; i++)
            {
                a.Step(releaseAt: 30);
            }
            for (int i = 1; i <= 12; i++)
            {
                _ = clock.AdvanceTo(i * (120 * (double)PhysicsWorld.TimeStep / 12), () => b.Step(releaseAt: 30));
            }
            Assert.Equal(120, b.Tick);
            Assert.Equal(a.Candies[0].Position, b.Candies[0].Position);
            Assert.True(a.IsReleased(0));
            Assert.True(a.Candies[0].Position.Y > 3);
        }
    }
}
