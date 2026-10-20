using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Exercises Windows magnets against an independent native oracle, using invented levels.</summary>
    public sealed class MagnetTests
    {
        /// <summary>The rope test designs plus an invented static magnet body and its force design.</summary>
        private static readonly string Design = RopeTests.Design.Replace("</layer></map>",
            "<circle id='magnet' body_type='static' friction='0.2' restitution='0.3' density='0.4' gravityScale='1' radius='16'/>" +
            "<magnet_design id='magnet_design' force='0.6'/></layer></map>", StringComparison.Ordinal);

        private static CandySimulation Create(string objects, string extraDesign = "", bool play = false)
        {
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(Design.Replace("</layer></map>", extraDesign + "</layer></map>", StringComparison.Ordinal)), play: play);
        }

        /// <summary>Invented levels in the oracle's body creation order.</summary>
        private static string Level(string scenario)
        {
            return scenario == "swing"
                ? "<grab x='160' y='60' length='140' target_id='candy' target_x='0' target_y='0' balloons='0'/><candy x='160' y='200' id='candy'/>" +
                  "<magnet x='60' y='200'/><magnet x='280' y='220' angle='30'/>"
                : "<candy x='150' y='150' id='candy'/><magnet x='150' y='250'/>";
        }

        /// <summary>Checks presses from the cached pose, facing, pull, candy damping and every body at every native tick.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("swing")]
        [InlineData("rest")]
        public void MatchesNativeMagnetTrace(string scenario)
        {
            CandySimulation scene = Create(Level(scenario));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MagnetReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(300, rows.Length);
            foreach (JsonElement row in rows)
            {
                string context = $"{scenario}, tick {row.GetProperty("Tick").GetInt32()}";
                JsonElement tap = row.GetProperty("Tap");
                if (tap[0].GetInt32() >= 0)
                {
                    Assert.True(scene.Tap(new(tap[1].GetSingle(), tap[2].GetSingle())), context);
                }
                scene.Step();
                Assert.Equal(row.GetProperty("Active").EnumerateArray().Select(a => a.GetBoolean()), scene.Magnets.Select(m => m.Active));
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Length);
                for (int b = 0; b < bodies.Length; b++)
                {
                    float[] actual = [bodies[b].Position.X, bodies[b].Position.Y, bodies[b].Velocity.X, bodies[b].Velocity.Y, bodies[b].Angle, bodies[b].AngularVelocity,
                        bodies[b].LinearDamping, bodies[b].GravityScale];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Assert.True(states[b][i].GetSingle() == actual[i], $"{context}, body {b}, component {i}: expected {states[b][i].GetSingle()}, actual {actual[i]}");
                    }
                }
            }
        }

        /// <summary>The press circle has radius 95 around the point 25 units along the cached facing; the boundary misses.</summary>
        [Fact]
        public void PressRadiusIsStrictAroundTheFacingPoint()
        {
            CandySimulation scene = Create("<candy x='160' y='400' id='candy'/><magnet x='100' y='100' angle='-90'/>");
            // Angle -90 degrees faces +x in logical units: the point is (225, 200).
            Assert.False(scene.Tap(new(225 + 95, 200)));
            Assert.True(scene.Tap(new(225 + 94.99f, 200)));
            Assert.True(Assert.Single(scene.Magnets).Active);
        }

        /// <summary>The magnet affects only the object named <c>candy</c>; without it presses still toggle but nothing is pulled.</summary>
        [Fact]
        public void OnlyTheCandyNamedCandyIsPulled()
        {
            CandySimulation scene = Create("<candy x='160' y='100' id='other'/><magnet x='160' y='300'/>");
            SimulationMagnet magnet = Assert.Single(scene.Magnets);
            Assert.True(scene.Tap(magnet.Body.Origin * 200));
            scene.Step();
            Assert.True(magnet.Active);
            Assert.Equal(0, magnet.Body.Angle);
            Assert.Equal(0, scene.Candies[0].LinearDamping);
        }

        /// <summary>An inactive magnet restores the candy's design damping after the rope update, which would slow the candy.</summary>
        [Fact]
        public void InactiveMagnetOverridesRopeSlowdown()
        {
            string level = "<grab x='160' y='60' length='60' target_id='candy'/><candy x='160' y='300' id='candy'/>";
            CandySimulation plain = Create(level), magnetic = Create(level + "<magnet x='20' y='450'/>");
            bool slowed = false;
            for (int i = 0; i < 60; i++)
            {
                plain.Step();
                magnetic.Step();
                slowed |= plain.Candies[0].LinearDamping > 0;
                Assert.Equal(0, magnetic.Candies[0].LinearDamping);
            }
            Assert.True(slowed);
        }

        /// <summary>Magnets are admitted by the gameplay constructor.</summary>
        [Fact]
        public void MagnetLevelCanStart()
        {
            CandySimulation scene = Create("<candy x='100' y='100' id='candy'/><omnom x='300' y='400'/><magnet x='200' y='100'/>", CharacterBodyDefinitionTests.Master, play: true);
            scene.Step();
            Assert.NotEqual(0, Assert.Single(scene.Magnets).Body.Angle);
        }
    }
}
