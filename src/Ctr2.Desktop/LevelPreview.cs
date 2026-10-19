using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Draws diagnostic geometry or supported gameplay with imported Windows artwork.</summary>
    /// <remarks>Static and simulation previews retain diagnostic scenery; playable sessions own native background and star resources.</remarks>
    /// <param name="layout">Initial logical scene geometry.</param>
    public sealed class LevelPreview(LevelLayout layout) : IDisposable
    {
        /// <summary>Optional cached character animation owned by this preview.</summary>
        private AnimationPreview? omnom;
        /// <summary>Optional classic candy atlas for the simulation preview.</summary>
        private CandySprite? candy;
        private ForestLeafSprites? forestLeaves;
        private AnimationPreview? swipeTutorial;
        private ForestBackground? background;
        private StarSprites? stars;
        private int sceneRevision = -1;
        private CandySimulation? animatedSimulation;
        /// <summary>Gets the optional playable session with pause and restart ownership.</summary>
        public GameSession? Session
        {
            get; private set;
        }
        /// <summary>Gets the optional candy, rope and static terrain simulation attached to this scene.</summary>
        /// <value>Null for static previews; otherwise mutable fixed-tick diagnostic state.</value>
        public CandySimulation? Simulation
        {
            get => Session?.Simulation ?? field;
            private set;
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
        /// <param name="play">Whether to enable supported gameplay and session controls.</param>
        /// <returns>The parsed level and a preview owning its decoded character assets.</returns>
        /// <exception cref="System.IO.InvalidDataException">The level, resource or animation is invalid.</exception>
        /// <exception cref="System.IO.IOException">A cache file cannot be read.</exception>
        public static (LevelDefinition Definition, LevelPreview Preview) Load(string cache, int pack, int level, int resolution, bool simulate = false, bool play = false)
        {
            LevelDefinition definition = LevelReader.LoadWindows(cache, pack, level);
            LevelPreview preview = new(LevelLayout.Create(definition));
            try
            {
                if (definition.Objects.Any(o => o.Kind == "omnom"))
                {
                    preview.omnom = AnimationPreview.Load(cache, 85, "IDLE", resolution);
                }
                if (play)
                {
                    preview.Session = new(definition, LevelReader.LoadWindowsDesign(cache), firstLevelArrival: pack == 1 && level == 1);
                    if (preview.Session.Simulation.Gameplay!.ArrivalLeaves != null)
                    {
                        preview.forestLeaves = new ForestLeafSprites(cache, resolution);
                    }
                    if (preview.Session.Simulation.Gameplay.SwipeTutorial != null)
                    {
                        preview.swipeTutorial = AnimationPreview.Load(cache, 104, "SWIPE_LOOP", resolution);
                    }
                    foreach (string scene in new[] { "IDLE", "MOUTH_OPEN", "MOUTH_CLOSE", "CHEWING", "PUZZLED", "FAIL", "FALLING", "FALLING_MOUTH_OPEN", "FALLING_MOUTH_CLOSE", "FALLING_CHEWING", "ROLLING", "STAND_UP" })
                    {
                        preview.omnom!.SelectScene(scene);
                    }
                    preview.omnom!.SelectScene("IDLE");
                    preview.Session.Stepped += preview.UpdateGameAnimation;
                }
                if (simulate || play)
                {
                    if (!play)
                    {
                        preview.Simulation = new CandySimulation(definition, LevelReader.LoadWindowsDesign(cache));
                    }
                    preview.candy = new CandySprite(cache, resolution);
                    if (play && definition.Objects.Any(o => o.Kind == "star"))
                    {
                        preview.stars = new StarSprites(cache, resolution);
                    }
                    if (play && definition.Settings.Any(s => s.Kind == "gameDesign" && s.Attributes.GetValueOrDefault("location") == "forest"))
                    {
                        preview.background = new ForestBackground(cache);
                    }
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
                background?.Draw(canvas, Layout.Size);
                if (Simulation?.Gameplay?.ArrivalLeaves is ForestLeafBurst leaves)
                {
                    forestLeaves!.Draw(canvas, leaves.Particles);
                }
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
                foreach (GeneralShape shape in Simulation?.Shapes ?? [])
                {
                    // Diagnostic fill until wood and land shape artwork is ported.
                    PhysicsBody body = shape.Body;
                    paint.Color = body.Type == PhysicsBodyType.Static ? new SKColor(132, 96, 64) : new SKColor(196, 150, 96);
                    Vector2 origin = body.Origin * 200;
                    if (body.Radius > 0)
                    {
                        canvas.DrawCircle(origin.X, origin.Y, body.Radius * 200, paint);
                        continue;
                    }
                    float halfWidth = float.Parse(shape.Design["width"], CultureInfo.InvariantCulture);
                    float halfHeight = float.Parse(shape.Design["height"], CultureInfo.InvariantCulture);
                    int restore = canvas.Save();
                    canvas.Translate(origin.X, origin.Y);
                    canvas.RotateRadians(body.Angle);
                    canvas.DrawRect(-halfWidth, -halfHeight, halfWidth * 2, halfHeight * 2, paint);
                    canvas.RestoreToCount(restore);
                }
                paint.Color = SKColors.IndianRed;
                foreach (SimulationBalloon balloon in Simulation?.Balloons ?? [])
                {
                    // Diagnostic balloon until its artwork is ported.
                    if (balloon.Body is PhysicsBody body)
                    {
                        canvas.DrawCircle(body.Position.X * 200, body.Position.Y * 200, body.Radius * 200, paint);
                    }
                }
                foreach (SimulationFrog frog in Simulation?.Frogs ?? [])
                {
                    // Diagnostic frog and tongue until their artwork is ported.
                    paint.Color = SKColors.SeaGreen;
                    canvas.DrawCircle(frog.Position.X, frog.Position.Y, 28, paint);
                    paint.Color = SKColors.HotPink;
                    foreach (PhysicsBody body in frog.Bodies)
                    {
                        if (body.Radius > 0)
                        {
                            canvas.DrawCircle(body.Position.X * 200, body.Position.Y * 200, body.Radius * 200, paint);
                            continue;
                        }
                        IReadOnlyList<Vector2> vertices = body.PolygonFixtures[0].WorldVertices(body.Origin, body.Angle);
                        using SKPathBuilder tongue = new();
                        tongue.MoveTo(vertices[0].X * 200, vertices[0].Y * 200);
                        foreach (Vector2 vertex in vertices.Skip(1))
                        {
                            tongue.LineTo(vertex.X * 200, vertex.Y * 200);
                        }
                        tongue.Close();
                        using SKPath path = tongue.Detach();
                        canvas.DrawPath(path, paint);
                    }
                }
                paint.Color = SKColors.LightSlateGray;
                foreach (PhysicsRevoluteJoint pin in Simulation?.PinJoints ?? [])
                {
                    // Diagnostic marker until pin artwork is ported.
                    canvas.DrawCircle(pin.AnchorB.X * 200, pin.AnchorB.Y * 200, 6, paint);
                }
                paint.Color = SKColors.Wheat;
                paint.StrokeWidth = 3;
                foreach (LevelRope rope in Simulation == null ? Layout.Ropes : [])
                {
                    canvas.DrawLine(rope.Start.X, rope.Start.Y, rope.End.X, rope.End.Y, paint);
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
                int starIndex = 0;
                int characterIndex = 0;
                foreach (PlacedLevelObject placed in Layout.Objects)
                {
                    if (Session != null && placed.Source.Kind == "tutorial_swipe")
                    {
                        continue;
                    }
                    if (placed.Position is not Vector2 point || placed.Source.Kind == "polygon_materialized" ||
                        (Simulation != null && (placed.Source.Kind is "pin" or "frog" or "balloon" || placed.Source.Kind.StartsWith("circle", StringComparison.Ordinal) || placed.Source.Kind.StartsWith("rectangle", StringComparison.Ordinal))))
                    {
                        continue;
                    }
                    if (placed.Source.Kind == "candy" && Simulation != null)
                    {
                        PhysicsBody body = Simulation.Candies[candyIndex++];
                        float opacity = Simulation.Gameplay?.CandyOpacity ?? 1;
                        if (opacity > 0)
                        {
                            candy!.Draw(canvas, body.Position * 200, body.Angle, opacity);
                        }
                    }
                    else if (placed.Source.Kind == "star" && Simulation != null)
                    {
                        SimulationStar star = Simulation.Stars[starIndex];
                        if (stars != null)
                        {
                            stars.Draw(canvas, star, starIndex, Simulation.Tick);
                        }
                        else if (!star.CollectedAt.HasValue)
                        {
                            paint.Color = SKColors.Gold;
                            canvas.DrawCircle(star.Position.X * 200, star.Position.Y * 200, star.Radius * 200, paint);
                        }
                        starIndex++;
                    }
                    else if (placed.Source.Kind == "omnom" && omnom != null)
                    {
                        Vector2 position = point;
                        float angle = placed.Angle * MathF.PI / 180;
                        if (Simulation != null)
                        {
                            PhysicsBody body = Simulation.Characters[characterIndex++];
                            position = body.Origin * 200;
                            angle = body.Angle;
                        }
                        Vector2 origin = (-omnom.CanvasSize / 2) + new Vector2(-8, -11);
                        Vector2 scale = Simulation?.Gameplay?.CharacterScale ?? Vector2.One;
                        Vector2 offset = Simulation?.Gameplay?.CharacterOffset ?? Vector2.Zero;
                        Matrix4x4 character = Matrix4x4.CreateTranslation(origin.X, origin.Y, 0) *
                            Matrix4x4.CreateScale(scale.X, scale.Y, 1) * Matrix4x4.CreateTranslation(offset.X, offset.Y, 0) *
                            Matrix4x4.CreateRotationZ(angle) * Matrix4x4.CreateTranslation(position.X, position.Y, 0);
                        double poseTime = seconds;
                        if (Simulation?.Gameplay is LevelGameplay game)
                        {
                            SyncGameAnimation();
                            poseTime = (Simulation.Tick - game.SceneStartedAt) * (double)PhysicsWorld.TimeStep;
                        }
                        omnom.DrawTransformed(canvas, character, poseTime);
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
                if (Simulation?.Gameplay?.SwipeTutorial is SwipeTutorial hint && hint.Opacity > 0)
                {
                    Vector2 origin = (-swipeTutorial!.CanvasSize / 2) + new Vector2(90, 0);
                    Matrix4x4 transformHint = Matrix4x4.CreateTranslation(origin.X, origin.Y, 0) *
                        Matrix4x4.CreateRotationZ(hint.Angle * MathF.PI / 180) * Matrix4x4.CreateTranslation(hint.Position.X, hint.Position.Y, 0);
                    double time = swipeTutorial.Duration > 0 ? hint.AnimationSeconds % swipeTutorial.Duration : 0;
                    swipeTutorial.DrawTransformed(canvas, transformHint, time, hint.Opacity);
                }
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        private void SyncGameAnimation()
        {
            LevelGameplay game = Simulation!.Gameplay!;
            if (animatedSimulation != Simulation || sceneRevision != game.SceneRevision)
            {
                omnom!.SelectScene(game.Scene);
                sceneRevision = game.SceneRevision;
                animatedSimulation = Simulation;
            }
        }

        private void UpdateGameAnimation()
        {
            SyncGameAnimation();
            LevelGameplay game = Simulation!.Gameplay!;
            if ((Simulation.Tick - game.SceneStartedAt) * (double)PhysicsWorld.TimeStep >= omnom!.Duration)
            {
                game.SceneFinished(game.Scene, game.SceneRevision);
                SyncGameAnimation();
            }
        }

        /// <summary>Releases owned character textures; repeated calls are safe.</summary>
        public void Dispose()
        {
            if (!disposed)
            {
                if (Session != null)
                {
                    Session.Stepped -= UpdateGameAnimation;
                    Session.Input.Cancel();
                }
                omnom?.Dispose();
                candy?.Dispose();
                forestLeaves?.Dispose();
                swipeTutorial?.Dispose();
                stars?.Dispose();
                background?.Dispose();
                disposed = true;
            }
        }
    }
}
