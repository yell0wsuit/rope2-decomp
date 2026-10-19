using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Fixed-tick timing and dismissal for one nonphysical tap or swipe hint, the source's <c>TutorialFinger</c>.</summary>
    /// <param name="position">Authored logical-space hint position.</param>
    /// <param name="angle">Authored clockwise swipe angle in degrees; taps ignore it.</param>
    /// <param name="delay">Seconds before the hint appears.</param>
    /// <param name="swipe">Whether this is a swipe hint (<c>tutorial_swipe</c>) rather than a tap hint (<c>tutorial_tap</c>).</param>
    public sealed class TutorialFinger(Vector2 position, float angle, float delay, bool swipe)
    {
        private readonly float wait = float.IsFinite(delay) && delay >= 0 && delay <= 36000 ? delay : throw new ArgumentOutOfRangeException(nameof(delay));
        private float elapsed;
        private float? hideElapsed;
        private bool removed;
        /// <summary>Gets the authored logical-space hint position.</summary>
        public Vector2 Position { get; } = position;
        /// <summary>Gets whether the hint shows a swipe; otherwise it shows a tap.</summary>
        public bool Swipe { get; } = swipe;
        /// <summary>Gets the authored clockwise angle in degrees.</summary>
        public float Angle { get; } = angle;
        /// <summary>Gets the container fade opacity.</summary>
        public float Opacity => removed || elapsed <= wait ? 0 : hideElapsed.HasValue ? Math.Clamp(1 - (hideElapsed.Value / .4f), 0, 1) : 1;
        /// <summary>Gets elapsed active animation seconds after the authored delay.</summary>
        public double AnimationSeconds => Math.Max(0, elapsed - wait);
        /// <summary>Advances one fixed game tick.</summary>
        public void Step()
        {
            if (removed)
            {
                return;
            }
            elapsed += PhysicsWorld.TimeStep;
            if (hideElapsed.HasValue)
            {
                hideElapsed += PhysicsWorld.TimeStep;
                removed = hideElapsed >= .4f;
            }
        }
        /// <summary>Applies the source early-removal or active-fade touch behavior.</summary>
        public void TouchDown()
        {
            if (elapsed < wait)
            {
                removed = true;
            }
            else
            {
                hideElapsed ??= 0;
            }
        }
    }
}
