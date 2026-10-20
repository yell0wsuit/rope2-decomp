using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks helicopter capture, carrying, tap release, cooldown and contact-started paths against the source rules,
    /// using an invented design.</summary>
    public sealed class HelicopterTests
    {
        private static readonly LevelDefinition Design = LevelReader.Read(CandySimulationTests.Design.Replace("</layer></map>",
            "<circle id='helicopter' body_type='kinematic' isSensor='true' radius='20' density='0.5' gravityScale='1' friction='0.1' restitution='0.2' " +
            "linearDamping='0' angularDamping='0' linearVelocityX='0' linearVelocityY='0' angularVelocity='0' filter.category='0' filter.collideWith='all'/></layer></map>"));

        private static CandySimulation Create(string helicopter = "")
        {
            // The candy starts inside the helicopter sensor.
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                $"<helicopter x='160' y='200' {helicopter}/><candy id='candy' x='160' y='205'/></layer></map>"), Design);
        }

        private static void StepUntilHinged(CandySimulation scene)
        {
            for (int i = 0; i < 120; i++)
            {
                scene.Step();
            }
        }

        /// <summary>A touching candy is pulled to the hook 0.49 m below the helicopter and hinged there: its point 0.2 m above
        /// its centre hangs on the helicopter's point 0.13 m below its origin.</summary>
        [Fact]
        public void TouchingCandyIsCarriedToTheHookAndHinged()
        {
            CandySimulation scene = Create();
            SimulationHelicopter helicopter = Assert.Single(scene.Helicopters);
            PhysicsBody candy = scene.Candies[0];
            scene.Step();
            Assert.Same(candy, helicopter.Captured);
            StepUntilHinged(scene);
            Vector2 anchor = candy.Origin + Vector2.Transform(new Vector2(0, -.2f), Matrix3x2.CreateRotation(candy.Angle));
            Assert.InRange(Vector2.Distance(anchor, helicopter.Body.Origin + new Vector2(0, .13f)), 0, .01f);
            Assert.Same(candy, helicopter.Captured);
        }

        /// <summary>A press within 93 units of the helicopter drops the candy; its hinge holds for one more step before the next
        /// update deletes it, then the candy falls freely and the half second cooldown keeps it from being caught again.</summary>
        [Fact]
        public void PressDropsTheCandyAfterOneMoreStep()
        {
            CandySimulation scene = Create();
            SimulationHelicopter helicopter = scene.Helicopters[0];
            PhysicsBody candy = scene.Candies[0];
            StepUntilHinged(scene);
            Vector2 origin = helicopter.Body.Origin * 200;
            Assert.False(scene.Tap(origin + new Vector2(93, 0)));
            Assert.True(scene.Tap(origin + new Vector2(92, 0)));
            Assert.Null(helicopter.Captured);
            scene.Step();
            Assert.InRange(candy.Velocity.Y, -.01f, .01f);
            float y = candy.Origin.Y;
            for (int i = 0; i < 20; i++)
            {
                scene.Step();
            }
            Assert.Null(helicopter.Captured);
            // Free fall at 0.7 g for 20 steps, with no hinge holding it back.
            Assert.InRange(candy.Origin.Y - y, .38f, .44f);
            Assert.InRange(candy.Velocity.Y, 2.3f, 2.38f);
        }

        /// <summary>A path that starts on contact waits for a capture, then the helicopter follows it carrying the candy.</summary>
        [Fact]
        public void ContactStartedPathMovesOnlyAfterCapture()
        {
            CandySimulation idle = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<helicopter x='160' y='200' path='60,0,' startOnContact='true' cyclePath='false' speed='1'/><candy id='candy' x='40' y='40'/></layer></map>"), Design);
            for (int i = 0; i < 60; i++)
            {
                idle.Step();
            }
            Assert.Equal(1.6f, idle.Helicopters[0].Body.Origin.X);

            CandySimulation scene = Create("path='60,0,' startOnContact='true' cyclePath='false' speed='1'");
            SimulationHelicopter helicopter = scene.Helicopters[0];
            for (int i = 0; i < 60; i++)
            {
                scene.Step();
            }
            Assert.True(helicopter.Body.Origin.X > 1.65f);
            Assert.Same(scene.Candies[0], helicopter.Captured);
            Assert.InRange(scene.Candies[0].Origin.X - helicopter.Body.Origin.X, -.05f, .05f);
        }
    }
}
