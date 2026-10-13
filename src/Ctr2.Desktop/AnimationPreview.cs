using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Owns decoded cached textures for one assembled character scene preview.</summary>
    public sealed class AnimationPreview : IDisposable
    {
        /// <summary>Decoded scene tree.</summary>
        private readonly AnimationElement root;
        /// <summary>Selected original scene ID.</summary>
        private int scene;
        /// <summary>Selected image resolution.</summary>
        private readonly int resolution;
        /// <summary>Cache root.</summary>
        private readonly string cache;
        /// <summary>Original imported resource catalog.</summary>
        private readonly ResourceCatalog catalog;
        /// <summary>Decoded images indexed by original resource ID.</summary>
        private readonly Dictionary<int, (ResourceEntry Resource, SKBitmap Bitmap)> images = [];
        /// <summary>Validated trimmed geometry indexed by image and quad.</summary>
        private readonly Dictionary<(int Source, int Quad), AtlasFrame> geometry = [];
        /// <summary>Total decoded texture allocation retained by this preview, in bytes.</summary>
        private long decodedBytes;
        /// <summary>Whether owned native resources have been released.</summary>
        private bool disposed;

        /// <summary>Gets the authored root canvas dimensions in logical units.</summary>
        /// <value>The positive width and height, independent of texture density.</value>
        public Vector2 CanvasSize => new(root.Width!.Value, root.Height!.Value);

        /// <summary>Gets the longest selected scene track duration.</summary>
        /// <value>The duration in seconds; zero indicates a static pose.</value>
        public double Duration
        {
            get; private set;
        }

        /// <summary>Initializes a preview with a decoded scene and cache catalog.</summary>
        /// <param name="cache">Cache root.</param>
        /// <param name="catalog">Validated catalog.</param>
        /// <param name="root">Animation tree.</param>
        /// <param name="scene">Selected scene ID.</param>
        /// <param name="resolution">Selected image resolution.</param>
        private AnimationPreview(string cache, ResourceCatalog catalog, AnimationElement root, int scene, int resolution)
        {
            this.cache = cache;
            this.catalog = catalog;
            this.root = root;
            this.scene = scene;
            this.resolution = resolution;
            Duration = AnimationPose.Duration(root, scene);
        }

        /// <summary>Loads one original animation resource and a named scene from a local cache.</summary>
        /// <param name="cache">Imported cache with resources.json and assets.</param>
        /// <param name="id">Original animation resource ID.</param>
        /// <param name="sceneName">Exact authored scene name, such as IDLE.</param>
        /// <param name="resolution">Image resolution enum; animation coordinates remain logical units.</param>
        /// <returns>A preview owning its decoded images; dispose it after use.</returns>
        /// <exception cref="InvalidDataException">Animation, scene, catalog, or image references are invalid.</exception>
        /// <exception cref="IOException">A cached file cannot be read.</exception>
        public static AnimationPreview Load(string cache, int id, string sceneName, int resolution)
        {
            ResourceCatalog catalog = ResourcePreview.LoadCatalog(cache);
            ResourceEntry[] matches = [.. catalog.Resources.Where(r => r.Id == id && r.Type == 7)];
            if (matches.Length != 1)
            {
                throw new InvalidDataException("Expected one animation resource for ID " + id);
            }
            string path = AssetPath(cache, matches[0].Path);
            using FileStream input = File.OpenRead(path);
            if (input.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("Animation exceeds 16 MiB.");
            }
            byte[] bytes = new byte[(int)input.Length];
            input.ReadExactly(bytes);
            AnimationElement root = AnimationReader.Read(bytes);
            if (root.Width is not > 0 || root.Height is not > 0)
            {
                throw new InvalidDataException("Animation preview requires a positive authored root canvas.");
            }
            AnimationTimeline[] scenes = [.. root.Timelines.Where(t => t.Name == sceneName && !t.IsEmpty)];
            if (scenes.Length != 1)
            {
                throw new InvalidDataException("Expected one named scene. Available: " + string.Join(", ", root.Timelines.Where(t => !t.IsEmpty).Select(t => t.Name)));
            }
            AnimationPreview preview = new(cache, catalog, root, scenes[0].Id, resolution);
            try
            {
                preview.Preload(root);
                _ = AnimationPose.Sample(root, preview.scene, 0, preview.Size);
                return preview;
            }
            catch
            {
                preview.Dispose();
                throw;
            }
        }

        /// <summary>Selects and validates a named scene while retaining decoded textures.</summary>
        /// <param name="name">Exact authored scene name.</param>
        public void SelectScene(string name)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            AnimationTimeline[] matches = [.. root.Timelines.Where(t => t.Name == name && !t.IsEmpty)];
            if (matches.Length != 1)
            {
                throw new InvalidDataException("Expected one named scene: " + name);
            }
            int previous = scene;
            scene = matches[0].Id;
            try
            {
                Preload(root);
                _ = AnimationPose.Sample(root, scene, 0, Size);
                Duration = AnimationPose.Duration(root, scene);
            }
            catch { scene = previous; throw; }
        }

        /// <summary>Draws an assembled pose on the uniformly fitted authored canvas.</summary>
        /// <param name="canvas">Caller-owned destination canvas.</param>
        /// <param name="width">Output width in pixels.</param>
        /// <param name="height">Output height in pixels.</param>
        /// <param name="seconds">Nonnegative elapsed scene time; the terminal pose holds.</param>
        /// <exception cref="ObjectDisposedException">The preview has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Dimensions or time are invalid.</exception>
        public void Draw(SKCanvas canvas, int width, int height, double seconds)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Viewport viewport = Viewport.Fit(width, height, root.Width!.Value, root.Height!.Value);
            canvas.Clear(new SKColor(24, 27, 32));
            Vector2 topLeft = viewport.ToScreen(Vector2.Zero), bottomRight = viewport.ToScreen(new(root.Width.Value, root.Height.Value));
            using SKPaint background = new()
            {
                Color = new SKColor(68, 73, 81)
            };
            canvas.DrawRect(new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y), background);
            DrawTransformed(canvas, viewport.Transform, seconds);
        }

        /// <summary>Draws a pose through a caller-supplied scene transform without clearing or fitting the canvas.</summary>
        /// <param name="canvas">Caller-owned destination canvas; its clipping and transform remain active.</param>
        /// <param name="sceneTransform">Transform from authored root coordinates into the destination scene.</param>
        /// <param name="seconds">Nonnegative elapsed scene time; the terminal pose holds.</param>
        /// <param name="opacity">Container opacity between zero and one.</param>
        /// <exception cref="ObjectDisposedException">The preview has been disposed.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Time is invalid.</exception>
        public void DrawTransformed(SKCanvas canvas, Matrix4x4 sceneTransform, double seconds, float opacity = 1)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(opacity));
            }
            foreach (AnimationSprite sprite in AnimationPose.Sample(root, scene, seconds, Size))
            {
                AtlasFrame frame = geometry[(sprite.Source, sprite.Quad)];
                Matrix4x4 transform = sprite.Transform * sceneTransform;
                SKMatrix matrix = new(transform.M11, transform.M21, transform.M41, transform.M12, transform.M22, transform.M42, 0, 0, 1);
                float[] tint = [sprite.Color.X, 0, 0, 0, 0, 0, sprite.Color.Y, 0, 0, 0, 0, 0, sprite.Color.Z, 0, 0, 0, 0, 0, sprite.Color.W * opacity, 0];
                using SKColorFilter filter = SKColorFilter.CreateColorMatrix(tint);
                using SKPaint paint = new()
                {
                    ColorFilter = filter,
                    IsAntialias = false
                };
                int saved = canvas.Save();
                try
                {
                    canvas.Concat(matrix);
                    SKRect source = new(frame.PixelX, frame.PixelY, frame.PixelX + frame.PixelWidth, frame.PixelY + frame.PixelHeight);
                    // Image::initWithTexture leaves restoreCutTransparency=false: parts start at the trimmed origin.
                    canvas.DrawBitmap(images[sprite.Source].Bitmap, source, new SKRect(0, 0, frame.LogicalWidth, frame.LogicalHeight), new SKSamplingOptions(SKFilterMode.Nearest), paint);
                }
                finally
                {
                    canvas.RestoreToCount(saved);
                }
            }
        }

        /// <summary>Releases all decoded native bitmaps; repeated calls are safe.</summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            foreach ((_, SKBitmap bitmap) in images.Values)
            {
                bitmap.Dispose();
            }
            images.Clear();
            geometry.Clear();
        }

        /// <summary>Validates every quad a selected scene can draw before entering the presentation loop.</summary>
        /// <param name="element">Current element in the selected scene.</param>
        private void Preload(AnimationElement element)
        {
            AnimationTimeline? timeline = element.Timelines.FirstOrDefault(t => t.Id == scene);
            if (timeline?.IsEmpty == true)
            {
                return;
            }
            HashSet<int> reachable = ReachableQuads(element, timeline);
            if (reachable.Count == 0)
            {
                return;
            }
            if (element.Type == 1)
            {
                foreach (int quad in reachable)
                {
                    _ = Size(element.Source, quad);
                }
            }
            foreach (AnimationElement child in element.Children)
            {
                Preload(child);
            }
        }

        /// <summary>Collects quads visible after complete simultaneous action groups.</summary>
        /// <param name="element">Element with its initial quad.</param>
        /// <param name="timeline">Selected scene, if present.</param>
        /// <returns>Potentially visible quads; empty for a permanently hidden element.</returns>
        private static HashSet<int> ReachableQuads(AnimationElement element, AnimationTimeline? timeline)
        {
            List<(double Time, AnimationKey Key)> actions = [];
            double time = 0;
            foreach (AnimationKey key in timeline?.Tracks.Where(t => t.Type == 4).SelectMany(t => t.Keys) ?? [])
            {
                time += key.Time;
                actions.Add((time, key));
            }
            HashSet<int> quads = [];
            int quad = element.Quad;
            bool visible = true;
            if (actions.Count == 0 || actions[0].Time > 0)
            {
                _ = quads.Add(quad);
            }
            foreach (IGrouping<double, (double Time, AnimationKey Key)> group in actions.GroupBy(a => a.Time))
            {
                foreach ((_, AnimationKey key) in group)
                {
                    if (key.Action == 0)
                    {
                        visible = key.P2 != 0;
                    }
                    else if (key.Action == 1)
                    {
                        if (key.P1 < 0 || key.P1 >= int.MaxValue || key.P1 != MathF.Truncate(key.P1))
                        {
                            throw new InvalidDataException("Invalid animation quad action.");
                        }
                        quad = checked((int)key.P1);
                    }
                }
                if (visible)
                {
                    _ = quads.Add(quad);
                }
            }
            return quads;
        }

        /// <summary>Resolves and caches trimmed logical dimensions with one hash check per image.</summary>
        /// <param name="source">Original image ID.</param>
        /// <param name="quad">Original quad index.</param>
        /// <returns>The trimmed logical dimensions.</returns>
        private Vector2 Size(int source, int quad)
        {
            if (!geometry.TryGetValue((source, quad), out AtlasFrame? frame))
            {
                if (!images.TryGetValue(source, out (ResourceEntry Resource, SKBitmap Bitmap) image))
                {
                    ResourceEntry[] matches = [.. catalog.Resources.Where(r => r.Id == source && r.Resolution == resolution && r.Type == 0)];
                    if (matches.Length != 1 || matches[0].PixelsPerLogicalUnit is not > 0 || matches[0].Quads == null)
                    {
                        throw new InvalidDataException($"Missing image {source} at known density/resolution {resolution}.");
                    }
                    string path = ResourcePreview.ResolveTexture(cache, matches[0]);
                    using SKCodec codec = SKCodec.Create(path) ?? throw new InvalidDataException("Cannot decode animation texture.");
                    if (codec.Info.Width > 8192 || codec.Info.Height > 8192 || codec.Info.Width <= 0 || codec.Info.Height <= 0)
                    {
                        throw new InvalidDataException("Animation texture dimensions exceed bounds.");
                    }
                    long bytes = (long)codec.Info.Width * codec.Info.Height * 4;
                    if (decodedBytes + bytes > 128 * 1024 * 1024)
                    {
                        throw new InvalidDataException("Animation decoded textures exceed the 128 MiB preview budget.");
                    }
                    SKBitmap bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Cannot decode animation texture.");
                    image = (matches[0], bitmap);
                    images.Add(source, image);
                    decodedBytes += bytes;
                }
                if (quad < 0 || quad >= image.Resource.Quads.Count)
                {
                    throw new InvalidDataException($"Image {source} has no quad {quad}.");
                }
                frame = ResourcePreview.Geometry(image.Resource, quad);
                if (frame.PixelX + frame.PixelWidth > image.Bitmap.Width || frame.PixelY + frame.PixelHeight > image.Bitmap.Height)
                {
                    throw new InvalidDataException("Animation quad exceeds cached texture bounds.");
                }
                geometry.Add((source, quad), frame);
            }
            return new(frame.LogicalWidth, frame.LogicalHeight);
        }

        /// <summary>Restricts an animation reference to a regular file in the cache assets directory.</summary>
        /// <param name="cache">Cache root.</param>
        /// <param name="name">Flat asset filename.</param>
        /// <returns>The validated absolute path.</returns>
        private static string AssetPath(string cache, string name)
        {
            if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsafe animation asset path.");
            }
            string assets = Path.GetFullPath(Path.Combine(cache, "assets")), path = Path.Combine(assets, name);
            return (File.GetAttributes(assets) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0
                ? throw new InvalidDataException("Linked cache animations are unsupported.")
                : path;
        }
    }
}
