using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>One accepted tap that a scene object consumed.</summary>
    /// <param name="Tick">Zero-based tick before which the tap applied.</param>
    /// <param name="Point">Tap position in logical units.</param>
    public sealed record SceneTap(int Tick, Vector2 Point);

    /// <summary>Queues fitted pointer presses and swipes for application immediately before a fixed simulation tick.</summary>
    /// <param name="simulation">Scene receiving accepted cuts.</param>
    /// <param name="canvasSize">Authored logical canvas dimensions.</param>
    public sealed class RopeInput(CandySimulation simulation, Vector2 canvasSize)
    {
        /// <summary>Bounded pending logical presses and pointer segments in event order.</summary>
        private readonly Queue<(Vector2 Start, Vector2 End, bool Tap)> pending = new();
        /// <summary>Accepted taps in application order.</summary>
        private readonly List<SceneTap> taps = [];
        /// <summary>Accepted immutable replay records in application order.</summary>
        private readonly List<RopeCut> cuts = [];
        /// <summary>Previous logical pointer position while held.</summary>
        private Vector2? previous;
        /// <summary>Output size at press time, used to cancel stale transforms.</summary>
        private (int Width, int Height) dimensions;
        /// <summary>Gets accepted tick-indexed cuts, including impulses.</summary>
        /// <value>A read-only view updated only when pending input is applied.</value>
        public IReadOnlyList<RopeCut> Cuts => cuts.AsReadOnly();
        /// <summary>Gets accepted tick-indexed taps that scene objects consumed.</summary>
        public IReadOnlyList<SceneTap> Taps => taps.AsReadOnly();

        /// <summary>Begins a primary-pointer drag only inside the fitted canvas.</summary>
        /// <param name="screen">Pointer position in output pixels.</param>
        /// <param name="width">Current output pixel width.</param>
        /// <param name="height">Current output pixel height.</param>
        public void Begin(Vector2 screen, int width, int height)
        {
            previous = null;
            Vector2 logical = Viewport.Fit(width, height, canvasSize.X, canvasSize.Y).ToLogical(screen);
            if (float.IsFinite(logical.X) && float.IsFinite(logical.Y) && logical.X >= 0 && logical.Y >= 0 && logical.X <= canvasSize.X && logical.Y <= canvasSize.Y)
            {
                previous = logical;
                dimensions = (width, height);
                // A press is a scene tap before it becomes a swipe.
                if (pending.Count < 1024)
                {
                    pending.Enqueue((logical, logical, true));
                }
            }
        }

        /// <summary>Queues a held-pointer segment without changing physics during event processing.</summary>
        /// <param name="screen">Current pointer position in output pixels.</param>
        /// <param name="width">Current output pixel width.</param>
        /// <param name="height">Current output pixel height.</param>
        public void Move(Vector2 screen, int width, int height)
        {
            if (previous is not Vector2 start)
            {
                return;
            }

            if (dimensions != (width, height) || pending.Count >= 1024)
            {
                Cancel();
                return;
            }
            Vector2 end = Viewport.Fit(width, height, canvasSize.X, canvasSize.Y).ToLogical(screen);
            if (!float.IsFinite(end.X) || !float.IsFinite(end.Y) || MathF.Abs(end.X) > 1000000 || MathF.Abs(end.Y) > 1000000)
            {
                Cancel();
                return;
            }
            pending.Enqueue((start, end, false));
            previous = end;
        }

        /// <summary>Ends the drag while retaining completed segments for the next fixed tick.</summary>
        public void End()
        {
            previous = null;
        }

        /// <summary>Cancels held and queued input on focus loss, resize or queue overflow.</summary>
        public void Cancel()
        {
            previous = null;
            pending.Clear();
        }

        /// <summary>Applies queued segments in event order before the current fixed tick.</summary>
        public void ApplyPending()
        {
            while (pending.TryDequeue(out (Vector2 Start, Vector2 End, bool Tap) item))
            {
                if (item.Tap)
                {
                    if (simulation.Tap(item.Start))
                    {
                        taps.Add(new(simulation.Tick, item.Start));
                    }
                    continue;
                }
                cuts.AddRange(simulation.Swipe(item.Start, item.End));
            }
        }
    }
}
