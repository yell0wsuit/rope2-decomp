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
    /// <param name="Position">Doubled editor position, or null when an unknown object has no position.</param>
    /// <param name="Angle">Authored clockwise degrees in the downward-positive logical scene.</param>
    public sealed record PlacedLevelObject(LevelElement Source, Vector2? Position, float Angle);

    /// <summary>A diagnostic straight rope link, without simulation or sag.</summary>
    /// <param name="Start">Initial grab position in logical units.</param>
    /// <param name="End">Target center, or null for an unverified offset.</param>
    /// <param name="RestLength">Doubled authored rope length in logical units.</param>
    public sealed record LevelRope(Vector2 Start, Vector2? End, float RestLength);

    /// <summary>An unsmoothed diagnostic polygon in logical scene coordinates.</summary>
    /// <param name="Vertices">Translated, doubled editor vertices; not collision geometry.</param>
    public sealed record LevelPolygon(IReadOnlyList<Vector2> Vertices);

    /// <summary>Initial logical layout prepared without constructing a physics world.</summary>
    /// <param name="Size">Doubled editor canvas dimensions.</param>
    /// <param name="Objects">All objects, including unknown kinds.</param>
    /// <param name="Ropes">Resolved basic grab links.</param>
    /// <param name="Polygons">Unsmoothed authored polygon outlines.</param>
    /// <param name="Diagnostics">Unsupported features and off-screen positions requiring later work.</param>
    public sealed record LevelLayout(Vector2 Size, IReadOnlyList<PlacedLevelObject> Objects,
        IReadOnlyList<LevelRope> Ropes, IReadOnlyList<LevelPolygon> Polygons, IReadOnlyList<string> Diagnostics)
    {
        /// <summary>Creates logical diagnostic geometry while preserving authored data and negative positions.</summary>
        /// <param name="level">Parsed editor-space definition.</param>
        /// <returns>A bounded static layout with explicit diagnostics for pending behavior.</returns>
        /// <exception cref="InvalidDataException">Coordinates, references, lengths or polygon data are invalid.</exception>
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
                Vector2? position = known || hasPosition ? new Vector2(Number(element, "x", integer: true), Number(element, "y", integer: true)) * 2 : null;
                PlacedLevelObject placed = new(element, position, Number(element, "angle", optional: true));
                objects.Add(placed);
                if (element.Attributes.TryGetValue("id", out string? id) && id.Length != 0 && !named.TryAdd(id, placed))
                {
                    throw new InvalidDataException("Duplicate level object ID: " + id);
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
                    string[] parts = Required(element, "polygon").TrimEnd(',').Split(',');
                    if (parts.Length < 6 || parts.Length > 8192 || parts.Length % 2 != 0)
                    {
                        throw new InvalidDataException("Invalid polygon vertex count.");
                    }
                    List<Vector2> vertices = [];
                    for (int i = 0; i < parts.Length; i += 2)
                    {
                        vertices.Add(position!.Value + (new Vector2(Parse(parts[i]), Parse(parts[i + 1])) * 2));
                    }
                    polygons.Add(new(vertices.AsReadOnly()));
                }
            }
            foreach (PlacedLevelObject grab in objects.Where(o => o.Source.Kind == "grab"))
            {
                string targetId = Required(grab.Source, "target_id");
                if (!named.TryGetValue(targetId, out PlacedLevelObject? target) || target.Position == null)
                {
                    throw new InvalidDataException("Unresolved rope target: " + targetId);
                }
                float length = Number(grab.Source, "length") * 2;
                if (length <= 0)
                {
                    throw new InvalidDataException("Rope length must be positive.");
                }
                float x = Number(grab.Source, "target_x", optional: true), y = Number(grab.Source, "target_y", optional: true);
                Vector2? end = target.Position;
                if (x != 0 || y != 0 || target.Source.Kind != "candy")
                {
                    diagnostics.Add("Unverified rope attachment: " + targetId);
                    end = null;
                }
                ropes.Add(new(grab.Position!.Value, end, length));
            }
            diagnostics.Add("Static layout only: physics, rope sag/cutting, star collection, tutorials, intro, polygon smoothing, backgrounds and gameplay camera are pending.");
            return new(size, objects.AsReadOnly(), ropes.AsReadOnly(), polygons.AsReadOnly(), diagnostics.AsReadOnly());
        }

        /// <summary>Reads a required nonempty authored attribute.</summary>
        /// <param name="element">Object record.</param>
        /// <param name="name">Required attribute name.</param>
        /// <returns>The authored text.</returns>
        private static string Required(LevelElement element, string name)
        {
            return !element.Attributes.TryGetValue(name, out string? value) || value.Length == 0
                ? throw new InvalidDataException($"Missing {element.Kind} attribute: {name}") : value;
        }

        /// <summary>Parses a bounded finite editor number with optional integer enforcement.</summary>
        /// <param name="element">Object record.</param>
        /// <param name="name">Attribute name.</param>
        /// <param name="integer">Whether fractional coordinates are invalid.</param>
        /// <param name="optional">Whether an absent attribute defaults to zero.</param>
        /// <returns>The numeric editor value.</returns>
        private static float Number(LevelElement element, string name, bool integer = false, bool optional = false)
        {
            if (optional && !element.Attributes.ContainsKey(name))
            {
                return 0;
            }
            string text = Required(element, name);
            return integer && !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                ? throw new InvalidDataException("Expected integer coordinate: " + name)
                : Parse(text);
        }

        /// <summary>Converts an invariant number, rejecting nonfinite or excessive magnitudes.</summary>
        /// <param name="text">Authored number.</param>
        /// <returns>A finite value within one million editor units.</returns>
        private static float Parse(string text)
        {
            return !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value) || MathF.Abs(value) > 1000000
                ? throw new InvalidDataException("Invalid level number: " + text) : value;
        }
    }
}
