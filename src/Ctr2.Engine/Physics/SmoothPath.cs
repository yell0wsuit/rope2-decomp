using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>The scene's open smoothed path: a chord-parameter Akima spline through the implicit origin and the authored
    /// points, with the original float and double arithmetic. Movable objects follow it back and forth.</summary>
    public sealed class SmoothPath
    {
        private readonly List<SmoothCyclePath.Tuple> splineX = [], splineY = [];

        /// <summary>Builds the path like the original constructor; movable objects never normalize it.</summary>
        /// <param name="position">Editor position in logical units.</param>
        /// <param name="path">Comma-separated editor coordinates relative to the implicit origin.</param>
        /// <exception cref="InvalidDataException">The path is malformed or has too few or too many points.</exception>
        public SmoothPath(Vector2 position, string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            Position = position;
            string[] parts = [.. path.Split(',')];
            if (parts.Length > 0 && parts[^1].Length == 0)
            {
                parts = parts[..^1];
            }
            if (parts.Length is < 2 or > 1024)
            {
                throw new InvalidDataException("Smooth path vertex count is out of range.");
            }
            List<Vector2> points = [Vector2.Zero];
            for (int v = 0; v + 1 < parts.Length; v += 2)
            {
                float x = SmoothCyclePath.Number(parts[v]), y = SmoothCyclePath.Number(parts[v + 1]);
                points.Add(new(x + x, y + y));
            }
            MaxRealT = Build(points);
        }

        /// <summary>Gets the editor position in logical units.</summary>
        public Vector2 Position
        {
            get;
        }

        /// <summary>Gets the spline's arc length in logical units.</summary>
        public float MaxRealT
        {
            get;
        }

        /// <summary>Native <c>getVectorForT</c>.</summary>
        /// <param name="t">Parameter from 0 to 1.</param>
        /// <returns>The point relative to <see cref="Position"/>.</returns>
        public Vector2 VectorAt(float t)
        {
            return new((float)Evaluate(splineX, t, false), (float)Evaluate(splineY, t, false));
        }

        /// <summary>Native <c>getDVectorForT</c>: the derivative with respect to arc length.</summary>
        /// <param name="t">Parameter from 0 to 1.</param>
        /// <returns>The tangent.</returns>
        public Vector2 DerivativeAt(float t)
        {
            return new((float)Evaluate(splineX, t, true), (float)Evaluate(splineY, t, true));
        }

        /// <summary>Native <c>AkimaSpline::buildSpline</c>: short paths gain mirrored end points that are dropped afterward.</summary>
        private float Build(List<Vector2> points)
        {
            int n = points.Count, startIndex = 0;
            Vector2 before = points[0] + (points[0] - points[1]), step = points[n - 1] - points[n - 2];
            Vector2 after = points[n - 1] + step, afterNext = after + step;
            if (n is 2 or 3)
            {
                points.Insert(0, before);
                startIndex = 1;
            }
            if (n is >= 2 and <= 4)
            {
                points.Add(after);
                if (n == 2)
                {
                    points.Add(afterNext);
                }
            }
            List<float> ts = [0f];
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 d = points[i - 1] - points[i];
                ts.Add(MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) + ts[i - 1]);
            }
            float start = ts[startIndex], end = ts[startIndex + n - 1];
            SmoothCyclePath.EuclidSpline([.. points.Select(p => p.X)], ts, splineX);
            SmoothCyclePath.EuclidSpline([.. points.Select(p => p.Y)], ts, splineY);
            foreach (List<SmoothCyclePath.Tuple> spline in new[] { splineX, splineY })
            {
                if (n is 2 or 3)
                {
                    spline.RemoveAt(0);
                }
                if (n is >= 2 and <= 4)
                {
                    spline.RemoveAt(spline.Count - 1);
                    if (n == 2)
                    {
                        spline.RemoveAt(spline.Count - 1);
                    }
                }
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

        private double Evaluate(List<SmoothCyclePath.Tuple> spline, float t, bool derivative)
        {
            float ta = t * MaxRealT;
            int i = 0;
            while (i + 1 < spline.Count && spline[i + 1].T < ta)
            {
                i++;
            }
            float h = (float)(ta - spline[i].T);
            SmoothCyclePath.Tuple segment = spline[i];
            if (derivative)
            {
                return segment.B + ((segment.C + segment.C) * h) + (segment.D * 3.0 * h * h);
            }
            double value = segment.A + (segment.B * h);
            value += segment.C * h * h;
            return value + (segment.D * h * h * h);
        }
    }
}
