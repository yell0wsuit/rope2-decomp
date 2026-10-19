using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Compares scene pins against an independent native oracle, using an invented level.</summary>
    public sealed class PinTests
    {
        /// <summary>Invented level in the oracle's body order; pins create no bodies, so their position in the file does not matter.</summary>
        private const string Level = """
            <map><layer name="settings"><map width="320" height="480"/></layer><layer name="Objects">
            <rectangle_materialized x="160" y="150" width="100" height="16" angle="0" material="wood_dynamic"/>
            <rectangle_materialized x="210" y="150" width="100" height="16" angle="90" material="wood_dynamic"/>
            <circle_materialized x="80" y="80" radius="12" material="wood_dynamic"/>
            <circle_materialized x="260" y="80" radius="12" material="wood_dynamic"/>
            <rectangle_materialized x="60" y="300" width="60" height="10" angle="30" material="wood_dynamic"/>
            <circle_materialized x="250" y="300" radius="10" material="wood_dynamic"/>
            <circle_materialized x="255" y="300" radius="10" material="wood_dynamic"/>
            <circle_materialized x="260" y="300" radius="10" material="wood_dynamic"/>
            <candy x="150" y="40" id="c"/>
            <pin x="205" y="150" pin_to_world="false" fixed_rotation="true" enable_motor="false" motor_speed="1" max_force="1"/>
            <pin x="115" y="150" pin_to_world="true" fixed_rotation="false" enable_motor="false" motor_speed="1" max_force="1"/>
            <pin x="80" y="80" pin_to_world="true" fixed_rotation="false" enable_motor="true" motor_speed="2" max_force="1"/>
            <pin x="260" y="80" pin_to_world="true" fixed_rotation="true" enable_motor="true" motor_speed="-1" max_force="1"/>
            <pin x="60" y="300" pin_to_world="0" fixed_rotation="0" enable_motor="0" motor_speed="1" max_force="1"/>
            <pin x="255" y="300" pin_to_world="false" fixed_rotation="true" enable_motor="false" motor_speed="1" max_force="1"/>
            <pin x="20" y="20" pin_to_world="true" fixed_rotation="true" enable_motor="false" motor_speed="1" max_force="1"/>
            </layer></map>
            """;

        /// <summary>Checks hinge creation and every body, including the main static anchor, at every native tick.</summary>
        [Fact]
        public void MatchesNativePinTrace()
        {
            CandySimulation scene = new(LevelReader.Read(Level), LevelReader.Read(RopeTests.Design));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "PinReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray()];
            Assert.Equal(240, rows.Length);
            Assert.Equal(rows[0].GetProperty("Joints").GetInt32(), scene.PinJoints.Count);
            foreach (JsonElement row in rows)
            {
                scene.Step();
                string context = $"tick {row.GetProperty("Tick").GetInt32()}";
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Length);
                for (int b = 0; b < bodies.Length; b++)
                {
                    float[] actual = [bodies[b].Position.X, bodies[b].Position.Y, bodies[b].Velocity.X, bodies[b].Velocity.Y, bodies[b].Angle, bodies[b].AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(states[b][i].GetSingle(), actual[i], $"{context}, body {b}, component {i}");
                    }
                }
            }
        }

        /// <summary>A negative motor torque is outside the supported revolute range and fails before play.</summary>
        [Fact]
        public void NegativeMotorTorqueFailsExplicitly()
        {
            string level = Level.Replace("motor_speed=\"2\" max_force=\"1\"", "motor_speed=\"2\" max_force=\"-1\"", StringComparison.Ordinal);
            _ = Assert.Throws<InvalidDataException>(() => new CandySimulation(LevelReader.Read(level), LevelReader.Read(RopeTests.Design)));
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
