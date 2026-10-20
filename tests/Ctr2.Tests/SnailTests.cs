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
    /// <summary>Invented polygon tracks exercise snail attachment, directional body geometry and touch activation.</summary>
    public sealed class SnailTests
    {
        internal const string Track = "120,0,120,40,0,40,";
        internal const string SnailPolygon = "20,-2,12,-30,-4,-35,-5,-2,";
        internal const string Terrain = "<polygon_materialized id='track' x='100' y='200' polygon='" + Track + "' smoothFactor='3' material='wood'/>";
        internal const string Designs = "<polygon id='wood' body_type='static' friction='0.4'/>" +
            "<polygon id='snail' body_type='kinematic' polygon='" + SnailPolygon + "' smoothFactor='1' friction='0.4' density='1' speed='2'/>";

        internal static CandySimulation Create(string objects, bool play = false)
        {
            string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + Designs + "</layer></map>", StringComparison.Ordinal);
            return new(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy x='300' y='30'/>" + objects + "</layer></map>"), LevelReader.Read(design), play: play);
        }

        /// <summary>A snail can precede its named terrain in the file; attachment moves its body in the loader post-pass.</summary>
        [Fact]
        public void AttachesBeforeTheFirstStep()
        {
            CandySimulation scene = Create("<snail x='20' y='30' attachTo='track' percent='0'/>" + Terrain);
            PhysicsBody snail = Assert.Single(scene.World.Bodies, b => b.Type == PhysicsBodyType.Kinematic);
            Assert.Equal(new Vector2(1, 2), snail.Origin);
            Assert.Equal(Vector2.Zero, snail.Velocity);
        }

        /// <summary>A negative speed mirrors the first four authored polygon points before the body factory runs.</summary>
        [Fact]
        public void NegativeSpeedMirrorsBodyGeometry()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track' speed='2'/><snail attachTo='track' speed='-2'/>");
            PhysicsBody[] snails = [.. scene.World.Bodies.Where(b => b.Type == PhysicsBodyType.Kinematic).Reverse()];
            Assert.Equal(2, snails.Length);
            Assert.Equal(-.05f, snails[0].PolygonFixtures.SelectMany(f => f.Vertices).Min(v => v.X));
            Assert.Equal(.2f, snails[0].PolygonFixtures.SelectMany(f => f.Vertices).Max(v => v.X));
            Assert.Equal(-.2f, snails[1].PolygonFixtures.SelectMany(f => f.Vertices).Min(v => v.X));
            Assert.Equal(.05f, snails[1].PolygonFixtures.SelectMany(f => f.Vertices).Max(v => v.X));
        }

        /// <summary>An active snail first sets velocity after the first step; movement begins in the following step.</summary>
        [Fact]
        public void ActiveOnStartMovesOnTheFollowingStep()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track' activeOnStart='true'/>");
            PhysicsBody snail = Assert.Single(scene.World.Bodies, b => b.Type == PhysicsBodyType.Kinematic);
            Vector2 start = snail.Origin;
            scene.Step();
            Assert.Equal(start, snail.Origin);
            Assert.NotEqual(Vector2.Zero, snail.Velocity);
            scene.Step();
            Assert.NotEqual(start, snail.Origin);
        }

        /// <summary>Windows boolalpha does not treat numeric 1 as active.</summary>
        [Fact]
        public void NumericOneDoesNotActivate()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track' activeOnStart='1'/>");
            scene.Step();
            PhysicsBody snail = Assert.Single(scene.World.Bodies, b => b.Type == PhysicsBodyType.Kinematic);
            Assert.Equal(Vector2.Zero, snail.Velocity);
            Assert.Equal(0, snail.AngularVelocity);
        }

        /// <summary>The Windows touch offset uses the quantized table rotation; direct sine/cosine would accept this outside press.</summary>
        [Fact]
        public void TouchUsesTableRotatedVisualOffset()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track'/>");
            scene.Step();
            PhysicsBody snail = Assert.Single(scene.World.Bodies, b => b.Type == PhysicsBodyType.Kinematic);
            scene.World.SetTransform(snail, snail.Origin, .01337f);
            Vector2 center = (snail.Origin * 200) + CandySimulation.TableRotate(new(0, -57), snail.Angle);
            Assert.False(scene.Tap(new(MathF.BitIncrement(center.X + 80), center.Y)));
            Assert.True(scene.Tap(center));
            scene.Step();
            Assert.NotEqual(Vector2.Zero, snail.Velocity);
            center = (snail.Origin * 200) + CandySimulation.TableRotate(new(0, -57), snail.Angle);
            Assert.True(scene.Tap(center));
            Assert.Equal(Vector2.Zero, snail.Velocity);
            Assert.Equal(0, snail.AngularVelocity);
        }

        /// <summary>Windows multiplies by its full reciprocal of pi; the older generic table helper chooses entry 113 here.</summary>
        [Fact]
        public void TouchTableIndexUsesWindowsReciprocal()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track'/>");
            scene.Step();
            SimulationSnail snail = Assert.Single(scene.Snails);
            float angle = BitConverter.UInt32BitsToSingle(0x3f317fff);
            scene.World.SetTransform(snail.Body, snail.Body.Origin, angle);
            float sine = (float)Math.Sin(2 * 112 * 3.14159265 / 1024.0), cosine = (float)Math.Cos(2 * 112 * 3.14159265 / 1024.0);
            Vector2 expected = (snail.Body.Origin * 200) + new Vector2(57 * sine, -57 * cosine);
            Assert.Equal(expected, snail.VisualCenter);
        }

        /// <summary>An unresolved polygon attachment fails explicitly rather than silently constructing an inert snail.</summary>
        [Fact]
        public void MissingTrackFailsExplicitly()
        {
            _ = Assert.Throws<InvalidDataException>(() => Create("<snail attachTo='missing'/>"));
        }

        /// <summary>The gameplay constructor admits snail levels after their physical attachment succeeds.</summary>
        [Fact]
        public void SnailLevelCanStart()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track'/><omnom x='280' y='400'/>", play: true);
            Assert.NotNull(scene.Gameplay);
        }

        /// <summary>Out-of-range editor percentages clamp to the inclusive spline endpoints.</summary>
        /// <param name="text">Authored percentage.</param>
        /// <param name="expected">Clamped parameter.</param>
        [Theory]
        [InlineData("-20", 0f)]
        [InlineData("200", 1f)]
        public void PercentClampsToTheTrackEndpoints(string text, float expected)
        {
            CandySimulation scene = Create(Terrain + $"<snail attachTo='track' percent='{text}'/>");
            Assert.Equal(expected, Assert.Single(scene.Snails).Parameter);
        }

        /// <summary>The snail's journal slot follows candy, while its static target has no journal slot.</summary>
        [Fact]
        public void JournalRegistersSnailAfterCandy()
        {
            CandySimulation scene = Create(Terrain + "<snail attachTo='track'/>");
            ReplayJournal journal = new("invented", 2, [],
                [new JournalSnapshot(1, [new JournalObjectState(1, 1, 2, null, null, null, null)])]);
            JournalReplayResult result = JournalReplay.Run(scene, journal);
            Assert.Empty(result.Unsupported);
            Assert.Equal("snail", Assert.Single(result.Objects).Kind);
            Assert.Equal(new Vector2(1, 2), Assert.Single(result.Samples).Port);
        }

        /// <summary>Every body matches independent native spline, walking, wrap, pause/resume and contact states.</summary>
        /// <param name="scenario">Walking direction.</param>
        /// <param name="speed">Authored signed speed.</param>
        /// <param name="percent">Initial percent near the wrap boundary.</param>
        /// <param name="normalized">Whether the target is normalized and rotated; its angle does not rotate the snail track.</param>
        [Theory]
        [InlineData("forward", "2", "98", false)]
        [InlineData("reverse", "-1.3", "2", false)]
        [InlineData("normalized", "2.5", "40", true)]
        public void MatchesNativeWalkingTrace(string scenario, string speed, string percent, bool normalized)
        {
            string target = normalized ? "<polygon id='track' x='100' y='200' polygon='" + Track + "' smoothFactor='3' normalizeShape='true' angle='25' body_type='static' friction='0.4'/>" : Terrain;
            CandySimulation scene = Create(target + $"<snail attachTo='track' speed='{speed}' percent='{percent}' activeOnStart='true'/>");
            SimulationSnail snail = Assert.Single(scene.Snails);
            using JsonDocument reference = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "SnailReference.json")));
            JsonElement[] rows = [.. reference.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(240, rows.Length);
            foreach (JsonElement row in rows)
            {
                if (scene.Tick is 40 or 65 or 130 or 160)
                {
                    Assert.True(scene.Tap(snail.VisualCenter));
                }
                scene.Step();
                Assert.Equal(row.GetProperty("T").GetSingle(), snail.Parameter);
                Assert.Equal(row.GetProperty("Active").GetBoolean(), snail.Active);
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
                        Assert.True(float.IsFinite(value) && value == state[j], $"{scenario}, tick {scene.Tick}, body {i}, component {j}: expected {value:R}, actual {state[j]:R}");
                    }
                }
            }
        }
    }
}
