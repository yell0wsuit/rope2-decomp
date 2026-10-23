using System;
using System.Collections.Generic;
using System.Linq;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>An authored tutorial sensor: a static sensor that requests the balloons power-up tutorial after the candy has
    /// stayed inside it for its delay.</summary>
    public sealed class SimulationTutorialSensor
    {
        internal SimulationTutorialSensor(PhysicsBody body, float delay, bool disabled)
        {
            Body = body;
            Delay = delay;
            Disabled = disabled;
        }

        /// <summary>Gets the static sensor body built from the merged design.</summary>
        public PhysicsBody Body
        {
            get;
        }
        /// <summary>Gets the authored delay in seconds.</summary>
        public float Delay
        {
            get;
        }
        /// <summary>Gets the remaining countdown in seconds, or -1 while the candy is outside.</summary>
        public float Timeout { get; internal set; } = -1;
        /// <summary>Gets whether the sensor is inert: another tutorial was authored, or it already requested its tutorial.</summary>
        public bool Disabled
        {
            get; internal set;
        }
        /// <summary>Gets the completed tick whose update requested the balloons tutorial, if any.</summary>
        public int? RequestedAt
        {
            get; internal set;
        }
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationTutorialSensor> tutorialSensors = [];

        /// <summary>Gets tutorial sensors in authored order; like triggers, they are not registered in hint journals.</summary>
        public IReadOnlyList<SimulationTutorialSensor> TutorialSensors => tutorialSensors.AsReadOnly();

        /// <summary>Windows TutorialSensor::initWith (0x4AB990): the master design merged with the placement, enabled only for
        /// the <c>balloons_1candy</c> tutorial. Unlike Android, Windows does not consult the shown-tutorial preference here.</summary>
        private void AddTutorialSensor(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "tutorial_sensor")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            bool disabled = placed.Source.Attributes.GetValueOrDefault("tutorial") != "balloons_1candy";
            tutorialSensors.Add(new(BodyFactory.Create(World, fields), Value(placed.Source, "delay"), disabled));
            World.RecordContactEvents = true;
        }

        /// <summary>Windows 0x4ABD10 (BeginContact) starts the countdown and 0x4ABD40 (EndContact) cancels it, for the candy only.</summary>
        private void HandleTutorialSensorContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            if (tutorialSensors.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind is PhysicsContactEventKind.Begin or PhysicsContactEventKind.End))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                foreach (SimulationTutorialSensor sensor in tutorialSensors.Where(s => !s.Disabled))
                {
                    PhysicsBody? other = a == sensor.Body ? b : b == sensor.Body ? a : null;
                    if (other != null && Candies.Contains(other))
                    {
                        sensor.Timeout = contact.Kind == PhysicsContactEventKind.Begin ? sensor.Delay : -1;
                    }
                }
            }
        }

        /// <summary>Windows TutorialSensor::update (0x4ABC70): the countdown requests the balloons power-up tutorial (scene
        /// tutorial 4, shown by GameController 0x4DB940 outside replays) and disables the sensor.</summary>
        /// <remarks>The tutorial itself grants a balloons power-up and points the HUD at it; the port has no power-ups yet.</remarks>
        private void UpdateTutorialSensors()
        {
            foreach (SimulationTutorialSensor sensor in tutorialSensors.Where(s => !s.Disabled && s.Timeout != -1))
            {
                sensor.Timeout -= PhysicsWorld.TimeStep;
                if (sensor.Timeout <= 0)
                {
                    sensor.RequestedAt = Tick;
                    sensor.Disabled = true;
                }
            }
        }
    }
}
