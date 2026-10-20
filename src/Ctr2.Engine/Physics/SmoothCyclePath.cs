using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>The scene's smoothed cyclic polygon path: a periodic chord-parameter Akima spline sampled per authored edge,
    /// then ear-clipped into triangles, with the original float and double arithmetic.</summary>
    public sealed class SmoothCyclePath
    {
        private readonly List<Tuple> splineX = [], splineY = [];
        private readonly int factor;

        /// <summary>Builds the path like the original constructor.</summary>
        /// <param name="position">Editor position in logical units; the body origin derives from it.</param>
        /// <param name="path">Comma-separated editor coordinates relative to the implicit origin.</param>
        /// <param name="normalize">Whether to subtract the mean of every vertex, including the implicit origin.</param>
        /// <param name="smoothFactor">Samples per authored edge.</param>
        /// <param name="angle">Degrees to rotate the vertices with the framework's table rotation; body polygons pass zero.</param>
        /// <exception cref="InvalidDataException">The path is malformed or too large.</exception>
        public SmoothCyclePath(Vector2 position, string path, bool normalize, int smoothFactor, float angle = 0)
        {
            ArgumentNullException.ThrowIfNull(path);
            Position = position;
            factor = smoothFactor is >= 1 and <= 16 ? smoothFactor : throw new InvalidDataException("Smooth factor must be 1 to 16.");
            List<string> parts = [];
            int index = 0;
            // The original splitter drops a trailing empty component.
            while (index < path.Length)
            {
                int end = path.IndexOf(',', index);
                end = end < 0 ? path.Length : end;
                parts.Add(path[index..end]);
                index = end + 1;
            }
            if (parts.Count is < 4 or > 1024)
            {
                throw new InvalidDataException("Smooth path vertex count is out of range.");
            }
            List<Vector2> vertices = [Vector2.Zero];
            for (int v = 0; v < parts.Count; v += 2)
            {
                float x = Number(parts[v]), y = v + 1 < parts.Count ? Number(parts[v + 1]) : 0;
                vertices.Add(new(x + x, y + y));
            }
            if (normalize)
            {
                Vector2 center = Vector2.Zero;
                foreach (Vector2 vertex in vertices)
                {
                    center = new(center.X + vertex.X, center.Y + vertex.Y);
                }
                float scale = (float)(1.0 / vertices.Count);
                center = new(center.X * scale, center.Y * scale);
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = new(vertices[i].X - center.X, vertices[i].Y - center.Y);
                }
            }
            if (angle != 0)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    vertices[i] = CandySimulation.TableRotate(vertices[i], (float)(angle * 3.14159265 / 180.0));
                }
            }
            MaxRealT = Build(vertices);
        }

        /// <summary>Gets the editor position in logical units.</summary>
        public Vector2 Position
        {
            get;
        }

        /// <summary>Gets the body origin in meters, as the original derives it from the editor position.</summary>
        public Vector2 BodyOrigin => new((float)(Position.X / 200.0), (float)(Position.Y / 200.0));

        /// <summary>Samples the spline: <c>smoothFactor</c> points per authored edge, in local logical units.</summary>
        /// <returns>The closed path without a repeated final point.</returns>
        public IReadOnlyList<Vector2> SmoothPoints()
        {
            List<float> ts = [.. splineX.Select(t => (float)(t.T / MaxRealT)), 1f];
            List<Vector2> result = [];
            for (int k = 0; k < splineX.Count; k++)
            {
                float current = ts[k], next = ts[k + 1], t = current;
                for (int j = 0; j < factor; j++)
                {
                    result.Add(new((float)Value(splineX, t), (float)Value(splineY, t)));
                    t += (next - current) / factor;
                }
            }
            return result.AsReadOnly();
        }

        /// <summary>Ear-clips the smooth points like the original triangulator, keeping triangles clipped before any failure.</summary>
        /// <returns>Triangles as consecutive point triples, in local logical units.</returns>
        public IReadOnlyList<Vector2> TrianglePoints()
        {
            IReadOnlyList<Vector2> contour = SmoothPoints();
            List<Vector2> result = [];
            int n = contour.Count;
            if (n < 3)
            {
                return result.AsReadOnly();
            }
            int[] order = new int[n];
            bool positive = Area(contour) > 0;
            for (int i = 0; i < n; i++)
            {
                order[i] = positive ? i : n - 1 - i;
            }
            int remaining = n, count = 2 * n, v = n - 1;
            while (remaining > 2)
            {
                if (count-- <= 0)
                {
                    break;
                }
                int u = remaining <= v ? 0 : v;
                v = remaining <= u + 1 ? 0 : u + 1;
                int w = remaining <= v + 1 ? 0 : v + 1;
                if (Snip(contour, u, v, w, remaining, order))
                {
                    result.Add(contour[order[u]]);
                    result.Add(contour[order[v]]);
                    result.Add(contour[order[w]]);
                    Array.Copy(order, v + 1, order, v, remaining - v - 1);
                    remaining--;
                    count = 2 * remaining;
                }
            }
            return result.AsReadOnly();
        }

        /// <summary>Gets the spline's arc length in logical units.</summary>
        public float MaxRealT
        {
            get;
        }

        /// <summary>Native <c>getVectorForT</c>: the spline point at a cyclic parameter.</summary>
        /// <param name="t">Parameter; values outside [0, 1] wrap.</param>
        /// <returns>The point relative to <see cref="Position"/>, in logical units.</returns>
        public Vector2 VectorAt(float t)
        {
            return new((float)Value(splineX, t), (float)Value(splineY, t));
        }

        /// <summary>Native <c>getDVectorForT</c>: the spline's derivative with respect to arc length at a cyclic parameter.</summary>
        /// <param name="t">Parameter; values outside [0, 1] wrap.</param>
        /// <returns>The tangent.</returns>
        public Vector2 DerivativeAt(float t)
        {
            return new((float)Derivative(splineX, t), (float)Derivative(splineY, t));
        }

        private double Derivative(List<Tuple> spline, float t)
        {
            while (t < 0)
            {
                t += 1f;
            }
            while (t > 1)
            {
                t -= 1f;
            }
            float ta = t * MaxRealT;
            int i = 0;
            while (i + 1 < spline.Count && spline[i + 1].T < ta)
            {
                i++;
            }
            float h = (float)(ta - spline[i].T);
            double c = spline[i].C;
            return spline[i].B + ((c + c) * h) + (spline[i].D * 3.0 * h * h);
        }

        private float Build(List<Vector2> points)
        {
            int n = points.Count;
            float miny = 3.4028e38f;
            int highest = -1;
            for (int i = 0; i < n; i++)
            {
                if (points[i].Y < miny)
                {
                    miny = points[i].Y;
                    highest = i;
                }
            }
            int previous = (highest + n - 1) % n, next = (highest + 1) % n;
            Vector2 previousDirection = points[highest] - points[previous], nextDirection = points[next] - points[highest];
            float cross = (previousDirection.X * nextDirection.Y) - (previousDirection.Y * nextDirection.X);
            bool clockwise = Math.Abs((double)cross) >= 0.001 ? cross > 0 : nextDirection.X > 0;
            if (!clockwise)
            {
                points.Reverse(1, n - 1);
            }
            List<Vector2> padded = [points[n - 3], points[n - 2], points[n - 1], .. points, points[0], points[1], points[2]];
            float[] xs = [.. padded.Select(p => p.X)], ys = [.. padded.Select(p => p.Y)];
            List<float> ts = [0f];
            for (int i = 1; i < padded.Count; i++)
            {
                Vector2 d = padded[i - 1] - padded[i];
                ts.Add(MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) + ts[i - 1]);
            }
            float start = ts[3], end = ts[n + 3];
            EuclidSpline(xs, ts, splineX);
            EuclidSpline(ys, ts, splineY);
            foreach (List<Tuple> spline in new[] { splineX, splineY })
            {
                spline.RemoveRange(0, 3);
                spline.RemoveRange(spline.Count - 3, 3);
                for (int j = 0; j < spline.Count; j++)
                {
                    spline[j] = spline[j] with
                    {
                        T = spline[j].T - start
                    };
                }
            }
            return end - start;
        }

        internal static void EuclidSpline(float[] points, List<float> knots, List<Tuple> spline)
        {
            int n = points.Length;
            List<float> ts = [.. knots];
            if (ts.Count == n)
            {
                ts.Add((float)(ts[^1] + 1.0));
            }
            double[] d = new double[n + 1], w = new double[n - 1], diff = new double[n];
            for (int i = 0; i < n - 1; i++)
            {
                diff[i] = (points[i + 1] - points[i]) / (ts[i + 1] - ts[i]);
            }
            for (int i = 1; i < n - 1; i++)
            {
                w[i] = Math.Abs(diff[i] - diff[i - 1]);
            }
            for (int i = 2; i < n - 2; i++)
            {
                d[i] = Math.Abs(w[i - 1]) + Math.Abs(w[i + 1]) == 0
                    ? (((ts[i + 1] - ts[i]) * diff[i - 1]) + ((ts[i] - ts[i - 1]) * diff[i])) / (ts[i + 1] - ts[i - 1])
                    : ((w[i + 1] * diff[i - 1]) + (w[i - 1] * diff[i])) / (w[i + 1] + w[i - 1]);
            }
            d[0] = DiffThreePoint(ts[0], ts[0], points[0], ts[1], points[1], ts[2], points[2]);
            d[1] = DiffThreePoint(ts[1], ts[0], points[0], ts[1], points[1], ts[2], points[2]);
            d[n - 2] = DiffThreePoint(ts[n - 2], ts[n - 3], points[n - 3], ts[n - 2], points[n - 2], ts[n - 1], points[n - 1]);
            d[n - 1] = DiffThreePoint(ts[n - 1], ts[n - 3], points[n - 3], ts[n - 2], points[n - 2], ts[n - 1], points[n - 1]);
            for (int i = 0; i < n; i++)
            {
                float h = ts[i + 1] - ts[i];
                double dd = (d[i] + d[i + 1] + (diff[i] * -2.0)) / (h * h);
                double c = ((diff[i] * 3.0) - (d[i] + d[i]) - d[i + 1]) / h;
                spline.Add(new(points[i], d[i], c, dd, ts[i]));
            }
        }

        private static double DiffThreePoint(double t, double x0, double f0, double x1, double f1, double x2, double f2)
        {
            double numerator = f2 - f0 - ((x2 - x0) / (x1 - x0) * (f1 - f0));
            double denominator = ((x2 - x0) * (x2 - x0)) - ((x1 - x0) * (x2 - x0));
            double a = numerator / denominator;
            return ((a + a) * (t - x0)) + ((f1 - f0 - (numerator / denominator * (x1 - x0) * (x1 - x0))) / (x1 - x0));
        }

        private double Value(List<Tuple> spline, float t)
        {
            while (t < 0)
            {
                t += 1f;
            }
            while (t > 1)
            {
                t -= 1f;
            }
            float ta = t * MaxRealT;
            int i = 0;
            // On the closing edge the original reads one tuple past the end: memory still holding the first erased tail tuple,
            // whose unshifted knot exceeds every query, so the search stops at the last real tuple.
            while (i + 1 < spline.Count && spline[i + 1].T < ta)
            {
                i++;
            }
            float h = (float)(ta - spline[i].T);
            double value = spline[i].A + (spline[i].B * h);
            value += spline[i].C * h * h;
            return value + (spline[i].D * h * h * h);
        }

        private static float Area(IReadOnlyList<Vector2> contour)
        {
            float area = 0;
            for (int p = contour.Count - 1, q = 0; q < contour.Count; p = q++)
            {
                area += (contour[p].X * contour[q].Y) - (contour[q].X * contour[p].Y);
            }
            return area * .5f;
        }

        private static bool Snip(IReadOnlyList<Vector2> contour, int u, int v, int w, int count, int[] order)
        {
            Vector2 a = contour[order[u]], b = contour[order[v]], c = contour[order[w]];
            if (!((((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X))) >= 1e-10))
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

        internal static float Number(string text)
        {
            return NativeText.FloatValue(text);
        }

        /// <summary>One Akima segment: value, first, second and third coefficients at knot <see cref="T"/>.</summary>
        internal readonly record struct Tuple(double A, double B, double C, double D, double T);
    }
}
