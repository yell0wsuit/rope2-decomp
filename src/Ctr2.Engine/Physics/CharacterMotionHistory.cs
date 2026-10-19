using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Bounded velocity history for the supported source falling and landing conditions.</summary>
    public sealed class CharacterMotionHistory
    {
        private readonly Vector2[] samples = new Vector2[59];
        private readonly float[] angular = new float[59];
        private int next;
        /// <summary>Gets the newest five angular samples averaged.</summary>
        public float ShortAngular
        {
            get; private set;
        }
        /// <summary>Gets angular samples 5 through 13 divided by 14, the source's 250 ms window.</summary>
        public float QuarterAngular
        {
            get; private set;
        }
        /// <summary>Gets older angular samples 29 through 58 divided by 59, matching the source.</summary>
        public float LongAngular
        {
            get; private set;
        }
        /// <summary>Gets the newest five-sample velocity average.</summary>
        public Vector2 Short
        {
            get; private set;
        }
        /// <summary>Gets the newest 14-sample velocity average, the source's 250 ms window.</summary>
        public Vector2 Quarter
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
        /// <param name="angularVelocity">Clockwise radians per second.</param>
        public void Sample(Vector2 velocity, float angularVelocity = 0)
        {
            if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(angularVelocity))
            {
                throw new ArgumentOutOfRangeException(nameof(velocity));
            }
            samples[next] = velocity;
            angular[next] = angularVelocity;
            ShortAngular = QuarterAngular = LongAngular = 0;
            next = (next + 1) % samples.Length;
            Vector2 sum = Vector2.Zero;
            for (int i = 0; i < samples.Length; i++)
            {
                float angularSample = angular[(next + samples.Length - 1 - i) % samples.Length];
                if (i < 5)
                {
                    ShortAngular += angularSample;
                }
                else if (i < 14)
                {
                    QuarterAngular += angularSample;
                }

                if (i >= 29)
                {
                    LongAngular += angularSample;
                }

                sum += samples[(next + samples.Length - 1 - i) % samples.Length];
                // The source scales each window's sum by a rounded reciprocal.
                if (i == 4)
                {
                    Short = sum * .2f;
                }
                if (i == 13)
                {
                    Quarter = sum * .071429f;
                }
                if (i == 28)
                {
                    Medium = sum * .034483f;
                }
            }
            Long = sum * .016949f;
            ShortAngular /= 5;
            QuarterAngular /= 14;
            LongAngular /= 59;
        }
    }
}
