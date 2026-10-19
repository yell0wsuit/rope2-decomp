using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises single-candy gameplay through real simulation steps and invented geometry.</summary>
    public sealed class GameplayTests
    {
        /// <summary>Builds a stationary invented scene without original assets.</summary>
        internal static CandySimulation Create(string objects = "<candy id='c' x='100' y='100'/><omnom x='100' y='100'/>", bool play = true,
            IReadOnlyDictionary<string, double>? scenes = null)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                "<circle id='star' body_type='kinematic' radius='13' isSensor='true'/>" + "</layer></map>")
                .Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"")
                .Replace("gravityScale='0.7'", "gravityScale='0'");
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), play, characterScenes: scenes);
        }

        /// <summary>Contact consumes once, retains a fading sensor briefly, then completes after chewing.</summary>
        [Fact]
        public void ContactStartsEatingBeforeIntegrationAndCompletesOnceAfterChewing()
        {
            CandySimulation scene = Create();
            LevelGameplay game = scene.Gameplay!;
            scene.Step();
            Assert.Equal(new CandyConsumption(0, 0, 0), Assert.Single(game.Consumptions));
            Assert.Equal(LevelResult.Running, game.Result);
            Assert.Equal(CharacterState.Chewing, game.CharacterState);
            Assert.Equal(CandyState.Eating, game.CandyState);
            Assert.True(scene.Candies[0].IsSensor);
            Assert.Equal(0, scene.Candies[0].GravityScale);
            for (int i = 1; i < 11; i++)
            {
                scene.Step();
            }
            Assert.Equal(CandyState.Eating, game.CandyState);
            scene.Step();
            Assert.Equal(CandyState.Removed, game.CandyState);
            Vector2 removedAt = scene.Candies[0].Position;
            for (int i = 12; i < 167; i++)
            {
                scene.Step();
            }
            Assert.Equal(removedAt, scene.Candies[0].Position);
            Assert.Equal(LevelResult.Running, game.Result);
            scene.Step();
            Assert.Equal(LevelResult.Won, game.Result);
            int? finished = game.FinishedAt;
            for (int i = 0; i < 20; i++)
            {
                scene.Step();
            }
            Assert.Equal(finished, game.FinishedAt);
            _ = Assert.Single(game.Consumptions);
        }

        /// <summary>World-space origin and rotation determine fixture contact, not a goal circle.</summary>
        [Theory]
        [InlineData(125, 100, 90, true)]
        [InlineData(130, 100, 0, false)]
        public void ContactUsesRotatedCompoundFixtures(int candyX, int candyY, int angle, bool consumed)
        {
            CandySimulation scene = Create($"<candy id='c' x='{candyX}' y='{candyY}'/><omnom x='100' y='100' angle='{angle}'/>");
            scene.Step();
            Assert.Equal(consumed, scene.Gameplay!.Consumptions.Count == 1);
        }

        /// <summary>Nearby but separated candy does not feed Om Nom without physical contact.</summary>
        [Fact]
        public void ContactUsesPreStepPositions()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='50'/><omnom x='100' y='100'/>");
            for (int i = 0; i < 10; i++)
            {
                scene.Step();
            }
            Assert.Empty(scene.Gameplay!.Consumptions);
        }

        /// <summary>Leaving the scene while chewing loses before a later completion callback can win.</summary>
        [Fact]
        public void FallingOutDuringChewingLoses()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='530'/><omnom x='100' y='530' gravityScale='100'/>");
            scene.Step();
            Assert.Equal(CharacterState.Chewing, scene.Gameplay!.CharacterState);
            scene.Step();
            Assert.Equal(LevelResult.Lost, scene.Gameplay.Result);
            scene.Gameplay.SceneFinished(scene.Gameplay.Scene, scene.Gameplay.SceneRevision);
            Assert.Equal(LevelResult.Lost, scene.Gameplay.Result);
        }

        /// <summary>Primary animation callbacks advance chewing and win once while stale callbacks are ignored.</summary>
        [Fact]
        public void ChewingCallbacksUseCurrentSceneRevision()
        {
            CandySimulation scene = Create();
            scene.Step();
            LevelGameplay game = scene.Gameplay!;
            int oldRevision = game.SceneRevision;
            game.SceneFinished("MOUTH_CLOSE", oldRevision - 1);
            Assert.Equal("MOUTH_CLOSE", game.Scene);
            game.SceneFinished("MOUTH_CLOSE", oldRevision);
            Assert.Equal("CHEWING", game.Scene);
            game.SceneFinished("MOUTH_CLOSE", oldRevision);
            Assert.Equal(LevelResult.Running, game.Result);
            game.SceneFinished("CHEWING", game.SceneRevision);
            Assert.Equal(LevelResult.Won, game.Result);
            int? finished = game.FinishedAt;
            scene.Step();
            game.SceneFinished("CHEWING", game.SceneRevision);
            Assert.Equal(finished, game.FinishedAt);
        }

        /// <summary>Retired candy cannot collect later stars, including during its fading sensor phase.</summary>
        [Fact]
        public void EatingCandyDoesNotCollectStars()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='100'/><omnom x='100' y='100'/><star x='100' y='100'/>");
            scene.Step();
            int collectedAtContact = scene.StarCollections.Count;
            for (int i = 0; i < 30; i++)
            {
                scene.Step();
            }
            Assert.Equal(collectedAtContact, scene.StarCollections.Count);
            Assert.Equal(CandyState.Removed, scene.Gameplay!.CandyState);
        }

        /// <summary>Body destruction cuts an intact rope next to the candy, as the native teardown does, and detaches both intact
        /// and cut rope tails without invalid solver references.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RemovalDetachesRopeTail(bool cut)
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='100'/><omnom x='100' y='100'/><grab x='100' y='0' length='100' target_id='c'/>");
            if (cut)
            {
                Assert.True(scene.Cut(0, 0, new(200, 30)));
            }
            for (int i = 0; i < 20; i++)
            {
                scene.Step();
            }
            Assert.Equal(CandyState.Removed, scene.Gameplay!.CandyState);
            Assert.Equal(2, scene.RopeSegments(0).Count);
            Assert.DoesNotContain(scene.RopeSegments(0).SelectMany(p => p), p => p == scene.Candies[0].Position * 200);
        }

        /// <summary>Diagnostics do not consume candy or change their reference trajectories.</summary>
        [Fact]
        public void GameplayIsOptIn()
        {
            CandySimulation scene = Create(play: false);
            scene.Step();
            Assert.Null(scene.Gameplay);
            Assert.False(scene.Candies[0].IsSensor);
        }

        /// <summary>Mouth animation uses source opening/closing distances with hysteresis.</summary>
        [Theory]
        [InlineData(170, CharacterState.MouthOpen)]
        [InlineData(190, CharacterState.Idle)]
        public void MouthUsesSourceDistanceThreshold(int candyX, CharacterState expected)
        {
            CandySimulation scene = Create($"<candy id='c' x='{candyX}' y='100'/><omnom x='100' y='100'/>");
            scene.Step();
            Assert.Equal(expected, scene.Gameplay!.CharacterState);
        }

        /// <summary>Unsupported mechanics and special arrival fail explicitly before a misleading play session.</summary>
        [Theory]
        [InlineData("<candy id='c' x='100' y='100'/>")]
        [InlineData("<candy id='c' x='100' y='100'/><omnom x='100' y='-97'/>")]
        [InlineData("<candy id='c' x='100' y='100'/><omnom x='100' y='100'/><bubble x='100' y='100'/>")]
        public void UnsupportedPlayScopesFailExplicitly(string objects)
        {
            _ = Assert.Throws<InvalidDataException>(() => Create(objects));
        }

        /// <summary>Location selects presentation only; supported gameplay starts in any authored location.</summary>
        /// <param name="location">Authored game design location.</param>
        [Theory]
        [InlineData("canyon")]
        [InlineData("ventilation")]
        public void GameplayIsIndependentOfLocation(string location)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>");
            CandySimulation scene = new(LevelReader.Read($"<map><layer name='settings'><map width='320' height='480'/><gameDesign location='{location}'/></layer><layer name='Objects'><candy id='c' x='100' y='100'/><omnom x='100' y='100'/></layer></map>"),
                LevelReader.Read(design), play: true);
            scene.Step();
            _ = Assert.Single(scene.Gameplay!.Consumptions);
        }

        /// <summary>With scene durations, gameplay completes authored scenes on its own fixed clock: the failure scene ends
        /// the level once, on the first tick at or beyond its duration, without any presentation callback.</summary>
        [Fact]
        public void SceneDurationsCompleteScenesWithoutPresentation()
        {
            const string Objects = "<candy id='c' x='100' y='576'/><omnom x='100' y='100'/>";
            CandySimulation owned = Create(Objects, scenes: new Dictionary<string, double> { ["FAIL"] = .05 });
            CandySimulation presented = Create(Objects);
            for (int i = 0; i < 10; i++)
            {
                owned.Step();
                presented.Step();
            }
            Assert.Equal((LevelResult.Lost, 4), (owned.Gameplay!.Result, owned.Gameplay.FinishedAt));
            Assert.Equal(LevelResult.Running, presented.Gameplay!.Result);
        }

        /// <summary>A closed mouth leaves the character puzzled, not idle, until the puzzled scene ends.</summary>
        [Fact]
        public void MouthCloseLeavesThePuzzledState()
        {
            LevelGameplay game = Create("<candy id='c' x='300' y='100'/><omnom x='100' y='100'/>").Gameplay!;
            game.SceneFinished("MOUTH_CLOSE", game.SceneRevision);
            Assert.Equal(("IDLE", CharacterState.Idle), (game.Scene, game.CharacterState));
            typeof(LevelGameplay).GetProperty(nameof(LevelGameplay.Scene))!.SetValue(game, "MOUTH_CLOSE");
            game.SceneFinished("MOUTH_CLOSE", game.SceneRevision);
            Assert.Equal(("PUZZLED", CharacterState.Puzzled), (game.Scene, game.CharacterState));
            game.SceneFinished("PUZZLED", game.SceneRevision);
            Assert.Equal(("IDLE", CharacterState.Idle), (game.Scene, game.CharacterState));
        }

        /// <summary>Ordinary dry scenes use the source margin plus 150 logical units for loss.</summary>
        [Fact]
        public void OutOfBoundsCandyFailsAndFreshConstructionResets()
        {
            CandySimulation lost = Create("<candy id='c' x='100' y='576'/><omnom x='100' y='100'/>");
            lost.Step();
            Assert.Equal(CharacterState.Fail, lost.Gameplay!.CharacterState);
            Assert.Equal(LevelResult.Running, lost.Gameplay.Result);
            lost.Gameplay.SceneFinished(lost.Gameplay.Scene, lost.Gameplay.SceneRevision);
            Assert.Equal(LevelResult.Lost, lost.Gameplay!.Result);
            CandySimulation fresh = Create("<candy id='c' x='170' y='100'/><omnom x='100' y='100'/>");
            Assert.Equal(0, fresh.Tick);
            Assert.Equal(LevelResult.Running, fresh.Gameplay!.Result);
            Assert.Empty(fresh.Gameplay.Consumptions);
        }
    }
}
