using System;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises Windows trigger contacts and pin releases with invented designs and levels.</summary>
    public sealed class TriggerTests
    {
        private const string Pin = "<circle_materialized x='250' y='100' radius='12' material='wood_dynamic'/>" +
            "<pin x='250' y='100' pin_to_world='true' fixed_rotation='true' enable_motor='true' motor_speed='2' max_force='1'/>";
        private const string Trigger = "<trigger x='100' y='100'/>";

        private static CandySimulation Create(string objects, string extraDesign = "", bool play = false)
        {
            string design = RopeTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                "<circle id='trigger' body_type='static' radius='16' isSensor='true'/>" + extraDesign + "</layer></map>", StringComparison.Ordinal);
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), play: play);
        }

        /// <summary>A sensor's begin contact releases a remote locked motor before that tick's solve.</summary>
        [Fact]
        public void CandyReleasesMotorInTheContactStep()
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy x='100' y='100'/>");
            PhysicsRevoluteJoint hinge = Assert.Single(scene.PinJoints);
            Assert.True(hinge.LimitEnabled);
            scene.Step();
            Assert.False(hinge.LimitEnabled);
            Assert.True(hinge.MotorEnabled);
            Assert.True(scene.Shapes[0].Body.Angle > 0);
        }

        /// <summary>The trigger does not enable motors or release fixed pins whose motors were disabled.</summary>
        [Fact]
        public void DisabledMotorStaysLocked()
        {
            CandySimulation scene = Create(Pin.Replace("enable_motor='true'", "enable_motor='false'", StringComparison.Ordinal) + Trigger + "<candy x='100' y='100'/>");
            scene.Step();
            Assert.True(Assert.Single(scene.PinJoints).LimitEnabled);
            Assert.False(scene.PinJoints[0].MotorEnabled);
        }

        /// <summary>A world pin releases only its world hinges, retaining the separate body-to-body hinge.</summary>
        [Fact]
        public void WorldPinKeepsItsBodyToBodyLimit()
        {
            CandySimulation scene = Create("<circle_materialized x='250' y='100' radius='10' material='wood_dynamic'/>" + Pin + Trigger + "<candy x='100' y='100'/>");
            Assert.Equal(3, scene.PinJoints.Count);
            scene.Step();
            Assert.True(scene.PinJoints[0].LimitEnabled);
            Assert.All(scene.PinJoints.Skip(1), h => Assert.False(h.LimitEnabled));
        }

        /// <summary>A pin without a world attachment releases its body-to-body motor limit.</summary>
        [Fact]
        public void BodyToBodyMotorIsReleased()
        {
            CandySimulation scene = Create("<circle_materialized x='250' y='100' radius='10' material='wood_dynamic'/>" +
                Pin.Replace("pin_to_world='true'", "pin_to_world='false'", StringComparison.Ordinal) + Trigger + "<candy x='100' y='100'/>");
            scene.Step();
            Assert.False(Assert.Single(scene.PinJoints).LimitEnabled);
        }

        /// <summary>Pushed is permanent, including when a body leaves and enters the trigger again.</summary>
        [Fact]
        public void TriggerOnlyReleasesOnce()
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy x='100' y='100'/>");
            scene.Step();
            PhysicsRevoluteJoint hinge = Assert.Single(scene.PinJoints);
            Assert.False(hinge.LimitEnabled);
            hinge.LimitEnabled = true;
            scene.Step();
            Assert.True(hinge.LimitEnabled);
            scene.World.SetTransform(scene.Candies[0], new Vector2(1, 3), 0);
            scene.Step();
            scene.World.SetTransform(scene.Candies[0], new Vector2(1, 1), 0);
            scene.Step();
            Assert.True(hinge.LimitEnabled);
        }

        /// <summary>Both solid and sensor dynamic shapes can push a trigger; kinematic bodies cannot.</summary>
        /// <param name="type">Body type of the touching shape.</param>
        /// <param name="sensor">Whether the touching shape is a sensor.</param>
        /// <param name="released">Expected motor release.</param>
        [Theory]
        [InlineData("dynamic", "false", true)]
        [InlineData("dynamic", "true", true)]
        [InlineData("kinematic", "false", false)]
        [InlineData("static", "false", false)]
        public void OnlyDynamicBodiesPush(string type, string sensor, bool released)
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy x='20' y='20'/>" +
                "<circle_materialized x='100' y='100' radius='8' material='touching'/>",
                $"<polygon id='touching' body_type='{type}' isSensor='{sensor}' density='1'/>");
            scene.Step();
            Assert.Equal(!released, Assert.Single(scene.PinJoints).LimitEnabled);
        }

        /// <summary>The native material-point game-body type never pushes a trigger.</summary>
        [Fact]
        public void RopeMaterialPointsDoNotPush()
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy id='c' x='100' y='200'/><grab x='100' y='20' target_id='c' length='180'/>");
            Assert.Contains(scene.RopePoints(0), p => Vector2.Distance(p, new(200, 200)) < 20);
            scene.Step();
            Assert.True(Assert.Single(scene.PinJoints).LimitEnabled);
        }

        /// <summary>Triggers are admitted by the gameplay constructor.</summary>
        [Fact]
        public void TriggerLevelCanStart()
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy x='100' y='100'/><omnom x='300' y='300'/>", play: true);
            scene.Step();
            Assert.False(Assert.Single(scene.PinJoints).LimitEnabled);
        }

        /// <summary>Frog tongue fixtures have game-body type FROG and cannot push triggers.</summary>
        [Fact]
        public void FrogTongueDoesNotPush()
        {
            CandySimulation scene = Create(Pin + Trigger + "<candy x='20' y='20'/><frog x='50' y='100' path='150,0,' tongueOut='true'/>");
            for (int i = 0; i < 20; i++)
            {
                scene.Step();
            }
            Assert.False(Assert.Single(scene.Triggers).Pushed);
            Assert.True(Assert.Single(scene.PinJoints).LimitEnabled);
        }

        /// <summary>Trigger normalization overrides the placement, while its origin, angle and other fields still merge.</summary>
        [Fact]
        public void AuthoredFieldsMergeBeforeForcedNormalization()
        {
            string designs = RopeTests.Design.Replace("</layer></map>",
                "<polygon id='trigger' body_type='static' polygon='10,0,30,0,30,20,10,20,' isSensor='true'/></layer></map>", StringComparison.Ordinal);
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy x='20' y='20'/><trigger x='100' y='100' angle='90' normalizeShape='false' friction='0.7'/></layer></map>"), LevelReader.Read(designs));
            PhysicsBody trigger = Assert.Single(scene.Triggers).Body;
            Assert.Equal(new Vector2(1, 1), trigger.Origin);
            Assert.InRange(trigger.Angle, 1.57079f, 1.57080f);
            Assert.Equal(.7f, trigger.Friction);
            Assert.True(trigger.IsSensor);
            Assert.InRange(trigger.LocalCenter.Length(), 0, .000001f);
        }

        /// <summary>A trigger does not reference a destroyed pin hinge when its body has already left the scene.</summary>
        [Fact]
        public void DestroyedPinBodyDoesNotPreventActivation()
        {
            CandySimulation scene = Create(Pin.Replace("pin_to_world='true'", "pin_to_world='false'", StringComparison.Ordinal) +
                "<circle_materialized x='250' y='100' radius='10' material='wood_dynamic'/>" + Trigger + "<candy x='100' y='100'/>");
            PhysicsBody removed = Assert.Single(scene.PinJoints).BodyA;
            Assert.True(scene.World.RemoveBody(removed));
            scene.Step();
            Assert.True(Assert.Single(scene.Triggers).Pushed);
        }
    }
}
