using System;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks playable session time/input ownership across pause, restart and completion.</summary>
    public sealed class ForestSessionTests
    {
        internal static ForestSession Create()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='100' y='100'/><omnom x='100' y='100'/></layer></map>");
            LevelDefinition design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>").Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"").Replace("gravityScale='0.7'", "gravityScale='0'"));
            return new(level, design);
        }

        /// <summary>Restart recreates state and rebases time without replaying stale input.</summary>
        [Fact]
        public void RestartReplacesStateAndInputAndRebasesHostClock()
        {
            ForestSession session = Create();
            CandySimulation original = session.Simulation;
            RopeInput originalInput = session.Input;
            session.AdvanceTo(.1);
            Assert.NotEmpty(original.Gameplay!.Consumptions);
            originalInput.Begin(new Vector2(200, 100), 640, 960);
            originalInput.Move(new Vector2(200, 200), 640, 960);
            session.Restart();
            Assert.NotSame(original, session.Simulation);
            Assert.NotSame(originalInput, session.Input);
            Assert.Equal(0, session.Simulation.Tick);
            Assert.Empty(session.Simulation.Gameplay!.Consumptions);
            Assert.Empty(session.Input.Cuts);
            session.AdvanceTo(100);
            Assert.Equal(0, session.Simulation.Tick);
            session.AdvanceTo(100 + (double)PhysicsWorld.TimeStep);
            Assert.Equal(1, session.Simulation.Tick);
        }

        /// <summary>Paused wall time never becomes simulation catch-up.</summary>
        [Fact]
        public void PauseCancelsInputAndDoesNotAccumulateCatchup()
        {
            ForestSession session = Create();
            session.AdvanceTo(.1);
            int tick = session.Simulation.Tick;
            session.Paused = true;
            session.AdvanceTo(10);
            session.Step();
            Assert.Equal(tick, session.Simulation.Tick);
            session.Paused = false;
            session.AdvanceTo(20);
            Assert.Equal(tick, session.Simulation.Tick);
            session.AdvanceTo(20 + (double)PhysicsWorld.TimeStep);
            Assert.Equal(tick + 1, session.Simulation.Tick);
        }

        /// <summary>Terminal results freeze ticks until a fresh restart.</summary>
        [Fact]
        public void TerminalSessionStopsTicksAndRestartRestoresRunning()
        {
            ForestSession session = Create();
            for (int i = 0; i < 180; i++)
            {
                session.Step();
            }
            Assert.Equal(ForestResult.Won, session.Simulation.Gameplay!.Result);
            int tick = session.Simulation.Tick;
            session.AdvanceTo(100);
            session.Step();
            Assert.Equal(tick, session.Simulation.Tick);
            session.Restart();
            Assert.False(session.Paused);
            Assert.Equal(ForestResult.Running, session.Simulation.Gameplay!.Result);
            Assert.Equal(0, session.Simulation.Tick);
        }

        /// <summary>Rejects invalid host time before changing session state.</summary>
        /// <param name="seconds">Invalid elapsed seconds.</param>
        [Theory]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidHostTimeIsRejected(double seconds)
        {
            ForestSession session = Create();
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTo(seconds));
        }

        /// <summary>Restart retains the monotonic host-time validation boundary.</summary>
        [Fact]
        public void BackwardTimeAfterRestartIsRejected()
        {
            ForestSession session = Create();
            session.AdvanceTo(1);
            session.Restart();
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTo(.5));
        }
    }
}
