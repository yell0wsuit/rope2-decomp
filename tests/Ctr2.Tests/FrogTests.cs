using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Compares frog tongues against an independent native oracle, using invented levels.</summary>
    public sealed class FrogTests
    {
        /// <summary>The rope test designs plus an invented static wood material in category 14.</summary>
        private static readonly string Design = RopeTests.Design.Replace("</layer></map>",
            "<polygon id='wood' body_type='static' gravityScale='1' friction='0.3' density='0.4' filter.category='14'/></layer></map>", StringComparison.Ordinal);

        private static string Level(string objects)
        {
            return "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>";
        }

        /// <summary>Invented levels in the oracle's body order, with taps keyed by native loop tick.</summary>
        private static (string Level, (int Tick, Vector2 Point)[] Taps) Scene(string scenario)
        {
            return scenario switch
            {
                "bridge" => (Level("<frog x='60' y='300' path='200,-20,' tongueOut='0'/><candy x='160' y='50' id='c'/>"),
                    [(5, new(120, 600)), (160, new(118, 598))]),
                "blocked" => (Level("<rectangle_materialized x='200' y='300' width='20' height='60' material='wood_dynamic'/>" +
                    "<rectangle_materialized x='200' y='345' width='100' height='20' material='wood'/><frog x='60' y='300' path='230,10,' tongueOut='0'/><candy x='10' y='10' id='c'/>"),
                    [(3, new(120, 600)), (40, new(120, 600)), (90, new(120, 600))]),
                _ => (Level("<rectangle_materialized x='160' y='260' width='40' height='40' material='wood'/><frog x='40' y='250' path='240,0,' tongueOut='true'/><candy x='120' y='190' id='c'/>"),
                    [(120, new(80, 500))])
            };
        }

        /// <summary>Checks tongue state, extension and every body at every native tick.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("bridge")]
        [InlineData("blocked")]
        [InlineData("out")]
        public void MatchesNativeFrogTrace(string scenario)
        {
            (string level, (int Tick, Vector2 Point)[] taps) = Scene(scenario);
            CandySimulation scene = new(LevelReader.Read(level), LevelReader.Read(Design));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "FrogReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(240, rows.Length);
            foreach (JsonElement row in rows)
            {
                int tick = row.GetProperty("Tick").GetInt32();
                foreach ((int _, Vector2 point) in taps.Where(t => t.Tick == tick))
                {
                    _ = scene.Tap(point);
                }
                scene.Step();
                string context = $"{scenario}, tick {tick}";
                SimulationFrog frog = scene.Frogs[0];
                Assert.True(row.GetProperty("Frog")[0].GetInt32() == (int)frog.State, context + ": state");
                Near(row.GetProperty("Frog")[1].GetSingle(), frog.Open, context + ": open");
                List<PhysicsBody> bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Count);
                for (int b = 0; b < bodies.Count; b++)
                {
                    float[] actual = [bodies[b].Position.X, bodies[b].Position.Y, bodies[b].Velocity.X, bodies[b].Velocity.Y, bodies[b].Angle, bodies[b].AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(states[b][i].GetSingle(), actual[i], $"{context}, body {b}, component {i}");
                    }
                }
            }
        }

        /// <summary>Taps toggle the nearest-added frog within 78 logical units and are consumed even mid-motion.</summary>
        [Fact]
        public void TapsToggleOnlyNearbyFrogs()
        {
            CandySimulation scene = new(LevelReader.Read(Level("<frog x='60' y='300' path='200,-20,' tongueOut='0'/><candy x='160' y='50' id='c'/>")), LevelReader.Read(Design));
            Assert.False(scene.Tap(new(120 + 78, 600)));
            Assert.True(scene.Tap(new(120, 600)));
            Assert.Equal(FrogState.Opening, scene.Frogs[0].State);
            Assert.True(scene.Tap(new(120, 600)));
            Assert.Equal(FrogState.Opening, scene.Frogs[0].State);
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
