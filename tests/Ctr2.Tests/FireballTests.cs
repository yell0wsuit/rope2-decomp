using System.Linq;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks fireball burning, rope cutting and timed removal against the source rules, using an invented level.</summary>
    public sealed class FireballTests
    {
        internal const string Fireball = "<circle id='fireball' body_type='dynamic' radius='16' density='0.4' gravityScale='1' friction='0.2' restitution='0.3' " +
            "isSensor='false' linearDamping='0' angularDamping='0' linearVelocityX='0' linearVelocityY='0' angularVelocity='0' filter.category='0' filter.collideWith='all'/>" +
            "<polygon id='wood' body_type='static' friction='0.2' restitution='0.1' density='0.2'/>";

        private static CandySimulation Create(string objects, SceneTimings? timings = null, string extraDesign = "")
        {
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy id='c' x='300' y='20'/>" + objects + "</layer></map>"), LevelReader.Read(RopeTests.Design.Replace("</layer></map>", Fireball + extraDesign + "</layer></map>")),
                timings: timings);
        }

        private static int StepUntilFading(CandySimulation scene)
        {
            while (!scene.Fireballs[0].Fading)
            {
                Assert.True(scene.Tick < 200);
                scene.Step();
            }
            return scene.Tick - 1;
        }

        /// <summary>A fireball landing on dynamic wood burns it and every shape pinned to it: it stops and turns into a sensor
        /// inside the contact's step, destroys its body and cuts its ropes in that step's update, the wood leaves six steps later
        /// and the fireball after its FADE_FIRE scene.</summary>
        [Fact]
        public void FireballBurnsConnectedWoodCutsItsRopesAndFades()
        {
            CandySimulation scene = Create(
                "<rectangle_materialized x='160' y='300' width='100' height='16' angle='0' material='wood_dynamic'/>" +
                "<rectangle_materialized x='160' y='340' width='100' height='16' angle='0' material='wood_dynamic'/>" +
                "<pin x='160' y='300' pin_to_world='true' fixed_rotation='true' enable_motor='false' motor_speed='0' max_force='0'/>" +
                "<pin x='120' y='300' pin_to_world='false' fixed_rotation='false' enable_motor='false' motor_speed='0' max_force='0'/>" +
                "<rectangle_materialized x='120' y='320' width='10' height='50' angle='0' material='wood_dynamic'/>" +
                "<fireball id='f' x='160' y='250'/><grab x='160' y='150' length='150' target_id='f'/>",
                new SceneTimings(Fireball: [0, 0, 0, .5]));
            Assert.Equal(3, scene.Shapes.Count);
            int burn = StepUntilFading(scene);
            SimulationFireball fireball = scene.Fireballs[0];
            // Stopped before integration: only one step of gravity, and no bounce off the wood.
            Assert.Equal(PhysicsWorld.TimeStep * 10, fireball.Body.Velocity.Y);
            Assert.True(fireball.Body.IsSensor);
            // Windows Fireball::update destroys the body while retaining the visual fade.
            Assert.DoesNotContain(fireball.Body, scene.World.Bodies);
            Assert.False(fireball.Removed);
            Assert.True(scene.RopeSegments(0).Count > 1);
            // The top plank burns, and so does the post pinned to it; the lower plank only touches.
            Assert.Equal(2, scene.Shapes.Count(scene.IsBurning));
            while (scene.Tick <= burn + 5)
            {
                Assert.Equal(3, scene.Shapes.Count);
                scene.Step();
            }
            scene.Step();
            _ = Assert.Single(scene.Shapes);
            while (scene.Tick <= burn + 29)
            {
                Assert.False(fireball.Removed);
                scene.Step();
            }
            scene.Step();
            Assert.True(fireball.Removed);
            Assert.DoesNotContain(fireball.Body, scene.World.Bodies);
        }

        /// <summary>Static wood never catches fire from a fireball, which then keeps bouncing like any body.</summary>
        [Fact]
        public void StaticWoodDoesNotBurn()
        {
            CandySimulation scene = Create("<rectangle_materialized x='160' y='300' width='100' height='16' angle='0' material='wood_dynamic' body_type='static'/><fireball x='160' y='250'/>");
            for (int i = 0; i < 120; i++)
            {
                scene.Step();
            }
            Assert.False(scene.Fireballs[0].Fading);
            _ = Assert.Single(scene.Shapes);
        }

        /// <summary>A burn spreads across a pin into static wood, although a direct fireball hit cannot ignite static wood.</summary>
        [Fact]
        public void BurnSpreadsToConnectedStaticWood()
        {
            CandySimulation scene = Create(
                "<rectangle_materialized x='160' y='300' width='100' height='16' angle='0' material='wood_dynamic'/>" +
                "<rectangle_materialized x='120' y='320' width='10' height='50' angle='0' material='wood_dynamic'/>" +
                "<pin x='120' y='300' pin_to_world='false' fixed_rotation='false' enable_motor='false' motor_speed='0' max_force='0'/>" +
                "<fireball x='160' y='250'/>");
            _ = Assert.Single(scene.PinJoints);
            // Authored pins select dynamic bodies only; retain the joint while making its neighbour static.
            scene.World.SetBodyType(scene.Shapes[1].Body, PhysicsBodyType.Static);
            int burn = StepUntilFading(scene);
            Assert.Equal(2, scene.Shapes.Count(scene.IsBurning));
            while (scene.Tick <= burn + 6)
            {
                scene.Step();
            }
            Assert.Empty(scene.Shapes);
            // Without the Windows fade timeline, its visual remains but its body has already left the world.
            Assert.False(scene.Fireballs[0].Removed);
            Assert.DoesNotContain(scene.Fireballs[0].Body, scene.World.Bodies);
        }

        /// <summary>Windows releases a fading fireball from its carrier immediately, before destroying the physics body.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FadingFireballLeavesItsCarrier(bool helicopter)
        {
            string kind = helicopter ? "helicopter" : "thrower";
            string carrier = $"<circle id='{kind}' body_type='{(helicopter ? "kinematic" : "dynamic")}' radius='20' " +
                $"isSensor='{(helicopter ? "true" : "false")}' gravityScale='0' density='0.5'/>";
            CandySimulation scene = Create(
                $"<{kind} x='160' y='200'/><fireball x='160' y='205'/>" +
                "<rectangle_materialized x='50' y='350' width='40' height='16' angle='0' material='wood_dynamic'/>",
                extraDesign: carrier + "<thrower_design id='thrower_design' power='5.5'/>");
            scene.Step();
            PhysicsBody fireball = scene.Fireballs[0].Body;
            Assert.Same(fireball, helicopter ? scene.Helicopters[0].Captured : scene.Throwers[0].Captured);
            scene.World.SetTransform(scene.Shapes[0].Body, fireball.Origin, 0);
            scene.Step();
            Assert.True(scene.Fireballs[0].Fading);
            Assert.Null(helicopter ? scene.Helicopters[0].Captured : scene.Throwers[0].Captured);
            Assert.DoesNotContain(fireball, scene.World.Bodies);
            scene.Step();
            Assert.Null(helicopter ? scene.Helicopters[0].Captured : scene.Throwers[0].Captured);
        }

        /// <summary>A fireball's retained visual pose must not be compared as a live journal body after fading starts.</summary>
        [Fact]
        public void ReplayReportsDestroyedFireballBodyAsMissingDuringVisualFade()
        {
            CandySimulation scene = Create(
                "<rectangle_materialized x='160' y='300' width='100' height='16' angle='0' material='wood_dynamic'/>" +
                "<pin x='160' y='300' pin_to_world='true' fixed_rotation='true'/><fireball x='160' y='250'/>",
                new SceneTimings(Fireball: [0, 0, 0, 10]));
            // Journal order is dynamic shape, candy, fireball.
            ReplayJournal journal = new("", 2, [], [new(100, [new(2, 1.6f, 3, 0, null, null, null)])]);
            JournalReplayResult result = JournalReplay.Run(scene, journal);
            Assert.True(scene.Fireballs[0].Fading);
            Assert.False(scene.Fireballs[0].Removed);
            JournalDrift fireball = Assert.Single(result.Objects);
            Assert.Equal("fireball", fireball.Kind);
            Assert.Equal(1, fireball.Missing);
            Assert.Empty(result.Samples);
        }

        /// <summary>Windows capture clears angular velocity as well as linear velocity, unlike the Android reference.</summary>
        [Fact]
        public void ThrowerCaptureStopsFireballSpin()
        {
            CandySimulation scene = Create("<thrower x='160' y='200'/><fireball x='160' y='205'/>",
                extraDesign: "<circle id='thrower' body_type='dynamic' radius='20' gravityScale='0' density='0.5'/>" +
                    "<thrower_design id='thrower_design' power='5.5'/>");
            PhysicsBody fireball = scene.Fireballs[0].Body;
            fireball.SetAngularVelocity(5);
            scene.Step();
            Assert.Same(fireball, scene.Throwers[0].Captured);
            Assert.Equal(0, fireball.AngularVelocity);
            // Keep the shipped executable's anchor despite older hint journals using the 0.26 m default.
            Assert.Equal(.15f, scene.Throwers[0].JointLength);
        }

        /// <summary>A helicopter contact and a burn in one step cannot leave a scheduled capture pointing at a destroyed body.</summary>
        [Fact]
        public void SameStepBurnCancelsScheduledHelicopterCapture()
        {
            CandySimulation scene = Create(
                "<helicopter x='160' y='200'/><fireball x='160' y='205'/>" +
                "<rectangle_materialized x='160' y='205' width='40' height='16' angle='0' material='wood_dynamic'/>",
                extraDesign: "<circle id='helicopter' body_type='kinematic' radius='20' isSensor='true' gravityScale='0' density='0.5'/>");
            scene.Step();
            Assert.True(scene.Fireballs[0].Fading);
            Assert.DoesNotContain(scene.Fireballs[0].Body, scene.World.Bodies);
            Assert.Null(scene.Helicopters[0].Captured);
            scene.Step();
            Assert.Null(scene.Helicopters[0].Captured);
        }

        /// <summary>Burned wood leaves its helicopter when the wood body is destroyed after its short fade.</summary>
        [Fact]
        public void BurnedWoodLeavesItsHelicopter()
        {
            CandySimulation scene = Create(
                "<helicopter x='160' y='200'/><fireball x='50' y='205'/>" +
                "<circle_materialized x='160' y='205' radius='12' material='wood_dynamic'/>",
                extraDesign: "<circle id='helicopter' body_type='kinematic' radius='20' isSensor='true' gravityScale='0' density='0.5'/>");
            scene.Step();
            PhysicsBody wood = scene.Shapes[0].Body;
            Assert.Same(wood, scene.Helicopters[0].Captured);
            scene.World.SetTransform(scene.Fireballs[0].Body, wood.Origin, 0);
            scene.Step();
            Assert.True(scene.Fireballs[0].Fading);
            for (int i = 0; i < 6; i++)
            {
                scene.Step();
            }
            Assert.DoesNotContain(wood, scene.World.Bodies);
            Assert.Null(scene.Helicopters[0].Captured);
        }

        /// <summary>A burning fireball queued by an occupied thrower cannot be recaptured after its body has been destroyed.</summary>
        [Fact]
        public void ThrowerForgetsDestroyedCollisionCandidates()
        {
            CandySimulation scene = Create(
                "<candy id='held' x='160' y='205'/><thrower x='160' y='200'/><fireball x='50' y='100'/>" +
                "<rectangle_materialized x='50' y='350' width='40' height='16' angle='0' material='wood_dynamic'/>",
                extraDesign: "<circle id='thrower' body_type='dynamic' radius='20' gravityScale='0' density='0.5'/>" +
                    "<thrower_design id='thrower_design' power='5.5'/>");
            scene.Step();
            SimulationThrower thrower = scene.Throwers[0];
            Assert.Same(scene.Candies[1], thrower.Captured);
            PhysicsBody fireball = scene.Fireballs[0].Body;
            scene.World.SetTransform(fireball, thrower.Body.Origin + new System.Numerics.Vector2(0, .025f), 0);
            scene.World.SetTransform(scene.Shapes[0].Body, fireball.Origin, 0);
            scene.Step();
            Assert.True(scene.Fireballs[0].Fading);
            Assert.DoesNotContain(fireball, scene.World.Bodies);
            Assert.True(scene.Tap(thrower.Body.Origin * 200));
            // The cooldown expires; an invalid queued capture used to throw on SetTransform.
            for (int i = 0; i < 60; i++)
            {
                scene.Step();
            }
            Assert.NotSame(fireball, thrower.Captured);
        }
    }
}
