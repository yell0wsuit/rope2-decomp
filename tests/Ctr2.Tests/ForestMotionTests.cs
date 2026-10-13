using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks source motion windows and falling/landing through actual terrain simulation.</summary>
    public sealed class ForestMotionTests
    {
        /// <summary>Builds invented motion scenarios with a stationary candy and ordinary floor material.</summary>
        internal static CandySimulation Create(string objects)
        {
            string design = CandySimulationTests.Design.Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"")
                .Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "<polygon id='wood' body_type='static' friction='0.4' restitution='0' isSensor='false'/></layer></map>");
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"), LevelReader.Read(design), true);
        }

        /// <summary>History starts with 59 zeros and averages newest samples in reverse insertion order.</summary>
        [Fact]
        public void HistoryUsesZeroFilledSourceWindows()
        {
            CharacterMotionHistory history = new();
            history.Sample(new(5, 10));
            Assert.Equal(new Vector2(1, 2), history.Short);
            Assert.Equal(new Vector2(5, 10) / 29, history.Medium);
            Assert.Equal(new Vector2(5, 10) / 59, history.Long);
            for (int i = 0; i < 58; i++)
            {
                history.Sample(new(1, 1));
            }
            Assert.Equal(Vector2.One, history.Short);
            Assert.Equal(Vector2.One, history.Medium);
            Assert.Equal(new Vector2(63, 68) / 59, history.Long);
            history.Sample(Vector2.One);
            Assert.Equal(Vector2.One, history.Long);
        }

        /// <summary>Fall detection requires both its directional threshold and consistent recent motion.</summary>
        [Theory]
        [InlineData(2, false)]
        [InlineData(2.01f, true)]
        [InlineData(-3, false)]
        [InlineData(-3.01f, true)]
        public void FallThresholdMatchesSource(float velocity, bool falling)
        {
            CharacterMotionHistory history = new();
            for (int i = 0; i < 59; i++)
            {
                history.Sample(new(0, velocity));
            }
            Assert.Equal(falling, history.IsFalling);
        }

        /// <summary>A short reversal does not count as a sustained fall.</summary>
        [Fact]
        public void FallRequiresPositiveShortMediumDot()
        {
            CharacterMotionHistory history = new();
            for (int i = 0; i < 59; i++)
            {
                history.Sample(new(0, -10));
            }
            for (int i = 0; i < 5; i++)
            {
                history.Sample(new(0, 3));
            }
            Assert.False(history.IsFalling);
        }

        /// <summary>A real falling polygon settles through actual terrain contacts before returning to idle.</summary>
        [Fact]
        public void TerrainLandingRequiresContactAndSettledHistory()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='0'/><omnom x='100' y='30' gravityScale='1'/><polygon_materialized x='0' y='250' material='wood' polygon='320,0,320,30,0,30,' smoothFactor='1'/>");
            bool fell = false;
            for (int i = 0; i < 180; i++)
            {
                scene.Step();
                fell |= scene.Gameplay!.CharacterState == ForestCharacterState.Falling;
            }
            Assert.True(fell);
            Assert.Equal(ForestCharacterState.Idle, scene.Gameplay!.CharacterState);
            Assert.Equal("IDLE", scene.Gameplay.Scene);
            Assert.Equal(ForestResult.Running, scene.Gameplay.Result);
        }

        /// <summary>Sensor polygons cannot satisfy the landing-contact latch.</summary>
        [Fact]
        public void SensorTerrainDoesNotLandCharacter()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='0'/><omnom x='100' y='30' gravityScale='1'/><polygon_materialized x='0' y='100' material='wood' isSensor='true' polygon='320,0,320,30,0,30,' smoothFactor='1'/>");
            for (int i = 0; i < 35; i++)
            {
                scene.Step();
            }
            Assert.Equal(ForestCharacterState.Falling, scene.Gameplay!.CharacterState);
        }

        /// <summary>Airborne open-mouth consumption follows the distinct falling chewing callbacks.</summary>
        [Fact]
        public void FallingMouthContactUsesFallingChewScenes()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='200'/><omnom x='100' y='0' gravityScale='1'/>");
            bool opened = false;
            for (int i = 0; i < 100 && scene.Gameplay!.Consumptions.Count == 0; i++)
            {
                scene.Step();
                opened |= scene.Gameplay!.CharacterState == ForestCharacterState.FallingMouthOpen;
            }
            Assert.True(opened);
            Assert.NotEmpty(scene.Gameplay!.Consumptions);
            Assert.Equal(ForestCharacterState.FallingChewing, scene.Gameplay.CharacterState);
            Assert.Equal("FALLING_MOUTH_CLOSE", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, scene.Gameplay.SceneRevision);
            Assert.Equal("FALLING_CHEWING", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, scene.Gameplay.SceneRevision);
            Assert.Equal(ForestResult.Won, scene.Gameplay.Result);
        }

        /// <summary>A missed nearby candy closes the airborne mouth; stale open-scene callbacks do nothing.</summary>
        [Fact]
        public void FallingMouthClosesAfterPassingCandy()
        {
            CandySimulation scene = Create("<candy id='c' x='155' y='200'/><omnom x='100' y='0' gravityScale='1'/>");
            int openRevision = -1;
            for (int i = 0; i < 100 && scene.Gameplay!.CharacterState != ForestCharacterState.FallingMouthClose; i++)
            {
                scene.Step();
                if (scene.Gameplay!.CharacterState == ForestCharacterState.FallingMouthOpen)
                {
                    openRevision = scene.Gameplay.SceneRevision;
                }
            }
            Assert.True(openRevision >= 0);
            Assert.Equal(ForestCharacterState.FallingMouthClose, scene.Gameplay!.CharacterState);
            scene.Gameplay.SceneFinished("FALLING_MOUTH_OPEN", openRevision);
            Assert.Equal("FALLING_MOUTH_CLOSE", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, scene.Gameplay.SceneRevision);
            Assert.Equal(ForestCharacterState.Falling, scene.Gameplay.CharacterState);
            Assert.Equal("FALLING", scene.Gameplay.Scene);
        }

        /// <summary>Chewing landing waits for terrain and long-term settling; an old airborne callback cannot win.</summary>
        [Fact]
        public void FallingChewLandingReplacesAirborneScene()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='190'/><omnom x='100' y='0' gravityScale='1'/><polygon_materialized x='0' y='250' material='wood' polygon='320,0,320,30,0,30,' smoothFactor='1'/>");
            for (int i = 0; i < 100 && scene.Gameplay!.Consumptions.Count == 0; i++)
            {
                scene.Step();
            }
            Assert.Equal(ForestCharacterState.FallingChewing, scene.Gameplay!.CharacterState);
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, scene.Gameplay.SceneRevision);
            int airborneRevision = scene.Gameplay.SceneRevision;
            for (int i = 0; i < 100 && scene.Gameplay.CharacterState != ForestCharacterState.Chewing; i++)
            {
                scene.Step();
            }
            Assert.Equal(ForestCharacterState.Chewing, scene.Gameplay.CharacterState);
            Assert.Equal("CHEWING", scene.Gameplay.Scene);
            Assert.Equal(ForestResult.Running, scene.Gameplay.Result);
            scene.Gameplay.SceneFinished("FALLING_CHEWING", airborneRevision);
            Assert.Equal(ForestResult.Running, scene.Gameplay.Result);
        }

        /// <summary>Restart discards airborne state and its accumulated velocity history.</summary>
        [Fact]
        public void RestartClearsFallingHistory()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='300' y='0'/><omnom x='100' y='0' gravityScale='1'/></layer></map>");
            LevelDefinition design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>"));
            ForestSession session = new(level, design);
            for (int i = 0; i < 30; i++)
            {
                session.Step();
            }
            Assert.Equal(ForestCharacterState.Falling, session.Simulation.Gameplay!.CharacterState);
            session.Restart();
            session.Step();
            Assert.Equal(ForestCharacterState.Idle, session.Simulation.Gameplay!.CharacterState);
            Assert.Equal("IDLE", session.Simulation.Gameplay.Scene);
        }
    }
}
