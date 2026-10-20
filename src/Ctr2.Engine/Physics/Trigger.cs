using System;
using System.Collections.Generic;
using System.Linq;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>A trigger whose first eligible begin contact releases the scene's motorized pins.</summary>
    public sealed class SimulationTrigger
    {
        internal SimulationTrigger(PhysicsBody body)
        {
            Body = body;
        }

        /// <summary>Gets the body built from the merged trigger design.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets whether an eligible dynamic body has pushed this trigger.</summary>
        public bool Pushed
        {
            get; internal set;
        }
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationTrigger> triggers = [];
        private readonly List<PhysicsRevoluteJoint> triggerMotorJoints = [];

        /// <summary>Gets triggers in authored order; triggers are not registered in hint journals.</summary>
        public IReadOnlyList<SimulationTrigger> Triggers => triggers.AsReadOnly();

        private void AddTrigger(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "trigger")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            // Windows Trigger::initWith (0x4AB190) forces normalization after merging the authored attributes.
            fields["normalizeShape"] = "true";
            triggers.Add(new(BodyFactory.Create(World, fields)));
            World.RecordContactEvents = true;
            World.ContactsUpdated = HandleContactsInStep;
        }

        /// <summary>Windows 0x4AB420 runs from BeginContact, including sensors, before the current solve.</summary>
        private void HandleTriggerContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind == PhysicsContactEventKind.Begin))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                if (a == null || b == null)
                {
                    continue;
                }
                SimulationTrigger? trigger = triggers.FirstOrDefault(t => t.Body == a || t.Body == b);
                PhysicsBody other = trigger?.Body == a ? b : a;
                if (trigger is not { Pushed: false } || other.Type != PhysicsBodyType.Dynamic ||
                    materialPoints.Contains(other) || tongueAnchors.Contains(other) || frogs.Any(f => f.Bodies.Contains(other)))
                {
                    continue;
                }
                trigger.Pushed = true;
                // Windows Pin::startMotor (0x4A08B0) releases limits only on already-enabled motors.
                foreach (PhysicsRevoluteJoint hinge in triggerMotorJoints.Where(h => h.MotorEnabled && World.Bodies.Contains(h.BodyA) && World.GetJoints(h.BodyA).Contains(h)))
                {
                    hinge.LimitEnabled = false;
                }
            }
        }
    }
}
