using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks fitted pointer input, cancellation, and tick-indexed cut replay.</summary>
    public sealed class RopeInputTests
    {
        /// <summary>Portrait and ultrawide drags select the same bone through the drawing inverse.</summary>
        /// <param name="width">Output pixel width.</param>
        /// <param name="height">Output pixel height.</param>
        [Theory]
        [InlineData(640, 960)]
        [InlineData(3440, 1440)]
        [InlineData(1280, 1920)]
        public void PointerMappingMatchesDrawingAndReplay(int width, int height)
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            RopeInput input = new(scene, new(640, 960));
            Viewport viewport = Viewport.Fit(width, height, 640, 960);
            input.Begin(viewport.ToScreen(new(350, 138)), width, height);
            input.Move(viewport.ToScreen(new(450, 138)), width, height);
            input.End();
            Assert.Empty(input.Cuts);
            input.ApplyPending();
            RopeCut cut = Assert.Single(input.Cuts);
            CandySimulation replay = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            Assert.True(replay.Cut(cut.Rope, cut.Segment, cut.Point, cut.Impulse));
            for (int i = 0; i < 120; i++)
            {
                scene.Step();
                replay.Step();
                Assert.Equal(scene.Candies[0].Position, replay.Candies[0].Position);
                Assert.Equal(scene.RopePoints(0), replay.RopePoints(0));
            }
        }

        /// <summary>Focus loss and letterbox presses cannot leave a held pointer or delayed cut.</summary>
        [Fact]
        public void CancellationDiscardsHeldAndQueuedInput()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            RopeInput input = new(scene, new(640, 960));
            input.Begin(new(350, 138), 640, 960);
            input.Move(new(450, 138), 640, 960);
            input.Cancel();
            input.ApplyPending();
            Assert.Empty(input.Cuts);
            input.Begin(new(0, 138), 3440, 1440);
            input.Move(Viewport.Fit(3440, 1440, 640, 960).ToScreen(new(450, 138)), 3440, 1440);
            input.ApplyPending();
            Assert.Empty(input.Cuts);
        }
    }
}
