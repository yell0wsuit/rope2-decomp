using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Validated authored character body data, separate from a simulated body.</summary>
    /// <param name="Name">Authored object ID.</param>
    /// <param name="Shape">Immutable local meter-space convex outline; not the original compound fixture topology.</param>
    /// <param name="Origin">Initial body origin in world meters.</param>
    /// <param name="Angle">Initial clockwise radians.</param>
    /// <param name="Mass">Fixture mass data relative to the local origin.</param>
    /// <param name="Density">Positive authored fixture density.</param>
    /// <param name="GravityScale">World gravity multiplier.</param>
    /// <param name="LinearDamping">Nonnegative linear damping coefficient.</param>
    /// <param name="AngularDamping">Nonnegative angular damping coefficient.</param>
    /// <param name="Friction">Nonnegative fixture friction.</param>
    /// <param name="Restitution">Nonnegative fixture restitution.</param>
    /// <param name="FixedRotation">Whether angular motion should be disabled when a body is instantiated.</param>
    public sealed record PolygonBodyDefinition(string Name, ConvexPolygon Shape, Vector2 Origin, float Angle,
        PolygonMass Mass, float Density, float GravityScale, float LinearDamping, float AngularDamping, float Friction, float Restitution, bool FixedRotation)
    {
        /// <summary>Gets the initial world-space center of mass, distinct from the authored origin.</summary>
        /// <value>The rotated local centroid translated by the body origin.</value>
        public Vector2 WorldCenter => Origin + Vector2.Transform(Mass.Center, Matrix3x2.CreateRotation(Angle));

        /// <summary>Loads supported authored Om Nom polygon body definitions.</summary>
        /// <param name="level">Editor-space level definition.</param>
        /// <param name="design">Matching master design.</param>
        /// <returns>Immutable authored definitions in character order; these are not integrated bodies.</returns>
        /// <exception cref="InvalidDataException">Master geometry or a supported body field is invalid.</exception>
        public static IReadOnlyList<PolygonBodyDefinition> LoadCharacters(LevelDefinition level, LevelDefinition design)
        {
            List<PolygonBodyDefinition> result = [];
            foreach (PlacedLevelObject placed in LevelLayout.Create(level).Objects.Where(o => o.Source.Kind == "omnom"))
            {
                LevelElement[] matches = [.. design.Objects.Where(o => o.Attributes.GetValueOrDefault("id") == "omnom")];
                if (matches.Length != 1 || matches[0].Kind != "polygon")
                {
                    throw new InvalidDataException("Expected one polygon master design for Om Nom.");
                }
                Dictionary<string, string> fields = new(matches[0].Attributes, StringComparer.Ordinal);
                foreach ((string key, string value) in placed.Source.Attributes)
                {
                    fields[key] = value;
                }
                if (fields.GetValueOrDefault("body_type") != "dynamic" || Flag(fields, "isSensor") || Flag(fields, "bullet") ||
                    Number(fields, "linearVelocityX") != 0 || Number(fields, "linearVelocityY") != 0 || Number(fields, "angularVelocity") != 0 ||
                    (fields.TryGetValue("smoothFactor", out string? smooth) ? NativeText.IntValue(smooth) : 1) != 1 || fields.GetValueOrDefault("filter.collideWith", "all") != "all" ||
                    Number(fields, "filter.category") != 0 || !string.IsNullOrEmpty(fields.GetValueOrDefault("path")) ||
                    !string.IsNullOrEmpty(fields.GetValueOrDefault("normalizedPath")))
                {
                    throw new InvalidDataException("Unsupported character body type, initial motion, smoothing or collision filter.");
                }
                float density = Number(fields, "density");
                float linear = Number(fields, "linearDamping"), angular = Number(fields, "angularDamping");
                float friction = Number(fields, "friction"), restitution = Number(fields, "restitution");
                if (density <= 0 || linear < 0 || angular < 0 || friction < 0 || friction > 100 || restitution < 0 || restitution > 10)
                {
                    throw new InvalidDataException("Invalid character body density, damping or material.");
                }
                ConvexPolygon shape = ParseShape(fields.GetValueOrDefault("polygon", ""));
                PolygonMass mass = shape.ComputeMass(density);
                if (mass.Mass < 1e-12f || mass.CenterInertia <= 0)
                {
                    throw new InvalidDataException("Character body mass or inertia is degenerate.");
                }
                result.Add(new(fields.GetValueOrDefault("id", "omnom"), shape, placed.Position!.Value / 200,
                    placed.Angle * MathF.PI / 180, mass, density, Number(fields, "gravityScale"), linear, angular, friction, restitution, Flag(fields, "fixedRotation")));
            }
            return result.AsReadOnly();
        }

        /// <summary>Parses editor vertices with the implicit local zero, centers them and normalizes winding.</summary>
        /// <param name="text">Comma-separated editor coordinates with an optional trailing comma.</param>
        /// <returns>The bounded local convex polygon in meters.</returns>
        /// <remarks>The original Om Nom initializer always sets <c>normalizeShape</c>, overriding authored values. Its path
        /// subtracts the mean of every logical-unit point, including the implicit and any closing zero, without moving the body origin.</remarks>
        /// <exception cref="InvalidDataException">The path cannot form a supported convex polygon.</exception>
        private static ConvexPolygon ParseShape(string text)
        {
            if (text.Length > 1024)
            {
                throw new InvalidDataException("Character polygon text exceeds geometry bounds.");
            }
            // As the smooth path reader: one trailing empty component is dropped and a missing final y reads zero.
            string[] values = (text.EndsWith(',') ? text[..^1] : text).Split(',');
            if (values.Length is < 4 or > 16)
            {
                throw new InvalidDataException("Invalid character polygon coordinate count.");
            }
            List<Vector2> points = [Vector2.Zero];
            Vector2 center = Vector2.Zero;
            for (int i = 0; i < values.Length; i += 2)
            {
                Vector2 point = new Vector2(NativeText.FloatValue(values[i]), i + 1 < values.Length ? NativeText.FloatValue(values[i + 1]) : 0) * 2;
                points.Add(point);
                center += point;
            }
            bool closed = points[^1] == Vector2.Zero;
            center *= (float)(1.0 / points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                points[i] = (points[i] - center) / 200;
            }
            if (closed)
            {
                points.RemoveAt(points.Count - 1);
            }
            float area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                area += (a.X * b.Y) - (a.Y * b.X);
            }
            if (area < 0)
            {
                points.Reverse(1, points.Count - 1);
            }
            try
            {
                return new(points);
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Invalid convex character polygon.", error);
            }
        }

        /// <summary>Reads a body number like the native body factory's <c>floatValue</c>: absent reads zero.</summary>
        /// <param name="fields">Merged master and placement attributes.</param>
        /// <param name="name">Field name.</param>
        /// <returns>The parsed value.</returns>
        private static float Number(IReadOnlyDictionary<string, string> fields, string name)
        {
            return NativeText.FloatValue(fields.GetValueOrDefault(name));
        }

        /// <summary>Reads an authored flag as the native body factory does: only the exact text <c>true</c> is true.</summary>
        /// <param name="fields">Merged master and placement attributes.</param>
        /// <param name="name">Flag name.</param>
        /// <returns>The requested flag, false when absent.</returns>
        private static bool Flag(IReadOnlyDictionary<string, string> fields, string name)
        {
            return fields.GetValueOrDefault(name) == "true";
        }
    }
}
