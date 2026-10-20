using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One fruit of a fruit challenge and its collection state.</summary>
    /// <param name="Body">Kinematic sensor body built from the clover design; static once collected.</param>
    /// <param name="Type">Native fruit type 0 to 2, which picks the artwork.</param>
    /// <param name="Radius">Sensor radius in meters.</param>
    /// <param name="CollectedAt">Zero-based contact tick, or null before collection.</param>
    /// <param name="Removed">Whether the scene destroyed the collected fruit after its animation and particles.</param>
    public sealed record SimulationFruit(PhysicsBody Body, int Type, float Radius, int? CollectedAt, bool Removed = false)
    {
        /// <summary>Gets the fruit's center in meters.</summary>
        public Vector2 Position => Body.Position;
    }

    /// <summary>One accepted candy/fruit or character/fruit contact.</summary>
    /// <param name="Tick">Zero-based tick before world integration.</param>
    /// <param name="Fruit">Zero-based index among the created fruits.</param>
    /// <param name="Candy">Zero-based candy index, or -1 when the character collected it.</param>
    public sealed record FruitCollection(int Tick, int Fruit, int Candy);

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationFruit> fruits = [];
        private readonly List<FruitCollection> fruitCollections = [];
        private readonly IReadOnlyList<double>? fruitScenes;

        /// <summary>Windows animation resource of the fruits, whose scenes 0, 1 and 2 are the apple, grape and strawberry.</summary>
        public const int FruitAnimationId = 30;

        /// <summary>Updates a collected fruit's particle burst runs before it reports finished: native <c>FruitParticles</c>
        /// emit six particles with a 0.7 second life and no variance, stop emitting once 0.7 seconds have elapsed, drop dead
        /// particles on the update after their life runs out and report on the first update with none left.</summary>
        internal static readonly int ParticleUpdates = CountParticleUpdates();

        /// <summary>Loads each native fruit type's scene duration: types 0, 1 and 2 play scenes 0, 2 and 1.</summary>
        /// <param name="cache">Local game cache.</param>
        /// <returns>Durations in seconds by fruit type.</returns>
        public static IReadOnlyList<double> LoadFruitScenes(string cache)
        {
            AnimationElement root = AnimationReader.LoadWindows(cache, FruitAnimationId);
            return Array.AsReadOnly([AnimationPose.Duration(root, 0), AnimationPose.Duration(root, 2), AnimationPose.Duration(root, 1)]);
        }

        private static int CountParticleUpdates()
        {
            float elapsed = 0, life = .7f;
            bool active = true;
            int count = 6, updates = 0;
            while (true)
            {
                updates++;
                if (count == 0 && !active)
                {
                    return updates;
                }
                if (active)
                {
                    elapsed += PhysicsWorld.TimeStep;
                    active = !(.7f < elapsed);
                }
                if (life <= 0)
                {
                    count = 0;
                }
                else
                {
                    life -= PhysicsWorld.TimeStep;
                }
            }
        }
        /// <summary>Gets the fruit challenge's fruits in creation order; empty in other variants.</summary>
        public IReadOnlyList<SimulationFruit> Fruits => fruits.AsReadOnly();

        /// <summary>Gets the accepted fruit contacts in tick order.</summary>
        public IReadOnlyList<FruitCollection> FruitCollections => fruitCollections.AsReadOnly();

        /// <summary>Native <c>GameScene::loadMap</c> fruit grid: 11 columns of 12 fruits over the logical screen, alternate
        /// columns shifted half a row and every column skewed, built after the ropes. A fruit whose center lies inside any
        /// existing fixture, stars and sensors included, is not created.</summary>
        /// <param name="design">Master design document.</param>
        /// <param name="screen">Logical screen size.</param>
        private void AddFruits(LevelDefinition design, Vector2 screen)
        {
            Dictionary<string, string> clover = new(Fields(Find(design, "clover")), StringComparer.Ordinal)
            {
                ["normalizeShape"] = "true"
            };
            // Fruits ignore Bookas (the "nommies"): the Booka design's category becomes an excluded one.
            LevelElement? booka = design.Objects.FirstOrDefault(o => o.Attributes.GetValueOrDefault("id") == "booka");
            int nommies = booka == null ? -1 : NativeText.IntValue(booka.Attributes.GetValueOrDefault("filter.category"));
            if (nommies >= 0)
            {
                clover["filter.excludeCategories"] = nommies.ToString(CultureInfo.InvariantCulture);
            }
            float width = (float)(screen.X / 11.0), height = (float)(screen.Y / 12.0);
            for (int column = 0; column < 11; column++)
            {
                bool odd = column % 2 == 1;
                for (int row = 0; row < 12; row++)
                {
                    float x = (float)((column * width) + (float)(width / 2.0)) + (float)(column * 1.8);
                    float shift = odd ? 0 : (float)(height / 2.0);
                    float y = (float)((float)((row * height) + 50.0) + shift) + (float)(column * -4.5);
                    AddFruit(clover, new(x, y), (odd ? row : row + 2) % 3);
                }
            }
        }

        /// <summary>Native <c>Fruit::initWith</c>. The position travels through text: half of it is printed like
        /// <c>ostream &lt;&lt; float</c> (six significant digits) and the body factory parses and doubles it again.</summary>
        private void AddFruit(Dictionary<string, string> clover, Vector2 position, int type)
        {
            Vector2 point = new((float)(position.X / 200.0), (float)(position.Y / 200.0));
            bool covered = false;
            World.QueryAabb(new(point - new Vector2(1.1921e-7f), point + new Vector2(1.1921e-7f)), fixture =>
            {
                covered |= World.TestPoint(fixture, point);
                return true;
            });
            if (covered)
            {
                return;
            }
            Dictionary<string, string> fields = new(clover, StringComparer.Ordinal)
            {
                ["x"] = Printed((float)(position.X / 2.0)),
                ["y"] = Printed((float)(position.Y / 2.0))
            };
            PhysicsBody body = BodyFactory.Create(World, fields);
            fruits.Add(new(body, type, body.Radius, null));
        }

        /// <summary>Formats like <c>std::ostream &lt;&lt; float</c> with its default precision of six significant digits.</summary>
        private static string Printed(float value)
        {
            return ((double)value).ToString("G6", CultureInfo.InvariantCulture);
        }

        /// <summary>Native <c>Fruit::handlePreSolveCollision</c> at contact-begin time: an overlapping active candy or the
        /// character collects a fruit once.</summary>
        private void CollectFruits()
        {
            bool candyActive = Gameplay == null || Gameplay.CandyState == CandyState.Active;
            for (int index = 0; index < fruits.Count; index++)
            {
                SimulationFruit fruit = fruits[index];
                if (fruit.CollectedAt.HasValue || fruit.Removed)
                {
                    continue;
                }
                int collector = -2;
                for (int candyIndex = 0; candyActive && candyIndex < Candies.Count; candyIndex++)
                {
                    PhysicsBody candy = Candies[candyIndex];
                    // Box2D sensor overlap accepts ten single-precision epsilons of distance.
                    float radius = candy.Radius + fruit.Radius + (10 * 1.1920929e-7f);
                    if (Vector2.DistanceSquared(candy.Position, fruit.Position) <= radius * radius)
                    {
                        collector = candyIndex;
                        break;
                    }
                }
                if (collector < 0 && Characters.Any(c => c.PolygonFixtures.Any(f => f.OverlapsCircle(fruit.Position, fruit.Radius, c.Origin, c.Angle))))
                {
                    collector = -1;
                }
                if (collector == -2)
                {
                    continue;
                }
                fruits[index] = fruit with
                {
                    CollectedAt = Tick
                };
                fruitCollections.Add(new(Tick, index, collector));
            }
        }

        /// <summary>Native <c>Fruit::update</c>: a collected fruit becomes static.</summary>
        private void UpdateFruits()
        {
            foreach (SimulationFruit fruit in fruits.Where(f => f.CollectedAt.HasValue && !f.Removed))
            {
                World.SetBodyType(fruit.Body, PhysicsBodyType.Static);
            }
        }

        /// <summary>Native <c>Fruit::timelineFinished</c> and <c>particlesFinished</c>: collection starts the fruit's scene,
        /// which first advances in the collecting frame. The frame it finishes adds the particle burst, which first updates in
        /// the next frame because children added during an update wait for the following one. Its report schedules the fruit,
        /// and the scene destroys it after the next frame's physics step.</summary>
        /// <remarks>Without scene durations, as in tests, collected fruits stay in the world.</remarks>
        private void RemoveFinishedFruits()
        {
            if (fruitScenes == null)
            {
                return;
            }
            for (int i = 0; i < fruits.Count; i++)
            {
                if (fruits[i] is not { CollectedAt: int collected, Removed: false } fruit)
                {
                    continue;
                }
                int sceneUpdates = 1;
                while (sceneUpdates * (double)PhysicsWorld.TimeStep < fruitScenes[fruit.Type])
                {
                    sceneUpdates++;
                }
                int finished = collected + sceneUpdates - 1;
                if (Tick == finished + ParticleUpdates + 1)
                {
                    _ = World.RemoveBody(fruit.Body);
                    fruits[i] = fruit with
                    {
                        Removed = true
                    };
                }
            }
        }
    }
}
