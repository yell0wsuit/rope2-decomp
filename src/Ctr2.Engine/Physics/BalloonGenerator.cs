using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>An authored balloon generator: a static sensor that ties balloons to the candy touching it, once.</summary>
    public sealed class SimulationBalloonGenerator
    {
        internal SimulationBalloonGenerator(PhysicsBody body, int balloons, float ropeLength)
        {
            Body = body;
            Balloons = balloons;
            RopeLength = ropeLength;
        }

        /// <summary>Gets the sensor body, retained after removal for diagnostics.</summary>
        public PhysicsBody Body
        {
            get;
        }
        /// <summary>Gets the authored number of balloons to generate.</summary>
        public int Balloons
        {
            get;
        }
        /// <summary>Gets the doubled authored rope length in logical units.</summary>
        internal float RopeLength
        {
            get;
        }
        /// <summary>Gets the candy body whose begin contact the next update consumes.</summary>
        internal PhysicsBody? Scheduled
        {
            get; set;
        }
        /// <summary>Gets whether the generator has fired and awaits the next removal phase.</summary>
        internal bool RemovalScheduled
        {
            get; set;
        }
        /// <summary>Gets whether the scene destroyed the generator after it fired.</summary>
        public bool Removed
        {
            get; internal set;
        }
    }

    public sealed partial class CandySimulation
    {
        private const int BalloonLimit = 10;
        private readonly List<SimulationBalloonGenerator> generators = [];
        private IReadOnlyDictionary<string, string>? spawnedBalloonDesign;

        /// <summary>Gets balloon generators in authored order; like triggers, they are not registered in hint journals.</summary>
        public IReadOnlyList<SimulationBalloonGenerator> BalloonGenerators => generators.AsReadOnly();

        /// <summary>Windows BalloonGenerator::initWith (0x485420): the master design at the doubled integer placement, scaled by 0.005.</summary>
        private void AddBalloonGenerator(PlacedLevelObject placed, LevelDefinition design)
        {
            IReadOnlyDictionary<string, string> attributes = placed.Source.Attributes;
            int x = 2 * NativeText.IntValue(attributes.GetValueOrDefault("x")), y = 2 * NativeText.IntValue(attributes.GetValueOrDefault("y"));
            PhysicsBody body = BodyFactory.Create(World, Fields(Find(design, "balloonGenerator")), new(x * .005f, y * .005f), 0);
            generators.Add(new(body, NativeText.IntValue(attributes.GetValueOrDefault("balloons")), Value(placed.Source, "rope_length") * 2));
            spawnedBalloonDesign ??= Fields(Find(design, "balloon"));
            LoadBalloonDesign(design);
            World.RecordContactEvents = true;
        }

        /// <summary>Windows 0x4859B0 runs from BeginContact, sensors included: a candy schedules its body for the next update.</summary>
        private void HandleGeneratorContacts(IReadOnlyList<PhysicsContactEvent> events)
        {
            if (generators.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in events.Where(e => e.Kind == PhysicsContactEventKind.Begin))
            {
                PhysicsBody? a = World.TryGetFixtureBody(contact.FirstFixture), b = World.TryGetFixtureBody(contact.SecondFixture);
                foreach (SimulationBalloonGenerator generator in generators.Where(g => !g.Removed))
                {
                    PhysicsBody? other = a == generator.Body ? b : b == generator.Body ? a : null;
                    if (other != null && Candies.Contains(other))
                    {
                        generator.Scheduled = other;
                        break;
                    }
                }
            }
        }

        /// <summary>Destroys fired generators; they were scheduled in the previous children update, so they leave first.</summary>
        private void RemoveGenerators()
        {
            foreach (SimulationBalloonGenerator generator in generators.Where(g => g.RemovalScheduled && !g.Removed))
            {
                DestroyBody(generator.Body);
                generator.Removed = true;
            }
        }

        /// <summary>Windows BalloonGenerator::update (0x4857C0): a scheduled candy removes the generator and receives its balloons.</summary>
        private void UpdateGenerators()
        {
            foreach (SimulationBalloonGenerator generator in generators.Where(g => !g.Removed && g.Scheduled != null))
            {
                generator.RemovalScheduled = true;
                SpawnBalloons(generator.Balloons, generator.Body.Origin * 200, generator.RopeLength, generator.Scheduled!);
                generator.Scheduled = null;
            }
        }

        /// <summary>Windows BalloonGenerator::activate inlined with Balloon::createBalloonWithRope (0x484F20): balloons fan out by
        /// half a radian, one rope length above the position, each roped to the target unless it is born dead.</summary>
        /// <param name="count">Balloons to create.</param>
        /// <param name="position">Generator position in logical units.</param>
        /// <param name="length">Rope length in logical units.</param>
        /// <param name="target">Body the ropes hold.</param>
        private void SpawnBalloons(int count, Vector2 position, float length, PhysicsBody target)
        {
            float angle = (count - 1) * -.25f;
            for (int i = 0; i < count; i++)
            {
                float s = MathF.Sin(angle), c = MathF.Cos(angle);
                float dx = position.X - position.X, dy = position.Y - length - position.Y;
                float x = position.X + ((c * dx) - (s * dy)), y = position.Y + ((c * dy) + (s * dx));
                Dictionary<string, string> fields = new(spawnedBalloonDesign!, StringComparer.Ordinal)
                {
                    ["x"] = ((int)(x * .5f)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["y"] = ((int)(y * .5f)).ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
                // The born-dead query runs before the balloon body exists.
                bool bornDead = IsBornDead(fields);
                SimulationBalloon balloon = new(null, BodyFactory.Create(World, fields.AsReadOnly()), spawned: true);
                if (bornDead)
                {
                    // Balloon::playAnimation(FLY) deactivates the balloon and destroys its body at once.
                    balloon.Active = false;
                    DestroyBalloonBody(balloon);
                }
                else
                {
                    // GameScene::addRope appends the rope's journal slot and child before addBalloon appends the balloon's.
                    ropes.Add(CreateRope(balloon.Body!, target, length, new(0, 45), Vector2.Zero, false, Candies.Contains(target), ropeBone, ropeFrequency));
                    spawned.Add(ropes[^1]);
                }
                balloons.Add(balloon);
                spawned.Add(balloon);
                LimitBalloons(balloon);
                angle += .5f;
            }
        }

        /// <summary>Windows GameScene::addBalloon (0x4F3A00): beyond ten balloons, the oldest active added balloon flies away.</summary>
        /// <remarks>Native removes finished balloons from its list after their animations, while the port keeps every balloon for its
        /// stable index, so above the limit this counts more balloons than native; no supplied level reaches it.</remarks>
        private void LimitBalloons(SimulationBalloon added)
        {
            if (!added.Active || balloons.Count <= BalloonLimit)
            {
                return;
            }
            if (balloons.FirstOrDefault(b => b.Spawned && b.Active) is SimulationBalloon oldest)
            {
                oldest.Active = false;
                DestroyBalloonBody(oldest);
            }
        }

        /// <summary>Windows Balloon::checkIsBornDead (0x483920, callback 0x483B00/0x483C40): fourteen rim points and the center;
        /// static solid fixtures remove the points they contain, and ten or fewer remaining points make the balloon born dead.</summary>
        private bool IsBornDead(IReadOnlyDictionary<string, string> design)
        {
            float cx = (float)(NativeText.FloatValue(design.GetValueOrDefault("x")) * 2.0) * .005f;
            float cy = (float)(NativeText.FloatValue(design.GetValueOrDefault("y")) * 2.0) * .005f;
            float r = (float)(NativeText.FloatValue(design.GetValueOrDefault("radius")) * 2.0) * .005f;
            List<Vector2> points = [];
            float theta = 0;
            for (int i = 0; i < 14; i++)
            {
                points.Add(new((MathF.Cos(theta) * r) + cx, (MathF.Sin(theta) * r) + cy));
                theta += .44879895f;
            }
            points.Add(new(cx, cy));
            bool dead = false;
            World.QueryAabb(new(new(cx - r, cy - r), new(r + cx, r + cy)), fixture =>
            {
                if (World.GetFixtureSensor(fixture) || World.GetFixtureBody(fixture) is { Type: not PhysicsBodyType.Static })
                {
                    return true;
                }
                Vector2[] inside = [.. points.Where(p => World.TestPoint(fixture, p))];
                _ = points.RemoveAll(inside.Contains);
                if (points.Count > 10)
                {
                    return true;
                }
                dead = true;
                return false;
            });
            return dead;
        }
    }
}
