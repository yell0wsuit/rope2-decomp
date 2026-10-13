using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Bounded velocity history for the supported source falling and landing conditions.</summary>
    public sealed class CharacterMotionHistory
    {
        private readonly Vector2[] samples = new Vector2[59];
        private int next;
        /// <summary>Gets the newest five-sample velocity average.</summary>
        public Vector2 Short
        {
            get; private set;
        }
        /// <summary>Gets the newest 29-sample velocity average.</summary>
        public Vector2 Medium
        {
            get; private set;
        }
        /// <summary>Gets the newest 59-sample velocity average.</summary>
        public Vector2 Long
        {
            get; private set;
        }
        /// <summary>Gets whether recent velocity meets the source fall threshold.</summary>
        public bool IsFalling => (Short.Y > 2 || Short.Y < -3) && Vector2.Dot(Short, Medium) > 0;
        /// <summary>Samples one completed fixed tick's velocity.</summary>
        /// <param name="velocity">Velocity in meters per second.</param>
        public void Sample(Vector2 velocity)
        {
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(velocity));
            }
            samples[next] = velocity;
            next = (next + 1) % samples.Length;
            Vector2 sum = Vector2.Zero;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[(next + samples.Length - 1 - i) % samples.Length];
                if (i == 4)
                {
                    Short = sum / 5;
                }
                if (i == 28)
                {
                    Medium = sum / 29;
                }
            }
            Long = sum / 59;
        }
    }
}
