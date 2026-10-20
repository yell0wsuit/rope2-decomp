using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native <c>MovableBodyObject</c> path movement: a kinematic body follows its authored path by having its
    /// velocity set after each step, cycling around a closed path or easing back and forth along an open one.</summary>
    internal sealed class PathMover
    {
        private enum PathState
        {
            Stay,
            Cycle,
            PingPong
        }

        private enum SpeedState
        {
            Acceleration,
            Normal,
            Deceleration
        }

        private readonly SmoothPath? open;
        private readonly SmoothCyclePath? cycle;
        private readonly float editorSpeed;
        private readonly float decelerationDistance;
        private PathState state;
        private readonly PathState next;
        private SpeedState speedState;
        private float pathTime, pathPercent, accelerationTime;
        private int direction = 1;

        /// <summary>Reads the movable fields like the native initializer: <c>path</c>, <c>cyclePath</c>, <c>startOnContact</c>
        /// and <c>speed</c>, with the editor position doubled.</summary>
        /// <param name="fields">Merged design and authored attributes.</param>
        internal PathMover(IReadOnlyDictionary<string, string> fields)
        {
            string path = fields.GetValueOrDefault("path", "");
            if (path.Length == 0)
            {
                return;
            }
            float x = Float(fields, "x"), y = Float(fields, "y");
            Vector2 position = new(x + x, y + y);
            bool startOnContact = Bool(fields, "startOnContact");
            if (Bool(fields, "cyclePath"))
            {
                cycle = new(position, path, false, 10);
                (state, next) = startOnContact ? (PathState.Stay, PathState.Cycle) : (PathState.Cycle, PathState.Stay);
            }
            else
            {
                open = new(position, path);
                (state, next) = startOnContact ? (PathState.Stay, PathState.PingPong) : (PathState.PingPong, PathState.Stay);
                speedState = SpeedState.Acceleration;
            }
            float length = Length;
            editorSpeed = (float)(Float(fields, "speed") * 100.0 / length);
            for (int i = 0; i < 60; i++)
            {
                decelerationDistance += (float)(EaseOut((float)(i * .016667), editorSpeed, -editorSpeed, 1) * .016667) * length;
            }
        }

        private float Length => open?.MaxRealT ?? cycle!.MaxRealT;

        /// <summary>Starts a path that waits for contact, as the helicopter does when it begins moving a captured body.</summary>
        internal void Start()
        {
            if (state == PathState.Stay)
            {
                state = next;
            }
        }

        /// <summary>Native <c>MovableBodyObject::update</c>: sets the body's velocity for the next step.</summary>
        /// <param name="body">Moving body.</param>
        /// <param name="delta">Elapsed seconds.</param>
        internal void Update(PhysicsBody body, float delta)
        {
            if (state == PathState.Cycle)
            {
                pathTime += delta * editorSpeed;
                if (pathTime > 1)
                {
                    pathTime += -1f;
                }
                pathPercent = pathTime;
                Vector2 speed = cycle!.DerivativeAt(pathPercent) * editorSpeed * direction * cycle.MaxRealT;
                body.SetLinearVelocity(Meters(speed));
            }
            else if (state == PathState.PingPong)
            {
                float length = open!.MaxRealT, speed = editorSpeed;
                if (speedState == SpeedState.Acceleration)
                {
                    speed = EaseIn(accelerationTime, 0, editorSpeed, 1);
                    accelerationTime += delta;
                    if (accelerationTime > 1)
                    {
                        accelerationTime = 0;
                        speedState = SpeedState.Normal;
                    }
                }
                if (speedState == SpeedState.Normal)
                {
                    float distance = pathPercent * length;
                    if ((direction == 1 && length - distance < decelerationDistance) || (direction == -1 && distance < decelerationDistance))
                    {
                        accelerationTime = 0;
                        speedState = SpeedState.Deceleration;
                    }
                }
                if (speedState == SpeedState.Deceleration)
                {
                    speed = EaseOut(accelerationTime >= 1 ? 1 : accelerationTime, editorSpeed, -editorSpeed, 1);
                    accelerationTime += delta;
                    if (accelerationTime > 1)
                    {
                        accelerationTime = 0;
                    }
                }
                pathTime += delta * speed;
                if (pathTime > 1)
                {
                    pathTime += -1f;
                    speedState = SpeedState.Acceleration;
                    accelerationTime = 0;
                    direction = -direction;
                }
                pathPercent = direction == -1 ? 1 - pathTime : pathTime;
                Vector2 velocity = open.DerivativeAt(pathPercent) * speed * direction * length;
                // The source also steers back onto the spline point, in logical units per second.
                Vector2 current = new((float)(body.Origin.X * 200.0), (float)(body.Origin.Y * 200.0));
                Vector2 correction = open.Position + open.VectorAt(pathPercent) - current;
                body.SetLinearVelocity(Meters(velocity + correction));
            }
        }

        private static Vector2 Meters(Vector2 value)
        {
            return new((float)(value.X / 200.0), (float)(value.Y / 200.0));
        }

        private static float EaseIn(float t, float b, float c, float d)
        {
            return (c * (t / d) * (t / d)) + b;
        }

        private static float EaseOut(float t, float b, float c, float d)
        {
            return b - (c * (t / d) * (float)((t / d) + -2.0));
        }

        private static float Float(IReadOnlyDictionary<string, string> fields, string key)
        {
            return fields.TryGetValue(key, out string? text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0;
        }

        /// <summary>Like <c>ZString::boolValue</c> for the authored forms: <c>true</c> or a nonzero integer.</summary>
        private static bool Bool(IReadOnlyDictionary<string, string> fields, string key)
        {
            string text = fields.GetValueOrDefault(key, "");
            return text.Equals("true", StringComparison.OrdinalIgnoreCase) || (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value != 0);
        }
    }
}
