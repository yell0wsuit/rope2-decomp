using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>A smoothed outline and static triangle fixtures derived from one authored polygon.</summary>
    /// <param name="Outline">Translated and rotated boundary in logical units.</param>
    /// <param name="Triangles">Counterclockwise triangle vertices in world meters.</param>
    /// <param name="Friction">Authored material friction.</param>
    /// <param name="Restitution">Authored material restitution.</param>
    /// <param name="IsSensor">Whether geometry is visual-only for this solid-contact subset.</param>
    public sealed record TerrainShape(IReadOnlyList<Vector2> Outline, IReadOnlyList<IReadOnlyList<Vector2>> Triangles,
        float Friction, float Restitution, bool IsSensor);

    /// <summary>Builds bounded static material geometry using periodic Akima sampling and ear clipping.</summary>
    public static class TerrainGeometry
    {
        /// <summary>Builds supported static polygon geometry from the level and its matching master materials.</summary>
        /// <param name="level">Authored editor-space level.</param>
        /// <param name="design">Master material definitions.</param>
        /// <returns>Immutable geometry shared by drawing and physics.</returns>
        /// <exception cref="InvalidDataException">Geometry, material, smoothing or supported body fields are invalid.</exception>
        public static IReadOnlyList<TerrainShape> Build(LevelDefinition level, LevelDefinition design)
        {
            List<TerrainShape> result = [];
            int triangleCount = 0;
            foreach (PlacedLevelObject placed in LevelLayout.Create(level).Objects.Where(o => o.Source.Kind == "polygon_materialized"))
            {
                LevelElement element = placed.Source;
                string material = Required(element, "material");
                LevelElement[] definitions = [.. design.Objects.Where(o => o.Attributes.TryGetValue("id", out string? id) && id == material)];
                if (definitions.Length != 1 || definitions[0].Kind != "polygon")
                {
                    throw new InvalidDataException("Missing unique polygon material: " + material);
                }

                LevelElement definition = definitions[0];
                string bodyType = element.Attributes.GetValueOrDefault("body_type", definition.Attributes.GetValueOrDefault("body_type", ""));
                if (bodyType != "static" || element.Attributes.GetValueOrDefault("normalizeShape", "false") != "false")
                {
                    throw new InvalidDataException("Only unnormalized static terrain is supported.");
                }

                float friction = Number(definition, "friction"), restitution = Number(definition, "restitution");
                if (friction < 0 || friction > 100 || restitution < 0 || restitution > 10)
                {
                    throw new InvalidDataException("Invalid terrain material coefficients.");
                }

                string sensor = definition.Attributes.GetValueOrDefault("isSensor", "false");
                if (sensor is not ("true" or "false"))
                {
                    throw new InvalidDataException("Invalid terrain sensor flag.");
                }

                string[] parts = Required(element, "polygon").TrimEnd(',').Split(',');
                if (parts.Length is < 4 or > 512 || parts.Length % 2 != 0)
                {
                    throw new InvalidDataException("Invalid bounded terrain path.");
                }

                List<Vector2> points = [Vector2.Zero];
                for (int i = 0; i < parts.Length; i += 2)
                {
                    points.Add(new(Parse(parts[i]) * 2, Parse(parts[i + 1]) * 2));
                }

                if (points[^1] == points[0])
                {
                    points.RemoveAt(points.Count - 1);
                }

                int factor = element.Attributes.TryGetValue("smoothFactor", out string? text) ?
                    int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0 : 1;
                if (factor is < 1 or > 16 || points.Count * factor > 512)
                {
                    throw new InvalidDataException("Terrain smoothing exceeds geometry bounds.");
                }

                NormalizeWinding(points);
                ValidateSimple(points);
                List<Vector2> smooth = Sample(points, factor);
                Matrix3x2 transform = Matrix3x2.CreateRotation(placed.Angle * MathF.PI / 180) * Matrix3x2.CreateTranslation(placed.Position!.Value);
                List<Vector2> outline = [.. smooth.Select(p => Vector2.Transform(p, transform))];
                ValidateSimple(outline);
                List<IReadOnlyList<Vector2>> triangles = Triangulate(smooth, placed.Position!.Value, placed.Angle);
                triangleCount += triangles.Count;
                if (triangleCount > 4096)
                {
                    throw new InvalidDataException("Terrain fixture budget exceeded.");
                }

                result.Add(new(outline.AsReadOnly(), triangles.AsReadOnly(), friction, restitution, sensor == "true"));
            }
            return result.AsReadOnly();
        }

        /// <summary>Samples every cyclic chord interval using component-wise Akima derivatives.</summary>
        /// <param name="points">Counterclockwise simple path including the implicit origin.</param>
        /// <param name="factor">Samples per authored interval.</param>
        /// <returns>A closed-path outline without a repeated final vertex.</returns>
        private static List<Vector2> Sample(List<Vector2> points, int factor)
        {
            int count = points.Count;
            float[] lengths = new float[count];
            Vector2[] slopes = new Vector2[count], derivatives = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 edge = points[(i + 1) % count] - points[i];
                lengths[i] = edge.Length();
                slopes[i] = edge / lengths[i];
            }
            for (int i = 0; i < count; i++)
            {
                int before = (i + count - 1) % count, earlier = (i + count - 2) % count, after = (i + 1) % count;
                derivatives[i] = new(
                    Derivative(slopes[earlier].X, slopes[before].X, slopes[i].X, slopes[after].X, lengths[before], lengths[i]),
                    Derivative(slopes[earlier].Y, slopes[before].Y, slopes[i].Y, slopes[after].Y, lengths[before], lengths[i]));
            }
            List<Vector2> result = [];
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                for (int j = 0; j < factor; j++)
                {
                    double u = (double)j / factor, square = u * u, cube = square * u;
                    result.Add(((float)((2 * cube) - (3 * square) + 1) * points[i]) +
                        ((float)(cube - (2 * square) + u) * lengths[i] * derivatives[i]) +
                        ((float)((-2 * cube) + (3 * square)) * points[next]) +
                        ((float)(cube - square) * lengths[i] * derivatives[next]));
                }
            }
            return result;
        }

        /// <summary>Computes one coordinate's periodic Akima tangent with a chord-weighted linear fallback.</summary>
        /// <param name="earlier">Slope two intervals before the knot.</param>
        /// <param name="before">Slope immediately before the knot.</param>
        /// <param name="current">Slope immediately after the knot.</param>
        /// <param name="after">Slope two intervals after the knot.</param>
        /// <param name="previousLength">Previous chord length.</param>
        /// <param name="length">Next chord length.</param>
        /// <returns>The coordinate derivative against chord distance.</returns>
        private static float Derivative(float earlier, float before, float current, float after, float previousLength, float length)
        {
            double left = Math.Abs((double)after - current), right = Math.Abs((double)before - earlier);
            return (float)(left + right == 0 ? ((length * (double)before) + (previousLength * (double)current)) / (length + previousLength) :
                ((left * before) + (right * current)) / (left + right));
        }

        /// <summary>Clips ears in the original order and places each triangle with its static body transform.</summary>
        /// <param name="contour">Smoothed local path in logical units, relative to the authored position.</param>
        /// <param name="position">Authored body position in logical units.</param>
        /// <param name="degrees">Authored clockwise body angle in degrees.</param>
        /// <returns>Counterclockwise world-space triangles in meters.</returns>
        /// <remarks>Like the original, a contour that stops yielding ears, such as a final collinear triple, keeps the triangles clipped so far.
        /// Ears that collapse to zero meter-space area are clipped without a fixture; the original keeps them as zero-area Box2D fixtures.</remarks>
        private static List<IReadOnlyList<Vector2>> Triangulate(List<Vector2> contour, Vector2 position, float degrees)
        {
            int n = contour.Count;
            int[] order = new int[n];
            bool positive = Area(contour) > 0;
            for (int i = 0; i < n; i++)
            {
                order[i] = positive ? i : n - 1 - i;
            }

            float angle = (float)(degrees * 3.14159265 / 180.0), cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            Vector2 origin = ToMeters(position);
            List<IReadOnlyList<Vector2>> result = [];
            int remaining = n, attempts = 2 * n, v = n - 1;
            while (remaining > 2 && attempts-- > 0)
            {
                int u = remaining <= v ? 0 : v;
                v = remaining <= u + 1 ? 0 : u + 1;
                int w = remaining <= v + 1 ? 0 : v + 1;
                if (!Snip(contour, u, v, w, remaining, order))
                {
                    continue;
                }

                Vector2 a = ToMeters(contour[order[u]]), b = ToMeters(contour[order[v]]), c = ToMeters(contour[order[w]]);
                if (Cross(b - a, c - b) < 0)
                {
                    (b, c) = (c, b);
                }

                Vector2 first = Place(a, origin, cos, sin), second = Place(b, origin, cos, sin), third = Place(c, origin, cos, sin);
                if (Cross(second - first, third - second) != 0)
                {
                    result.Add(Array.AsReadOnly([first, second, third]));
                }

                Array.Copy(order, v + 1, order, v, remaining - v - 1);
                remaining--;
                attempts = 2 * remaining;
            }
            return result;
        }

        /// <summary>Computes the original single-precision signed contour area.</summary>
        /// <param name="contour">Cyclic path.</param>
        /// <returns>Half the shoelace sum.</returns>
        private static float Area(List<Vector2> contour)
        {
            float area = 0;
            for (int p = contour.Count - 1, q = 0; q < contour.Count; p = q++)
            {
                area += (contour[p].X * contour[q].Y) - (contour[q].X * contour[p].Y);
            }
            return area * .5f;
        }

        /// <summary>Tests whether a positive candidate ear contains no other remaining vertex, including boundary points.</summary>
        /// <param name="contour">Cyclic path.</param>
        /// <param name="u">Previous remaining index.</param>
        /// <param name="v">Candidate ear index.</param>
        /// <param name="w">Next remaining index.</param>
        /// <param name="count">Remaining vertex count.</param>
        /// <param name="order">Remaining contour indices.</param>
        /// <returns>Whether the candidate may be clipped.</returns>
        private static bool Snip(List<Vector2> contour, int u, int v, int w, int count, int[] order)
        {
            Vector2 a = contour[order[u]], b = contour[order[v]], c = contour[order[w]];
            if (((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X)) < 1e-10)
            {
                return false;
            }

            for (int p = 0; p < count; p++)
            {
                if (p == u || p == v || p == w)
                {
                    continue;
                }

                Vector2 point = contour[order[p]];
                if (((c.X - b.X) * (point.Y - b.Y)) - ((c.Y - b.Y) * (point.X - b.X)) >= 0 &&
                    ((a.X - c.X) * (point.Y - c.Y)) - ((a.Y - c.Y) * (point.X - c.X)) >= 0 &&
                    ((b.X - a.X) * (point.Y - a.Y)) - ((b.Y - a.Y) * (point.X - a.X)) >= 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Converts logical units to meters with the original double-precision divisor.</summary>
        /// <param name="point">Logical point.</param>
        /// <returns>The meter-space point.</returns>
        private static Vector2 ToMeters(Vector2 point)
        {
            return new((float)(point.X / 200.0), (float)(point.Y / 200.0));
        }

        /// <summary>Applies a Box2D rotation and translation to one local fixture vertex.</summary>
        /// <param name="local">Local vertex in meters.</param>
        /// <param name="origin">Body position in meters.</param>
        /// <param name="cos">Body angle cosine.</param>
        /// <param name="sin">Body angle sine.</param>
        /// <returns>The world vertex.</returns>
        private static Vector2 Place(Vector2 local, Vector2 origin, float cos, float sin)
        {
            return new((cos * local.X) - (sin * local.Y) + origin.X, (sin * local.X) + (cos * local.Y) + origin.Y);
        }

        /// <summary>Orients the path counterclockwise while retaining its first authored vertex.</summary>
        /// <param name="points">Mutable path.</param>
        private static void NormalizeWinding(List<Vector2> points)
        {
            float area = 0;
            for (int i = 0; i < points.Count; i++)
            {
                area += Cross(points[i], points[(i + 1) % points.Count]);
            }

            if (MathF.Abs(area) < 1e-5f)
            {
                throw new InvalidDataException("Zero-area terrain path.");
            }

            if (area < 0)
            {
                points.Reverse(1, points.Count - 1);
            }
        }

        /// <summary>Rejects duplicate edges, invalid magnitudes and nonadjacent edge intersections.</summary>
        /// <param name="points">Bounded cyclic path.</param>
        private static void ValidateSimple(List<Vector2> points)
        {
            if (points.Count < 3)
            {
                throw new InvalidDataException("Terrain requires three distinct points.");
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i], b = points[(i + 1) % points.Count];
                if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || MathF.Abs(a.X) > 2000000 || MathF.Abs(a.Y) > 2000000 || Vector2.DistanceSquared(a, b) < 1e-6f)
                {
                    throw new InvalidDataException("Invalid or repeated terrain vertex.");
                }

                for (int j = i + 2; j < points.Count; j++)
                {
                    if ((j + 1) % points.Count == i)
                    {
                        continue;
                    }

                    Vector2 c = points[j], d = points[(j + 1) % points.Count];
                    if (MathF.Max(MathF.Min(a.X, b.X), MathF.Min(c.X, d.X)) <= MathF.Min(MathF.Max(a.X, b.X), MathF.Max(c.X, d.X)) &&
                        MathF.Max(MathF.Min(a.Y, b.Y), MathF.Min(c.Y, d.Y)) <= MathF.Min(MathF.Max(a.Y, b.Y), MathF.Max(c.Y, d.Y)) &&
                        Cross(b - a, c - a) * Cross(b - a, d - a) <= 0 && Cross(d - c, a - c) * Cross(d - c, b - c) <= 0)
                    {
                        throw new InvalidDataException("Self-intersecting terrain path.");
                    }
                }
            }
        }

        /// <summary>Reads a required terrain attribute.</summary>
        /// <param name="element">Authored record.</param>
        /// <param name="name">Required field.</param>
        /// <returns>Nonempty authored text.</returns>
        private static string Required(LevelElement element, string name)
        {
            return element.Attributes.TryGetValue(name, out string? value) && value.Length > 0 ? value : throw new InvalidDataException("Missing terrain field: " + name);
        }

        /// <summary>Reads a bounded invariant material number.</summary>
        /// <param name="element">Authored record.</param>
        /// <param name="name">Required field.</param>
        /// <returns>A finite numeric value.</returns>
        private static float Number(LevelElement element, string name)
        {
            return Parse(Required(element, name));
        }

        /// <summary>Parses a bounded invariant terrain coordinate or coefficient.</summary>
        /// <param name="text">Authored text.</param>
        /// <returns>A finite value with magnitude at most one million.</returns>
        private static float Parse(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && MathF.Abs(value) <= 1000000
                ? value : throw new InvalidDataException("Invalid terrain number.");
        }

        /// <summary>Computes a scalar signed cross product.</summary>
        /// <param name="a">First vector.</param>
        /// <param name="b">Second vector.</param>
        /// <returns>The determinant.</returns>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }
    }
}
