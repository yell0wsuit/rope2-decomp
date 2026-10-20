using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine
{
    /// <summary>An object and its initial logical position; unknown objects can lack coordinates.</summary>
    /// <param name="Source">Unmodified authored attributes.</param>
    /// <param name="Position">Doubled editor position, or null when an unknown object has no position. Candy and grabs read
    /// whole editor units, as the scene loader's <c>2 * intValue</c> does; every other object reads its position as a float.</param>
    /// <param name="Angle">Authored clockwise degrees in the downward-positive logical scene.</param>
    public sealed record PlacedLevelObject(LevelElement Source, Vector2? Position, float Angle);

    /// <summary>A diagnostic straight rope link, without simulation or sag.</summary>
    /// <param name="Start">Initial grab position in logical units.</param>
    /// <param name="End">Target attach point: its position plus the rotated target shift.</param>
    /// <param name="RestLength">Doubled authored rope length in logical units.</param>
    public sealed record LevelRope(Vector2 Start, Vector2 End, float RestLength);

    /// <summary>An unsmoothed diagnostic polygon in logical scene coordinates.</summary>
    /// <param name="Vertices">The placement origin, the path's implicit first vertex, then the translated, doubled editor
    /// vertices; not collision geometry.</param>
    public sealed record LevelPolygon(IReadOnlyList<Vector2> Vertices);

    /// <summary>Initial logical layout prepared without constructing a physics world.</summary>
    /// <param name="Size">Doubled editor canvas dimensions.</param>
    /// <param name="Objects">All objects, including unknown kinds.</param>
    /// <param name="Ropes">Resolved basic grab links.</param>
    /// <param name="Polygons">Unsmoothed authored polygon outlines.</param>
    /// <param name="Diagnostics">Unsupported features and off-screen positions requiring later work.</param>
    /// <param name="Named">Editor IDs of name-registering objects; a later duplicate replaces an earlier one.</param>
    public sealed record LevelLayout(Vector2 Size, IReadOnlyList<PlacedLevelObject> Objects,
        IReadOnlyList<LevelRope> Ropes, IReadOnlyList<LevelPolygon> Polygons, IReadOnlyList<string> Diagnostics,
        IReadOnlyDictionary<string, PlacedLevelObject> Named)
    {
        /// <summary>Reports whether the scene loader registers this object's editor ID for name lookup.</summary>
        /// <param name="kind">Authored element name.</param>
        /// <returns>True for Om Nom, throwers, Bookas, candy, fireballs and circle/rectangle/polygon shapes.</returns>
        public static bool RegistersName(string kind)
        {
            return kind is "omnom" or "thrower" or "booka" or "candy" or "fireball" ||
                kind.StartsWith("circle", StringComparison.Ordinal) || kind.StartsWith("rectangle", StringComparison.Ordinal) ||
                kind.StartsWith("polygon", StringComparison.Ordinal);
        }

        /// <summary>Creates logical diagnostic geometry while preserving authored data and negative positions.</summary>
        /// <remarks>Numbers read like the native accessors: absent ones are zero, but malformed text fails.</remarks>
        /// <param name="level">Parsed editor-space definition.</param>
        /// <returns>A bounded static layout with explicit diagnostics for pending behavior.</returns>
        /// <exception cref="InvalidDataException">Numbers, lengths or polygon data are invalid.</exception>
        public static LevelLayout Create(LevelDefinition level)
        {
            List<PlacedLevelObject> objects = [];
            List<LevelRope> ropes = [];
            List<LevelPolygon> polygons = [];
            List<string> diagnostics = [];
            Dictionary<string, PlacedLevelObject> named = new(StringComparer.Ordinal);
            Vector2 size = new(level.Width * 2, level.Height * 2);
            foreach (LevelElement element in level.Objects)
            {
                bool known = element.Kind is "candy" or "omnom" or "star" or "grab" or "polygon_materialized";
                bool hasPosition = element.Attributes.ContainsKey("x") || element.Attributes.ContainsKey("y");
                Vector2? position = !known && !hasPosition ? null
                    : element.Kind is "candy" or "grab" ? new Vector2(NativeText.IntValue(Text(element, "x")), NativeText.IntValue(Text(element, "y"))) * 2
                    : new Vector2(NativeText.FloatValue(Text(element, "x")), NativeText.FloatValue(Text(element, "y"))) * 2;
                PlacedLevelObject placed = new(element, position, NativeText.FloatValue(Text(element, "angle")));
                objects.Add(placed);
                if (RegistersName(element.Kind) && element.Attributes.TryGetValue("id", out string? id))
                {
                    named[id] = placed;
                }
                if (!known)
                {
                    diagnostics.Add("Unsupported object: " + element.Kind);
                }
                if (position is Vector2 point && (point.X < 0 || point.Y < 0 || point.X > size.X || point.Y > size.Y))
                {
                    diagnostics.Add($"Initial off-screen object: {element.Kind} at ({point.X.ToString(CultureInfo.InvariantCulture)}, {point.Y.ToString(CultureInfo.InvariantCulture)}).");
                }
                if (element.Kind == "polygon_materialized")
                {
                    // As the smooth path reader: the origin is an implicit vertex, so two authored points make a triangle,
                    // one trailing empty component is dropped and a missing final y reads zero.
                    string path = Text(element, "polygon") ?? "";
                    string[] parts = (path.EndsWith(',') ? path[..^1] : path).Split(',');
                    if (path.Length == 0 || parts.Length < 4 || parts.Length > 8192)
                    {
                        throw new InvalidDataException("Invalid polygon vertex count.");
                    }
                    List<Vector2> vertices = [position!.Value];
                    for (int i = 0; i < parts.Length; i += 2)
                    {
                        vertices.Add(position.Value + (new Vector2(NativeText.FloatValue(parts[i]), i + 1 < parts.Length ? NativeText.FloatValue(parts[i + 1]) : 0) * 2));
                    }
                    polygons.Add(new(vertices.AsReadOnly()));
                }
            }
            foreach (PlacedLevelObject grab in objects.Where(o => o.Source.Kind == "grab"))
            {
                // An absent target name means "candy", as in the scene.
                string targetId = grab.Source.Attributes.GetValueOrDefault("target_id", "candy");
                if (!named.TryGetValue(targetId, out PlacedLevelObject? target) || target.Position == null)
                {
                    diagnostics.Add("Grab without rope target: " + targetId);
                    continue;
                }
                float length = NativeText.FloatValue(Text(grab.Source, "length")) * 2;
                if (length <= 0)
                {
                    throw new InvalidDataException("Rope length must be positive.");
                }
                // The doubled target shift is local to the target; rotate it by the authored angle.
                Vector2 shift = new Vector2(NativeText.FloatValue(Text(grab.Source, "target_x")), NativeText.FloatValue(Text(grab.Source, "target_y"))) * 2;
                float angle = target.Angle * MathF.PI / 180;
                Vector2 rotated = new((shift.X * MathF.Cos(angle)) - (shift.Y * MathF.Sin(angle)), (shift.X * MathF.Sin(angle)) + (shift.Y * MathF.Cos(angle)));
                ropes.Add(new(grab.Position!.Value, target.Position.Value + rotated, length));
            }
            diagnostics.Add("Static layout only: physics, rope sag/cutting, star collection, tutorials, intro, polygon smoothing, backgrounds and gameplay camera are pending.");
            return new(size, objects.AsReadOnly(), ropes.AsReadOnly(), polygons.AsReadOnly(), diagnostics.AsReadOnly(), named.AsReadOnly());
        }

        /// <summary>Reads an authored attribute, or null when it is absent.</summary>
        /// <param name="element">Object record.</param>
        /// <param name="name">Attribute name.</param>
        /// <returns>The authored text.</returns>
        private static string? Text(LevelElement element, string name)
        {
            return element.Attributes.GetValueOrDefault(name);
        }
    }
}
