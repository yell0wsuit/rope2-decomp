using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks thrower capture, hold, throw and cooldown against the source rules, using an invented design.</summary>
    public sealed class ThrowerTests
    {
        private static readonly LevelDefinition Design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>",
            "<polygon id='thrower' body_type='dynamic' gravityScale='0' density='0.9' friction='0.9' restitution='0.1' filter.category='2' polygon='40,0,40,20,0,20,' smoothFactor='1'/>" +
            "<thrower_design id='thrower_design' power='5.5'/></layer></map>"));

        private static CandySimulation Create()
        {
            // The candy rests on the thrower, which floats without gravity.
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<thrower id='thrower' x='160' y='200' angle='0'/><candy id='candy' x='160' y='180'/></layer></map>"), Design);
        }

        /// <summary>A touching candy is captured after the step: teleported onto the thrower origin, stopped and hinged
        /// 0.1 m along its local y axis.</summary>
        [Fact]
        public void TouchingCandyIsCapturedAndHeld()
        {
            CandySimulation scene = Create();
            SimulationThrower thrower = Assert.Single(scene.Throwers);
            PhysicsBody candy = scene.Candies[0];
            for (int i = 0; i < 40 && thrower.Captured == null; i++)
            {
                scene.Step();
            }
            Assert.Same(candy, thrower.Captured);
            for (int i = 0; i < 30; i++)
            {
                scene.Step();
            }
            // The hinge holds the candy's (0, 0.1) point on the thrower origin.
            Vector2 anchor = candy.Origin + Vector2.Transform(new Vector2(0, .1f), Matrix3x2.CreateRotation(candy.Angle));
            Assert.InRange(Vector2.Distance(anchor, thrower.Body.Origin), 0, .01f);
        }

        /// <summary>A press within 90 units throws along the thrower's up axis with the captured mass times the design
        /// power, then a 0.3 second cooldown blocks recapture.</summary>
        [Fact]
        public void PressThrowsWithMassScaledImpulseAndCoolsDown()
        {
            CandySimulation scene = Create();
            SimulationThrower thrower = scene.Throwers[0];
            PhysicsBody candy = scene.Candies[0];
            while (thrower.Captured == null)
            {
                scene.Step();
            }
            Vector2 origin = thrower.Body.Origin * 200;
            Assert.False(scene.Tap(origin + new Vector2(90, 0)));
            Vector2 before = candy.Velocity;
            Assert.True(scene.Tap(origin + new Vector2(89, 0)));
            Assert.Null(thrower.Captured);
            Vector2 impulse = Vector2.Transform(new Vector2(0, -(candy.Mass * 5.5f)), Matrix3x2.CreateRotation(thrower.Body.Angle));
            Assert.InRange(Vector2.Distance(before + (candy.InverseMass * impulse), candy.Velocity), 0, 1e-5f);
            scene.Step();
            Assert.Null(thrower.Captured);
        }
    }
}
