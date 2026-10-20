using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One authored helicopter: a kinematic sensor that follows its path, carries a touching candy, Om Nom or circle
    /// to its hook and drops it on a tap.</summary>
    public sealed class SimulationHelicopter
    {
        internal enum CaptureState
        {
            None,
            Scheduled,
            Moving,
            Complete
        }

        internal readonly PathMover Mover;
        internal readonly List<PhysicsJoint> JointsToDelete = [];
        internal CaptureState State;
        /// <summary>The captured object (its delegate) and its body; the source can pair an old delegate with another body.</summary>
        internal PhysicsBody? CapturedObject, CapturedBody, OldObject, OldBody;
        internal bool NeedAngleCorrect, OmnomForced;
        internal float NoCaptureTimer, CapturedAngularDamping;
        internal PhysicsJoint? Joint;

        internal SimulationHelicopter(PhysicsBody body, PathMover mover)
        {
            Body = body;
            Mover = mover;
        }

        /// <summary>Gets the helicopter body.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets the carried body, if any.</summary>
        public PhysicsBody? Captured => CapturedBody;
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationHelicopter> helicopters = [];
        /// <summary>Each object's helicopter delegate flag: the helicopter that captured it.</summary>
        private readonly Dictionary<PhysicsBody, SimulationHelicopter> carriedBy = [];

        /// <summary>Gets the authored helicopters in scene order.</summary>
        /// <value>Each owns a kinematic sensor body.</value>
        public IReadOnlyList<SimulationHelicopter> Helicopters => helicopters.AsReadOnly();

        private void AddHelicopter(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "helicopter")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            fields["normalizeShape"] = "true";
            PhysicsBody body = BodyFactory.Create(World, fields);
            helicopters.Add(new(body, new PathMover(fields)));
            World.RecordContactEvents = true;
        }

        /// <summary>Native <c>Helicopter::handlePreSolveCollision</c> from begin-contact events, objects ordered by game body type.</summary>
        private void HandleHelicopterContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            if (helicopters.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind == PhysicsContactEventKind.Begin))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                if (a == null || b == null)
                {
                    continue;
                }
                foreach (SimulationHelicopter helicopter in helicopters)
                {
                    // Every candidate type ranks below the helicopter, so the candidate is the other object.
                    PhysicsBody? other = a == helicopter.Body ? b : b == helicopter.Body ? a : null;
                    if (other == null || !(helicopter.OmnomForced || helicopter.CapturedObject == null) || helicopter.NoCaptureTimer > 0)
                    {
                        continue;
                    }
                    PhysicsBody? scheduled = null;
                    if (Characters.Contains(other) && !carriedBy.ContainsKey(other))
                    {
                        helicopter.NeedAngleCorrect = true;
                        scheduled = other;
                        helicopter.CapturedObject = other;
                        if (helicopter.OmnomForced)
                        {
                            // The source records the character as the old delegate, paired with the candy body it carried.
                            helicopter.OmnomForced = false;
                            helicopter.OldObject = helicopter.CapturedObject;
                            helicopter.OldBody = helicopter.CapturedBody;
                        }
                    }
                    if (helicopter.CapturedObject == null)
                    {
                        if (Shapes.FirstOrDefault(s => s.Body == other) is GeneralShape shape && shape.Kind.StartsWith("circle", StringComparison.Ordinal) &&
                            !carriedBy.ContainsKey(other))
                        {
                            helicopter.NeedAngleCorrect = false;
                            scheduled = other;
                            helicopter.CapturedObject = other;
                            helicopter.OmnomForced = false;
                        }
                        if (Candies.Contains(other) && !carriedBy.ContainsKey(other))
                        {
                            helicopter.NeedAngleCorrect = false;
                            scheduled = other;
                            helicopter.CapturedObject = other;
                            helicopter.OmnomForced = true;
                        }
                        // A fireball keeps upright like Om Nom.
                        if (fireballs.Any(f => f.Body == other) && !carriedBy.ContainsKey(other))
                        {
                            helicopter.NeedAngleCorrect = true;
                            scheduled = other;
                            helicopter.CapturedObject = other;
                            helicopter.OmnomForced = false;
                        }
                    }
                    if (scheduled != null)
                    {
                        helicopter.CapturedBody = scheduled;
                        helicopter.State = SimulationHelicopter.CaptureState.Scheduled;
                    }
                }
            }
        }

        /// <summary>Native <c>Helicopter::update</c>: moves along its path, captures a scheduled object, drains deferred joint
        /// deletions, carries the object to the hook and damps its spin.</summary>
        private void UpdateHelicopter(SimulationHelicopter helicopter)
        {
            helicopter.Mover.Update(helicopter.Body, PhysicsWorld.TimeStep);
            if (helicopter.NoCaptureTimer > 0)
            {
                helicopter.NoCaptureTimer -= PhysicsWorld.TimeStep;
            }
            if (helicopter.State == SimulationHelicopter.CaptureState.Scheduled)
            {
                if (helicopter.OldBody != null && helicopter.OldObject != null)
                {
                    ReleaseCarried(helicopter, true);
                }
                if (helicopter.CapturedObject is PhysicsBody captured)
                {
                    helicopter.State = SimulationHelicopter.CaptureState.Moving;
                    carriedBy[captured] = helicopter;
                    if (Characters.Contains(captured))
                    {
                        Gameplay?.CharacterHelicopterCaptured();
                    }
                    PhysicsBody body = helicopter.CapturedBody!;
                    helicopter.CapturedAngularDamping = body.AngularDamping;
                    body.AngularDamping = (float)(body.AngularDamping * 3.0) + 2;
                }
            }
            foreach (PhysicsJoint joint in helicopter.JointsToDelete)
            {
                _ = World.Remove(joint);
            }
            helicopter.JointsToDelete.Clear();
            if (helicopter.State == SimulationHelicopter.CaptureState.Moving && helicopter.CapturedBody is PhysicsBody carried)
            {
                Vector2 hook = helicopter.Body.Origin + new Vector2(0, .49f), distance = hook - carried.Origin;
                helicopter.Mover.Start();
                if (distance.LengthSquared() < 0.012)
                {
                    float angle = QuantAngle(carried.Angle);
                    World.SetTransform(carried, hook, angle);
                    Vector2 anchor = AnchorPoint(helicopter.CapturedObject!);
                    Vector2 local = helicopter.NeedAngleCorrect ? anchor : PhysicsMath.InverseRotate(carried.Position + anchor - carried.Origin, carried.Angle);
                    helicopter.Joint = World.AddRevolute(helicopter.Body, carried, new PhysicsRevoluteDefinition(new(0, .13f), local,
                        helicopter.NeedAngleCorrect ? 0 : angle, EnableLimit: true, LowerAngle: -1, UpperAngle: 1, EnableMotor: true, MotorSpeed: 0, MaxMotorTorque: .003f));
                    helicopter.State = SimulationHelicopter.CaptureState.Complete;
                }
                else
                {
                    carried.SetLinearVelocity(4 * PhysicsMath.Normalize(distance));
                    if (helicopter.NeedAngleCorrect)
                    {
                        double wrapped = QuantAngle(carried.Angle);
                        double turn = wrapped - ((int)(wrapped / 6.28318531) * 6.28318531);
                        carried.SetAngularVelocity((float)(turn * -4.0) + (float)(turn * -4.0));
                    }
                }
            }
            if (helicopter.CapturedBody is PhysicsBody spinning && spinning.AngularVelocity >= 2)
            {
                spinning.SetAngularVelocity((float)(spinning.AngularVelocity * .65));
            }
        }

        /// <summary>Native <c>quantAngle</c>: wraps an angle into (-pi, pi] with float steps.</summary>
        private static float QuantAngle(float angle)
        {
            while (angle <= -3.14159265)
            {
                angle = (float)(angle + 6.28318531);
            }
            while (angle > 3.14159265)
            {
                angle = (float)(angle + -6.28318531);
            }
            return angle;
        }

        /// <summary>Each object's <c>helicopterAnchorPoint</c>.</summary>
        private Vector2 AnchorPoint(PhysicsBody body)
        {
            return Candies.Contains(body) || fireballs.Any(f => f.Body == body) ? new(0, -.2f) : Characters.Contains(body) ? new(0, -.32f) :
                body.PolygonFixtures.Count == 0 ? new(0, (float)(-0.02 - body.Radius)) : new(0, -.3f);
        }

        /// <summary>Native <c>releaseCapturedObject</c>: the object leaves the helicopter, its hinge is deleted on the next update
        /// (after one more physics step), its angular damping is restored and the helicopter cools down for half a second.</summary>
        /// <param name="helicopter">Carrying helicopter.</param>
        /// <param name="old">Whether to release the old capture slot instead of the current one.</param>
        private void ReleaseCarried(SimulationHelicopter helicopter, bool old)
        {
            PhysicsBody? obj = old ? helicopter.OldObject : helicopter.CapturedObject, body = old ? helicopter.OldBody : helicopter.CapturedBody;
            if (obj == null)
            {
                return;
            }
            _ = carriedBy.Remove(obj);
            if (Characters.Contains(obj))
            {
                Gameplay?.CharacterHelicopterReleased();
            }
            if (helicopter.Joint != null)
            {
                helicopter.JointsToDelete.Add(helicopter.Joint);
            }
            helicopter.Joint = null;
            body?.AngularDamping = helicopter.CapturedAngularDamping;
            if (old)
            {
                (helicopter.OldObject, helicopter.OldBody) = (null, null);
            }
            else
            {
                (helicopter.CapturedObject, helicopter.CapturedBody) = (null, null);
            }
            helicopter.NoCaptureTimer = .5f;
            helicopter.State = SimulationHelicopter.CaptureState.None;
        }

        /// <summary>Native <c>Helicopter::processTouchDown</c>, newest first: a press near the helicopter or its load drops the load.</summary>
        private bool TapHelicopter(Vector2 point)
        {
            for (int i = helicopters.Count - 1; i >= 0; i--)
            {
                SimulationHelicopter helicopter = helicopters[i];
                if (helicopter.CapturedObject == null || helicopter.CapturedBody is not PhysicsBody body)
                {
                    continue;
                }
                Vector2 near = new Vector2((float)(helicopter.Body.Origin.X * 200.0), (float)(helicopter.Body.Origin.Y * 200.0)) - point;
                Vector2 load = new Vector2((float)(body.Origin.X * 200.0), (float)(body.Origin.Y * 200.0)) - point;
                if (MathF.Sqrt((near.X * near.X) + (near.Y * near.Y)) < 93 || MathF.Sqrt((load.X * load.X) + (load.Y * load.Y)) < 60)
                {
                    ReleaseCarried(helicopter, false);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Releases a disappearing object, including a capture scheduled during this step but not yet started.</summary>
        internal void DropFromHelicopter(PhysicsBody body)
        {
            foreach (SimulationHelicopter helicopter in helicopters.Where(h => h.CapturedObject == body))
            {
                ReleaseCarried(helicopter, false);
            }
        }
    }
}
