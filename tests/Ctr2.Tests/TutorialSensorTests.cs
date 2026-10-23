using System;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises Windows tutorial sensors (0x4AB990, update 0x4ABC70, contacts 0x4ABD10/0x4ABD40) with invented levels.</summary>
    public sealed class TutorialSensorTests
    {
        private static CandySimulation Create(string objects, bool gravity = false, bool play = false)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                "<circle id='tutorial_sensor' body_type='static' isSensor='true' radius='5' friction='0.2' density='0.4'/></layer></map>", StringComparison.Ordinal);
            if (!gravity)
            {
                design = design.Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"", StringComparison.Ordinal);
            }
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(design), play: play);
        }

        private const string Sensor = "<tutorial_sensor x='100' y='100' radius='15' delay='0.1' tutorial='balloons_1candy'/>";

        /// <summary>The master design merges with the placement, so the authored radius replaces the design's.</summary>
        [Fact]
        public void BuildsStaticSensorWithAuthoredRadius()
        {
            CandySimulation scene = Create("<candy id='candy' x='20' y='20'/>" + Sensor);
            SimulationTutorialSensor sensor = Assert.Single(scene.TutorialSensors);
            Assert.Equal(PhysicsBodyType.Static, sensor.Body.Type);
            Assert.True(sensor.Body.IsSensor);
            Assert.Equal(.15f, sensor.Body.Radius);
            Assert.Equal(new System.Numerics.Vector2(1, 1), sensor.Body.Origin);
        }

        /// <summary>A candy inside the sensor requests the balloons tutorial once the delay has elapsed, counting the contact
        /// tick, and the sensor then disables itself.</summary>
        [Fact]
        public void CandyContactRequestsTutorialAfterDelay()
        {
            CandySimulation scene = Create("<candy id='candy' x='100' y='100'/>" + Sensor);
            SimulationTutorialSensor sensor = scene.TutorialSensors[0];
            // 0.1 s at the fixed 0.0167 s step: the timeout reaches zero or below on the sixth update.
            for (int i = 0; i < 5; i++)
            {
                scene.Step();
                Assert.Null(sensor.RequestedAt);
            }
            scene.Step();
            Assert.Equal(6, sensor.RequestedAt);
            Assert.True(sensor.Disabled);
            for (int i = 0; i < 10; i++)
            {
                scene.Step();
            }
            Assert.Equal(6, sensor.RequestedAt);
        }

        /// <summary>The finished-contact delegate cancels the countdown when the candy leaves before the delay.</summary>
        [Fact]
        public void LeavingCancelsTheCountdown()
        {
            CandySimulation scene = Create("<candy id='candy' x='100' y='60'/>" +
                "<tutorial_sensor x='100' y='100' radius='15' delay='5' tutorial='balloons_1candy'/>", gravity: true);
            SimulationTutorialSensor sensor = scene.TutorialSensors[0];
            bool counted = false;
            for (int i = 0; i < 120; i++)
            {
                scene.Step();
                counted |= sensor.Timeout >= 0;
            }
            Assert.True(counted);
            Assert.Equal(-1, sensor.Timeout);
            Assert.Null(sensor.RequestedAt);
            Assert.False(sensor.Disabled);
        }

        /// <summary>Only the <c>balloons_1candy</c> tutorial is enabled, and only the candy game-body type starts it.</summary>
        /// <param name="objects">Invented placement.</param>
        [Theory]
        [InlineData("<candy id='candy' x='100' y='100'/><tutorial_sensor x='100' y='100' radius='15' delay='0.1' tutorial='other'/>")]
        [InlineData("<candy id='candy' x='100' y='100'/><tutorial_sensor x='100' y='100' radius='15' delay='0.1'/>")]
        [InlineData("<candy id='candy' x='20' y='20'/><circle_materialized x='100' y='100' radius='10' material='ball'/>" + Sensor)]
        public void OtherTutorialsAndBodiesNeverRequest(string objects)
        {
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(CandySimulationTests.Design.Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"", StringComparison.Ordinal).Replace("</layer></map>",
                    "<circle id='tutorial_sensor' body_type='static' isSensor='true' radius='5'/><polygon id='ball' body_type='dynamic' density='1' gravityScale='0'/></layer></map>",
                    StringComparison.Ordinal)));
            for (int i = 0; i < 30; i++)
            {
                scene.Step();
            }
            Assert.Null(scene.TutorialSensors[0].RequestedAt);
        }

        /// <summary>Native loadMap adds tutorial sensors as children only, so they take no hint-journal slot.</summary>
        [Fact]
        public void SensorTakesNoJournalSlot()
        {
            CandySimulation plain = Create("<candy id='candy' x='20' y='20'/>"), sensed = Create("<candy id='candy' x='20' y='20'/>" + Sensor);
            Assert.Equal(plain.InitialJournalCount, sensed.InitialJournalCount);
        }

        /// <summary>Tutorial sensors are admitted by the gameplay constructor.</summary>
        [Fact]
        public void SensorLevelCanStart()
        {
            CandySimulation scene = Create("<candy id='candy' x='100' y='100'/><omnom x='300' y='400'/>" + Sensor, play: true);
            scene.Step();
            Assert.NotNull(scene.Gameplay);
        }
    }
}
