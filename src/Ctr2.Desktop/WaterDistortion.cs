using System;
using System.Numerics;

using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    internal sealed record WaterMesh(SKPoint[] Positions, SKPoint[] Coordinates);

    /// <summary>Windows WaterDrawer: vertex displacement followed by triangle interpolation.</summary>
    internal sealed class WaterDistortion
    {
        private const int Segments = 20;
        private int tick;
        private bool inverted;
        private Vector2 maximum;
        internal float Time
        {
            get; private set;
        }

        internal void Reset()
        {
            tick = 0;
            Time = 0;
            inverted = false;
            maximum = Vector2.Zero;
        }
        internal void Advance(int target)
        {
            if (target < tick)
            {
                Reset();
            }
            for (; tick < target; tick++)
            {
                Time += PhysicsWorld.TimeStep;
            }
        }
        internal void UseFramebuffer()
        {
            inverted = true;
            maximum = Vector2.One;
        }
        internal void UseBackground(Vector2 size)
        {
            // Windows 0x601450 compares maxY, inversion and originX, but not maxX.
            if (inverted || MathF.Abs(maximum.Y - size.Y) > .0001f)
            {
                Time += 3.1415926f;
                inverted = false;
                maximum = size;
            }
        }

        internal static WaterMesh Build(Vector2 fullScreen, Vector2 offset, float level, float seconds, bool inverseY, Vector2 maxUv)
        {
            SKPoint[] grid = new SKPoint[441], uv = new SKPoint[441];
            Vector2 step = fullScreen * (1f / Segments), texStep = maxUv * (1f / Segments);
            for (int y = 0; y <= Segments; y++)
            {
                for (int x = 0; x <= Segments; x++)
                {
                    float px = (x * step.X) - offset.X + (x == 0 ? -2 : x == Segments ? 2 : 0);
                    float py = (y * step.Y) - offset.Y + (y == 0 ? -2 : y == Segments ? 2 : 0);
                    float u = x * texStep.X, v = (inverseY ? Segments - y : y) * texStep.Y;
                    if (py - 50 > level + 15)
                    {
                        float envelope = MathF.Sin(py * 3.1415926f / 192);
                        u = Math.Clamp(u + (maxUv.X * MathF.Sin((py / 120) + seconds) * .004f * .0112f * envelope), 0, maxUv.X);
                        v = Math.Clamp(v + (maxUv.Y * MathF.Sin((px / 60) + seconds) * .0112f * envelope), 0, maxUv.Y);
                    }
                    grid[(y * 21) + x] = new(px, py);
                    uv[(y * 21) + x] = new(u, v);
                }
            }
            SKPoint[] positions = new SKPoint[2400], coordinates = new SKPoint[2400];
            int index = 0;
            for (int x = 0; x < Segments; x++)
            {
                for (int y = 0; y < Segments; y++)
                {
                    int tl = (y * 21) + x, tr = tl + 1, bl = tl + 21, br = bl + 1;
                    foreach (int vertex in new[] { tl, tr, br, tl, bl, br })
                    {
                        positions[index] = grid[vertex];
                        coordinates[index++] = uv[vertex];
                    }
                }
            }
            return new(positions, coordinates);
        }

        internal void DrawFrame(SKCanvas canvas, SKImage image, Vector2 size, int width, int height, float level, int fixedTick)
        {
            Advance(fixedTick);
            UseFramebuffer();
            Viewport viewport = Viewport.Fit(width, height, size.X, size.Y);
            float scale = viewport.Transform.M11;
            Vector2 full = new(width / scale, height / scale);
            WaterMesh mesh = Build(full, (full - size) / 2, level, Time, true, maximum);
            canvas.Clear(new SKColor(24, 27, 32));
            int saved = canvas.Save();
            try
            {
                Matrix4x4 m = viewport.Transform;
                canvas.Concat(new SKMatrix(m.M11, m.M21, m.M41, m.M12, m.M22, m.M42, 0, 0, 1));
                canvas.ClipRect(new SKRect(0, 0, size.X, size.Y));
                using SKShader shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
                Render(canvas, shader, mesh, new(image.Width, image.Height), true);
            }
            finally
            {
                canvas.RestoreToCount(saved);
            }
        }

        internal void DrawBackground(SKCanvas canvas, SKBitmap bitmap, AtlasFrame frame, Vector2 size, int width, int height, float level, int fixedTick)
        {
            Advance(fixedTick);
            UseBackground(new(frame.PixelWidth / bitmap.Width, frame.PixelHeight / bitmap.Height));
            float scale = Viewport.Fit(width, height, size.X, size.Y).Transform.M11;
            Vector2 full = new(width / scale, height / scale);
            WaterMesh mesh = Build(full, (full - size) / 2, level, Time, false, maximum);
            using SKShader shader = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
            Render(canvas, shader, mesh, new(bitmap.Width, bitmap.Height), false);
        }

        private static void Render(SKCanvas canvas, SKShader shader, WaterMesh mesh, Vector2 pixels, bool inverseY)
        {
            for (int i = 0; i < mesh.Coordinates.Length; i++)
            {
                SKPoint uv = mesh.Coordinates[i];
                mesh.Coordinates[i] = new(uv.X * pixels.X, (inverseY ? 1 - uv.Y : uv.Y) * pixels.Y);
            }
            using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, mesh.Positions, mesh.Coordinates, null);
            using SKPaint paint = new()
            {
                Shader = shader,
                BlendMode = SKBlendMode.Src,
                IsAntialias = false
            };
            canvas.DrawVertices(vertices, SKBlendMode.Modulate, paint);
        }
    }
}
