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
    /// <summary>Exercises Windows balloon generators against an independent native oracle, using invented levels.</summary>
    public sealed class BalloonGeneratorTests
    {
        /// <summary>The balloon test designs plus an invented generator sensor and a static block material.</summary>
        private static readonly string Design = RopeTests.Design.Replace("</layer></map>",
            "<circle id='balloon' body_type='dynamic' linearDamping='5' angularDamping='5' gravityScale='-1' friction='0' restitution='0.3' density='0.5' radius='20'/>" +
            "<balloon_design id='balloon_design' stratosphere_y='120' stratosphere_koeff='1' stratosphere_damping_koeff='2.5'/>" +
            "<circle id='balloonGenerator' body_type='static' linearDamping='1.5' gravityScale='1' friction='0.2' restitution='0.3' density='0.1' isSensor='true' radius='25'/>" +
            "<polygon id='stone' body_type='static' friction='0.4' restitution='0.1' density='1'/></layer></map>", StringComparison.Ordinal);

        private static CandySimulation Create(string objects, string extraDesign = "", bool play = false)
        {
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects + "</layer></map>"),
                LevelReader.Read(Design.Replace("</layer></map>", extraDesign + "</layer></map>", StringComparison.Ordinal)), play: play);
        }

        /// <summary>Invented levels in the oracle's body creation order.</summary>
        private static string Level(string scenario)
        {
            return scenario == "lift"
                ? "<rectangle_materialized x='251' y='83' width='40' height='60' material='stone'/>" +
                  "<grab x='160' y='40' length='100' target_id='c' target_x='0' target_y='0' balloons='0'/><candy x='100' y='120' id='c'/>" +
                  "<balloonGenerator x='210' y='127' rope_length='50' balloons='3'/><balloonGenerator x='60' y='300' rope_length='40' balloons='1'/>"
                : "<circle_materialized x='250' y='300' radius='15' material='wood_dynamic' id='ball'/><candy x='100' y='50' id='c'/>" +
                  "<balloon x='250' y='200' length='60' target_x='0' target_y='0' target_id='ball'/>" +
                  "<balloonGenerator x='100' y='150' rope_length='40' balloons='1'/>";
        }

        /// <summary>Checks spawning, born-dead checks, removal, spawned taps and cuts, and every body at every native tick.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("lift")]
        [InlineData("drop")]
        public void MatchesNativeGeneratorTrace(string scenario)
        {
            CandySimulation scene = Create(Level(scenario));
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BalloonGeneratorReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(300, rows.Length);
            foreach (JsonElement row in rows)
            {
                string context = $"{scenario}, tick {row.GetProperty("Tick").GetInt32()}";
                JsonElement tap = row.GetProperty("Tap");
                if (tap[0].GetInt32() == 1)
                {
                    Assert.True(scene.Tap(new(tap[1].GetSingle(), tap[2].GetSingle())), context);
                }
                JsonElement cut = row.GetProperty("Cut");
                if (cut[0].GetInt32() >= 0)
                {
                    Assert.True(scene.Cut(cut[0].GetInt32(), cut[1].GetInt32(), new(cut[2].GetSingle(), cut[3].GetSingle())), context);
                }
                scene.Step();
                Assert.Equal(row.GetProperty("Ropes").GetInt32(), scene.RopeCount);
                Assert.Equal(row.GetProperty("Active").EnumerateArray().Select(a => a.GetBoolean()), scene.Balloons.Select(b => b.Active));
                Assert.Equal(row.GetProperty("Generators").EnumerateArray().Select(a => a.GetBoolean()), scene.BalloonGenerators.Select(g => !g.Removed));
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

        /// <summary>The three balloons fan out by half a radian around the generator, and the one inside the block is born dead.</summary>
        [Fact]
        public void SpawnFansOutAndRejectsBalloonsInsideStaticFixtures()
        {
            CandySimulation scene = Create(Level("lift"));
            while (scene.Balloons.Count == 0)
            {
                scene.Step();
            }
            Assert.Equal(3, scene.Balloons.Count);
            Assert.All(scene.Balloons, b => Assert.True(b.Spawned));
            Assert.Equal([true, true, false], scene.Balloons.Select(b => b.Active));
            Assert.Null(scene.Balloons[2].Body);
            // A born-dead balloon gets no rope: the grab rope plus one rope per live balloon.
            Assert.Equal(3, scene.RopeCount);
            Assert.False(scene.BalloonGenerators[0].Removed);
            scene.Step();
            Assert.True(scene.BalloonGenerators[0].Removed);
            Assert.False(scene.BalloonGenerators[1].Removed);
        }

        /// <summary>Only the candy game-body type triggers a generator; other dynamic bodies pass through its sensor.</summary>
        [Fact]
        public void OnlyCandyTriggersGenerator()
        {
            CandySimulation scene = Create("<candy x='20' y='20'/><circle_materialized x='100' y='100' radius='10' material='wood_dynamic'/>" +
                "<balloonGenerator x='100' y='130' rope_length='40' balloons='1'/>");
            for (int i = 0; i < 60; i++)
            {
                scene.Step();
            }
            Assert.Empty(scene.Balloons);
            Assert.False(Assert.Single(scene.BalloonGenerators).Removed);
        }

        /// <summary>A generator has no journal slot; its spawn appends the rope slot and then the balloon slot, and journaled
        /// cuts address the spawned rope by that slot.</summary>
        [Fact]
        public void SpawnAppendsRopeThenBalloonJournalSlots()
        {
            CandySimulation scene = Create("<candy x='100' y='100'/><balloonGenerator x='100' y='100' rope_length='40' balloons='1'/>");
            // Registry: candy (0); the spawn after step 1 appends the rope (1) and the balloon (2).
            ReplayJournal journal = new("invented", 3,
                [new JournalEvent(1, JournalEventType.RopeCut, 200, 180, 0, 1, 0)],
                [new JournalSnapshot(1, [new JournalObjectState(0, 1, 1, 0, null, null, null), new JournalObjectState(2, 1, .6f, 0, null, null, null)]),
                 new JournalSnapshot(3, [new JournalObjectState(2, 1, .6f, 0, null, null, null)])]);
            JournalReplayResult result = JournalReplay.Run(scene, journal);
            Assert.Empty(result.Unsupported);
            Assert.Equal(["candy", "balloon"], result.Objects.Select(o => o.Kind));
            Assert.Equal([0, 2], result.Objects.Select(o => o.Index));
            JournalDrift balloon = result.Objects[1];
            Assert.Equal(1, balloon.Samples);
            Assert.Equal(1, balloon.Missing);
            Assert.False(Assert.Single(scene.Balloons).Active);
        }

        /// <summary>Burned shapes leave the scene's shape list but keep their journal slots, so a generated rope keeps its slot.</summary>
        [Fact]
        public void BurnedShapeKeepsGeneratedRopeSlot()
        {
            CandySimulation scene = Create("<rectangle_materialized x='160' y='300' width='100' height='16' angle='0' material='wood_dynamic'/>" +
                "<fireball id='f' x='160' y='250'/><candy x='100' y='100'/><balloonGenerator x='100' y='100' rope_length='40' balloons='1'/>", FireballTests.Fireball);
            // Registry: the dynamic wood (0), candy (1) and fireball (2); the spawn appends the rope (3) and the balloon (4).
            scene.Step();
            Assert.Equal(1, scene.RopeCount);
            while (scene.Shapes.Count > 0)
            {
                Assert.True(scene.Tick < 200);
                scene.Step();
            }
            Assert.Equal(0, JournalReplay.RopeIndex(scene, 3));
            Assert.Equal(-1, JournalReplay.RopeIndex(scene, 4));
        }

        /// <summary>A spawned balloon is a late child: it takes a press before the authored balloons, newest first.</summary>
        [Fact]
        public void SpawnedBalloonTakesPressBeforeAuthoredBalloon()
        {
            CandySimulation scene = Create("<candy x='100' y='100' id='c'/><balloon x='100' y='80' length='20' target_x='0' target_y='0' target_id='c'/>" +
                "<balloonGenerator x='100' y='100' rope_length='20' balloons='1'/>");
            scene.Step();
            Assert.Equal(2, scene.Balloons.Count);
            Vector2 between = (scene.Balloons[0].Body!.Origin + scene.Balloons[1].Body!.Origin) * 100;
            Assert.True(scene.Tap(between));
            Assert.Equal([true, false], scene.Balloons.Select(b => b.Active));
        }

        /// <summary>Generator levels are admitted by the gameplay constructor.</summary>
        [Fact]
        public void GeneratorLevelCanStart()
        {
            CandySimulation scene = Create("<candy x='100' y='100'/><omnom x='300' y='400'/><balloonGenerator x='100' y='100' rope_length='40' balloons='1'/>",
                CharacterBodyDefinitionTests.Master, play: true);
            scene.Step();
            _ = Assert.Single(scene.Balloons);
        }
    }
}
