using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One authored Booka: a dynamic circle that slows its ropes and scares Om Nom, then flees.</summary>
    public sealed class SimulationBooka
    {
        internal enum BookaState
        {
            Idle,
            Booed,
            Exiting,
            Removed
        }

        internal SimulationBooka(PhysicsBody body)
        {
            Body = body;
        }

        /// <summary>Gets the Booka body.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets whether Om Nom has caught the Booka, which no longer counts as one for contacts.</summary>
        public bool Booed => State != BookaState.Idle;

        /// <summary>Gets whether the scene destroyed the Booka.</summary>
        public bool Removed => State == BookaState.Removed;

        internal BookaState State;
        internal bool ScheduleDetach;
        internal int CaughtAt;
        /// <summary>The tick whose step destroys the Booka once its removal is scheduled.</summary>
        internal int? RemoveAt;
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationBooka> bookas = [];
        /// <summary>Stands in for the unseeded <c>arc4random</c> that picks a caught Booka's flight angle.</summary>
        private uint bookaRandom = 0x424F4F4B;

        /// <summary>Gets the authored Bookas in scene order.</summary>
        public IReadOnlyList<SimulationBooka> Bookas => bookas.AsReadOnly();

        /// <summary>Native <c>Booka::initWith</c>: the master design with the authored attributes merged over it.</summary>
        private PhysicsBody AddBooka(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "booka")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            PhysicsBody body = BodyFactory.Create(World, fields);
            bookas.Add(new(body));
            World.RecordContactEvents = true;
            World.ContactsUpdated = HandleContactsInStep;
            return body;
        }

        /// <summary>The scene's contact listener inside the step, for the delegates whose effects reach the current solve.</summary>
        private void HandleContactsInStep(IReadOnlyList<PhysicsContactEvent> events)
        {
            HandleTriggerContacts(events);
            HandleFireballContacts(events);
            HandleBookaContacts(events);
        }

        /// <summary>Native <c>Omnom::handlePreSolveCollision</c> for Bookas: a begin contact between Om Nom (game body type 6) and
        /// an uncaught Booka (11), outside Om Nom's boo cooldown, boos Om Nom from the Booka's center and catches the Booka.</summary>
        private void HandleBookaContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            if (bookas.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind == PhysicsContactEventKind.Begin))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                if (a == null || b == null || (Gameplay?.BooTimer ?? 0) > 0)
                {
                    continue;
                }
                SimulationBooka? booka = bookas.FirstOrDefault(k => k.Body == a || k.Body == b);
                PhysicsBody other = booka?.Body == a ? b : a;
                if (booka is not { State: SimulationBooka.BookaState.Idle } || !Characters.Contains(other))
                {
                    continue;
                }
                Gameplay?.BooFrom(booka.Body.Position);
                CatchBooka(booka);
            }
        }

        /// <summary>Native <c>Booka::onOmnomCatched</c>: the Booka will drop its ropes, becomes a stopped sensor kicked 0.11 along
        /// a random heading from -110 to -70 degrees, and stops counting as a Booka.</summary>
        private void CatchBooka(SimulationBooka booka)
        {
            booka.ScheduleDetach = true;
            booka.Body.IsSensor = true;
            booka.Body.SetLinearVelocity(Vector2.Zero);
            bookaRandom ^= bookaRandom << 13;
            bookaRandom ^= bookaRandom >> 17;
            bookaRandom ^= bookaRandom << 5;
            float angle = (float)(((int)(bookaRandom % 41) - 110) * 3.14159265 / 180.0);
            // Windows takes float sine and cosine and scales them by the double 0.11 (Android and iOS use double trigonometry).
            booka.Body.ApplyLinearImpulse(new((float)(MathF.Cos(angle) * .11), (float)(MathF.Sin(angle) * .11)), booka.Body.Position);
            booka.State = SimulationBooka.BookaState.Booed;
            booka.CaughtAt = Tick;
        }

        /// <summary>Native <c>Booka::update</c>, after the frogs: a Booka beyond the screen margin of 40 leaves; a caught one cuts
        /// its ropes on its first update and leaves once its ACTION scene ends, which first advances in the catching step.</summary>
        private void UpdateBookas()
        {
            foreach (SimulationBooka booka in bookas.Where(k => k.RemoveAt == null))
            {
                if (booka.State == SimulationBooka.BookaState.Booed && timings?.Booka is IReadOnlyList<double> scenes &&
                    Tick >= booka.CaughtAt + SceneTimings.Updates(scenes[2]) - 1)
                {
                    booka.State = SimulationBooka.BookaState.Exiting;
                }
                if (OutOfScreen(booka.Body, 40) || booka.State == SimulationBooka.BookaState.Exiting)
                {
                    booka.RemoveAt = Tick + 1;
                }
                if (booka.ScheduleDetach)
                {
                    for (int r = 0; r < ropes.Count; r++)
                    {
                        CutWithBodyDestroyed(r, booka.Body);
                    }
                    booka.ScheduleDetach = false;
                }
            }
        }

        /// <summary>Destroys Bookas scheduled in the previous update, after this step.</summary>
        private void RemoveBookas()
        {
            foreach (SimulationBooka booka in bookas.Where(k => k.RemoveAt == Tick))
            {
                booka.State = SimulationBooka.BookaState.Removed;
                booka.RemoveAt = null;
                DestroyBody(booka.Body);
            }
        }

        /// <summary>Native <c>SingleBodyObject::isOutOfScreen</c> outside water: beyond the screen grown by the margin plus 150.</summary>
        internal bool OutOfScreen(PhysicsBody body, int margin)
        {
            Vector2 point = new((float)(body.Origin.X * 200.0), (float)(body.Origin.Y * 200.0));
            int expanded = margin + 150;
            return point.X < -expanded || point.Y < -expanded || point.X > Screen.X + expanded || point.Y > Screen.Y + expanded;
        }
    }
}
