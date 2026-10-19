using System;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks source stand-up thresholds and impulses through fixed simulation ticks.</summary>
    public sealed class StandingTests
    {
        private static CandySimulation Create(float angle, bool chewing = false)
        {
            string candy = chewing ? "<candy id='c' x='100' y='300'/>" : "<candy id='c' x='300' y='0'/>";
            return CharacterMotionTests.Create(candy + FormattableString.Invariant($"<omnom x='100' y='{(chewing ? 300 : 100)}' angle='{angle}' gravityScale='0'/>"));
        }

        /// <summary>Angles are truncated to integer degrees before applying the strict 60-degree threshold.</summary>
        [Theory]
        [InlineData(-60.9f, false)]
        [InlineData(60.9f, false)]
        [InlineData(-61.2f, true)]
        [InlineData(61.2f, true)]
        [InlineData(450, true)]
        [InlineData(-450, true)]
        public void StationaryTiltUsesNormalizedIntegerThreshold(float angle, bool stands)
        {
            CandySimulation scene = Create(angle);
            scene.Step();
            Assert.Equal(stands ? "StandUp" : "Idle", scene.Gameplay!.CharacterState.ToString());
        }

        /// <summary>Preparation does not move the body; completion applies a center impulse and signed angular impulse.</summary>
        [Theory]
        [InlineData(-90, .008f)]
        [InlineData(90, -.008f)]
        [InlineData(-120, .014f)]
        [InlineData(120, -.014f)]
        [InlineData(-110.9f, .008f)]
        [InlineData(110.9f, -.008f)]
        [InlineData(-111.2f, .014f)]
        [InlineData(111.2f, -.014f)]
        public void PreparationAppliesSourceImpulsesOnce(float angle, float angularImpulse)
        {
            CandySimulation scene = Create(angle);
            PhysicsBody body = scene.Characters[0];
            Vector2 position = body.Position;
            scene.Step();
            Assert.Equal("StandUp", scene.Gameplay!.CharacterState.ToString());
            int oldRevision = scene.Gameplay.SceneRevision;
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, oldRevision);
            for (int i = 0; i < 8; i++)
            {
                scene.Step();
            }

            Assert.Equal(Vector2.Zero, body.Velocity);
            Assert.Equal(0, body.AngularVelocity);
            scene.Step();
            Assert.Equal(position, body.Position);
            Assert.InRange(body.Velocity.Y, (-.19f / body.Mass) - .00001f, (-.19f / body.Mass) + .00001f);
            float inertia = body.Mass * ((.2f * .2f) + (.4f * .4f)) / 12;
            Assert.InRange(body.AngularVelocity, (angularImpulse / inertia) - .0001f, (angularImpulse / inertia) + .0001f);
            Assert.Equal("STAND_UP", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished("IDLE", oldRevision);
            Assert.Equal("STAND_UP", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished("STAND_UP", scene.Gameplay.SceneRevision);
            Assert.Equal(CharacterState.Idle, scene.Gameplay.CharacterState);
            float velocity = body.Velocity.Y;
            scene.Gameplay.SceneFinished("STAND_UP", scene.Gameplay.SceneRevision);
            Assert.Equal(velocity, body.Velocity.Y);
        }

        /// <summary>The stand impulse respects the body's authored fixed-rotation constraint.</summary>
        [Fact]
        public void FixedRotationKeepsAngleDuringStandImpulse()
        {
            CandySimulation scene = CharacterMotionTests.Create("<candy id='c' x='300' y='0'/><omnom x='100' y='100' angle='90' gravityScale='0' fixedRotation='true'/>");
            float angle = scene.Characters[0].Angle;
            for (int i = 0; i < 10; i++)
            {
                scene.Step();
            }

            Assert.Equal("STAND_UP", scene.Gameplay!.Scene);
            Assert.Equal(0, scene.Characters[0].AngularVelocity);
            Assert.Equal(angle, scene.Characters[0].Angle);
            Assert.True(scene.Characters[0].Velocity.Y < 0);
        }

        /// <summary>A tilted body must settle before stand preparation can begin.</summary>
        [Fact]
        public void MovingTiltDoesNotStartStanding()
        {
            CandySimulation scene = CharacterMotionTests.Create("<candy id='c' x='300' y='0'/><omnom x='100' y='100' angle='90' gravityScale='1'/>");
            scene.Step();
            Assert.False(scene.Gameplay!.ProcedureActive);
            Assert.Equal(CharacterState.Idle, scene.Gameplay.CharacterState);
        }

        /// <summary>Preparation peaks at .1 seconds and remains independent of pause and restart.</summary>
        [Theory]
        [InlineData(-90, -10)]
        [InlineData(90, 10)]
        public void PreparationPosePausesAndRestartClearsItsCallback(float angle, float offset)
        {
            LevelDefinition level = LevelReader.Read(FormattableString.Invariant($"<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='300' y='0' gravityScale='0'/><omnom x='100' y='100' angle='{angle}' gravityScale='0'/></layer></map>"));
            LevelDefinition design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>"));
            GameSession session = new(level, design);
            for (int i = 0; i < 7; i++)
            {
                session.Step();
            }

            LevelGameplay oldGame = session.Simulation.Gameplay!;
            Assert.True(oldGame.ProcedureActive);
            float weight = 1 - (((6 * PhysicsWorld.TimeStep) - .1f) / .05f);
            Assert.InRange(Vector2.Distance(Vector2.Lerp(Vector2.One, new(.85f, 1.3f), weight), oldGame.CharacterScale), 0, .00001f);
            Assert.InRange(Vector2.Distance(new(offset * weight, 0), oldGame.CharacterOffset), 0, .00001f);
            session.Paused = true;
            for (int i = 0; i < 100; i++)
            {
                session.Step();
            }

            Assert.Equal(7, session.Simulation.Tick);
            Assert.Equal(Vector2.Zero, session.Simulation.Characters[0].Velocity);
            session.Restart();
            oldGame.SceneFinished(oldGame.Scene, oldGame.SceneRevision);
            Assert.Equal(0, session.Simulation.Tick);
            Assert.Equal(Vector2.One, session.Simulation.Gameplay!.CharacterScale);
            Assert.Equal(Vector2.Zero, session.Simulation.Characters[0].Velocity);
            for (int i = 0; i < 9; i++)
            {
                session.Step();
            }

            Assert.Equal(Vector2.Zero, session.Simulation.Characters[0].Velocity);
            session.Step();
            Assert.True(session.Simulation.Characters[0].Velocity.Y < 0);
        }

        /// <summary>Eating during preparation replaces it, so the old stand impulse never fires.</summary>
        [Fact]
        public void ConsumptionInterruptsPreparationImpulse()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='100' y='-50'/><omnom x='100' y='100' angle='90' gravityScale='0'/></layer></map>");
            LevelDefinition design = LevelReader.Read(CandySimulationTests.Design.Replace("gravityScale=\"0.7\"", "gravityScale=\"152\"").Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>"));
            CandySimulation scene = new(level, design, play: true);
            scene.Step();
            Assert.Equal(CharacterState.StandUp, scene.Gameplay!.CharacterState);
            for (int i = 0; i < 20 && scene.Gameplay.Consumptions.Count == 0; i++)
            {
                scene.Step();
            }

            _ = Assert.Single(scene.Gameplay.Consumptions);
            Assert.InRange(scene.Tick, 2, 9);
            // The replacement may start its own chewing preparation; check the old deadline.
            int replacementStarted = scene.Tick;
            while (scene.Tick < 10)
            {
                scene.Step();
            }

            Assert.Equal(Vector2.Zero, scene.Characters[0].Velocity);
            Assert.Equal(0, scene.Characters[0].AngularVelocity);
            while (scene.Tick < replacementStarted + 9)
            {
                scene.Step();
            }

            Assert.True(scene.Characters[0].Velocity.Y < 0);
        }

        /// <summary>Standing during chewing preserves the original completion fallback.</summary>
        [Fact]
        public void ChewingStandUpStillCompletes()
        {
            CandySimulation scene = Create(90, chewing: true);
            scene.Step();
            Assert.Equal("StandUpChewing", scene.Gameplay!.CharacterState.ToString());
            for (int i = 0; i < 180 && scene.Gameplay.Result == LevelResult.Running; i++)
            {
                scene.Step();
            }

            Assert.Equal(LevelResult.Won, scene.Gameplay.Result);
            _ = Assert.Single(scene.Gameplay.Consumptions);
            Assert.Equal(CandyState.Removed, scene.Gameplay.CandyState);
            Assert.InRange(scene.Gameplay.FinishedAt!.Value, 168, 169);
        }
    }
}
