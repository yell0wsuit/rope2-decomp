using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks snapshot-corrected journal replay, which re-applies recorded states like the original hint playback.</summary>
    public sealed class JournalCorrectionTests
    {
        private static CandySimulation Scene()
        {
            return new(CandySimulationTests.Map(), LevelReader.Read(CandySimulationTests.Design));
        }

        /// <summary>A full-sync state teleports the body and sets its velocities, after the drift before it was measured.</summary>
        [Fact]
        public void AppliesRecordedTransformAndVelocitiesAfterMeasuring()
        {
            CandySimulation scene = Scene();
            // Registry: the grab rope (0), then the candy (1).
            ReplayJournal journal = new("invented", 2, [], [new JournalSnapshot(2, [new JournalObjectState(1, 1.5f, 1.2f, .3f, .1f, -.2f, 2)])]);
            JournalReplayResult result = JournalReplay.Run(scene, journal, corrected: true);
            PhysicsBody candy = scene.Candies[0];
            Assert.Equal(new Vector2(1.5f, 1.2f), candy.Origin);
            Assert.Equal(.3f, candy.Angle);
            Assert.Equal(new Vector2(.1f, -.2f), candy.Velocity);
            Assert.Equal(2, candy.AngularVelocity);
            JournalSample sample = Assert.Single(result.Samples);
            Assert.NotEqual(sample.Recorded, sample.Port);
        }

        /// <summary>A lightweight state without velocities moves the body but keeps its simulated velocities.</summary>
        [Fact]
        public void PositionOnlyStateKeepsVelocities()
        {
            CandySimulation corrected = Scene(), open = Scene();
            ReplayJournal journal = new("invented", 2, [], [new JournalSnapshot(2, [new JournalObjectState(1, 1.5f, 1.2f, .3f, null, null, null)])]);
            _ = JournalReplay.Run(corrected, journal, corrected: true);
            _ = JournalReplay.Run(open, journal);
            Assert.Equal(new Vector2(1.5f, 1.2f), corrected.Candies[0].Origin);
            Assert.Equal(open.Candies[0].Velocity, corrected.Candies[0].Velocity);
            Assert.Equal(open.Candies[0].AngularVelocity, corrected.Candies[0].AngularVelocity);
        }

        /// <summary>Native playback teleports only when position and angle are all recorded, as stars and helicopters omit the angle.</summary>
        [Fact]
        public void StateWithoutAngleIsNotApplied()
        {
            CandySimulation corrected = Scene(), open = Scene();
            ReplayJournal journal = new("invented", 2, [], [new JournalSnapshot(2, [new JournalObjectState(1, 1.5f, 1.2f, null, null, null, null)])]);
            _ = JournalReplay.Run(corrected, journal, corrected: true);
            _ = JournalReplay.Run(open, journal);
            Assert.Equal(open.Candies[0].Origin, corrected.Candies[0].Origin);
        }

        /// <summary>Open-loop replay, the default, never applies snapshots.</summary>
        [Fact]
        public void OpenLoopReplayLeavesBodiesAlone()
        {
            CandySimulation scene = Scene();
            ReplayJournal journal = new("invented", 2, [], [new JournalSnapshot(2, [new JournalObjectState(1, 1.5f, 1.2f, .3f, 0, 0, 0)])]);
            _ = JournalReplay.Run(scene, journal);
            Assert.NotEqual(new Vector2(1.5f, 1.2f), scene.Candies[0].Origin);
        }

        /// <summary>Corrected measurements restart from each applied state: a recording equal to the port's own states never drifts.</summary>
        [Fact]
        public void EachMeasurementStartsFromTheLastCorrection()
        {
            CandySimulation reference = Scene();
            JournalObjectState[] states = new JournalObjectState[6];
            for (int frame = 1; frame <= 6; frame++)
            {
                reference.Step();
                PhysicsBody candy = reference.Candies[0];
                // Each recorded state is offset by a millimetre from the port's own trajectory.
                states[frame - 1] = new(1, candy.Origin.X + .001f, candy.Origin.Y, candy.Angle, null, null, null);
            }
            ReplayJournal journal = new("invented", 2, [], [.. states.Select((s, i) => new JournalSnapshot(i + 1, [s]))]);
            JournalReplayResult result = JournalReplay.Run(Scene(), journal, corrected: true);
            Assert.Equal(6, result.Samples.Count);
            // Uncorrected, the offset would stay a millimetre; corrected, the port follows the shifted recording within a hair.
            Assert.All(result.Samples.Skip(1), s => Assert.True(Vector2.Distance(s.Recorded, s.Port) < .0002f));
        }

        /// <summary>Booka::applyObjectState ignores states once Om Nom has caught the Booka, whose flight is random.</summary>
        [Fact]
        public void CaughtBookaIsNotCorrected()
        {
            CandySimulation scene = BookaTests.Create("<candy id='c' x='300' y='20'/><omnom x='100' y='300'/><booka id='b' x='125' y='300'/>" +
                "<grab x='125' y='200' length='120' target_id='b'/>");
            // Registry: the grab rope (0), candy (1), Booka (2) and Om Nom (3); Om Nom catches the Booka in the first step.
            ReplayJournal journal = new("invented", 2, [], [new JournalSnapshot(1, [new JournalObjectState(2, 1, 1, 0, 0, 0, 0)])]);
            _ = JournalReplay.Run(scene, journal, corrected: true);
            Assert.True(scene.Bookas[0].Booed);
            Assert.NotEqual(Vector2.One, scene.Bookas[0].Body.Origin);
        }
    }
}
