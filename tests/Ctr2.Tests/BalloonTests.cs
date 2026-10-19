using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Compares balloons against an independent native oracle, using invented levels.</summary>
    public sealed class BalloonTests
    {
        /// <summary>The rope test designs plus invented balloon body and stratosphere designs.</summary>
        private static readonly string Design = RopeTests.Design.Replace("</layer></map>",
            "<circle id='balloon' body_type='dynamic' linearDamping='5' angularDamping='5' gravityScale='-1' friction='0' restitution='0.3' density='0.5' radius='20'/>" +
            "<balloon_design id='balloon_design' stratosphere_y='120' stratosphere_koeff='1' stratosphere_damping_koeff='2.5'/></layer></map>", StringComparison.Ordinal);

        private static string Level(string scenario)
        {
            string objects = scenario == "float"
                ? "<grab x='160' y='300' length='70' target_id='c'/><candy x='160' y='250' id='c'/>" +
                  "<balloon x='140' y='180' length='80' target_x='0' target_y='0' target_id='c'/><balloon x='180' y='175' length='80' target_x='0' target_y='0' target_id='c'/>"
                : "<circle_materialized x='160' y='300' radius='15' material='wood_dynamic' id='ball'/><candy x='10' y='460' id='c'/>" +
                  "<balloon x='160' y='200' length='60' target_x='3' target_y='0' target_id='ball'/>";
            return "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>";
        }

        /// <summary>Checks balloon activity and every body, including gravity scale and damping, at every native tick.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("float")]
        [InlineData("cutline")]
        public void MatchesNativeBalloonTrace(string scenario)
        {
            CandySimulation scene = new(LevelReader.Read(Level(scenario)), LevelReader.Read(Design));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BalloonReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(300, rows.Length);
            foreach (JsonElement row in rows)
            {
                int tick = row.GetProperty("Tick").GetInt32();
                if (scenario == "float" && tick == 60)
                {
                    Assert.True(scene.Tap(new(280, 262)));
                }
                JsonElement cut = row.GetProperty("Cut");
                if (cut[0].GetInt32() >= 0)
                {
                    Assert.True(scene.Cut(cut[0].GetInt32(), cut[1].GetInt32(), new(cut[2].GetSingle(), cut[3].GetSingle())));
                }
                if (scenario == "float" && tick == 261)
                {
                    Assert.Empty(scene.Swipe(new(200, -50), new(440, -50)));
                    Assert.Equal(new BalloonPop(260, 1), Assert.Single(scene.BalloonPops));
                }
                scene.Step();
                string context = $"{scenario}, tick {tick}";
                Assert.Equal(row.GetProperty("Active").EnumerateArray().Select(a => a.GetBoolean()), scene.Balloons.Select(b => b.Active));
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Length);
                for (int b = 0; b < bodies.Length; b++)
                {
                    float[] actual = [bodies[b].Position.X, bodies[b].Position.Y, bodies[b].Velocity.X, bodies[b].Velocity.Y, bodies[b].Angle, bodies[b].AngularVelocity,
                        bodies[b].LinearDamping, bodies[b].GravityScale];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(states[b][i].GetSingle(), actual[i], $"{context}, body {b}, component {i}");
                    }
                }
            }
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
