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
        private AnimationPreview? tutorial;
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
        /// <param name="variant">Game mode to play, which decides stars, fruits and tutorial fingers.</param>
        /// <returns>The parsed level and a preview owning its decoded character assets.</returns>
        /// <exception cref="System.IO.InvalidDataException">The level, resource or animation is invalid.</exception>
        /// <exception cref="System.IO.IOException">A cache file cannot be read.</exception>
        public static (LevelDefinition Definition, LevelPreview Preview) Load(string cache, int pack, int level, int resolution, bool simulate = false, bool play = false,
            LevelVariant variant = LevelVariant.Main)
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
                    preview.Session = new(definition, LevelReader.LoadWindowsDesign(cache), firstLevelArrival: pack == 1 && level == 1,
                        SceneTimings.Load(cache, definition, variant), variant);
                    if (preview.Session.Simulation.Gameplay!.ArrivalLeaves != null)
                    {
                        preview.forestLeaves = new ForestLeafSprites(cache, resolution);
                    }
                    if (preview.Session.Simulation.Gameplay.Tutorial is TutorialFinger finger)
                    {
                        preview.tutorial = AnimationPreview.Load(cache, 104, finger.Swipe ? "SWIPE_LOOP" : "TAP", resolution);
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
                    if (shape.Kind.StartsWith("polygon", StringComparison.Ordinal))
                    {
                        // Static polygons are drawn as terrain above; moving ones draw their triangle fixtures.
                        if (body.Type != PhysicsBodyType.Static)
                        {
                            foreach (ConvexPolygon fixture in body.PolygonFixtures)
                            {
                                using SKPathBuilder triangle = new();
                                for (int i = 0; i < fixture.Vertices.Count; i++)
                                {
                                    Vector2 point = origin + (Vector2.Transform(fixture.Vertices[i], Matrix3x2.CreateRotation(body.Angle)) * 200);
                                    if (i == 0)
                                    {
                                        triangle.MoveTo(point.X, point.Y);
                                    }
                                    else
                                    {
                                        triangle.LineTo(point.X, point.Y);
                                    }
                                }
                                triangle.Close();
                                using SKPath outline = triangle.Detach();
                                canvas.DrawPath(outline, paint);
                            }
                        }
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
                paint.Color = SKColors.Plum;
                foreach (SimulationBalloonGenerator generator in Simulation?.BalloonGenerators.Where(g => !g.Removed) ?? [])
                {
                    // Diagnostic generator until its artwork is ported.
                    canvas.DrawCircle(generator.Body.Origin.X * 200, generator.Body.Origin.Y * 200, generator.Body.Radius * 200, paint);
                }
                foreach (SimulationHelicopter helicopter in Simulation?.Helicopters ?? [])
                {
                    // Diagnostic helicopter until its artwork is ported; it brightens while carrying a body.
                    paint.Color = helicopter.Captured == null ? SKColors.SteelBlue : SKColors.DeepSkyBlue;
                    canvas.DrawCircle(helicopter.Body.Position.X * 200, helicopter.Body.Position.Y * 200, helicopter.Body.Radius * 200, paint);
                }
                foreach (SimulationThrower thrower in Simulation?.Throwers ?? [])
                {
                    // Diagnostic thrower until its artwork is ported; it brightens while holding a body.
                    paint.Color = thrower.Captured == null ? SKColors.DarkOrange : SKColors.Orange;
                    Vector2 origin = thrower.Body.Origin * 200;
                    foreach (ConvexPolygon fixture in thrower.Body.PolygonFixtures)
                    {
                        using SKPathBuilder triangle = new();
                        for (int i = 0; i < fixture.Vertices.Count; i++)
                        {
                            Vector2 point = origin + (Vector2.Transform(fixture.Vertices[i], Matrix3x2.CreateRotation(thrower.Body.Angle)) * 200);
                            if (i == 0)
                            {
                                triangle.MoveTo(point.X, point.Y);
                            }
                            else
                            {
                                triangle.LineTo(point.X, point.Y);
                            }
                        }
                        triangle.Close();
                        using SKPath outline = triangle.Detach();
                        canvas.DrawPath(outline, paint);
                    }
                }
                foreach (SimulationGremlin gremlin in Simulation?.Gremlins.Where(g => !g.Removed) ?? [])
                {
                    // Diagnostic fixture outlines until gremlin artwork is ported.
                    paint.Color = SKColors.CornflowerBlue;
                    int gremlinSave = canvas.Save();
                    canvas.Translate(gremlin.Body.Origin.X * 200, gremlin.Body.Origin.Y * 200);
                    canvas.RotateRadians(gremlin.Body.Angle);
                    foreach (ConvexPolygon fixture in gremlin.Body.PolygonFixtures)
                    {
                        using SKPathBuilder path = new();
                        path.MoveTo(fixture.Vertices[0].X * 200, fixture.Vertices[0].Y * 200);
                        foreach (Vector2 vertex in fixture.Vertices.Skip(1))
                        {
                            path.LineTo(vertex.X * 200, vertex.Y * 200);
                        }
                        path.Close();
                        using SKPath outline = path.Detach();
                        canvas.DrawPath(outline, paint);
                    }
                    canvas.RestoreToCount(gremlinSave);
                }
                foreach (SimulationSnail snail in Simulation?.Snails ?? [])
                {
                    // Diagnostic snail marker at its touch center until artwork is ported.
                    paint.Color = snail.Active ? SKColors.Gold : SKColors.Goldenrod;
                    canvas.DrawCircle(snail.VisualCenter.X, snail.VisualCenter.Y, 26, paint);
                }
                foreach (SimulationPump pump in Simulation?.Pumps ?? [])
                {
                    // Diagnostic pump and blow direction until its artwork is ported.
                    paint.Color = SKColors.SlateBlue;
                    canvas.DrawCircle(pump.Position.X, pump.Position.Y, 30, paint);
                    float radians = pump.Angle * MathF.PI / 180;
                    using SKPaint nozzle = new()
                    {
                        Color = SKColors.SlateBlue,
                        StrokeWidth = 10,
                        IsAntialias = true
                    };
                    canvas.DrawLine(pump.Position.X, pump.Position.Y, pump.Position.X + (MathF.Sin(radians) * 60), pump.Position.Y - (MathF.Cos(radians) * 60), nozzle);
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
                foreach (SimulationTrigger trigger in Simulation?.Triggers ?? [])
                {
                    // Diagnostic marker until trigger artwork is ported.
                    paint.Color = trigger.Pushed ? SKColors.LimeGreen : SKColors.OrangeRed;
                    canvas.DrawCircle(trigger.Body.Origin.X * 200, trigger.Body.Origin.Y * 200, 16, paint);
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
                if (Simulation != null)
                {
                    // Fruit artwork is pending; uncollected fruits are drawn diagnostically.
                    paint.Color = SKColors.OrangeRed;
                    foreach (SimulationFruit fruit in Simulation.Fruits.Where(f => !f.CollectedAt.HasValue))
                    {
                        canvas.DrawCircle(fruit.Position.X * 200, fruit.Position.Y * 200, fruit.Radius * 200, paint);
                    }
                    // Booka artwork is pending: Bookas are drawn purple, caught ones light purple.
                    foreach (SimulationBooka booka in Simulation.Bookas.Where(k => !k.Removed))
                    {
                        paint.Color = booka.Booed ? SKColors.Plum : SKColors.Purple;
                        canvas.DrawCircle(booka.Body.Position.X * 200, booka.Body.Position.Y * 200, booka.Body.Radius * 200, paint);
                    }
                    // Fireball artwork is pending: idle fireballs are drawn red, fading ones dark red.
                    foreach (SimulationFireball fireball in Simulation.Fireballs.Where(f => !f.Removed))
                    {
                        paint.Color = fireball.Fading ? SKColors.DarkRed : SKColors.Red;
                        canvas.DrawCircle(fireball.Body.Position.X * 200, fireball.Body.Position.Y * 200, fireball.Body.Radius * 200, paint);
                    }
                }
                int candyIndex = 0;
                int starIndex = 0;
                int characterIndex = 0;
                foreach (PlacedLevelObject placed in Layout.Objects)
                {
                    if (placed.Source.Kind == "clover")
                    {
                        // The Windows loader leaves authored clover placements out of the scene.
                        continue;
                    }
                    if (Session != null && placed.Source.Kind is "tutorial_swipe" or "tutorial_tap")
                    {
                        continue;
                    }
                    if (placed.Position is not Vector2 point || placed.Source.Kind == "polygon_materialized" ||
                        (Simulation != null && (placed.Source.Kind is "pin" or "trigger" or "frog" or "balloon" or "rope_instance" or "pump" or "thrower" or "helicopter" or "fireball" or "booka" or "gremlin" or "snail" || placed.Source.Kind.StartsWith("circle", StringComparison.Ordinal) || placed.Source.Kind.StartsWith("rectangle", StringComparison.Ordinal))))
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
                        // A fruit challenge destroys its stars after the first step.
                        SimulationStar star = Simulation.Stars[starIndex];
                        if (stars != null && !star.Removed)
                        {
                            stars.Draw(canvas, star, starIndex, Simulation.Tick);
                        }
                        else if (stars == null && !star.Removed && !star.CollectedAt.HasValue)
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
                        if (Simulation?.Gameplay?.ArtFacingRight ?? (position.X < Layout.Size.X / 2))
                        {
                            // The artwork faces left; the source mirrors its scale to face right.
                            scale.X = -scale.X;
                        }
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
                if (Simulation?.Gameplay?.Tutorial is TutorialFinger hint && hint.Opacity > 0)
                {
                    // A swipe finger is shifted 90 units along its rotated axis; a tap finger is centered on the hint.
                    Vector2 origin = (-tutorial!.CanvasSize / 2) + new Vector2(hint.Swipe ? 90 : 0, 0);
                    Matrix4x4 transformHint = Matrix4x4.CreateTranslation(origin.X, origin.Y, 0) *
                        Matrix4x4.CreateRotationZ(hint.Swipe ? hint.Angle * MathF.PI / 180 : 0) * Matrix4x4.CreateTranslation(hint.Position.X, hint.Position.Y, 0);
                    double time = tutorial.Duration > 0 ? hint.AnimationSeconds % tutorial.Duration : 0;
                    tutorial.DrawTransformed(canvas, transformHint, time, hint.Opacity);
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
            // Gameplay completes authored scenes on its own fixed clock; presentation only follows the selected scene.
            SyncGameAnimation();
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
                tutorial?.Dispose();
                stars?.Dispose();
                background?.Dispose();
                disposed = true;
            }
        }
    }
}
