using System;
using System.Numerics;

using Ctr2.Engine.Physics;

using SkiaSharp;

namespace Ctr2.Desktop
{
    /// <summary>Draws Windows stationary star scenes from fixed-tick collection snapshots.</summary>
    internal sealed class StarSprites : IDisposable
    {
        private readonly AnimationPreview idle;
        private readonly AnimationPreview collected;

        internal StarSprites(string cache, int resolution)
        {
            idle = AnimationPreview.Load(cache, 95, "IDLE", resolution);
            try
            {
                collected = AnimationPreview.Load(cache, 95, "FX_PARTICLE", resolution);
            }
            catch { idle.Dispose(); throw; }
        }

        internal void Draw(SKCanvas canvas, SimulationStar star, int index, int tick)
        {
            double time = tick * (double)PhysicsWorld.TimeStep;
            // Star::initWith and Windows x86 use three phases, plus a separate looping scale track.
            double phase = idle.Duration > 0 ? (time + ((index % 3) * idle.Duration / 3)) % idle.Duration : 0;
            float scale = idle.Duration > 0 ? .8f + .2f * (1 - MathF.Abs((float)(phase / idle.Duration * 2) - 1)) : .8f;
            AnimationPreview animation = idle;
            double sceneTime = phase;
            if (star.CollectedAt is int contactTick)
            {
                animation = collected;
                sceneTime = Math.Max(0, tick - contactTick) * (double)PhysicsWorld.TimeStep;
                if (sceneTime >= collected.Duration)
                {
                    return;
                }
            }
            Vector2 center = star.Position * 200;
            Vector2 origin = -animation.CanvasSize / 2;
            Matrix4x4 transform = Matrix4x4.CreateTranslation(origin.X, origin.Y, 0) *
                Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(center.X, center.Y, 0);
            animation.DrawTransformed(canvas, transform, sceneTime);
        }

        public void Dispose()
        {
            idle.Dispose();
            collected.Dispose();
        }
    }
}
