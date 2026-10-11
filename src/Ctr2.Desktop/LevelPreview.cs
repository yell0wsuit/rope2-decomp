using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Draws authored level geometry, an optional assembled Om Nom pose, and supported candy physics.</summary>
    /// <remarks>Creates a geometry-only diagnostic preview, without loading original assets.</remarks>
    /// <param name="layout">Initial logical scene geometry.</param>
    public sealed class LevelPreview(LevelLayout layout) : IDisposable
    {
        /// <summary>Optional cached character animation owned by this preview.</summary>
        private AnimationPreview? omnom;
        /// <summary>Optional classic candy atlas for the simulation preview.</summary>
        private CandySprite? candy;
        /// <summary>Gets the optional candy, rope and static terrain simulation attached to this scene.</summary>
        /// <value>Null for static previews; otherwise mutable fixed-tick diagnostic state.</value>
        public CandySimulation? Simulation
        {
            get; private set;
        }

        /// <summary>Whether native resources have been released.</summary>
        private bool disposed;

        /// <summary>Gets the initial layout and explicit pending-feature diagnostics.</summary>
        /// <value>The immutable logical geometry used by this preview.</value>
        public LevelLayout Layout
        {
            get;
        } = layout;

        /// <summary>Loads a Windows level and its default Om Nom IDLE animation when present.</summary>
        /// <param name="cache">Imported Windows cache root.</param>
        /// <param name="pack">One-based desktop pack ID.</param>
        /// <param name="level">One-based desktop level entry.</param>
        /// <param name="resolution">Known image density enum, independent of map coordinates.</param>
        /// <param name="simulate">Whether to load master-design candy/rope physics and classic candy artwork.</param>
        /// <returns>The parsed level and a preview owning its decoded character assets.</returns>
        /// <exception cref="System.IO.InvalidDataException">The level, resource or animation is invalid.</exception>
        /// <exception cref="System.IO.IOException">A cache file cannot be read.</exception>
        public static (LevelDefinition Definition, LevelPreview Preview) Load(string cache, int pack, int level, int resolution, bool simulate = false)
        {
            LevelDefinition definition = LevelReader.LoadWindows(cache, pack, level);
            LevelPreview preview = new(LevelLayout.Create(definition));
            try
            {
                if (definition.Objects.Any(o => o.Kind == "omnom"))
                {
                    preview.omnom = AnimationPreview.Load(cache, 85, "IDLE", resolution);
                }
                if (simulate)
                {
                    preview.Simulation = new CandySimulation(definition, LevelReader.LoadWindowsDesign(cache));
                    preview.candy = new CandySprite(cache, resolution);
                }
                return (definition, preview);
            }
            catch
            {
                preview.Dispose();
                throw;
            }
        }

        /// <summary>Draws a uniformly fitted scene, clipped to its authored logical canvas.</summary>
        /// <param name="canvas">Caller-owned destination canvas.</param>
        /// <param name="width">Positive output width in pixels.</param>
        /// <param name="height">Positive output height in pixels.</param>
        /// <param name="seconds">Nonnegative character pose time; physics uses its separate fixed clock.</param>
        /// <exception cref="ObjectDisposedException">The preview has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Dimensions or time are invalid.</exception>
        public void Draw(SKCanvas canvas, int width, int height, double seconds)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!double.IsFinite(seconds) || seconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            Viewport viewport = Viewport.Fit(width, height, Layout.Size.X, Layout.Size.Y);
            canvas.Clear(new SKColor(24, 27, 32));
            Matrix4x4 transform = viewport.Transform;
            int saved = canvas.Save();
            try
            {
                canvas.Concat(new SKMatrix(transform.M11, transform.M21, transform.M41, transform.M12, transform.M22, transform.M42, 0, 0, 1));
                canvas.ClipRect(new SKRect(0, 0, Layout.Size.X, Layout.Size.Y));
                using SKPaint paint = new()
                {
                    Color = new SKColor(42, 55, 48),
                    IsAntialias = true
                };
                canvas.DrawRect(0, 0, Layout.Size.X, Layout.Size.Y, paint);
                paint.Color = new SKColor(132, 96, 64);
                foreach (IReadOnlyList<Vector2> vertices in Simulation == null ? Layout.Polygons.Select(p => p.Vertices) : Simulation.Terrain.Select(s => s.Outline))
                {
                    using SKPathBuilder path = new();
                    path.MoveTo(vertices[0].X, vertices[0].Y);
                    foreach (Vector2 vertex in vertices.Skip(1))
                    {
                        path.LineTo(vertex.X, vertex.Y);
                    }
                    path.Close();
                    using SKPath outline = path.Detach();
                    canvas.DrawPath(outline, paint);
                }
                paint.Color = SKColors.Wheat;
                paint.StrokeWidth = 3;
                foreach (LevelRope rope in Simulation == null ? Layout.Ropes : [])
                {
                    if (rope.End is Vector2 end)
                    {
                        canvas.DrawLine(rope.Start.X, rope.Start.Y, end.X, end.Y, paint);
                    }
                }
                if (Simulation != null)
                {
                    for (int i = 0; i < Simulation.RopeCount; i++)
                    {
                        foreach (IReadOnlyList<Vector2> points in Simulation.RopeSegments(i))
                        {
                            for (int j = 1; j < points.Count; j++)
                            {
                                canvas.DrawLine(points[j - 1].X, points[j - 1].Y, points[j].X, points[j].Y, paint);
                            }
                        }
                    }
                }
                int candyIndex = 0;
                foreach (PlacedLevelObject placed in Layout.Objects)
                {
                    if (placed.Position is not Vector2 point || placed.Source.Kind == "polygon_materialized")
                    {
                        continue;
                    }
                    if (placed.Source.Kind == "candy" && Simulation != null)
                    {
                        PhysicsBody body = Simulation.Candies[candyIndex++];
                        candy!.Draw(canvas, body.Position * 200, body.Angle);
                    }
                    else if (placed.Source.Kind == "omnom" && omnom != null)
                    {
                        Vector2 origin = (-omnom.CanvasSize / 2) + new Vector2(-8, -11);
                        Matrix4x4 character = Matrix4x4.CreateTranslation(origin.X, origin.Y, 0) *
                            Matrix4x4.CreateRotationZ(placed.Angle * MathF.PI / 180) * Matrix4x4.CreateTranslation(point.X, point.Y, 0);
                        omnom.DrawTransformed(canvas, character, seconds);
                    }
                    else
                    {
                        paint.Color = placed.Source.Kind switch
                        {
                            "candy" => SKColors.Coral,
                            "star" => SKColors.Gold,
                            "grab" => SKColors.Wheat,
                            "omnom" => SKColors.LawnGreen,
                            _ => SKColors.Magenta
                        };
                        canvas.DrawCircle(point.X, point.Y, placed.Source.Kind == "grab" ? 6 : 12, paint);
                    }
                }
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        /// <summary>Releases owned character textures; repeated calls are safe.</summary>
        public void Dispose()
        {
            if (!disposed)
            {
                omnom?.Dispose();
                candy?.Dispose();
                disposed = true;
            }
        }
    }
}
