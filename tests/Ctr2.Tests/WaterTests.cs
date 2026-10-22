using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Invented water scenes exercise the Windows late child update and body callbacks.</summary>
    public sealed class WaterTests
    {
        internal static CandySimulation Create(string objects, bool play = false, SceneTimings? timings = null)
        {
            string design = RopeTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                "<water_design id='water_design' gravity_multiplier='-0.6' water_damping='3' water_angular_damping='1'/>" +
                "<circle id='fireball' body_type='dynamic' radius='12' density='0.4' gravityScale='1'/>" +
                "<circle id='magnet' body_type='static' radius='10'/><magnet_design id='magnet_design' force='0.6'/>" +
                "</layer></map>", StringComparison.Ordinal);
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + objects +
                "</layer></map>"), LevelReader.Read(design), play: play, timings: timings);
        }

        /// <summary>Compares all body components with independent native Box2D over four invented scenes.</summary>
        [Theory]
        [InlineData("rope")]
        [InlineData("wood")]
        [InlineData("righting")]
        [InlineData("fireball")]
        public void MatchesNativeWaterTrace(string scenario)
        {
            string objects = scenario == "rope"
                ? "<grab x='160' y='60' length='140' target_id='candy'/><candy x='160' y='200' id='candy'/><water y='210'/>"
                : "<candy x='20' y='20'/>" + (scenario == "wood"
                    ? "<rectangle_materialized material='wood_dynamic' x='200' y='250' width='50' height='15' gravityScale='0.25'/>"
                    : scenario == "righting" ? "<omnom x='250' y='250' angle='70'/>" : "<fireball x='200' y='250'/>") + "<water y='100'/>";
            CandySimulation scene = Create(objects, play: scenario == "righting");
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WaterReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(240, rows.Length);
            foreach (JsonElement row in rows)
            {
                scene.Step();
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] states = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(states.Length, bodies.Length);
                for (int b = 0; b < bodies.Length; b++)
                {
                    PhysicsBody body = bodies[b];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity,
                        body.LinearDamping, body.GravityScale, body.AngularDamping];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Assert.True(float.IsFinite(states[b][i].GetSingle()) && states[b][i].GetSingle() == actual[i],
                            $"{scenario}, tick {scene.Tick}, body {b}, component {i}: expected {states[b][i].GetSingle()}, actual {actual[i]}");
                    }
                }
            }
        }

        /// <summary>Clones created between ticks are picked up on the next water walk using their master design.</summary>
        [Fact]
        public void NewlyClonedGremlinReceivesWaterPhysics()
        {
            CandySimulation scene = GremlinTests.Create("<gremlin x='100' y='200' gravityScale='5'/><water y='0'/>",
                extraDesign: "<water_design id='water_design' gravity_multiplier='-0.6' water_damping='3' water_angular_damping='1'/>");
            scene.Step();
            Assert.True(scene.Tap(scene.Gremlins[0].Body.Origin * 200));
            SimulationGremlin clone = scene.Gremlins[1];
            Assert.Equal(0, clone.Body.LinearDamping);
            scene.Step();
            Assert.Equal(3, clone.Body.LinearDamping);
            Assert.Equal(0, clone.Body.GravityScale);
            Assert.True(scene.IsUnderwater(clone.Body));
        }

        /// <summary>A rising water line is advanced after the current body's above/below decision.</summary>
        [Fact]
        public void FlowChangesTheFollowingWaterUpdate()
        {
            CandySimulation scene = Create("<candy x='100' y='100'/><water y='150' flowspeed='-20000'/>");
            scene.Step();
            Assert.Equal(.7f, scene.Candies[0].GravityScale);
            scene.Step();
            Assert.Equal(-.42f, scene.Candies[0].GravityScale);
        }

        /// <summary>Native Om Nom samples angular motion before applying its late righting impulse.</summary>
        [Fact]
        public void GameplaySamplesMotionBeforeRightingImpulse()
        {
            CandySimulation scene = Create("<candy x='20' y='20'/><omnom x='250' y='250' angle='70'/><water y='100'/>", play: true);
            for (int i = 0; i < 31; i++)
            {
                scene.Step();
            }
            CharacterMotionHistory motion = Assert.IsType<CharacterMotionHistory>(typeof(LevelGameplay).GetField("motion",
                BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scene.Gameplay));
            Assert.Equal(0, motion.ShortAngular);
            Assert.True(scene.Characters[0].AngularVelocity < 0);
        }

        /// <summary>Full immersion uses stream-rounded master gravity and the water damping.</summary>
        [Fact]
        public void SubmergedCandyUsesRoundedMasterDesign()
        {
            CandySimulation scene = Create("<candy x='100' y='200' gravityScale='5'/><water y='100' flowspeed='0'/>");
            scene.Step();
            PhysicsBody candy = scene.Candies[0];
            Assert.Equal(-.42f, candy.GravityScale);
            Assert.Equal(3, candy.LinearDamping);
            Assert.Equal(1, candy.AngularDamping);
            Assert.True(candy.Velocity.Y > 0); // Water changes the next solve, not the one just finished.
            float speed = candy.Velocity.Y;
            scene.Step();
            Assert.True(candy.Velocity.Y < speed);
        }

        /// <summary>The surface test uses world center, with float-rounded depth times double 1/80.</summary>
        [Fact]
        public void PartialImmersionScalesOnlyGravity()
        {
            CandySimulation scene = Create("<candy x='100' y='120'/><water y='100'/>");
            scene.Step();
            PhysicsBody candy = scene.Candies[0];
            float depth = (candy.Position.Y * 200) - 200;
            Assert.Equal((float)(depth * .0125) * -.42f, candy.GravityScale);
            Assert.Equal(3, candy.LinearDamping);
        }

        /// <summary>Water restores above-water defaults after magnets and ropes have changed them.</summary>
        [Fact]
        public void AboveWaterRestoresDesignAfterMagnet()
        {
            CandySimulation scene = Create("<candy x='100' y='100' id='candy'/><magnet x='200' y='100'/><water y='400'/>");
            Assert.True(scene.Tap(scene.Magnets[0].Body.Origin * 200));
            scene.Candies[0].GravityScale = 4;
            scene.Candies[0].AngularDamping = 9;
            scene.Step();
            Assert.Equal(.7f, scene.Candies[0].GravityScale);
            Assert.Equal(0, scene.Candies[0].LinearDamping);
            Assert.Equal(1, scene.Candies[0].AngularDamping);
        }

        /// <summary>Dynamic general polygons keep their authored design rather than a master material's gravity.</summary>
        [Fact]
        public void DynamicShapeUsesMergedDesign()
        {
            CandySimulation scene = Create("<candy x='20' y='20'/><rectangle_materialized material='wood_dynamic' x='200' y='250' width='50' height='15' gravityScale='0.25'/><water y='100'/>");
            scene.Step();
            PhysicsBody block = Assert.Single(scene.Shapes).Body;
            Assert.Equal(-.15f, block.GravityScale);
            Assert.Equal(3, block.LinearDamping);
        }

        /// <summary>Rope material points have no design-copy override and retain their own gravity.</summary>
        [Fact]
        public void RopePointsDoNotReceiveWaterPhysics()
        {
            CandySimulation scene = Create("<grab x='160' y='60' length='140' target_id='candy'/><candy x='160' y='200' id='candy'/><water y='0'/>");
            scene.Step();
            Assert.All(scene.World.Bodies.Where(b => b.Type == PhysicsBodyType.Dynamic && b != scene.Candies[0]), b => Assert.Equal(.6f, b.GravityScale));
        }

        /// <summary>The late water callback starts a fade; the next fireball update cuts ropes and destroys the body.</summary>
        [Fact]
        public void FireballExtinguishesThenDestroysBodyAndFinishesWaterTimeline()
        {
            CandySimulation scene = Create("<candy x='20' y='20'/><fireball x='200' y='250'/><water y='100'/>",
                timings: new(Fireball: [1, 1, .05, 1]));
            SimulationFireball fireball = Assert.Single(scene.Fireballs);
            scene.Step();
            Assert.True(fireball.Fading);
            Assert.True(fireball.Body.IsSensor);
            Assert.Equal(Vector2.Zero, fireball.Body.Velocity);
            Assert.Contains(fireball.Body, scene.World.Bodies);
            scene.Step();
            Assert.DoesNotContain(fireball.Body, scene.World.Bodies);
            Assert.False(fireball.Removed);
            scene.Step();
            scene.Step();
            Assert.False(fireball.Removed);
            scene.Step();
            Assert.True(fireball.Removed);
        }

        /// <summary>Stationary submerged objects beyond the side margin are lost even inside the expanded bounds.</summary>
        [Theory]
        [InlineData(-50, true)]
        [InlineData(370, true)]
        [InlineData(100, false)]
        public void UnderwaterSideLossUsesPreviousWaterCallback(float x, bool expected)
        {
            CandySimulation scene = Create($"<candy x='{x}' y='200'/><water y='100'/>");
            scene.Step();
            scene.Candies[0].SetLinearVelocity(Vector2.Zero);
            Assert.Equal(expected, scene.OutOfScreen(scene.Candies[0], 40));
            scene.Candies[0].SetLinearVelocity(new(.01f, 0));
            Assert.False(scene.OutOfScreen(scene.Candies[0], 40));
        }

        /// <summary>Gameplay accepts water without adding a body or journal slot.</summary>
        [Fact]
        public void WaterHasNoBodyOrJournalSlot()
        {
            const string objects = "<candy x='100' y='100'/><omnom x='280' y='400'/>";
            CandySimulation plain = Create(objects, play: true), wet = Create(objects + "<water y='200'/>", play: true);
            Assert.Equal(plain.InitialJournalCount, wet.InitialJournalCount);
            Assert.Equal(plain.World.Bodies.Count, wet.World.Bodies.Count);
        }
    }
}
