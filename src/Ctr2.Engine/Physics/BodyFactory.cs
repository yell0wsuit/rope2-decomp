using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Builds physics bodies from merged design dictionaries, like the scene's shared body factory.</summary>
    /// <remarks>Absent numbers read zero and absent flags read false, so an absent <c>gravityScale</c> means no gravity
    /// and an absent <c>body_type</c> means static. Polygon shapes and bullets are outside this factory's scope.</remarks>
    public static class BodyFactory
    {
        /// <summary>Merges a materialized shape's material design, its tag-derived shape type and its authored attributes.</summary>
        /// <param name="element">Authored <c>circle</c>, <c>rectangle</c> or <c>polygon</c> element, optionally <c>_materialized</c>.</param>
        /// <param name="master">Matching master design document.</param>
        /// <returns>A new design dictionary; later sources replace earlier keys.</returns>
        /// <exception cref="InvalidDataException">The tag is not a shape or its material design is missing or ambiguous.</exception>
        public static IReadOnlyDictionary<string, string> MaterializedDesign(LevelElement element, LevelDefinition master)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(master);
            string shape = element.Kind.Split('_')[0];
            if (shape is not ("circle" or "rectangle" or "polygon") || !element.Kind.StartsWith(shape, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Not a materialized shape: " + element.Kind);
            }
            Dictionary<string, string> design = new(StringComparer.Ordinal);
            if (element.Kind.EndsWith("materialized", StringComparison.Ordinal))
            {
                string material = element.Attributes.GetValueOrDefault("material", "");
                LevelElement[] matches = [.. master.Objects.Where(o => o.Attributes.GetValueOrDefault("id") == material)];
                if (matches.Length != 1)
                {
                    throw new InvalidDataException("Expected one master design for material: " + material);
                }
                foreach ((string key, string value) in matches[0].Attributes)
                {
                    design[key] = value;
                }
            }
            design["shapeType"] = shape;
            foreach ((string key, string value) in element.Attributes)
            {
                design[key] = value;
            }
            return design.AsReadOnly();
        }

        /// <summary>Creates a body at the design's doubled editor position and authored degree angle.</summary>
        /// <param name="world">Owning world.</param>
        /// <param name="design">Merged design dictionary.</param>
        /// <returns>The new body.</returns>
        /// <exception cref="InvalidDataException">A value is malformed or the design is outside the supported scope.</exception>
        public static PhysicsBody Create(PhysicsWorld world, IReadOnlyDictionary<string, string> design)
        {
            ArgumentNullException.ThrowIfNull(design);
            float angle = 0, x = 0, y = 0;
            if (design.TryGetValue("angle", out string? text))
            {
                // The native conversion runs in double precision before narrowing.
                angle = (float)(FloatValue(text) * 3.14159265 / 180.0);
            }
            if (design.TryGetValue("x", out text))
            {
                float value = FloatValue(text);
                x = value + value;
            }
            if (design.TryGetValue("y", out text))
            {
                float value = FloatValue(text);
                y = value + value;
            }
            return Create(world, design, new(x / 200, y / 200), angle);
        }

        /// <summary>Creates a circle or rectangle body with one fixture at an explicit origin and angle.</summary>
        /// <param name="world">Owning world.</param>
        /// <param name="design">Merged design dictionary.</param>
        /// <param name="position">Body origin in meters.</param>
        /// <param name="angle">Clockwise radians.</param>
        /// <returns>The new body.</returns>
        /// <exception cref="InvalidDataException">A value is malformed or the design is outside the supported scope.</exception>
        public static PhysicsBody Create(PhysicsWorld world, IReadOnlyDictionary<string, string> design, Vector2 position, float angle)
        {
            ArgumentNullException.ThrowIfNull(world);
            ArgumentNullException.ThrowIfNull(design);
            PhysicsBodyType type = design.GetValueOrDefault("body_type") switch
            {
                "kinematic" => PhysicsBodyType.Kinematic,
                "dynamic" => PhysicsBodyType.Dynamic,
                _ => PhysicsBodyType.Static
            };
            Vector2 velocity = new(Number(design, "linearVelocityX"), Number(design, "linearVelocityY"));
            float angularVelocity = Number(design, "angularVelocity");
            bool fixedRotation = design.GetValueOrDefault("fixedRotation") == "true";
            if (design.GetValueOrDefault("bullet") == "true")
            {
                throw new InvalidDataException("Bullet bodies are deliberately unported; no authored body uses them.");
            }
            if (type == PhysicsBodyType.Static && (velocity != Vector2.Zero || angularVelocity != 0))
            {
                // Native islands copy a static body's definition velocity into contact solving; no authored body needs that.
                throw new InvalidDataException("Static design bodies with initial velocity are not supported.");
            }
            float gravityScale = Number(design, "gravityScale"), density = Number(design, "density");
            if (type == PhysicsBodyType.Dynamic && density <= 0)
            {
                throw new InvalidDataException("Dynamic design bodies require positive density.");
            }
            PhysicsBody body;
            try
            {
                switch (design.GetValueOrDefault("shapeType"))
                {
                    case "circle":
                        float radius = Number(design, "radius");
                        body = world.AddCircle(position, (float)((radius + radius) / 200.0), density, gravityScale, angle, type, fixedRotation);
                        break;
                    case "rectangle":
                        float width = Number(design, "width"), height = Number(design, "height");
                        float w = (float)((width + width) / 200.0), h = (float)((height + height) / 200.0);
                        body = world.AddPolygon(ConvexPolygon.Box((float)(w / 2.0), (float)(h / 2.0)), position, density, gravityScale, angle,
                            fixedRotation, compound: false, type);
                        break;
                    default:
                        throw new InvalidDataException("Design body factory supports circle and rectangle shapes only.");
                }
                body.LinearDamping = Number(design, "linearDamping");
                body.AngularDamping = Number(design, "angularDamping");
                body.Friction = Number(design, "friction");
                body.Restitution = Number(design, "restitution");
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Invalid design body geometry, mass or material.", error);
            }
            body.IsSensor = design.GetValueOrDefault("isSensor") == "true";
            PhysicsCollisionFilter filter = Filter(design);
            if (filter != PhysicsCollisionFilter.Default)
            {
                // New proxies already await pair discovery, so this matches a creation-time filter.
                foreach (int fixture in world.GetFixtureLabels(body))
                {
                    world.SetFixtureFilter(fixture, filter);
                }
            }
            if (type != PhysicsBodyType.Static)
            {
                body.SetLinearVelocity(velocity);
                body.SetAngularVelocity(angularVelocity);
            }
            return body;
        }

        /// <summary>Builds the fixture filter from the authored category, collide-with mode and excluded categories.</summary>
        /// <param name="design">Merged design dictionary.</param>
        /// <returns>The native 16-bit filter with group zero.</returns>
        private static PhysicsCollisionFilter Filter(IReadOnlyDictionary<string, string> design)
        {
            ushort category = (ushort)(1 << Integer(design.GetValueOrDefault("filter.category")));
            ushort mask = design.GetValueOrDefault("filter.collideWith") switch
            {
                "like me" => category,
                "not like me" => (ushort)~category,
                _ => ushort.MaxValue
            };
            if (design.TryGetValue("filter.excludeCategories", out string? excluded))
            {
                foreach (string part in excluded.Split(','))
                {
                    mask &= (ushort)~(1 << Integer(part));
                }
            }
            return new(category, mask, 0);
        }

        /// <summary>Reads an absent or empty value as zero, otherwise one bounded invariant decimal.</summary>
        /// <param name="design">Merged design dictionary.</param>
        /// <param name="name">Attribute name.</param>
        /// <returns>The parsed value.</returns>
        private static float Number(IReadOnlyDictionary<string, string> design, string name)
        {
            return FloatValue(design.GetValueOrDefault(name));
        }

        /// <summary>Parses like <c>ZString::floatValue</c> for well-formed text; partial numbers are rejected.</summary>
        /// <param name="text">Authored text, or null.</param>
        /// <returns>Zero for null or blank text, otherwise the parsed value.</returns>
        /// <exception cref="InvalidDataException">The text is not a finite decimal within one million.</exception>
        private static float FloatValue(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? 0
                : float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && MathF.Abs(value) <= 1000000
                ? value : throw new InvalidDataException("Invalid design number: " + text);
        }

        /// <summary>Parses like <c>ZString::intValue</c> for well-formed text; partial numbers are rejected.</summary>
        /// <param name="text">Authored text, or null.</param>
        /// <returns>Zero for null or blank text, otherwise a category index from 0 to 31.</returns>
        /// <exception cref="InvalidDataException">The text is not an integer category index.</exception>
        private static int Integer(string? text)
        {
            // Shifts beyond the 16 filter bits produce zero after narrowing, as in the native assignment.
            return string.IsNullOrWhiteSpace(text) ? 0
                : int.TryParse(text, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) && value is >= 0 and < 32
                ? value : throw new InvalidDataException("Invalid design filter category: " + text);
        }
    }
}
