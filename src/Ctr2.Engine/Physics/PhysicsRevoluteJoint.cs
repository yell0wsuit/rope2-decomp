// Altered C# revolute subset of Box2D 2.2.1. See PHYSICS.md for scope.
/*
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/
using System;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native angular limit state from the most recent velocity initialization.</summary>
    public enum PhysicsJointLimitState
    {
        /// <summary>No active angular limit.</summary>
        Inactive,
        /// <summary>At or below the lower angle.</summary>
        AtLower,
        /// <summary>At or above the upper angle.</summary>
        AtUpper,
        /// <summary>Limits are closer than twice angular slop.</summary>
        Equal
    }
    /// <summary>Local-anchor revolute joint definition in meters and radians.</summary>
    /// <param name="LocalAnchorA">Anchor relative to body A's origin.</param>
    /// <param name="LocalAnchorB">Anchor relative to body B's origin.</param>
    /// <param name="ReferenceAngle">Reference body-B angle minus body-A angle.</param>
    /// <param name="EnableLimit">Whether angular limits are enabled.</param>
    /// <param name="LowerAngle">Lower relative angle in radians.</param>
    /// <param name="UpperAngle">Upper relative angle in radians.</param>
    /// <param name="EnableMotor">Whether the angular motor is enabled.</param>
    /// <param name="MotorSpeed">Desired relative angular speed in radians per second.</param>
    /// <param name="MaxMotorTorque">Nonnegative maximum motor torque in newton meters.</param>
    /// <param name="CollideConnected">Whether connected bodies can collide.</param>
    public readonly record struct PhysicsRevoluteDefinition(Vector2 LocalAnchorA, Vector2 LocalAnchorB, float ReferenceAngle,
        bool EnableLimit = false, float LowerAngle = 0, float UpperAngle = 0, bool EnableMotor = false,
        float MotorSpeed = 0, float MaxMotorTorque = 0, bool CollideConnected = false);

    /// <summary>A native-style point hinge with optional angular limits and motor.</summary>
    public sealed class PhysicsRevoluteJoint : PhysicsJoint
    {
        /// <summary>Anchor relative to body A's origin.</summary>
        public Vector2 LocalAnchorA
        {
            get;
        }
        /// <summary>Anchor relative to body B's origin.</summary>
        public Vector2 LocalAnchorB
        {
            get;
        }
        /// <summary>Initial/reference relative angle in radians.</summary>
        public float ReferenceAngle
        {
            get;
        }
        /// <summary>Current relative angle after subtracting the reference.</summary>
        public float Angle => B.Angle - A.Angle - ReferenceAngle;
        /// <summary>Current relative angular speed.</summary>
        public float Speed => B.AngularVelocity - A.AngularVelocity;
        /// <summary>Whether angular limits are enabled.</summary>
        public bool LimitEnabled
        {
            get => limitEnabled;
            set
            {
                if (value != limitEnabled)
                {
                    limitEnabled = value;
                    impulse.Z = 0;
                }
            }
        }
        private bool limitEnabled;
        /// <summary>Lower angle in radians.</summary>
        public float LowerAngle
        {
            get; private set;
        }
        /// <summary>Upper angle in radians.</summary>
        public float UpperAngle
        {
            get; private set;
        }
        /// <summary>Whether the angular motor is enabled.</summary>
        public bool MotorEnabled
        {
            get; set;
        }
        /// <summary>Desired relative angular speed in radians per second.</summary>
        public float MotorSpeed
        {
            get => motorSpeed; set => motorSpeed = Parameter(value);
        }
        private float motorSpeed;
        /// <summary>Nonnegative torque bound in newton meters.</summary>
        public float MaxMotorTorque
        {
            get => maxMotorTorque;
            set
            {
                Parameter(value);
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));
                maxMotorTorque = value;
            }
        }
        private float maxMotorTorque;
        /// <summary>Motor torque at the fixed timestep.</summary>
        public float MotorTorque => (1 / PhysicsWorld.TimeStep) * motorImpulse;
        /// <summary>Limit state from the last initialization.</summary>
        public PhysicsJointLimitState LimitState
        {
            get; private set;
        }
        /// <inheritdoc/>
        public override Vector2 AnchorA => A.Origin + PhysicsMath.Rotate(LocalAnchorA, A.Angle);
        /// <inheritdoc/>
        public override Vector2 AnchorB => B.Origin + PhysicsMath.Rotate(LocalAnchorB, B.Angle);
        /// <inheritdoc/>
        public override Vector2 ReactionForce => (1 / PhysicsWorld.TimeStep) * new Vector2(impulse.X, impulse.Y);
        /// <inheritdoc/>
        public override float ReactionTorque => (1 / PhysicsWorld.TimeStep) * impulse.Z;
        /// <summary>Changes ordered lower and upper angles.</summary>
        /// <param name="lower">Lower angle in radians.</param>
        /// <param name="upper">Upper angle in radians.</param>
        public void SetLimits(float lower, float upper)
        {
            Parameter(lower);
            Parameter(upper);
            if (lower > upper)
                throw new ArgumentOutOfRangeException(nameof(lower));
            if (lower != LowerAngle || upper != UpperAngle)
            {
                LowerAngle = lower;
                UpperAngle = upper;
                impulse.Z = 0;
            }
        }
        private static float Parameter(float value) => float.IsFinite(value) && MathF.Abs(value) <= 1000000 ? value :
            throw new ArgumentOutOfRangeException(nameof(value));
        internal PhysicsRevoluteJoint(PhysicsBody a, PhysicsBody b, PhysicsRevoluteDefinition definition) : base(a, b, definition.CollideConnected)
        {
            LocalAnchorA = definition.LocalAnchorA;
            LocalAnchorB = definition.LocalAnchorB;
            ReferenceAngle = definition.ReferenceAngle;
            LimitEnabled = definition.EnableLimit;
            SetLimits(definition.LowerAngle, definition.UpperAngle);
            MotorEnabled = definition.EnableMotor;
            MotorSpeed = definition.MotorSpeed;
            MaxMotorTorque = definition.MaxMotorTorque;
        }
        private Vector3 impulse;
        private float motorImpulse;
        private Vector2 armA, armB;
        private float inverseMassA, inverseMassB, inverseInertiaA, inverseInertiaB, motorMass;
        private Matrix33 mass;
        private const float AngularSlop = (1f / 180) * MathF.PI;
        private const float MaxAngularCorrection = (8f / 180) * MathF.PI;

        internal override void Initialize(bool firstStep)
        {
            inverseMassA = A.InverseMass;
            inverseMassB = B.InverseMass;
            inverseInertiaA = A.InverseInertia;
            inverseInertiaB = B.InverseInertia;
            armA = PhysicsMath.Rotate(LocalAnchorA - A.LocalCenter, A.Angle);
            armB = PhysicsMath.Rotate(LocalAnchorB - B.LocalCenter, B.Angle);
            float mA = inverseMassA, mB = inverseMassB, iA = inverseInertiaA, iB = inverseInertiaB;
            bool fixedRotation = iA + iB == 0;
            mass.Ex = new(mA + mB + armA.Y * armA.Y * iA + armB.Y * armB.Y * iB,
                -armA.Y * armA.X * iA - armB.Y * armB.X * iB, -armA.Y * iA - armB.Y * iB);
            mass.Ey = new(mass.Ex.Y, mA + mB + armA.X * armA.X * iA + armB.X * armB.X * iB,
                armA.X * iA + armB.X * iB);
            mass.Ez = new(mass.Ex.Z, mass.Ey.Z, iA + iB);
            motorMass = iA + iB;
            if (motorMass > 0)
                motorMass = 1 / motorMass;
            if (!MotorEnabled || fixedRotation)
                motorImpulse = 0;
            if (LimitEnabled && !fixedRotation)
            {
                float angle = B.Angle - A.Angle - ReferenceAngle;
                if (MathF.Abs(UpperAngle - LowerAngle) < 2 * AngularSlop)
                    LimitState = PhysicsJointLimitState.Equal;
                else if (angle <= LowerAngle)
                {
                    if (LimitState != PhysicsJointLimitState.AtLower)
                        impulse.Z = 0;
                    LimitState = PhysicsJointLimitState.AtLower;
                }
                else if (angle >= UpperAngle)
                {
                    if (LimitState != PhysicsJointLimitState.AtUpper)
                        impulse.Z = 0;
                    LimitState = PhysicsJointLimitState.AtUpper;
                }
                else
                {
                    LimitState = PhysicsJointLimitState.Inactive;
                    impulse.Z = 0;
                }
            }
            else
                LimitState = PhysicsJointLimitState.Inactive;
            if (firstStep)
            {
                impulse = Vector3.Zero;
                motorImpulse = 0;
            }
            Vector2 p = new(impulse.X, impulse.Y);
            A.Velocity -= mA * p;
            A.AngularVelocity -= iA * (Cross(armA, p) + motorImpulse + impulse.Z);
            B.Velocity += mB * p;
            B.AngularVelocity += iB * (Cross(armB, p) + motorImpulse + impulse.Z);
        }

        internal override void SolveVelocity()
        {
            Vector2 vA = A.Velocity, vB = B.Velocity;
            float wA = A.AngularVelocity, wB = B.AngularVelocity;
            float mA = inverseMassA, mB = inverseMassB, iA = inverseInertiaA, iB = inverseInertiaB;
            bool fixedRotation = iA + iB == 0;
            if (MotorEnabled && LimitState != PhysicsJointLimitState.Equal && !fixedRotation)
            {
                float speed = wB - wA - MotorSpeed;
                float change = -motorMass * speed;
                float old = motorImpulse;
                float maximum = PhysicsWorld.TimeStep * MaxMotorTorque;
                motorImpulse = Math.Clamp(motorImpulse + change, -maximum, maximum);
                change = motorImpulse - old;
                wA -= iA * change;
                wB += iB * change;
            }
            if (LimitEnabled && LimitState != PhysicsJointLimitState.Inactive && !fixedRotation)
            {
                Vector2 speed = vB + Cross(wB, armB) - vA - Cross(wA, armA);
                Vector3 change = -mass.Solve33(new(speed.X, speed.Y, wB - wA));
                if (LimitState == PhysicsJointLimitState.Equal)
                    impulse += change;
                else
                {
                    float next = impulse.Z + change.Z;
                    if ((LimitState == PhysicsJointLimitState.AtLower && next < 0) ||
                        (LimitState == PhysicsJointLimitState.AtUpper && next > 0))
                    {
                        Vector2 rhs = -speed + impulse.Z * new Vector2(mass.Ez.X, mass.Ez.Y);
                        Vector2 reduced = mass.Solve22(rhs);
                        change.X = reduced.X;
                        change.Y = reduced.Y;
                        change.Z = -impulse.Z;
                        impulse.X += reduced.X;
                        impulse.Y += reduced.Y;
                        impulse.Z = 0;
                    }
                    else
                        impulse += change;
                }
                Vector2 p = new(change.X, change.Y);
                vA -= mA * p;
                wA -= iA * (Cross(armA, p) + change.Z);
                vB += mB * p;
                wB += iB * (Cross(armB, p) + change.Z);
            }
            else
            {
                Vector2 speed = vB + Cross(wB, armB) - vA - Cross(wA, armA);
                Vector2 change = mass.Solve22(-speed);
                impulse.X += change.X;
                impulse.Y += change.Y;
                vA -= mA * change;
                wA -= iA * Cross(armA, change);
                vB += mB * change;
                wB += iB * Cross(armB, change);
            }
            A.Velocity = vA;
            A.AngularVelocity = wA;
            B.Velocity = vB;
            B.AngularVelocity = wB;
        }

        internal override bool SolvePosition()
        {
            Vector2 cA = A.Position, cB = B.Position;
            float aA = A.Angle, aB = B.Angle;
            float mA = inverseMassA, mB = inverseMassB, iA = inverseInertiaA, iB = inverseInertiaB;
            float angularError = 0;
            if (LimitEnabled && LimitState != PhysicsJointLimitState.Inactive && iA + iB != 0)
            {
                float angle = aB - aA - ReferenceAngle;
                float change = 0;
                if (LimitState == PhysicsJointLimitState.Equal)
                {
                    float c = Math.Clamp(angle - LowerAngle, -MaxAngularCorrection, MaxAngularCorrection);
                    change = -motorMass * c;
                    angularError = MathF.Abs(c);
                }
                else if (LimitState == PhysicsJointLimitState.AtLower)
                {
                    float c = angle - LowerAngle;
                    angularError = -c;
                    c = Math.Clamp(c + AngularSlop, -MaxAngularCorrection, 0);
                    change = -motorMass * c;
                }
                else if (LimitState == PhysicsJointLimitState.AtUpper)
                {
                    float c = angle - UpperAngle;
                    angularError = c;
                    c = Math.Clamp(c - AngularSlop, 0, MaxAngularCorrection);
                    change = -motorMass * c;
                }
                aA -= iA * change;
                aB += iB * change;
            }
            Vector2 rA = PhysicsMath.Rotate(LocalAnchorA - A.LocalCenter, aA);
            Vector2 rB = PhysicsMath.Rotate(LocalAnchorB - B.LocalCenter, aB);
            Vector2 error = cB + rB - cA - rA;
            float positionError = MathF.Sqrt(error.X * error.X + error.Y * error.Y);
            // Preserve native position-matrix multiplication order, which differs from initialization.
            float k11 = mA + mB + iA * rA.Y * rA.Y + iB * rB.Y * rB.Y;
            float k12 = -iA * rA.X * rA.Y - iB * rB.X * rB.Y;
            float k22 = mA + mB + iA * rA.X * rA.X + iB * rB.X * rB.X;
            Vector2 p = -Solve22(k11, k12, k12, k22, error);
            cA -= mA * p;
            aA -= iA * Cross(rA, p);
            cB += mB * p;
            aB += iB * Cross(rB, p);
            A.Position = cA;
            A.Angle = aA;
            B.Position = cB;
            B.Angle = aB;
            return positionError <= PhysicsWorld.LinearSlop && angularError <= AngularSlop;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
        private static Vector2 Cross(float scalar, Vector2 value) => new(-scalar * value.Y, scalar * value.X);
        private static Vector2 Solve22(float a11, float a12, float a21, float a22, Vector2 b)
        {
            float determinant = a11 * a22 - a12 * a21;
            if (determinant != 0)
                determinant = 1 / determinant;
            return new(determinant * (a22 * b.X - a12 * b.Y), determinant * (a11 * b.Y - a21 * b.X));
        }
        private struct Matrix33
        {
            internal Vector3 Ex, Ey, Ez;
            internal readonly Vector2 Solve22(Vector2 b) => PhysicsRevoluteJoint.Solve22(Ex.X, Ey.X, Ex.Y, Ey.Y, b);
            internal readonly Vector3 Solve33(Vector3 b)
            {
                float determinant = Dot(Ex, Cross(Ey, Ez));
                if (determinant != 0)
                    determinant = 1 / determinant;
                return new(determinant * Dot(b, Cross(Ey, Ez)), determinant * Dot(Ex, Cross(b, Ez)),
                    determinant * Dot(Ex, Cross(Ey, b)));
            }
            private static float Dot(Vector3 a, Vector3 b) => (a.X * b.X + a.Y * b.Y) + a.Z * b.Z;
            private static Vector3 Cross(Vector3 a, Vector3 b) => new(a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Adds a revolute joint with local anchors and explicit reference angle.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="definition">Native-style hinge definition.</param>
        /// <returns>The owned hinge.</returns>
        /// <exception cref="ArgumentException">Bodies, definition or joint budget are invalid.</exception>
        public PhysicsRevoluteJoint AddRevolute(PhysicsBody a, PhysicsBody b, PhysicsRevoluteDefinition definition)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            if (a == b || !bodies.Contains(a) || !bodies.Contains(b) || joints.Count >= 8192 ||
                !ValidAnchor(definition.LocalAnchorA) || !ValidAnchor(definition.LocalAnchorB) ||
                (!float.IsFinite(definition.ReferenceAngle) || MathF.Abs(definition.ReferenceAngle) > 1000000) || !float.IsFinite(definition.LowerAngle) || !float.IsFinite(definition.UpperAngle) ||
                definition.LowerAngle > definition.UpperAngle || !float.IsFinite(definition.MotorSpeed) ||
                !float.IsFinite(definition.MaxMotorTorque) || definition.MaxMotorTorque < 0)
                throw new ArgumentException("Invalid revolute definition or owned bodies.", nameof(definition));
            PhysicsRevoluteJoint joint = new(a, b, definition);
            joints.Add(joint);
            if (!joint.CollideConnected)
                ChangeCollisionBlock(a, b, 1);
            return joint;
        }
        /// <summary>Adds a free hinge using native world-anchor initialization.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="worldAnchor">Shared world anchor in meters.</param>
        /// <returns>The owned hinge with the current relative reference angle.</returns>
        public PhysicsRevoluteJoint AddRevolute(PhysicsBody a, PhysicsBody b, Vector2 worldAnchor)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            if (!ValidAnchor(worldAnchor))
                throw new ArgumentOutOfRangeException(nameof(worldAnchor));
            return AddRevolute(a, b, new PhysicsRevoluteDefinition(PhysicsMath.InverseRotate(worldAnchor - a.Origin, a.Angle),
                PhysicsMath.InverseRotate(worldAnchor - b.Origin, b.Angle), b.Angle - a.Angle));
        }
    }
}
