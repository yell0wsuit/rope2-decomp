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
    /// <summary>Invented gremlins exercise the Windows clone factory, touch rectangle and initial-count limit.</summary>
    public sealed class GremlinTests
    {
        private const string GremlinDesign = "<rectangle id='gremlin' x='999' y='999' width='30' height='20' body_type='dynamic' density='1' gravityScale='0' isSensor='true'/>";
        private const string Candy = "<candy x='300' y='30'/>";
        private const string ThrowerDesign = "<rectangle id='thrower' width='20' height='20' body_type='dynamic' density='1' gravityScale='0'/><thrower_design id='thrower_design' power='5'/>";

        internal static CandySimulation Create(string objects, bool play = false, string extraDesign = "")
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + GremlinDesign + extraDesign + "</layer></map>", StringComparison.Ordinal);
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" + Candy + objects + "</layer></map>"),
                LevelReader.Read(design), play: play);
        }

        /// <summary>An authored gremlin is built immediately but cannot clone before its first child update.</summary>
        [Fact]
        public void AuthoredGremlinClonesAfterItsFirstUpdate()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/>");
            Assert.Equal(3, scene.World.Bodies.Count);
            Assert.False(scene.Tap(new(200, 400)));
            scene.Step();
            Assert.True(scene.Tap(new(200, 400)));
            Assert.Equal(4, scene.World.Bodies.Count);
            PhysicsBody clone = scene.World.Bodies[0];
            Assert.Equal(new Vector2(1, 1.9f), clone.Origin);
            Assert.Equal(Vector2.Zero, clone.Velocity);
            Assert.False(scene.Tap(new(200, 360)));
        }

        /// <summary>The bottom and right touch edges are exclusive; the top and left edges are inclusive.</summary>
        /// <param name="x">Offset from the gremlin's logical x.</param>
        /// <param name="y">Offset from the gremlin's logical y.</param>
        /// <param name="accepted">Whether the native half-open rectangle consumes the press.</param>
        [Theory]
        [InlineData(-81, 0, true)]
        [InlineData(81, 0, false)]
        [InlineData(0, -57.5f, true)]
        [InlineData(0, 57.5f, false)]
        public void TouchRectangleHasNativeEdges(float x, float y, bool accepted)
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/>");
            scene.Step();
            Assert.Equal(accepted, scene.Tap(new(200 + x, 400 + y)));
        }

        /// <summary>The clone offset uses master height and the parent's live rotation, with no inherited placement fields.</summary>
        [Fact]
        public void RotatedCloneUsesMasterDesignAndFreshVelocity()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200' angle='90' height='60' density='7'/>");
            scene.Step();
            PhysicsBody parent = scene.World.Bodies[0];
            parent.SetLinearVelocity(new(3, -2));
            parent.SetAngularVelocity(4);
            Assert.True(scene.Tap(parent.Origin * 200));
            PhysicsBody clone = scene.World.Bodies[0];
            Vector2 expected = parent.Position + PhysicsMath.Rotate(new(0, -.1f), parent.Angle);
            Assert.Equal(expected, clone.Origin);
            Assert.Equal(parent.Angle, clone.Angle);
            Assert.Equal(.06f, clone.Mass, 5);
            Assert.Equal(Vector2.Zero, clone.Velocity);
            Assert.Equal(0, clone.AngularVelocity);
        }

        /// <summary>Ten authored gremlins exhaust the loader cap; later placements create no bodies.</summary>
        [Fact]
        public void LoaderKeepsAtMostTenAuthoredGremlins()
        {
            CandySimulation scene = Create(string.Concat(Enumerable.Repeat("<gremlin x='100' y='200'/>", 12)));
            Assert.Equal(12, scene.World.Bodies.Count);
        }

        /// <summary>A gremlin-bearing level is admitted by the gameplay constructor.</summary>
        [Fact]
        public void GremlinLevelCanStart()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/><omnom x='200' y='400'/>", play: true);
            scene.Step();
            Assert.True(scene.Tap(new(200, 400)));
        }

        /// <summary>The source subtracts fixed dt from a 0.1-second cooldown; it expires on the sixth child update.</summary>
        [Fact]
        public void ParentCooldownLastsSixUpdates()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/>");
            scene.Step();
            SimulationGremlin parent = scene.Gremlins[0];
            Vector2 press = (parent.Body.Origin * 200) + new Vector2(0, 57);
            Assert.True(scene.Tap(press));
            for (int i = 0; i < 6; i++)
            {
                Assert.False(scene.Tap(press));
                scene.Step();
            }
            Assert.True(scene.Tap(press));
        }

        /// <summary>Cap eviction retires the oldest clone after the next step; authored gremlins remain.</summary>
        [Fact]
        public void OldestCloneIsRemovedAfterOneMoreStep()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/>");
            scene.Step();
            for (int i = 0; i < 9; i++)
            {
                Assert.True(scene.Tap(scene.Gremlins[^1].Body.Origin * 200));
                scene.Step();
            }
            SimulationGremlin oldest = scene.Gremlins[1];
            Assert.True(scene.Tap(scene.Gremlins[^1].Body.Origin * 200));
            Assert.False(oldest.Active);
            Assert.False(oldest.Removed);
            Assert.Contains(oldest.Body, scene.World.Bodies);
            scene.Step();
            Assert.True(oldest.Removed);
            Assert.DoesNotContain(oldest.Body, scene.World.Bodies);
            Assert.False(scene.Gremlins[0].Removed);
            Assert.Equal(10, scene.Gremlins.Count(g => !g.Removed));
        }

        /// <summary>A thrower captures a touching gremlin with its dedicated 0.17 m hinge.</summary>
        [Fact]
        public void ThrowerCapturesGremlin()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/><thrower x='100' y='200'/>", extraDesign:
                ThrowerDesign);
            scene.Step();
            SimulationThrower thrower = Assert.Single(scene.Throwers);
            Assert.Same(scene.Gremlins[0].Body, thrower.Captured);
            Assert.Equal(.17f, thrower.JointLength);
            Assert.True(scene.Tap(thrower.Body.Origin * 200));
            _ = Assert.Single(scene.Gremlins);
            Assert.Null(thrower.Captured);
        }

        /// <summary>Authored gremlins and the newest clone are protected, even when that leaves eleven bodies.</summary>
        [Fact]
        public void SameFrameClonesProtectTenAuthoredGremlins()
        {
            CandySimulation scene = Create(string.Concat(Enumerable.Repeat("<gremlin x='100' y='200'/>", 10)));
            scene.Step();
            for (int i = 0; i < 3; i++)
            {
                Assert.True(scene.Tap(new(200, 400)));
            }
            Assert.Equal(13, scene.Gremlins.Count);
            Assert.Equal(2, scene.Gremlins.Count(g => !g.Active));
            Assert.All(scene.Gremlins.Take(10), g => Assert.True(g.Active));
            scene.Step();
            Assert.Equal(11, scene.Gremlins.Count(g => !g.Removed));
        }

        /// <summary>The cap skips an old clone held by a thrower and removes the next unheld clone.</summary>
        [Fact]
        public void HeldCloneIsProtectedFromEviction()
        {
            CandySimulation scene = Create("<gremlin x='100' y='200'/><thrower x='270' y='200'/>", extraDesign: ThrowerDesign);
            scene.Step();
            Assert.True(scene.Tap(scene.Gremlins[0].Body.Origin * 200));
            scene.Step();
            SimulationGremlin held = scene.Gremlins[1];
            SimulationThrower thrower = Assert.Single(scene.Throwers);
            scene.World.SetTransform(held.Body, thrower.Body.Origin, 0);
            scene.Step();
            Assert.Same(held.Body, thrower.Captured);
            for (int i = 0; i < 9; i++)
            {
                Assert.True(scene.Tap(scene.Gremlins[^1].Body.Origin * 200));
                scene.Step();
            }
            Assert.True(held.Active);
            Assert.False(held.Removed);
            Assert.Same(held.Body, thrower.Captured);
            Assert.True(scene.Gremlins[2].Removed);
        }

        /// <summary>Initial gremlins precede the character, but a clone appends after every initial journal slot.</summary>
        [Fact]
        public void JournalAppendsClonesAfterInitialObjects()
        {
            const string level = "<gremlin x='100' y='200'/><omnom x='200' y='400'/>";
            CandySimulation scene = Create(level);
            ReplayJournal journal = new("invented", 2,
                [new JournalEvent(1, JournalEventType.TouchDown, 200, 400, 0, 0, 0)],
                [new JournalSnapshot(1, [new JournalObjectState(1, 1, 2, 0, null, null, null)]),
                 new JournalSnapshot(2, [new JournalObjectState(3, 1, 1.9f, 0, null, null, null)])]);
            JournalReplayResult result = JournalReplay.Run(scene, journal);
            Assert.Empty(result.Unsupported);
            Assert.Equal([1, 3], result.Objects.Select(o => o.Index));
            Assert.All(result.Objects, o => Assert.Equal("gremlin", o.Kind));
            Assert.All(result.Samples, s => Assert.Equal(s.Recorded, s.Port));
        }

        /// <summary>A solid clone stack, native cap eviction and floor collisions match independent Box2D states at every tick.</summary>
        [Fact]
        public void CloneStackMatchesNativePhysicsTrace()
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>",
                "<rectangle id='gremlin' width='30' height='20' body_type='dynamic' density='0.8' gravityScale='0.7' friction='0.4' restitution='0.1' " +
                "linearDamping='0.3' angularDamping='0.2' filter.category='2'/><polygon id='floor' body_type='static' friction='0.4'/></layer></map>", StringComparison.Ordinal);
            CandySimulation scene = new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<rectangle_materialized x='160' y='350' width='300' height='10' material='floor'/>" + Candy + "<gremlin x='100' y='100' angle='22'/></layer></map>"), LevelReader.Read(design));
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GremlinReference.json")));
            Assert.Equal(240, reference.RootElement.GetArrayLength());
            foreach (JsonElement row in reference.RootElement.EnumerateArray())
            {
                if (scene.Tick is >= 1 and <= 89 && (scene.Tick - 1) % 8 == 0)
                {
                    Assert.True(scene.Tap(scene.Gremlins[^1].Body.Origin * 200));
                }
                scene.Step();
                PhysicsBody[] bodies = [.. scene.World.Bodies.Reverse()];
                JsonElement[] expected = [.. row.GetProperty("Bodies").EnumerateArray()];
                Assert.Equal(expected.Length, bodies.Length);
                for (int i = 0; i < bodies.Length; i++)
                {
                    PhysicsBody b = bodies[i];
                    float[] state = [b.Origin.X, b.Origin.Y, b.Velocity.X, b.Velocity.Y, b.Angle, b.AngularVelocity];
                    for (int j = 0; j < state.Length; j++)
                    {
                        float value = expected[i][j].GetSingle();
                        Assert.True(float.IsFinite(value) && value == state[j], $"Tick {scene.Tick}, body {i}, component {j}: expected {value:R}, actual {state[j]:R}");
                    }
                }
            }
        }
    }
}
