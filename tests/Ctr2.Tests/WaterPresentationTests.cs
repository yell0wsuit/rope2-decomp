using System.Collections.Generic;
using System.Numerics;
using System.Reflection;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Invented levels exercise Windows water scenes and the fixed-clock root connection timelines.</summary>
    public sealed class WaterPresentationTests
    {
        private static CandySimulation Create(SceneTimings? timings = null)
        {
            return WaterTests.Create("<candy x='20' y='20'/><omnom x='250' y='250'/><water y='100'/>", play: true, timings: timings);
        }

        private static void Steps(CandySimulation scene, int count)
        {
            for (int i = 0; i < count; i++)
            {
                scene.Step();
            }
        }

        private static void Finish(CandySimulation scene)
        {
            LevelGameplay game = scene.Gameplay!;
            game.SceneFinished(game.Scene, game.SceneRevision);
        }

        private static CandySimulation Swimming()
        {
            CandySimulation scene = Create();
            Steps(scene, 12); // Three callbacks, then eight ticks of connection-out.
            Assert.Equal("WATER_DIVE", scene.Gameplay!.Scene);
            Steps(scene, 18); // Recovery finishes and the long history fills before dive completion.
            Finish(scene);
            Assert.Equal("WaterSwimming", scene.Gameplay.CharacterState.ToString());
            return scene;
        }

        /// <summary>The third immersion callback drives the next character update, then a root squash precedes dive.</summary>
        [Fact]
        public void DiveConsumesPreviousHistoryAndStartsAfterConnectionOut()
        {
            CandySimulation scene = Create();
            Steps(scene, 3);
            Assert.Equal("Idle", scene.Gameplay!.CharacterState.ToString());
            scene.Step();
            Assert.Equal("WaterDive", scene.Gameplay.CharacterState.ToString());
            Assert.Equal("IDLE", scene.Gameplay.Scene);
            Assert.True(scene.Gameplay.ProcedureActive);
            Steps(scene, 4);
            float weight = 4 * PhysicsWorld.TimeStep / .125f;
            Assert.Equal(Vector2.Lerp(Vector2.One, new(1.1f, .9f), weight), scene.Gameplay.CharacterScale);
            Assert.Equal(new Vector2(0, 6 * weight), scene.Gameplay.CharacterOffset);
            Steps(scene, 4);
            Assert.Equal("WATER_DIVE", scene.Gameplay.Scene);
            Assert.Equal(new Vector2(1.1f, .9f), scene.Gameplay.CharacterScale);
            Steps(scene, 8);
            Assert.Equal(Vector2.One, scene.Gameplay.CharacterScale);
            Finish(scene);
            Assert.Equal("WATER_SWIM", scene.Gameplay.Scene);
        }

        /// <summary>The authored dive duration advances while root recovery is active; it does not wait for rendering.</summary>
        [Fact]
        public void DiveCompletesFromLoadedSceneTiming()
        {
            CandySimulation scene = Create(new(Character: new Dictionary<string, double> { ["WATER_DIVE"] = .05 }));
            Steps(scene, 14);
            Assert.Equal("WATER_DIVE", scene.Gameplay!.Scene);
            scene.Step();
            Assert.Equal("WATER_SWIM", scene.Gameplay.Scene);
        }

        /// <summary>Swimming mouth hysteresis uses strict 150/170 logical units and mouth-close returns to swimming.</summary>
        [Fact]
        public void SwimmingUsesWaterMouthAndRejectsStaleCompletions()
        {
            CandySimulation scene = Swimming();
            PhysicsBody candy = scene.Candies[0], character = scene.Characters[0];
            scene.World.SetTransform(candy, character.Position + new Vector2(.6f, 0), 0);
            scene.Step();
            Assert.Equal("WATER_MOUTH_OPEN", scene.Gameplay!.Scene);
            Assert.True(scene.Gameplay.FacingRight);
            int stale = scene.Gameplay.SceneRevision;
            scene.World.SetTransform(candy, character.Position + new Vector2(-1, 0), 0);
            scene.Step();
            Assert.Equal("WATER_MOUTH_CLOSE", scene.Gameplay.Scene);
            scene.Gameplay.SceneFinished("WATER_MOUTH_OPEN", stale);
            Assert.Equal("WaterMouthClose", scene.Gameplay.CharacterState.ToString());
            Finish(scene);
            Assert.Equal("WATER_SWIM", scene.Gameplay.Scene);
            Assert.True(scene.Gameplay.ProcedureActive); // Mouth-close completion starts recovery only.
        }

        /// <summary>Exact opening distance does not open; switching attention through a new water scene resets mirroring.</summary>
        [Fact]
        public void WaterMouthUsesStrictBoundaryAndCanFaceBothWays()
        {
            CandySimulation scene = Swimming();
            PhysicsBody candy = scene.Candies[0], character = scene.Characters[0];
            scene.World.SetTransform(candy, character.Position + new Vector2(.75f, 0), 0);
            scene.Gameplay!.AfterStep();
            Assert.Equal("WATER_SWIM", scene.Gameplay.Scene);
            scene.World.SetTransform(candy, character.Position + new Vector2(.7f, 0), 0);
            scene.Gameplay.AfterStep();
            Assert.Equal("WATER_MOUTH_OPEN", scene.Gameplay.Scene);
            Assert.True(scene.Gameplay.ArtFacingRight);
            scene.World.SetTransform(candy, character.Position + new Vector2(-1, 0), 0);
            scene.Gameplay.AfterStep();
            Finish(scene);
            Steps(scene, 8);
            scene.World.SetTransform(candy, character.Position + new Vector2(-.7f, 0), 0);
            scene.Gameplay.AfterStep();
            Assert.Equal("WATER_MOUTH_OPEN", scene.Gameplay.Scene);
            Assert.False(scene.Gameplay.FacingRight);
            Assert.False(scene.Gameplay.ArtFacingRight);
        }

        /// <summary>Only swimming exits on the long history; a brief surface crossing retains it.</summary>
        [Fact]
        public void ResurfacingUsesLongHistoryAndConnectionTransition()
        {
            CandySimulation scene = Swimming();
            Steps(scene, 45);
            PhysicsBody character = scene.Characters[0];
            for (int i = 0; i < 35; i++)
            {
                scene.World.SetTransform(character, new(2.5f, .5f), 0);
                character.SetLinearVelocity(Vector2.Zero);
                scene.Step();
            }
            Assert.Equal("WaterSwimming", scene.Gameplay!.CharacterState.ToString());
            scene.World.SetTransform(character, new(2.5f, .5f), 0);
            scene.Step(); // Reads 35 dry callbacks: 24/59 remains above 0.4.
            Assert.Equal("WaterSwimming", scene.Gameplay.CharacterState.ToString());
            scene.World.SetTransform(character, new(2.5f, .5f), 0);
            scene.Step(); // Reads 36 dry callbacks: 23/59 falls below 0.4.
            Assert.Equal("Idle", scene.Gameplay.CharacterState.ToString());
            Assert.True(scene.Gameplay.ProcedureActive);
        }

        /// <summary>Physical candy contact in water-mouth-open follows water mouth-close then water chewing.</summary>
        [Fact]
        public void WaterFeedingPlaysWaterChewingAndWins()
        {
            CandySimulation scene = Swimming();
            PhysicsBody candy = scene.Candies[0], character = scene.Characters[0];
            scene.World.SetTransform(candy, character.Position + new Vector2(.6f, 0), 0);
            scene.Step();
            Assert.Equal("WATER_MOUTH_OPEN", scene.Gameplay!.Scene);
            scene.World.SetTransform(candy, character.Position, 0);
            scene.Step();
            Assert.Equal(CandyState.Eating, scene.Gameplay.CandyState);
            Assert.Equal("WaterChewing", scene.Gameplay.CharacterState.ToString());
            Assert.Equal("WATER_MOUTH_CLOSE", scene.Gameplay.Scene);
            Finish(scene);
            Assert.Equal("WATER_CHEWING", scene.Gameplay.Scene);
            Steps(scene, 8);
            Finish(scene);
            Assert.Equal(LevelResult.Won, scene.Gameplay.Result);
        }

        /// <summary>Candy loss while swimming chooses water failure, through connection-out and recovery.</summary>
        [Fact]
        public void CandyLossUsesWaterFailureTimeline()
        {
            CandySimulation scene = Swimming();
            scene.World.SetTransform(scene.Candies[0], new(-10, 0), 0);
            scene.Step();
            Assert.Equal("Fail", scene.Gameplay!.CharacterState.ToString());
            Assert.Equal(LevelResult.Running, scene.Gameplay.Result);
            Steps(scene, 8);
            Assert.Equal("WATER_FAIL", scene.Gameplay.Scene);
            Steps(scene, 8);
            Finish(scene);
            Assert.Equal(LevelResult.Lost, scene.Gameplay.Result);
        }

        /// <summary>A Booka scene replaces connection-out; its old pending dive must not start later.</summary>
        [Fact]
        public void BookaInterruptsPendingDiveConnection()
        {
            CandySimulation scene = Create();
            Steps(scene, 5);
            Assert.True(scene.Gameplay!.ProcedureActive);
            scene.Gameplay.BooFrom(new(2, 2));
            Assert.Equal("WITH_BOOKA", scene.Gameplay.Scene);
            Assert.False(scene.Gameplay.ProcedureActive);
            Steps(scene, 12);
            Assert.Equal("WITH_BOOKA", scene.Gameplay.Scene);
            Assert.Equal(Vector2.One, scene.Gameplay.CharacterScale);
            Assert.Equal(Vector2.Zero, scene.Gameplay.CharacterOffset);
        }

        /// <summary>A pending Booka cooldown blocks dive even when the short immersion threshold is satisfied.</summary>
        [Fact]
        public void BookaCooldownBlocksDive()
        {
            CandySimulation scene = Create();
            typeof(LevelGameplay).GetProperty("BooTimer", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene.Gameplay, 1f);
            Steps(scene, 20);
            Assert.Equal("Idle", scene.Gameplay!.CharacterState.ToString());
        }
    }
}
