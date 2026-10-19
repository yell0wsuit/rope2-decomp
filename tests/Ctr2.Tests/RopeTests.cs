using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Compares scene ropes with attach shifts against an independent native oracle, using invented levels.</summary>
    public sealed class RopeTests
    {
        /// <summary>Invented master designs matching the oracle's synthetic definitions.</summary>
        internal const string Design = """
            <map><layer name="settings"><map width="320" height="480"/></layer><layer name="Objects">
            <circle id="candy" body_type="dynamic" radius="16" density="0.5" gravityScale="0.7" angularDamping="1" friction="0.3" restitution="0.4"/>
            <circle id="grab" body_type="static" radius="8" density="0.4" gravityScale="1" friction="0.8" restitution="0.2" isSensor="true"/>
            <circle id="ropeMaterialPoint" body_type="dynamic" radius="3" density="0.6" gravityScale="0.6" isSensor="true"/>
            <polygon id="wood_dynamic" body_type="dynamic" linearDamping="0.5" angularDamping="0.5" gravityScale="0.9" friction="0.2" restitution="0.1" density="0.2" filter.category="15"/>
            <rope_design id="rope_design" bone_length="40" stretch_hertz="5"/>
            </layer></map>
            """;

        /// <summary>Invented levels in the oracle's body creation order.</summary>
        private static string Level(string scenario)
        {
            string objects = scenario == "anchored"
                ? "<grab x='60' y='40' length='110' target_id='plank' target_x='-40' target_y='0'/>" +
                  "<grab x='260' y='40' length='130' target_id='plank' target_x='40' target_y='3'/>" +
                  "<grab x='160' y='20' length='60' target_id='c'/>" +
                  "<rectangle_materialized x='160' y='170' width='100' height='16' angle='20' material='wood_dynamic' id='plank'/>" +
                  "<candy x='160' y='100' id='c'/>" +
                  "<circle_materialized x='60' y='120' radius='12' material='wood_dynamic' id='ball'/>" +
                  "<grab x='50' y='100' length='30' target_id='ball' target_x='5' target_y='-2'/>"
                : "<grab x='160' y='40' length='60' target_id='c'/>" +
                  "<grab x='100' y='40' length='80' target_id='c'/>" +
                  "<candy x='160' y='300' id='c'/>" +
                  "<grab x='250' y='150' length='25' target_id='ball' target_x='3'/>" +
                  "<circle_materialized x='290' y='200' radius='10' material='wood_dynamic' id='ball'/>";
            return "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>";
        }

        /// <summary>Checks every body, including material points, cut tips and rope-driven damping, at every native tick.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("anchored")]
        [InlineData("stretch")]
        public void MatchesNativeRopeTrace(string scenario)
        {
            CandySimulation scene = new(LevelReader.Read(Level(scenario)), LevelReader.Read(Design));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "RopeReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(240, rows.Length);
            foreach (JsonElement row in rows)
            {
                JsonElement cut = row.GetProperty("Cut");
                if (cut[0].GetInt32() >= 0)
                {
                    Assert.True(scene.Cut(cut[0].GetInt32(), cut[1].GetInt32(), new(cut[2].GetSingle(), cut[3].GetSingle()),
                        cut[0].GetInt32() == 1 ? new Vector2(.001f, 0) : Vector2.Zero));
                }
                scene.Step();
                string context = $"{scenario}, tick {row.GetProperty("Tick").GetInt32()}";
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Length);
                for (int b = 0; b < bodies.Length; b++)
                {
                    PhysicsBody body = bodies[b];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity, body.LinearDamping];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(states[b][i].GetSingle(), actual[i], $"{context}, body {b}, component {i}");
                    }
                }
            }
        }

        /// <summary>A grab without a target name binds to the candy named "candy", as the scene does.</summary>
        [Fact]
        public void AbsentTargetNameMeansCandy()
        {
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='160' y='100' id='candy'/><grab x='160' y='20' length='60'/></layer></map>"),
                LevelReader.Read(Design));
            Assert.Equal(1, scene.RopeCount);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
