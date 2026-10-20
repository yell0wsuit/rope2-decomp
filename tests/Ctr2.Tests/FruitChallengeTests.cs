using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks the fruit challenge scene against the native loader, using an invented clover design.</summary>
    public sealed class FruitChallengeTests
    {
        private const string Clover = "<circle id='clover' body_type='kinematic' isSensor='true' radius='15' density='0.1' gravityScale='1' friction='0.2' " +
            "restitution='0.3' linearDamping='0' angularDamping='0' linearVelocityX='0' linearVelocityY='0' angularVelocity='0' filter.category='0' filter.collideWith='all'/>" +
            "<circle id='star' body_type='kinematic' radius='13' isSensor='true'/>";

        private static CandySimulation Create(string objects, LevelVariant variant = LevelVariant.FruitChallenge, double[]? scenes = null)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", Clover + "</layer></map>").Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"");
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), variant: variant, fruitScenes: scenes);
        }

        /// <summary>Eleven columns of twelve fruits cover the screen, except where a fixture already holds the point; each
        /// position is half printed with six significant digits and doubled again.</summary>
        [Fact]
        public void GridSkipsCoveredPointsAndRoundsPositionsThroughText()
        {
            CandySimulation open = Create("<candy x='300' y='470'/>");
            Assert.Equal(132, open.Fruits.Count);
            // Column 0, row 0: 640 / 11 / 2 = 29.0909..., printed as half of it, 14.5455.
            Assert.Equal(new Vector2((14.5455f + 14.5455f) / 200, (45f + 45f) / 200), open.Fruits[0].Body.Origin);
            Assert.Equal([2, 0, 1], open.Fruits.Take(3).Select(f => f.Type));

            CandySimulation covered = Create("<candy x='15' y='45'/>");
            Assert.Equal(131, covered.Fruits.Count);
            Assert.Empty(Create("<candy x='15' y='45'/>", LevelVariant.Challenge).Fruits);
        }

        /// <summary>The stars are destroyed after the first step, which can still collect them.</summary>
        [Fact]
        public void StarsLastOneStep()
        {
            CandySimulation scene = Create("<candy x='300' y='470'/><star x='300' y='470'/><star x='160' y='20'/>");
            Assert.All(scene.Stars, s => Assert.False(s.Removed));
            scene.Step();
            Assert.All(scene.Stars, s => Assert.True(s.Removed));
            Assert.Equal(0, Assert.Single(scene.StarCollections).Star);
            Assert.All(Create("<candy x='300' y='470'/><star x='160' y='20'/>", LevelVariant.Challenge).Stars, s => Assert.False(s.Removed));
        }

        /// <summary>An overlapping candy collects a fruit, which turns static at once and is destroyed after its scene,
        /// 44 particle updates and one more step.</summary>
        [Fact]
        public void CollectedFruitTurnsStaticAndIsRemovedAfterItsAnimation()
        {
            Assert.Equal(44, CandySimulation.ParticleUpdates);
            // The candy overlaps the first fruit's circle without covering its center.
            CandySimulation scene = Create("<candy x='35' y='45'/>", scenes: [.1, .1, .1]);
            scene.Step();
            Assert.Contains(new FruitCollection(0, 0, 0), scene.FruitCollections);
            SimulationFruit fruit = scene.Fruits[0];
            Assert.Equal(PhysicsBodyType.Static, fruit.Body.Type);
            // Six updates finish a 0.1 second scene in tick 5; the particles report in tick 49 and the tick 50 step destroys it.
            while (scene.Tick <= 50)
            {
                Assert.False(scene.Fruits[0].Removed);
                scene.Step();
            }
            Assert.True(scene.Fruits[0].Removed);
            Assert.DoesNotContain(fruit.Body, scene.World.Bodies);
        }

        /// <summary>The character collects fruits too.</summary>
        [Fact]
        public void CharacterCollectsFruits()
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + Clover + "</layer></map>")
                .Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"").Replace("gravityScale='0.7'", "gravityScale='0'");
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy id='c' x='300' y='470'/><omnom x='14.5' y='70'/></layer></map>"), LevelReader.Read(design), play: true, variant: LevelVariant.FruitChallenge);
            scene.Step();
            Assert.Contains(new FruitCollection(0, 0, -1), scene.FruitCollections);
        }
    }
}
