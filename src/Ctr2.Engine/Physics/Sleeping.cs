// Altered C# sleep/wake subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        /// <summary>Gets whether the body is awake; sleeping bodies are skipped until something wakes them.</summary>
        public bool IsAwake
        {
            get; private set;
        } = true;

        /// <summary>Gets or sets whether this body may fall asleep automatically; disabling wakes it, as native does.</summary>
        public bool SleepingAllowed
        {
            get;
            set
            {
                field = value;
                if (!value)
                {
                    SetAwake(true);
                }
            }
        } = true;

        /// <summary>Accumulated time below the sleep tolerances.</summary>
        internal float SleepTime;

        /// <summary>Native <c>SetAwake</c>: waking resets the sleep timer; sleeping also clears velocities and forces.</summary>
        /// <param name="awake">Whether the body should be awake.</param>
        public void SetAwake(bool awake)
        {
            if (awake)
            {
                if (!IsAwake)
                {
                    IsAwake = true;
                    SleepTime = 0;
                }
                return;
            }
            IsAwake = false;
            SleepTime = 0;
            Velocity = Vector2.Zero;
            AngularVelocity = 0;
            Force = Vector2.Zero;
            Torque = 0;
        }

        /// <summary>Whether native collision and TOI treat the body as moving: awake and not static.</summary>
        internal bool IsActiveMover => IsAwake && Type != PhysicsBodyType.Static;
    }

    public sealed partial class PhysicsWorld
    {
        private const float TimeToSleep = .5f;
        private const float LinearSleepTolerance = .01f;
        private const float AngularSleepTolerance = 2f / 180 * MathF.PI;

        /// <summary>Gets or sets whether resting islands fall asleep. False by default, as every game scene except the intro sets.</summary>
        /// <remarks>Disabling it wakes every body, as native <c>SetAllowSleeping(false)</c> does.</remarks>
        public bool AllowSleeping
        {
            get;
            set
            {
                if (value == field)
                {
                    return;
                }
                field = value;
                if (!value)
                {
                    foreach (PhysicsBody body in bodies)
                    {
                        body.SetAwake(true);
                    }
                }
            }
        }

        /// <summary>Native island sleep check after the island's position solve and report.</summary>
        private void SleepIsland(List<PhysicsBody> islandBodies, bool positionSolved)
        {
            if (!AllowSleeping)
            {
                return;
            }
            float minimum = float.MaxValue;
            const float linear = LinearSleepTolerance * LinearSleepTolerance, angular = AngularSleepTolerance * AngularSleepTolerance;
            foreach (PhysicsBody body in islandBodies)
            {
                if (body.Type == PhysicsBodyType.Static)
                {
                    continue;
                }
                if (!body.SleepingAllowed || body.AngularVelocity * body.AngularVelocity > angular || Vector2.Dot(body.Velocity, body.Velocity) > linear)
                {
                    body.SleepTime = 0;
                    minimum = 0;
                }
                else
                {
                    body.SleepTime += TimeStep;
                    minimum = MathF.Min(minimum, body.SleepTime);
                }
            }
            if (minimum >= TimeToSleep && positionSolved)
            {
                foreach (PhysicsBody body in islandBodies)
                {
                    body.SetAwake(false);
                }
            }
        }
    }
}
