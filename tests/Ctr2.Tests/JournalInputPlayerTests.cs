using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks frame-by-frame journal input playback with an invented journal.</summary>
    public sealed class JournalInputPlayerTests
    {
        /// <summary>Inputs apply just before the step after their frame; a cut of an object that is not a rope is reported
        /// instead of applied.</summary>
        [Fact]
        public void AppliesEachFrameBeforeItsStepAndReportsUnusableCuts()
        {
            CandySimulation scene = new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
            ReplayJournal journal = new("", 2,
            [
                new(2, JournalEventType.RopeCut, 400, 150, 0, 7, 0),
                new(3, JournalEventType.RopeCut, 400, 150, 0, 0, 1),
                new(3, JournalEventType.StarCatch, 0, 0, 0, 4, 0)
            ], []);
            JournalInputPlayer player = new(journal);
            Assert.Equal(3, player.LastFrame);
            for (int frame = 0; frame < 3; frame++)
            {
                Assert.Equal(frame == 2 ? 1 : 0, player.Apply(scene).Count);
                scene.Step();
            }
            _ = Assert.Single(scene.RopeSegments(0));
            _ = Assert.Single(player.Unsupported);
            Assert.Equal(2, player.Apply(scene).Count);
            scene.Step();
            Assert.Equal(2, scene.RopeSegments(0).Count);
        }
    }
}
