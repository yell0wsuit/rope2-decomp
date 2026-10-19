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
    /// <summary>Compares design-dictionary body construction with an independent native oracle, using invented designs.</summary>
    public sealed class BodyFactoryTests
    {
        /// <summary>Builds invented wood-like material designs; authored keys replace material keys.</summary>
        private static Dictionary<string, string> Wood(bool dynamic, params (string Key, string Value)[] overrides)
        {
            Dictionary<string, string> design = dynamic
                ? new()
                {
                    ["body_type"] = "dynamic",
                    ["linearVelocityX"] = "0",
                    ["linearVelocityY"] = "0",
                    ["angularVelocity"] = "0",
                    ["linearDamping"] = "0.5",
                    ["angularDamping"] = "0.5",
                    ["fixedRotation"] = "false",
                    ["bullet"] = "false",
                    ["gravityScale"] = "0.9",
                    ["friction"] = "0.2",
                    ["restitution"] = "0.1",
                    ["density"] = "0.2",
                    ["isSensor"] = "false",
                    ["filter.category"] = "15",
                    ["filter.collideWith"] = "all"
                }
                : new()
                {
                    ["body_type"] = "static",
                    ["linearVelocityX"] = "0",
                    ["linearVelocityY"] = "0",
                    ["angularVelocity"] = "0",
                    ["linearDamping"] = "0",
                    ["angularDamping"] = "0",
                    ["fixedRotation"] = "false",
                    ["bullet"] = "false",
                    ["gravityScale"] = "1",
                    ["friction"] = "0.3",
                    ["restitution"] = "0",
                    ["density"] = "0.4",
                    ["isSensor"] = "false",
                    ["filter.category"] = "14",
                    ["filter.collideWith"] = "all"
                };
            foreach ((string key, string value) in overrides)
            {
                design[key] = value;
            }
            return design;
        }

        private static Dictionary<string, string> Design(params (string Key, string Value)[] values)
        {
            return values.ToDictionary(v => v.Key, v => v.Value);
        }

        /// <summary>Invented designs in native creation order for each scenario.</summary>
        private static IEnumerable<Dictionary<string, string>> Designs(string scenario)
        {
            if (scenario == "wood")
            {
                yield return Wood(false, ("shapeType", "rectangle"), ("x", "160"), ("y", "400"), ("width", "300"), ("height", "20"));
                yield return Wood(false, ("shapeType", "rectangle"), ("x", "40"), ("y", "330"), ("width", "90"), ("height", "14"), ("angle", "30"));
                yield return Wood(true, ("shapeType", "rectangle"), ("x", "136"), ("y", "273"), ("width", "70"), ("height", "20"), ("angle", "0"));
                yield return Wood(true, ("shapeType", "rectangle"), ("x", "137"), ("y", "214"), ("width", "135"), ("height", "16"), ("angle", "90"));
                yield return Wood(true, ("shapeType", "rectangle"), ("x", "190"), ("y", "137"), ("width", "170"), ("height", "16"), ("angle", "-50"));
                yield return Wood(true, ("shapeType", "rectangle"), ("x", "81"), ("y", "184"), ("width", "60"), ("height", "20"), ("angle", "35"));
                yield return Wood(true, ("shapeType", "circle"), ("x", "186"), ("y", "85"), ("radius", "18"));
                yield return Wood(true, ("shapeType", "circle"), ("x", "236"), ("y", "132"), ("radius", "18"));
                yield return Design(("shapeType", "circle"), ("x", "60"), ("y", "60"), ("radius", "16"), ("body_type", "dynamic"), ("gravityScale", "0.7"),
                    ("density", "0.5"), ("angularDamping", "1"), ("friction", "0.2"), ("filter.category", "0"), ("filter.collideWith", "all"));
                yield break;
            }
            yield return Wood(false, ("shapeType", "rectangle"), ("x", "160"), ("y", "420"), ("width", "320"), ("height", "30"));
            yield return Wood(true, ("shapeType", "rectangle"), ("x", "60"), ("y", "300"), ("width", "50"), ("height", "30"), ("angle", "20"), ("fixedRotation", "true"));
            yield return Design(("shapeType", "circle"), ("x", "120"), ("y", "200"), ("radius", "15"), ("body_type", "kinematic"), ("linearVelocityX", "0.3"),
                ("angularVelocity", "1.5"), ("density", "0.4"), ("friction", "0.5"));
            yield return Wood(true, ("shapeType", "circle"), ("x", "200"), ("y", "120"), ("radius", "12"), ("linearVelocityX", "-0.4"), ("linearVelocityY", "-1"),
                ("angularVelocity", "-2"), ("gravityScale", "1.2"));
            Dictionary<string, string> weightless = Wood(true, ("shapeType", "rectangle"), ("x", "250"), ("y", "380"), ("width", "40"), ("height", "40"));
            _ = weightless.Remove("gravityScale");
            yield return weightless;
            yield return Wood(true, ("shapeType", "circle"), ("x", "100"), ("y", "330"), ("radius", "14"), ("filter.category", "3"), ("filter.collideWith", "like me"));
            yield return Wood(true, ("shapeType", "circle"), ("x", "104"), ("y", "280"), ("radius", "14"), ("filter.category", "3"), ("filter.collideWith", "not like me"));
            yield return Wood(true, ("shapeType", "circle"), ("x", "160"), ("y", "330"), ("radius", "14"), ("filter.excludeCategories", "14,2"));
            yield return Wood(true, ("shapeType", "rectangle"), ("x", "220"), ("y", "300"), ("width", "60"), ("height", "12"), ("isSensor", "true"), ("angle", "-15"));
            yield return Design(("shapeType", "rectangle"), ("x", "290"), ("y", "250"), ("width", "20"), ("height", "80"), ("angle", "12.5"), ("friction", "0.4"));
        }

        /// <summary>Checks creation data and every native tick of body state and solid contacts.</summary>
        /// <param name="scenario">Synthetic native scene.</param>
        [Theory]
        [InlineData("wood")]
        [InlineData("attributes")]
        public void MatchesNativeFactoryTrace(string scenario)
        {
            PhysicsWorld world = new();
            List<PhysicsBody> bodies = [.. Designs(scenario).Select(d => BodyFactory.Create(world, d))];
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "BodyFactoryReference.json")));
            JsonElement[] rows = [.. fixture.RootElement.EnumerateArray().Where(r => r.GetProperty("Scenario").GetString() == scenario)];
            Assert.Equal(181, rows.Length);
            HashSet<int> sensors = [];
            foreach (JsonElement created in rows[0].GetProperty("Created").EnumerateArray())
            {
                int label = created[0].GetInt32();
                PhysicsBody body = bodies[label - 1];
                string context = $"{scenario}, body {label}";
                PhysicsBodyType type = created[1].GetInt32() switch
                {
                    0 => PhysicsBodyType.Static,
                    1 => PhysicsBodyType.Kinematic,
                    _ => PhysicsBodyType.Dynamic
                };
                Assert.True(type == body.Type, context);
                Near(created[2].GetSingle(), body.Mass, context + ", mass");
                Near(created[3].GetSingle(), body.RotationalInertia, context + ", inertia");
                PhysicsCollisionFilter filter = world.GetFixtureFilter(world.GetFixtureLabels(body).Single());
                Assert.True((created[4].GetInt32(), created[5].GetInt32()) == (filter.CategoryBits, filter.MaskBits), context + ", filter");
                Assert.True(created[6].GetBoolean() == body.IsSensor, context + ", sensor");
                Assert.True(created[7].GetBoolean() == body.FixedRotation, context + ", fixed rotation");
                float[] values = [body.GravityScale, body.LinearDamping, body.AngularDamping, body.Friction, body.Restitution];
                for (int i = 0; i < values.Length; i++)
                {
                    Assert.True(created[8 + i].GetSingle() == values[i], $"{context}, coefficient {i}");
                }
                if (body.IsSensor)
                {
                    _ = sensors.Add(label);
                }
            }
            foreach (JsonElement row in rows.Skip(1))
            {
                world.Step();
                string context = $"{scenario}, tick {row.GetProperty("Tick").GetInt32()}";
                foreach (JsonElement state in row.GetProperty("Bodies").EnumerateArray())
                {
                    PhysicsBody body = bodies[state[0].GetInt32() - 1];
                    float[] actual = [body.Position.X, body.Position.Y, body.Velocity.X, body.Velocity.Y, body.Angle, body.AngularVelocity];
                    for (int i = 0; i < actual.Length; i++)
                    {
                        Near(state[i + 1].GetSingle(), actual[i], $"{context}, body {state[0]}, component {i}");
                    }
                }
                // Sensor pairs are excluded from the solid contact capture.
                (int, int, bool)[] expected = [.. row.GetProperty("Contacts").EnumerateArray()
                    .Where(c => !sensors.Contains(c[0].GetInt32()) && !sensors.Contains(c[1].GetInt32()))
                    .Select(c => (c[0].GetInt32(), c[1].GetInt32(), c[2].GetBoolean()))];
                Assert.Equal(expected, world.CaptureBodyContacts().Select(c => (c.FirstFixture, c.SecondFixture, c.Touching)));
            }
        }

        /// <summary>Merges material, tag shape type and authored attributes in loader order.</summary>
        [Fact]
        public void MaterializedDesignOverlaysAuthoredAttributes()
        {
            LevelDefinition master = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><polygon id='wood_dynamic' body_type='dynamic' x='98' y='178' density='0.2'/></layer></map>");
            LevelElement element = new("rectangle_materialized", new Dictionary<string, string> { ["x"] = "136", ["y"] = "273", ["material"] = "wood_dynamic", ["width"] = "70" });
            IReadOnlyDictionary<string, string> design = BodyFactory.MaterializedDesign(element, master);
            Assert.Equal("rectangle", design["shapeType"]);
            Assert.Equal("136", design["x"]);
            Assert.Equal("0.2", design["density"]);
            Assert.Equal("dynamic", design["body_type"]);
            _ = Assert.Throws<InvalidDataException>(() => BodyFactory.MaterializedDesign(element with
            {
                Attributes = new Dictionary<string, string> { ["material"] = "missing" }
            }, master));
        }

        /// <summary>Builds polygons from the smoothed path's triangles: the origin round-trips the editor position, each
        /// triangle becomes one counterclockwise fixture, and the material applies to every fixture.</summary>
        [Fact]
        public void PolygonDesignsBuildTriangleFixtures()
        {
            PhysicsWorld world = new();
            Dictionary<string, string> design = Wood(false, ("shapeType", "polygon"), ("x", "80.7"), ("y", "210.3"), ("angle", "12"),
                ("polygon", "50,0,50,40,25,15,0,40,"), ("smoothFactor", "2"));
            PhysicsBody body = BodyFactory.Create(world, design);
            SmoothCyclePath path = new(new((float)((float)(80.7f + 80.7f) / 200 * 200.0), (float)((float)(210.3f + 210.3f) / 200 * 200.0)),
                design["polygon"], false, 2);
            Assert.Equal(path.BodyOrigin, body.Origin);
            Assert.Equal((float)(12 * 3.14159265 / 180.0), body.Angle);
            Assert.Equal(path.TrianglePoints().Count / 3, body.PolygonFixtures.Count);
            foreach (ConvexPolygon fixture in body.PolygonFixtures)
            {
                Vector2 a = fixture.Vertices[0], b = fixture.Vertices[1], c = fixture.Vertices[2];
                Assert.True(((b.X - a.X) * (c.Y - b.Y)) - ((b.Y - a.Y) * (c.X - b.X)) >= 0);
            }
            Assert.Equal((PhysicsBodyType.Static, .3f), (body.Type, body.Friction));
            Assert.All(world.GetFixtureLabels(body), label => Assert.Equal(new PhysicsCollisionFilter(1 << 14, ushort.MaxValue, 0), world.GetFixtureFilter(label)));

            // Om Nom-style dynamic polygons normalize the path about the mean of every vertex, including the implicit origin.
            Dictionary<string, string> normalized = Wood(true, ("shapeType", "polygon"), ("polygon", "30,0,30,30,0,30,"), ("normalizeShape", "true"));
            PhysicsBody square = BodyFactory.Create(world, normalized);
            Assert.Equal(Vector2.Zero, square.Origin);
            Assert.InRange(square.LocalCenter.Length(), 0, 1e-6f);
            Assert.Equal(2, square.PolygonFixtures.Count);
        }

        /// <summary>Designs outside the ported factory scope fail before creating a misleading body.</summary>
        /// <param name="key">Overridden key.</param>
        /// <param name="value">Unsupported value.</param>
        [Theory]
        [InlineData("bullet", "true")]
        [InlineData("shapeType", "triangle")]
        [InlineData("density", "0")]
        [InlineData("radius", "1e2x")]
        [InlineData("filter.category", "1.5")]
        public void UnsupportedDesignsFailExplicitly(string key, string value)
        {
            Dictionary<string, string> design = Wood(true, ("shapeType", "circle"), ("radius", "10"));
            design[key] = value;
            _ = Assert.Throws<InvalidDataException>(() => BodyFactory.Create(new PhysicsWorld(), design));
        }

        private static void Near(float expected, float actual, string context)
        {
            Assert.True(float.IsFinite(expected) && float.IsFinite(actual) && MathF.Abs(expected - actual) <= .00002f * MathF.Max(1, MathF.Abs(expected)),
                $"{context}: expected {expected}, actual {actual}");
        }
    }
}
