using System;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks Booka catches, Om Nom's boo jump and the Booka's exit against the source rules, using an invented level.</summary>
    public sealed class BookaTests
    {
        private const string Designs = "<circle id='booka' body_type='dynamic' radius='16' density='0.5' gravityScale='0' friction='0.3' restitution='0.4' " +
            "isSensor='false' filter.category='2' filter.collideWith='all'/>" +
            "<omnom_design id='omnom_design' boo_jump_power='1.2' boo_jump_angle='70' boo_jump_delay='0.5'/>";

        internal static CandySimulation Create(string objects, SceneTimings? timings = null)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + Designs + "</layer></map>")
                .Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"").Replace("gravityScale='0.7'", "gravityScale='0'");
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), play: true, timings: timings);
        }

        /// <summary>Om Nom touching a Booka boos: the Booka drops its ropes, turns into a kicked sensor and stops counting as a
        /// Booka, and Om Nom shows WITH_BOOKA for a 0.15 second cooldown, then jumps up and away from it.</summary>
        [Fact]
        public void CatchBoosOmNomWhoJumpsAwayFromTheBooka()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='20'/><omnom x='100' y='300'/><booka id='b' x='125' y='300'/>" +
                "<grab x='125' y='200' length='120' target_id='b'/>");
            LevelGameplay game = scene.Gameplay!;
            SimulationBooka booka = scene.Bookas[0];
            scene.Step();
            Assert.True(booka.Booed);
            Assert.True(booka.Body.IsSensor);
            Assert.True(scene.RopeSegments(0).Count > 1);
            // The kick heads -110 to -70 degrees: upward.
            float heading = MathF.Atan2(booka.Body.Velocity.Y, booka.Body.Velocity.X) * 180 / MathF.PI;
            Assert.InRange(heading, -110.001f, -69.999f);
            Assert.Equal(CharacterState.Booed, game.CharacterState);
            Assert.Equal("WITH_BOOKA", game.Scene);
            Assert.InRange(game.BooTimer, .13f, .15f);
            game.SceneFinished("WITH_BOOKA", game.SceneRevision);
            Assert.Equal(CharacterState.Falling, game.CharacterState);
            Assert.Equal("WITH_BOOKA_JUMP", game.Scene);
            // The Booka is to the right, so Om Nom faces right and jumps 20 degrees left of straight up.
            Assert.True(game.FacingRight);
            Vector2 velocity = scene.Characters[0].Velocity;
            Assert.InRange(MathF.Atan2(-velocity.X, -velocity.Y) * 180 / MathF.PI, 19.99f, 20.01f);
        }

        /// <summary>A chewing Om Nom jumps at once, still chewing.</summary>
        [Fact]
        public void ChewingOmNomJumpsAtOnce()
        {
            CandySimulation scene = Create("<candy id='c' x='100' y='300'/><omnom x='100' y='300'/><booka x='125' y='300'/>");
            scene.Step();
            Assert.Equal(CharacterState.FallingChewing, scene.Gameplay!.CharacterState);
            Assert.True(scene.Characters[0].Velocity.Y < 0);
        }

        /// <summary>A caught Booka leaves when its ACTION scene ends: it is scheduled in the update where the scene finishes and
        /// destroyed after the next step.</summary>
        [Fact]
        public void CaughtBookaLeavesAfterItsActionScene()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='20'/><omnom x='100' y='300'/><booka x='125' y='300'/>",
                new SceneTimings(Booka: [0, 0, .1, 0]));
            scene.Step();
            // Six updates finish a 0.1 second scene in tick 5; the tick 6 step destroys the Booka.
            while (scene.Tick <= 6)
            {
                Assert.False(scene.Bookas[0].Removed);
                scene.Step();
            }
            Assert.True(scene.Bookas[0].Removed);
            Assert.DoesNotContain(scene.Bookas[0].Body, scene.World.Bodies);
        }

        /// <summary>An uncaught Booka that leaves the screen by more than the margin is destroyed after the next step.</summary>
        [Fact]
        public void BookaBeyondTheScreenLeaves()
        {
            CandySimulation scene = Create("<candy id='c' x='300' y='20'/><omnom x='60' y='300'/><booka x='300' y='300'/>");
            scene.World.SetTransform(scene.Bookas[0].Body, new Vector2(0, 6), 0);
            scene.Step();
            Assert.False(scene.Bookas[0].Removed);
            scene.Step();
            Assert.True(scene.Bookas[0].Removed);
            Assert.DoesNotContain(scene.Bookas[0].Body, scene.World.Bodies);
        }
    }
}
