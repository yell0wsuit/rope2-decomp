// Altered C# prismatic subset of Box2D 2.2.1. See PHYSICS.md for scope.
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
    /// <summary>Local-anchor prismatic joint definition in meters and radians.</summary>
    /// <param name="LocalAnchorA">Anchor relative to body A's origin.</param>
    /// <param name="LocalAnchorB">Anchor relative to body B's origin.</param>
    /// <param name="LocalAxisA">Translation axis in body A's frame; normalized on creation.</param>
    /// <param name="ReferenceAngle">Reference body-B angle minus body-A angle.</param>
    /// <param name="EnableLimit">Whether translation limits are enabled.</param>
    /// <param name="LowerTranslation">Lower translation in meters.</param>
    /// <param name="UpperTranslation">Upper translation in meters.</param>
    /// <param name="EnableMotor">Whether the linear motor is enabled.</param>
    /// <param name="MaxMotorForce">Nonnegative maximum motor force in newtons.</param>
    /// <param name="MotorSpeed">Desired translation speed in meters per second.</param>
    /// <param name="CollideConnected">Whether connected bodies can collide.</param>
    public readonly record struct PhysicsPrismaticDefinition(Vector2 LocalAnchorA, Vector2 LocalAnchorB, Vector2 LocalAxisA, float ReferenceAngle,
        bool EnableLimit = false, float LowerTranslation = 0, float UpperTranslation = 0, bool EnableMotor = false, float MaxMotorForce = 0,
        float MotorSpeed = 0, bool CollideConnected = false);

    /// <summary>A native-style slider that locks relative rotation, with optional translation limits and motor.</summary>
    public sealed class PhysicsPrismaticJoint : PhysicsJoint
    {
        private readonly Vector2 localYAxis;
        private Vector3 impulse;
        private float motorImpulse, motorMass, a1, a2, s1, s2;
        private float mA, mB, iA, iB;
        private Vector2 axis, perp;
        private Vector3 kx, ky, kz;
        private bool limitEnabled;
        private const float LinearSlop = PhysicsWorld.LinearSlop;
        private const float MaxLinearCorrection = .2f;
        private const float AngularSlop = 1f / 180 * MathF.PI;

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
        /// <summary>Normalized translation axis in body A's frame.</summary>
        public Vector2 LocalAxisA
        {
            get;
        }
        /// <summary>Reference relative angle in radians.</summary>
        public float ReferenceAngle
        {
            get;
        }
        /// <summary>Whether translation limits are enabled; changing it clears the cached limit impulse.</summary>
        public bool LimitEnabled
        {
            get => limitEnabled;
            set
            {
                if (value != limitEnabled)
                {
                    Wake();
                    limitEnabled = value;
                    impulse.Z = 0;
                }
            }
        }
        /// <summary>Lower translation in meters.</summary>
        public float LowerTranslation
        {
            get; private set;
        }
        /// <summary>Upper translation in meters.</summary>
        public float UpperTranslation
        {
            get; private set;
        }
        /// <summary>Whether the motor is enabled.</summary>
        public bool MotorEnabled
        {
            get;
            set
            {
                Wake();
                field = value;
            }
        }
        /// <summary>Desired translation speed in meters per second.</summary>
        public float MotorSpeed
        {
            get;
            set
            {
                _ = Parameter(value);
                Wake();
                field = value;
            }
        }
        /// <summary>Nonnegative maximum motor force in newtons.</summary>
        public float MaxMotorForce
        {
            get;
            set
            {
                if (Parameter(value) < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                Wake();
                field = value;
            }
        }
        /// <summary>Limit state from the last initialization.</summary>
        public PhysicsJointLimitState LimitState
        {
            get; private set;
        }
        /// <summary>Motor force at the fixed timestep.</summary>
        public float MotorForce => 1 / PhysicsWorld.TimeStep * motorImpulse;
        /// <inheritdoc/>
        public override Vector2 AnchorA => A.GetWorldPoint(LocalAnchorA);
        /// <inheritdoc/>
        public override Vector2 AnchorB => B.GetWorldPoint(LocalAnchorB);
        /// <inheritdoc/>
        public override Vector2 ReactionForce => 1 / PhysicsWorld.TimeStep * ((impulse.X * perp) + ((motorImpulse + impulse.Z) * axis));
        /// <inheritdoc/>
        public override float ReactionTorque => 1 / PhysicsWorld.TimeStep * impulse.Y;
        /// <summary>Native <c>GetJointTranslation</c> along the world axis.</summary>
        public float Translation => Vector2.Dot(B.GetWorldPoint(LocalAnchorB) - A.GetWorldPoint(LocalAnchorA), A.GetWorldVector(LocalAxisA));
        /// <summary>Native <c>GetJointSpeed</c>, including the axis rotation term.</summary>
        public float Speed
        {
            get
            {
                Vector2 rA = PhysicsMath.Rotate(LocalAnchorA - A.LocalCenter, A.Angle), rB = PhysicsMath.Rotate(LocalAnchorB - B.LocalCenter, B.Angle);
                Vector2 d = B.Position + rB - (A.Position + rA);
                Vector2 worldAxis = PhysicsMath.Rotate(LocalAxisA, A.Angle);
                return Vector2.Dot(d, Cross(A.AngularVelocity, worldAxis)) +
                    Vector2.Dot(worldAxis, B.Velocity + Cross(B.AngularVelocity, rB) - A.Velocity - Cross(A.AngularVelocity, rA));
            }
        }

        internal PhysicsPrismaticJoint(PhysicsBody a, PhysicsBody b, PhysicsPrismaticDefinition definition) : base(a, b, definition.CollideConnected)
        {
            LocalAnchorA = definition.LocalAnchorA;
            LocalAnchorB = definition.LocalAnchorB;
            LocalAxisA = PhysicsMath.Normalize(definition.LocalAxisA);
            localYAxis = new(-LocalAxisA.Y, LocalAxisA.X);
            ReferenceAngle = definition.ReferenceAngle;
            LowerTranslation = definition.LowerTranslation;
            UpperTranslation = definition.UpperTranslation;
            MaxMotorForce = definition.MaxMotorForce;
            MotorSpeed = definition.MotorSpeed;
            limitEnabled = definition.EnableLimit;
            MotorEnabled = definition.EnableMotor;
            Constructed = true;
        }

        /// <summary>Changes ordered translation limits, clearing the cached limit impulse only when they change.</summary>
        /// <param name="lower">Lower translation in meters.</param>
        /// <param name="upper">Upper translation in meters.</param>
        public void SetLimits(float lower, float upper)
        {
            _ = Parameter(lower);
            _ = Parameter(upper);
            if (lower > upper)
            {
                throw new ArgumentOutOfRangeException(nameof(lower));
            }
            if (lower != LowerTranslation || upper != UpperTranslation)
            {
                Wake();
                LowerTranslation = lower;
                UpperTranslation = upper;
                impulse.Z = 0;
            }
        }

        internal override void Initialize(bool firstStep)
        {
            (mA, mB, iA, iB) = (A.InverseMass, B.InverseMass, A.InverseInertia, B.InverseInertia);
            Vector2 cA = A.Position, cB = B.Position;
            Vector2 rA = PhysicsMath.Rotate(LocalAnchorA - A.LocalCenter, A.Angle), rB = PhysicsMath.Rotate(LocalAnchorB - B.LocalCenter, B.Angle);
            Vector2 d = cB - cA + rB - rA;
            axis = PhysicsMath.Rotate(LocalAxisA, A.Angle);
            a1 = Cross(d + rA, axis);
            a2 = Cross(rB, axis);
            motorMass = mA + mB + (iA * a1 * a1) + (iB * a2 * a2);
            if (motorMass > 0)
            {
                motorMass = 1 / motorMass;
            }
            perp = PhysicsMath.Rotate(localYAxis, A.Angle);
            s1 = Cross(d + rA, perp);
            s2 = Cross(rB, perp);
            (kx, ky, kz) = Matrix(s1, s2, a1, a2);
            if (limitEnabled)
            {
                float translation = Vector2.Dot(axis, d);
                if (MathF.Abs(UpperTranslation - LowerTranslation) < 2 * LinearSlop)
                {
                    LimitState = PhysicsJointLimitState.Equal;
                }
                else if (translation <= LowerTranslation)
                {
                    if (LimitState != PhysicsJointLimitState.AtLower)
                    {
                        LimitState = PhysicsJointLimitState.AtLower;
                        impulse.Z = 0;
                    }
                }
                else if (translation >= UpperTranslation)
                {
                    if (LimitState != PhysicsJointLimitState.AtUpper)
                    {
                        LimitState = PhysicsJointLimitState.AtUpper;
                        impulse.Z = 0;
                    }
                }
                else
                {
                    LimitState = PhysicsJointLimitState.Inactive;
                    impulse.Z = 0;
                }
            }
            else
            {
                LimitState = PhysicsJointLimitState.Inactive;
                impulse.Z = 0;
            }
            if (!MotorEnabled)
            {
                motorImpulse = 0;
            }
            if (firstStep)
            {
                impulse = Vector3.Zero;
                motorImpulse = 0;
            }
            Vector2 p = (impulse.X * perp) + ((motorImpulse + impulse.Z) * axis);
            float la = (impulse.X * s1) + impulse.Y + ((motorImpulse + impulse.Z) * a1);
            float lb = (impulse.X * s2) + impulse.Y + ((motorImpulse + impulse.Z) * a2);
            Apply(p, la, lb);
        }

        internal override void SolveVelocity()
        {
            if (MotorEnabled && LimitState != PhysicsJointLimitState.Equal)
            {
                float cdot = Vector2.Dot(axis, B.Velocity - A.Velocity) + (a2 * B.AngularVelocity) - (a1 * A.AngularVelocity);
                float change = motorMass * (MotorSpeed - cdot);
                float old = motorImpulse;
                float maximum = PhysicsWorld.TimeStep * MaxMotorForce;
                motorImpulse = Math.Clamp(motorImpulse + change, -maximum, maximum);
                change = motorImpulse - old;
                Apply(change * axis, change * a1, change * a2);
            }
            Vector2 cdot1 = new(Vector2.Dot(perp, B.Velocity - A.Velocity) + (s2 * B.AngularVelocity) - (s1 * A.AngularVelocity),
                B.AngularVelocity - A.AngularVelocity);
            if (limitEnabled && LimitState != PhysicsJointLimitState.Inactive)
            {
                float cdot2 = Vector2.Dot(axis, B.Velocity - A.Velocity) + (a2 * B.AngularVelocity) - (a1 * A.AngularVelocity);
                Vector3 f1 = impulse;
                impulse += Solve33(kx, ky, kz, -new Vector3(cdot1.X, cdot1.Y, cdot2));
                if (LimitState == PhysicsJointLimitState.AtLower)
                {
                    impulse.Z = MathF.Max(impulse.Z, 0);
                }
                else if (LimitState == PhysicsJointLimitState.AtUpper)
                {
                    impulse.Z = MathF.Min(impulse.Z, 0);
                }
                Vector2 b = -cdot1 - ((impulse.Z - f1.Z) * new Vector2(kz.X, kz.Y));
                Vector2 f2r = Solve22(kx.X, ky.X, kx.Y, ky.Y, b) + new Vector2(f1.X, f1.Y);
                impulse.X = f2r.X;
                impulse.Y = f2r.Y;
                Vector3 df = impulse - f1;
                Apply((df.X * perp) + (df.Z * axis), (df.X * s1) + df.Y + (df.Z * a1), (df.X * s2) + df.Y + (df.Z * a2));
            }
            else
            {
                Vector2 df = Solve22(kx.X, ky.X, kx.Y, ky.Y, -cdot1);
                impulse.X += df.X;
                impulse.Y += df.Y;
                Apply(df.X * perp, (df.X * s1) + df.Y, (df.X * s2) + df.Y);
            }
        }

        internal override bool SolvePosition()
        {
            Vector2 cA = A.Position, cB = B.Position;
            float aA = A.Angle, aB = B.Angle;
            Vector2 rA = PhysicsMath.Rotate(LocalAnchorA - A.LocalCenter, aA), rB = PhysicsMath.Rotate(LocalAnchorB - B.LocalCenter, aB);
            Vector2 d = cB + rB - cA - rA;
            Vector2 worldAxis = PhysicsMath.Rotate(LocalAxisA, aA), worldPerp = PhysicsMath.Rotate(localYAxis, aA);
            float pa1 = Cross(d + rA, worldAxis), pa2 = Cross(rB, worldAxis);
            float ps1 = Cross(d + rA, worldPerp), ps2 = Cross(rB, worldPerp);
            Vector2 c1 = new(Vector2.Dot(worldPerp, d), aB - aA - ReferenceAngle);
            float linearError = MathF.Abs(c1.X), angularError = MathF.Abs(c1.Y);
            bool active = false;
            float c2 = 0;
            if (limitEnabled)
            {
                float translation = Vector2.Dot(worldAxis, d);
                if (MathF.Abs(UpperTranslation - LowerTranslation) < 2 * LinearSlop)
                {
                    c2 = Math.Clamp(translation, -MaxLinearCorrection, MaxLinearCorrection);
                    linearError = MathF.Max(linearError, MathF.Abs(translation));
                    active = true;
                }
                else if (translation <= LowerTranslation)
                {
                    c2 = Math.Clamp(translation - LowerTranslation + LinearSlop, -MaxLinearCorrection, 0);
                    linearError = MathF.Max(linearError, LowerTranslation - translation);
                    active = true;
                }
                else if (translation >= UpperTranslation)
                {
                    c2 = Math.Clamp(translation - UpperTranslation - LinearSlop, 0, MaxLinearCorrection);
                    linearError = MathF.Max(linearError, translation - UpperTranslation);
                    active = true;
                }
            }
            Vector3 change;
            if (active)
            {
                (Vector3 x, Vector3 y, Vector3 z) = Matrix(ps1, ps2, pa1, pa2);
                change = Solve33(x, y, z, -new Vector3(c1.X, c1.Y, c2));
            }
            else
            {
                float k11 = mA + mB + (iA * ps1 * ps1) + (iB * ps2 * ps2), k12 = (iA * ps1) + (iB * ps2), k22 = iA + iB;
                if (k22 == 0)
                {
                    k22 = 1;
                }
                Vector2 solved = Solve22(k11, k12, k12, k22, -c1);
                change = new(solved.X, solved.Y, 0);
            }
            Vector2 p = (change.X * worldPerp) + (change.Z * worldAxis);
            float la = (change.X * ps1) + change.Y + (change.Z * pa1);
            float lb = (change.X * ps2) + change.Y + (change.Z * pa2);
            A.Position = cA - (mA * p);
            A.Angle = aA - (iA * la);
            B.Position = cB + (mB * p);
            B.Angle = aB + (iB * lb);
            return linearError <= LinearSlop && angularError <= AngularSlop;
        }

        /// <summary>Native effective-mass matrix columns with the rotation-lock row.</summary>
        private (Vector3 X, Vector3 Y, Vector3 Z) Matrix(float ms1, float ms2, float ma1, float ma2)
        {
            float k11 = mA + mB + (iA * ms1 * ms1) + (iB * ms2 * ms2);
            float k12 = (iA * ms1) + (iB * ms2);
            float k13 = (iA * ms1 * ma1) + (iB * ms2 * ma2);
            float k22 = iA + iB;
            if (k22 == 0)
            {
                k22 = 1;
            }
            float k23 = (iA * ma1) + (iB * ma2);
            float k33 = mA + mB + (iA * ma1 * ma1) + (iB * ma2 * ma2);
            return (new(k11, k12, k13), new(k12, k22, k23), new(k13, k23, k33));
        }

        private void Apply(Vector2 p, float la, float lb)
        {
            A.Velocity -= mA * p;
            A.AngularVelocity -= iA * la;
            B.Velocity += mB * p;
            B.AngularVelocity += iB * lb;
        }

        private static float Parameter(float value)
        {
            return float.IsFinite(value) && MathF.Abs(value) <= 1000000 ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static Vector2 Cross(float scalar, Vector2 value)
        {
            return new(-scalar * value.Y, scalar * value.X);
        }

        private static Vector2 Solve22(float a11, float a12, float a21, float a22, Vector2 b)
        {
            float determinant = (a11 * a22) - (a12 * a21);
            if (determinant != 0)
            {
                determinant = 1 / determinant;
            }
            return new(determinant * ((a22 * b.X) - (a12 * b.Y)), determinant * ((a11 * b.Y) - (a21 * b.X)));
        }

        private static Vector3 Solve33(Vector3 ex, Vector3 ey, Vector3 ez, Vector3 b)
        {
            float determinant = Dot(ex, Cross(ey, ez));
            if (determinant != 0)
            {
                determinant = 1 / determinant;
            }
            return new(determinant * Dot(b, Cross(ey, ez)), determinant * Dot(ex, Cross(b, ez)), determinant * Dot(ex, Cross(ey, b)));
        }

        private static float Dot(Vector3 a, Vector3 b)
        {
            return (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
        }

        private static Vector3 Cross(Vector3 a, Vector3 b)
        {
            return new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Adds a prismatic joint with local anchors, axis and explicit reference angle.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="definition">Native-style slider definition.</param>
        /// <returns>The owned slider.</returns>
        /// <exception cref="ArgumentException">Bodies, definition or joint budget are invalid.</exception>
        public PhysicsPrismaticJoint AddPrismatic(PhysicsBody a, PhysicsBody b, PhysicsPrismaticDefinition definition)
        {
            ThrowIfLocked();
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            Vector2 axisA = definition.LocalAxisA;
            if (a == b || !bodies.Contains(a) || !bodies.Contains(b) || joints.Count >= 8192 || !ValidAnchor(definition.LocalAnchorA) ||
                !ValidAnchor(definition.LocalAnchorB) || !ValidAnchor(axisA) || ((axisA.X * axisA.X) + (axisA.Y * axisA.Y)) < 1e-12f ||
                !float.IsFinite(definition.ReferenceAngle) || MathF.Abs(definition.ReferenceAngle) > 1000000 ||
                !float.IsFinite(definition.LowerTranslation) || !float.IsFinite(definition.UpperTranslation) ||
                MathF.Abs(definition.LowerTranslation) > 1000000 || MathF.Abs(definition.UpperTranslation) > 1000000 ||
                definition.LowerTranslation > definition.UpperTranslation || !float.IsFinite(definition.MotorSpeed) ||
                MathF.Abs(definition.MotorSpeed) > 1000000 || !float.IsFinite(definition.MaxMotorForce) || definition.MaxMotorForce < 0 ||
                definition.MaxMotorForce > 1000000)
            {
                throw new ArgumentException("Invalid prismatic definition or owned bodies.", nameof(definition));
            }
            PhysicsPrismaticJoint joint = new(a, b, definition);
            joints.Add(joint);
            if (!joint.CollideConnected)
            {
                ChangeCollisionBlock(a, b, 1);
            }
            return joint;
        }

        /// <summary>Adds a prismatic joint from a world anchor and axis, as native <c>b2PrismaticJointDef::Initialize</c>.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="worldAnchor">Shared world anchor in meters.</param>
        /// <param name="worldAxis">World translation axis.</param>
        /// <param name="definition">Limits, motor and collision settings; its anchors, axis and reference angle are replaced.</param>
        /// <returns>The owned slider with the current relative reference angle.</returns>
        public PhysicsPrismaticJoint AddPrismatic(PhysicsBody a, PhysicsBody b, Vector2 worldAnchor, Vector2 worldAxis, PhysicsPrismaticDefinition definition = default)
        {
            ArgumentNullException.ThrowIfNull(a);
            ArgumentNullException.ThrowIfNull(b);
            return !ValidAnchor(worldAnchor) || !ValidAnchor(worldAxis)
                ? throw new ArgumentException("Invalid prismatic anchor or axis.", nameof(worldAnchor))
                : AddPrismatic(a, b, definition with
                {
                    LocalAnchorA = a.GetLocalPoint(worldAnchor),
                    LocalAnchorB = b.GetLocalPoint(worldAnchor),
                    LocalAxisA = PhysicsMath.InverseRotate(worldAxis, a.Angle),
                    ReferenceAngle = b.Angle - a.Angle
                });
        }
    }
}
