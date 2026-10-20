using System;
using System.Linq;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>The Windows loader compares the clover tag but never instantiates it, unlike premium mobile builds.</summary>
    public sealed class WindowsCloverTests
    {
        private const string Objects = "<candy x='100' y='100'/><omnom x='200' y='300'/>";
        private const string Clover = "<clover x='100' y='100' id='unused' density='-1' body_type='dynamic'/>";

        private static CandySimulation Create(string objects, LevelVariant variant)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                "<circle id='clover' body_type='kinematic' isSensor='true' radius='15' density='0.1'/></layer></map>", StringComparison.Ordinal);
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), play: true, variant: variant);
        }

        /// <summary>Ignored clover placements must not add fixtures, shift proxy ordering, excite Om Nom or alter fruit-grid occupancy.</summary>
        /// <param name="variant">Windows gameplay mode.</param>
        [Theory]
        [InlineData(LevelVariant.Main)]
        [InlineData(LevelVariant.Challenge)]
        [InlineData(LevelVariant.FruitChallenge)]
        public void CloversHaveNoEffectOnWindowsSimulation(LevelVariant variant)
        {
            CandySimulation plain = Create(Objects, variant), clovers = Create(Clover + Objects + Clover, variant);
            Assert.Equal(plain.World.Bodies.Count, clovers.World.Bodies.Count);
            Assert.Equal(plain.Fruits.Select(f => f.Body.Origin), clovers.Fruits.Select(f => f.Body.Origin));
            for (int tick = 0; tick < 180; tick++)
            {
                plain.Step();
                clovers.Step();
                Assert.Equal(plain.Gameplay!.CharacterState, clovers.Gameplay!.CharacterState);
                Assert.Equal(plain.World.Bodies.Select(b => (b.Origin, b.Velocity, b.Angle, b.AngularVelocity)),
                    clovers.World.Bodies.Select(b => (b.Origin, b.Velocity, b.Angle, b.AngularVelocity)));
            }
        }

        /// <summary>Ignoring a clover does not require its unused master design.</summary>
        [Fact]
        public void MainLevelDoesNotRequireCloverDesign()
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>", StringComparison.Ordinal);
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                Objects + Clover + "</layer></map>"), LevelReader.Read(design), play: true);
            Assert.Equal(3, scene.World.Bodies.Count);
        }
    }
}
