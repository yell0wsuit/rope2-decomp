using System;
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

        /// <summary>Centers the implicit zero and authored vertices on the unchanged placement origin.</summary>
        [Fact]
        public void LoadsNormalizedPolygonWithPlacementOverrides()
        {
            PolygonBodyDefinition body = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom id='o' x='30' y='-20' angle='90' density='3' friction='0.6'/>"), Map(Master)));
            Assert.Equal("o", body.Name);
            Assert.Equal(new Vector2(.3f, -.2f), body.Origin);
            Assert.Equal(4, body.Shape.Vertices.Count);
            Assert.Equal(new Vector2(-.1f, -.2f), body.Shape.Vertices[0]);
            Assert.Equal(.24f, body.Mass.Mass, 6);
            Assert.InRange(body.Mass.Center.Length(), 0, .00001f);
            Assert.InRange(Vector2.Distance(body.WorldCenter, body.Origin), 0, .00001f);
            Assert.Equal(.6f, body.Friction);
            Assert.Equal(.03f, body.Restitution);
            Assert.Equal(.7f, body.GravityScale);
        }

        /// <summary>Reverses clockwise vertices after averaging every authored point, including a closing zero.</summary>
        [Fact]
        public void ClockwisePathAveragesClosingZeroBeforeRemovingIt()
        {
            PolygonBodyDefinition body = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom x='0' y='0' polygon='0,40,20,40,20,0,0,0,'/>"), Map(Master)));
            Assert.Equal(4, body.Shape.Vertices.Count);
            Assert.Equal(new Vector2(-.08f, -.16f), body.Shape.Vertices[0]);
            Assert.Equal(new Vector2(.12f, -.16f), body.Shape.Vertices[1]);
            Assert.Equal(.16f, body.Mass.Mass, 6);
        }

        /// <summary>The original Om Nom initializer forces normalization over any authored value.</summary>
        [Fact]
        public void AuthoredNormalizeShapeIsOverridden()
        {
            PolygonBodyDefinition forced = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom x='0' y='0' normalizeShape='false'/>"), Map(Master)));
            PolygonBodyDefinition plain = Assert.Single(PolygonBodyDefinition.LoadCharacters(Map("<omnom x='0' y='0'/>"), Map(Master)));
            Assert.Equal(plain.Shape.Vertices, forced.Shape.Vertices);
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
        [InlineData("isSensor='true'")]
        [InlineData("bullet='true'")]
        [InlineData("smoothFactor='2'")]
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
            Assert.InRange(Vector2.Distance(body.WorldCenter, new(1, 2)), 0, .00001f);
            scene.Step();
            Assert.Same(body, Assert.Single(scene.CharacterDefinitions));
        }

        /// <summary>Authored compound characters fall onto static material terrain using the shared fixed tick.</summary>
        [Fact]
        public void CharacterFallsAndRestsOnTerrain()
        {
            CandySimulation scene = new(Map("<candy id='c' x='260' y='0'/><omnom x='100' y='0'/><polygon_materialized x='0' y='300' material='wood' polygon='320,0,320,40,0,40,' smoothFactor='1'/>"),
                LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>", Master + "<polygon id='wood' body_type='static' friction='0.3' restitution='0' isSensor='false'/></layer></map>")));
            PhysicsBody body = Assert.Single(scene.Characters);
            Assert.InRange(MathF.Abs(body.Origin.Y), 0, .00001f);
            for (int i = 0; i < 180; i++)
            {
                scene.Step();
            }
            Assert.InRange(body.Origin.Y, 2.78f, 2.81f);
            Assert.InRange(MathF.Abs(body.Velocity.Y), 0, .001f);
            Assert.InRange(MathF.Abs(body.Angle), 0, .01f);
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
