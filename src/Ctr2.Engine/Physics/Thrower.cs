using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One authored thrower: a dynamic body that captures a touching game object on a locked hinge and throws it on a tap.</summary>
    public sealed class SimulationThrower
    {
        internal readonly List<PhysicsBody> Collided = [];
        internal PhysicsBody? ToCapture;
        internal PhysicsJoint? Joint;
        internal float NoCaptureTimer;
        internal float JointLength;

        internal SimulationThrower(PhysicsBody body)
        {
            Body = body;
        }

        /// <summary>Gets the thrower body.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets the captured body, if any.</summary>
        public PhysicsBody? Captured
        {
            get; internal set;
        }
    }

    public sealed partial class CandySimulation
    {
        /// <summary>Native game body types that order contact objects and select capture lengths.</summary>
        private enum GameBodyType
        {
            GeneralPolygon = 3,
            Candy = 4,
            Character = 6,
            Thrower = 8,
            Booka = 11,
            Fireball = 13
        }

        private readonly List<SimulationThrower> throwers = [];
        /// <summary>The thrower holding each captured body, the source's per-object thrower link.</summary>
        private readonly Dictionary<PhysicsBody, SimulationThrower> capturedBy = [];
        private float throwPower;

        /// <summary>Gets the authored throwers in scene order.</summary>
        /// <value>Each owns a dynamic compound body.</value>
        public IReadOnlyList<SimulationThrower> Throwers => throwers.AsReadOnly();

        /// <summary>Builds a thrower like its initializer: the master design, the authored attributes and a forced normalized shape.</summary>
        private void AddThrower(PlacedLevelObject placed, LevelDefinition design)
        {
            if (throwers.Count == 0)
            {
                throwPower = Value(Find(design, "thrower_design"), "power");
            }
            Dictionary<string, string> fields = new(Fields(Find(design, "thrower")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            fields["normalizeShape"] = "true";
            throwers.Add(new(BodyFactory.Create(World, fields)));
            World.RecordContactEvents = true;
        }

        /// <summary>Returns the native game body type of a body's object, or null when it is not a capture candidate type.</summary>
        /// <param name="body">Contact body.</param>
        /// <param name="circle">Whether a general polygon is a circle shape, the only general polygon a thrower captures.</param>
        private GameBodyType? BodyType(PhysicsBody body, out bool circle)
        {
            circle = false;
            if (Candies.Contains(body))
            {
                return GameBodyType.Candy;
            }
            if (Characters.Contains(body))
            {
                return GameBodyType.Character;
            }
            if (throwers.Any(t => t.Body == body))
            {
                return GameBodyType.Thrower;
            }
            // A caught Booka becomes a default game body.
            if (bookas.Any(k => k.Body == body && !k.Booed))
            {
                return GameBodyType.Booka;
            }
            if (fireballs.Any(f => f.Body == body))
            {
                return GameBodyType.Fireball;
            }
            if (Shapes.FirstOrDefault(s => s.Body == body) is GeneralShape shape)
            {
                circle = shape.Kind.StartsWith("circle", StringComparison.Ordinal);
                return GameBodyType.GeneralPolygon;
            }
            return null;
        }

        /// <summary>Native <c>Thrower::handlePreSolveCollision</c> and <c>handleFinishedCollision</c>, which the scene's contact
        /// listener calls from begin and end contact events with the two objects ordered by game body type, in delegate
        /// registration (creation) order until a delegate returns true.</summary>
        private void HandleThrowerContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            if (throwers.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in events)
            {
                if (contact.Kind is not (PhysicsContactEventKind.Begin or PhysicsContactEventKind.End))
                {
                    continue;
                }
                // A destroyed object clears its user data first, so its finished contacts reach no delegate.
                PhysicsBody? first = World.TryGetFixtureBody(contact.FirstFixture), second = World.TryGetFixtureBody(contact.SecondFixture);
                if (first == null || second == null)
                {
                    continue;
                }
                GameBodyType? firstType = BodyType(first, out bool firstCircle), secondType = BodyType(second, out bool secondCircle);
                if (firstType == null || secondType == null)
                {
                    // Other game objects never become capture candidates; finished contacts with them erase nothing a thrower stored.
                    if (contact.Kind == PhysicsContactEventKind.End)
                    {
                        foreach (SimulationThrower thrower in throwers.Where(t => t.Body == first || t.Body == second))
                        {
                            _ = thrower.Collided.Remove(first);
                            _ = thrower.Collided.Remove(second);
                        }
                    }
                    continue;
                }
                if (secondType < firstType)
                {
                    (first, second) = (second, first);
                    (firstType, secondType) = (secondType, firstType);
                    (firstCircle, secondCircle) = (secondCircle, firstCircle);
                }
                foreach (SimulationThrower thrower in throwers)
                {
                    if (contact.Kind == PhysicsContactEventKind.End)
                    {
                        if (thrower.Body == first || thrower.Body == second)
                        {
                            _ = thrower.Collided.Remove(first);
                            _ = thrower.Collided.Remove(second);
                        }
                        continue;
                    }
                    PhysicsBody? candidate = null;
                    bool circle = false;
                    if (second == thrower.Body && firstType is GameBodyType.Thrower or GameBodyType.Candy or GameBodyType.Character or GameBodyType.GeneralPolygon)
                    {
                        (candidate, circle) = (first, firstCircle);
                    }
                    // Higher game body types capture from the thrower's side: throwers and fireballs (Bookas and gremlins pending).
                    if (first == thrower.Body && secondType is GameBodyType.Thrower or GameBodyType.Booka or GameBodyType.Fireball)
                    {
                        (candidate, circle) = (second, secondCircle);
                    }
                    if (candidate != null && BodyType(candidate, out _) == GameBodyType.GeneralPolygon && !circle)
                    {
                        candidate = null;
                    }
                    if (candidate == null)
                    {
                        continue;
                    }
                    if (thrower.Captured != null)
                    {
                        if (!thrower.Collided.Contains(candidate))
                        {
                            thrower.Collided.Add(candidate);
                        }
                        continue;
                    }
                    // Schedule the outermost thrower of a captured chain instead of its captured object.
                    PhysicsBody scheduled = candidate;
                    while (capturedBy.TryGetValue(scheduled, out SimulationThrower? holder) && holder != thrower)
                    {
                        scheduled = holder.Body;
                    }
                    thrower.ToCapture = scheduled;
                    // Scheduling returns true, which stops the scene listener from calling later delegates for this contact:
                    // of two touching throwers, only the one created first schedules the other.
                    break;
                }
            }
        }

        /// <summary>Native <c>Thrower::update</c>: cools down, retries a scheduled capture while touching, then tries a stored collision.</summary>
        private void UpdateThrower(SimulationThrower thrower)
        {
            float timer = thrower.NoCaptureTimer;
            _ = MoveToTarget(ref timer, 0, 1, PhysicsWorld.TimeStep);
            thrower.NoCaptureTimer = timer;
            if (thrower.ToCapture is PhysicsBody target && (Capture(thrower, target) || !World.AreTouching(target, thrower.Body)))
            {
                thrower.ToCapture = null;
            }
            if (thrower.Captured == null && thrower.Collided.Count > 0 && Capture(thrower, thrower.Collided[0]))
            {
                thrower.Collided.RemoveAt(0);
            }
        }

        /// <summary>Native <c>Thrower::captureObject</c>: teleports the body onto the thrower and locks it on a zero-width hinge.</summary>
        private bool Capture(SimulationThrower thrower, PhysicsBody body)
        {
            if (thrower.Captured != null || thrower.NoCaptureTimer > 0)
            {
                return false;
            }
            // A thrower already holding this thrower, directly or through a chain, cannot be captured by it.
            for (SimulationThrower? held = throwers.FirstOrDefault(t => t.Body == body); held != null; held = held.Captured is PhysicsBody next ? throwers.FirstOrDefault(t => t.Body == next) : null)
            {
                if (held.Captured == thrower.Body)
                {
                    return false;
                }
            }
            thrower.JointLength = BodyType(body, out _) switch
            {
                GameBodyType.GeneralPolygon => body.Radius + -.06f,
                GameBodyType.Candy => .1f,
                GameBodyType.Character => .23f,
                GameBodyType.Fireball => .15f,
                GameBodyType.Booka => .1f,
                // Throwers, and the source's other default capture types, hang farther out.
                GameBodyType.Thrower or null => .26f,
                _ => .26f
            };
            thrower.Captured = body;
            capturedBy[body] = thrower;
            if (Characters.Contains(body))
            {
                Gameplay?.CharacterCaptured();
            }
            PlaceCaptured(thrower);
            // Windows capture (0x4AA080) also clears spin; the Android reference only clears linear velocity.
            body.SetAngularVelocity(0);
            thrower.Joint = World.AddRevolute(thrower.Body, body, new PhysicsRevoluteDefinition(Vector2.Zero, new(0, thrower.JointLength), 0, EnableLimit: true));
            return true;
        }

        /// <summary>Native <c>Thrower::updateCapturedObject</c>: the scene object's zero height places the body at the thrower origin.</summary>
        private void PlaceCaptured(SimulationThrower thrower)
        {
            if (thrower.Captured is not PhysicsBody body)
            {
                return;
            }
            Vector2 position = thrower.Body.Origin + PhysicsMath.Rotate(new(0, -0f * thrower.JointLength), thrower.Body.Angle);
            World.SetTransform(body, position, thrower.Body.Angle);
            body.SetLinearVelocity(Vector2.Zero);
            if (throwers.FirstOrDefault(t => t.Body == body) is SimulationThrower inner)
            {
                PlaceCaptured(inner);
            }
        }

        /// <summary>Native <c>Thrower::processTouchDown</c> for the newest thrower holding a body within 90 logical units of the press.</summary>
        private bool TapThrower(Vector2 point)
        {
            for (int i = throwers.Count - 1; i >= 0; i--)
            {
                SimulationThrower thrower = throwers[i];
                if (thrower.Captured is not PhysicsBody body)
                {
                    continue;
                }
                Vector2 d = new Vector2((float)(thrower.Body.Origin.X * 200.0), (float)(thrower.Body.Origin.Y * 200.0)) - point;
                if (MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) >= 90)
                {
                    continue;
                }
                Release(thrower, body, true);
                body.ApplyLinearImpulse(PhysicsMath.Rotate(new(0, -(body.Mass * throwPower)), thrower.Body.Angle), body.Position);
                thrower.NoCaptureTimer = .3f;
                return true;
            }
            return false;
        }

        /// <summary>Releases a captured body: it leaves the thrower, which destroys the hinge.</summary>
        /// <param name="thrower">Holding thrower.</param>
        /// <param name="body">Captured body.</param>
        /// <param name="thrown">Whether the object is told it was released, as only a throw does.</param>
        private void Release(SimulationThrower thrower, PhysicsBody body, bool thrown)
        {
            _ = capturedBy.Remove(body);
            if (thrown && Characters.Contains(body))
            {
                Gameplay?.CharacterReleased();
            }
            if (thrower.Joint != null)
            {
                _ = World.Remove(thrower.Joint);
                thrower.Joint = null;
            }
            thrower.Captured = null;
        }

        /// <summary>Native <c>Thrower::swapCapturedObject</c> when the character eats held candy: the candy leaves without a cooldown
        /// and the character is scheduled instead.</summary>
        internal void SwapThrowerCapture(PhysicsBody candy, PhysicsBody character)
        {
            if (capturedBy.TryGetValue(candy, out SimulationThrower? thrower))
            {
                Release(thrower, candy, false);
                thrower.NoCaptureTimer = 0;
                PhysicsBody scheduled = character;
                while (capturedBy.TryGetValue(scheduled, out SimulationThrower? holder) && holder != thrower)
                {
                    scheduled = holder.Body;
                }
                thrower.ToCapture = scheduled;
            }
        }

        /// <summary>The scene releases a destroyed object from its thrower (with the cooldown) and from every pending capture.</summary>
        internal void ForgetThrownBody(PhysicsBody body)
        {
            if (capturedBy.TryGetValue(body, out SimulationThrower? thrower))
            {
                Release(thrower, body, false);
                thrower.NoCaptureTimer = .3f;
            }
            foreach (SimulationThrower pending in throwers)
            {
                if (pending.ToCapture == body)
                {
                    pending.ToCapture = null;
                }
                // Destruction clears fixture lookup before End events are drained; those events cannot erase this entry.
                _ = pending.Collided.Remove(body);
            }
        }
    }

    public sealed partial class PhysicsWorld
    {
        /// <summary>Native <c>isTouching</c>: whether any touching contact joins the two bodies.</summary>
        internal bool AreTouching(PhysicsBody a, PhysicsBody b)
        {
            return bodyContacts.Values.Any(c => c.Touching && ((c.A.Body == a && c.B.Body == b) || (c.A.Body == b && c.B.Body == a)));
        }
    }
}
