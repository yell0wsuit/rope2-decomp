using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks pump presses against the native cone raycast and falloff, using an invented design.</summary>
    public sealed class PumpTests
    {
        private static readonly LevelDefinition Design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>",
            "<pump_design id='pump_design' power='0.15' cone_height='2.0' cone_width='0.35' debug='false'/></layer></map>"));

        private static CandySimulation Create(string pump)
        {
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy id='candy' x='80' y='100'/><grab x='80' y='20' length='40'/>" + pump + "</layer></map>"), Design);
        }

        /// <summary>A press within 90 units pushes the candy along the pump's rotated up axis, scaled by its remaining cone distance;
        /// rope material points in the cone are ignored.</summary>
        [Fact]
        public void PressPushesBodiesInTheCone()
        {
            CandySimulation scene = Create("<pump x='80' y='150' angle='0'/>");
            PhysicsBody candy = scene.Candies[0];
            Assert.False(scene.Tap(new(160, 300 + 90)));
            Assert.Equal(Vector2.Zero, candy.Velocity);
            Assert.True(scene.Tap(new(160, 300 + 89)));
            // Origin (0.8, 1.5); candy center (0.8, 1.0): t = 0.5 / 2, so the impulse is (0, -0.15) * 0.75.
            float t = 1 - (.5f / 2);
            Assert.Equal(candy.InverseMass * new Vector2(0, -.15f * t), candy.Velocity);
            Assert.All(scene.World.Bodies.Where(b => b != candy && b.Type == PhysicsBodyType.Dynamic), b => Assert.Equal(Vector2.Zero, b.Velocity));
        }

        /// <summary>The authored angle rotates the cone: a pump facing right misses a candy above it.</summary>
        [Fact]
        public void AngleRotatesTheCone()
        {
            CandySimulation scene = Create("<pump x='80' y='150' angle='90'/>");
            Assert.True(scene.Tap(new(160, 300)));
            Assert.Equal(Vector2.Zero, scene.Candies[0].Velocity);
        }
    }
}
