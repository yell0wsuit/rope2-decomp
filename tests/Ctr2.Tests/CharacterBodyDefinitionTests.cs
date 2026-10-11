using System.IO;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises authored character body definitions using invented XML only.</summary>
    public sealed class CharacterBodyDefinitionTests
    {
        /// <summary>Invented offset character polygon, unrelated to original game geometry.</summary>
        internal const string Master = "<polygon id='omnom' body_type='dynamic' polygon='20,0,20,40,0,40,' density='2' gravityScale='0.7' friction='0.15' restitution='0.03' smoothFactor='1'/>";

        /// <summary>Includes the implicit zero and distinguishes the authored origin from its rotated centroid.</summary>
        [Fact]
        public void LoadsOffsetPolygonWithPlacementOverrides()
        {
            PolygonBodyDefinition body = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom id='o' x='30' y='-20' angle='90' density='3' friction='0.6'/>"), Map(Master)));
            Assert.Equal("o", body.Name);
            Assert.Equal(new Vector2(.3f, -.2f), body.Origin);
            Assert.Equal(4, body.Shape.Vertices.Count);
            Assert.Equal(Vector2.Zero, body.Shape.Vertices[0]);
            Assert.Equal(.24f, body.Mass.Mass, 6);
            Assert.Equal(new Vector2(.1f, .2f), body.Mass.Center);
            Assert.InRange(Vector2.Distance(body.WorldCenter, new(.1f, -.1f)), 0, .00001f);
            Assert.Equal(.6f, body.Friction);
            Assert.Equal(.03f, body.Restitution);
            Assert.Equal(.7f, body.GravityScale);
        }

        /// <summary>Normalizes clockwise authored vertices while keeping the local origin first.</summary>
        [Fact]
        public void ClockwisePathPreservesImplicitOrigin()
        {
            PolygonBodyDefinition body = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom x='0' y='0' polygon='0,40,20,40,20,0,0,0,'/>"), Map(Master)));
            Assert.Equal(Vector2.Zero, body.Shape.Vertices[0]);
            Assert.Equal(new Vector2(.2f, 0), body.Shape.Vertices[1]);
            Assert.Equal(.16f, body.Mass.Mass, 6);
        }

        /// <summary>No character requires no character master definition.</summary>
        [Fact]
        public void EmptyCharacterListRequiresNoMaster()
        {
            Assert.Empty(PolygonBodyDefinition.LoadCharacters(Map("<candy x='0' y='0'/>"), Map("")));
        }

        /// <summary>Invalid body fields fail explicitly before simulation could consume them.</summary>
        /// <param name="attributes">Invalid placement overrides.</param>
        [Theory]
        [InlineData("body_type='static'")]
        [InlineData("density='0'")]
        [InlineData("density='NaN'")]
        [InlineData("linearVelocityX='1'")]
        [InlineData("angularVelocity='1'")]
        [InlineData("linearDamping='-1'")]
        [InlineData("angularDamping='NaN'")]
        [InlineData("friction='-1'")]
        [InlineData("restitution='11'")]
        [InlineData("fixedRotation='maybe'")]
        [InlineData("isSensor='true'")]
        [InlineData("bullet='true'")]
        [InlineData("smoothFactor='2'")]
        [InlineData("normalizeShape='true'")]
        [InlineData("polygon='20,0,10,10,20,40,0,40,'")]
        [InlineData("polygon='20,NaN,20,40,0,40,'")]
        [InlineData("filter.collideWith='none'")]
        [InlineData("path='20,0,'")]
        public void InvalidDefinitionsFail(string attributes)
        {
            _ = Assert.Throws<InvalidDataException>(() => PolygonBodyDefinition.LoadCharacters(Map("<omnom x='0' y='0' " + attributes + "/>"), Map(Master)));
        }

        /// <summary>The candy scene exposes immutable character definitions without pretending they are integrated bodies.</summary>
        [Fact]
        public void CandySceneLoadsCharacterDefinitions()
        {
            CandySimulation scene = new(Map("<candy id='c' x='0' y='0'/><omnom id='o' x='100' y='200' angle='90'/>"),
                LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>", Master + "</layer></map>")));
            PolygonBodyDefinition body = Assert.Single(scene.CharacterDefinitions);
            Assert.InRange(Vector2.Distance(body.WorldCenter, new(.8f, 2.1f)), 0, .00001f);
            scene.Step();
            Assert.Same(body, Assert.Single(scene.CharacterDefinitions));
        }

        /// <summary>Creates an invented map containing the requested objects.</summary>
        /// <param name="objects">XML objects fragment.</param>
        /// <returns>The parsed editor-space map.</returns>
        private static LevelDefinition Map(string objects)
        {
            return LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>");
        }
    }
}
