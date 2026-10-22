using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One authored fireball: a dynamic circle that burns the wood it touches, then fades and cuts its ropes.</summary>
    public sealed class SimulationFireball
    {
        internal enum FireballState
        {
            Idle,
            FadingWater,
            FadingBurn,
            Removed
        }

        internal SimulationFireball(PhysicsBody body)
        {
            Body = body;
        }

        /// <summary>Gets the fireball body, retained as the last visual pose after Windows destroys it at fade start.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets whether the fireball has burned something and is fading.</summary>
        public bool Fading => State is FireballState.FadingBurn or FireballState.FadingWater;

        /// <summary>Gets whether the scene destroyed the fireball after its fade.</summary>
        public bool Removed => State == FireballState.Removed;

        internal FireballState State;
        /// <summary>Ropes attached to the fireball, by rope index; released once the fade cuts them.</summary>
        internal List<int>? Ropes = [];
        internal int FadeStartedAt;
    }

    public sealed partial class CandySimulation
    {
        /// <summary>Native <c>GeneralPolygon::burnByFireball</c> fades the burning shape over this many seconds before removing it.</summary>
        private const double BurnSeconds = .1;

        private readonly List<SimulationFireball> fireballs = [];
        /// <summary>Burning shapes and the tick their burn started.</summary>
        private readonly Dictionary<GeneralShape, int> burningShapes = [];

        /// <summary>Gets the authored fireballs in scene order.</summary>
        public IReadOnlyList<SimulationFireball> Fireballs => fireballs.AsReadOnly();

        /// <summary>Gets whether a shape is burning.</summary>
        /// <param name="shape">Authored shape.</param>
        /// <returns>True from the burn until the shape is removed.</returns>
        public bool IsBurning(GeneralShape shape)
        {
            return burningShapes.ContainsKey(shape);
        }

        /// <summary>Native <c>Fireball::initWith</c>: the master design with the authored attributes merged over it.</summary>
        private PhysicsBody AddFireball(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "fireball")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            PhysicsBody body = BodyFactory.Create(World, fields);
            fireballs.Add(new(body));
            World.RecordContactEvents = true;
            World.ContactsUpdated = HandleContactsInStep;
            return body;
        }

        /// <summary>Native <c>Fireball::attachedToRope</c>, which every rope ending at a fireball calls.</summary>
        private void AttachRopeToFireball(int rope, PhysicsBody body)
        {
            fireballs.FirstOrDefault(f => f.Body == body)?.Ropes?.Add(rope);
        }

        /// <summary>Native <c>Fireball::handlePreSolveCollision</c>, called inside the step from begin-contact events: an idle
        /// fireball touching a dynamic shape (game body type 3, below the fireball's 13) burns it, becomes a sensor and stops,
        /// so the touching contact is not solved.</summary>
        private void HandleFireballContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind == PhysicsContactEventKind.Begin))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                if (a == null || b == null)
                {
                    continue;
                }
                SimulationFireball? fireball = fireballs.FirstOrDefault(f => f.Body == a || f.Body == b);
                PhysicsBody other = fireball?.Body == a ? b : a;
                if (fireball is not { State: SimulationFireball.FireballState.Idle } || other.Type != PhysicsBodyType.Dynamic ||
                    shapes.FirstOrDefault(s => s.Body == other) is not GeneralShape shape)
                {
                    continue;
                }
                BurnShape(shape);
                fireball.Body.IsSensor = true;
                fireball.Body.SetLinearVelocity(Vector2.Zero);
                fireball.State = SimulationFireball.FireballState.FadingBurn;
                fireball.FadeStartedAt = Tick;
            }
        }

        /// <summary>Native <c>GeneralPolygon::burnByFireball</c>: the shape starts its fade and the burn spreads through its
        /// joints, newest first, to every connected shape, static ones included.</summary>
        private void BurnShape(GeneralShape shape)
        {
            if (!burningShapes.TryAdd(shape, Tick))
            {
                return;
            }
            foreach (PhysicsJoint joint in World.GetJoints(shape.Body))
            {
                PhysicsBody other = joint.A == shape.Body ? joint.B : joint.A;
                if (shapes.FirstOrDefault(s => s.Body == other) is GeneralShape connected)
                {
                    BurnShape(connected);
                }
            }
        }

        /// <summary>Windows <c>Fireball::update</c> (0x489470): the first fade update releases both carriers and destroys the
        /// physics body, cutting its ropes. The visual remains until its timeline finishes. Android instead turns it static.</summary>
        private void UpdateFireballs()
        {
            foreach (SimulationFireball fireball in fireballs.Where(f => f.Fading && f.Ropes != null))
            {
                DestroyBody(fireball.Body);
                fireball.Ropes = null;
            }
        }

        /// <summary>Destroys burned shapes and faded fireballs: each schedules its removal when its fade timeline finishes
        /// in an update, and the scene destroys it after the next step.</summary>
        private void RemoveBurnedObjects()
        {
            foreach ((GeneralShape shape, int started) in burningShapes.ToArray())
            {
                if (Tick == started + SceneTimings.Updates(BurnSeconds))
                {
                    _ = burningShapes.Remove(shape);
                    _ = shapes.Remove(shape);
                    DestroyBody(shape.Body);
                }
            }
            if (timings?.Fireball is not IReadOnlyList<double> scenes)
            {
                return;
            }
            foreach (SimulationFireball fireball in fireballs.Where(f => f.Fading))
            {
                // FADE_WATER is scene 2; FADE_FIRE is scene 3.
                if (Tick == fireball.FadeStartedAt + SceneTimings.Updates(scenes[fireball.State == SimulationFireball.FireballState.FadingWater ? 2 : 3]))
                {
                    fireball.State = SimulationFireball.FireballState.Removed;
                }
            }
        }

        /// <summary>Native <c>GameScene::removeGameObject</c> and <c>SingleBodyObject::destroyB2Body</c>: throwers forget the
        /// object, every rope holding it is cut next to it and the body is destroyed with its joints.</summary>
        private void DestroyBody(PhysicsBody body)
        {
            ForgetThrownBody(body);
            DropFromHelicopter(body);
            for (int r = 0; r < ropes.Count; r++)
            {
                CutWithBodyDestroyed(r, body);
            }
            _ = World.RemoveBody(body);
        }
    }
}
