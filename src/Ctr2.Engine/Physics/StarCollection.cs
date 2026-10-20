using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One star's sensor body and collection state.</summary>
    /// <param name="Body">Kinematic sensor body, moved along the authored path when there is one.</param>
    /// <param name="Radius">Sensor radius in meters, independent of artwork.</param>
    /// <param name="CollectedAt">Zero-based contact tick, or null before collection.</param>
    /// <param name="Removed">Whether the scene destroyed the star, as a fruit challenge does after its first step.</param>
    public sealed record SimulationStar(PhysicsBody Body, float Radius, int? CollectedAt, bool Removed = false)
    {
        /// <summary>Gets the star's current center in meters.</summary>
        public Vector2 Position => Body.Position;
    }

    /// <summary>One accepted candy/star or character/star contact in the fixed-step replay.</summary>
    /// <param name="Tick">Zero-based tick before world integration.</param>
    /// <param name="Star">Zero-based star index in authored order.</param>
    /// <param name="Candy">Zero-based candy index in authored order, or -1 when the character collected it.</param>
    public sealed record StarCollection(int Tick, int Star, int Candy);

    public sealed partial class CandySimulation
    {
        /// <summary>Owned star snapshots, replaced when a sensor is collected.</summary>
        private readonly List<SimulationStar> stars = [];
        /// <summary>Accepted one-time sensor contacts in deterministic authored order.</summary>
        private readonly List<StarCollection> starCollections = [];
        /// <summary>Path movers of stars with an authored path, in scene order.</summary>
        private readonly List<(PathMover Mover, PhysicsBody Body)> starMovers = [];
        /// <summary>Whether a fruit challenge scheduled the stars for removal at the end of the next step.</summary>
        private bool starsScheduledForRemoval;

        /// <summary>Gets the stars in authored order.</summary>
        /// <value>A read-only live view of immutable geometry and collection snapshots.</value>
        public IReadOnlyList<SimulationStar> Stars
        {
            get;
        }

        /// <summary>Gets the accepted one-time candy/star contact records.</summary>
        /// <value>A read-only live view ordered by tick, star and candy index.</value>
        public IReadOnlyList<StarCollection> StarCollections
        {
            get;
        }

        /// <summary>Creates one star as the scene does: a kinematic sensor body from its merged design, moving along its
        /// authored path like the native <c>MovableBodyObject</c>.</summary>
        /// <param name="placed">Authored star placement.</param>
        /// <param name="design">Matching master document.</param>
        /// <exception cref="InvalidDataException">A star has unsupported motion, geometry or collision filtering.</exception>
        private void AddStar(PlacedLevelObject placed, LevelDefinition design)
        {
            LevelElement master = Find(design, "star");
            Dictionary<string, string> attributes = new(master.Attributes, StringComparer.Ordinal)
            {
                ["shapeType"] = master.Kind
            };
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                attributes[key] = value;
            }
            attributes["normalizeShape"] = "true";
            LevelElement merged = new(master.Kind, attributes);
            if (master.Kind != "circle" || attributes.GetValueOrDefault("body_type") != "kinematic" ||
                attributes.GetValueOrDefault("isSensor") != "true" ||
                Value(merged, "linearVelocityX") != 0 || Value(merged, "linearVelocityY") != 0 || Value(merged, "angularVelocity") != 0 ||
                !string.IsNullOrEmpty(attributes.GetValueOrDefault("normalizedPath")) ||
                attributes.GetValueOrDefault("filter.collideWith", "all") != "all" || Value(merged, "filter.category") != 0)
            {
                throw new InvalidDataException("Star collection supports kinematic circle sensors moved only by their path, with default collision filtering.");
            }
            float radius = Value(merged, "radius") / 100;
            if (radius is <= 0 or > 10000)
            {
                throw new InvalidDataException("Star radius must be positive and within physics bounds.");
            }
            PhysicsBody body = BodyFactory.Create(World, attributes.AsReadOnly());
            stars.Add(new(body, radius, null));
            if (!string.IsNullOrEmpty(attributes.GetValueOrDefault("path")))
            {
                starMovers.Add((new PathMover(attributes), body));
            }
        }

        /// <summary>Native <c>GameScene::removeGameObject</c> for the stars a fruit challenge scheduled: their bodies are
        /// destroyed after the first step, so they can still be collected during it.</summary>
        private void RemoveStars()
        {
            starsScheduledForRemoval = false;
            for (int i = 0; i < stars.Count; i++)
            {
                _ = World.RemoveBody(stars[i].Body);
                stars[i] = stars[i] with
                {
                    Removed = true
                };
            }
            starMovers.Clear();
        }

        /// <summary>Native <c>Star::update</c>: moving stars set their velocity for the next step, collected or not.</summary>
        private void UpdateStars()
        {
            foreach ((PathMover mover, PhysicsBody body) in starMovers)
            {
                mover.Update(body, PhysicsWorld.TimeStep);
            }
        }

        /// <summary>Native <c>Star::handlePreSolveCollision</c> at contact-begin time before this tick's integration: an
        /// overlapping candy collects a star and excites the character, and the character collects one on its own.</summary>
        /// <remarks>Sensor contacts use current transforms rather than a swept path; rope material points cannot collect stars.</remarks>
        private void CollectStars()
        {
            bool candyActive = Gameplay == null || Gameplay.CandyState == CandyState.Active;
            for (int starIndex = 0; starIndex < stars.Count; starIndex++)
            {
                SimulationStar star = stars[starIndex];
                if (star.CollectedAt.HasValue || star.Removed)
                {
                    continue;
                }
                int collector = -2;
                for (int candyIndex = 0; candyActive && candyIndex < Candies.Count; candyIndex++)
                {
                    PhysicsBody candy = Candies[candyIndex];
                    // Box2D sensor overlap accepts ten single-precision epsilons of distance.
                    float radius = candy.Radius + star.Radius + (10 * 1.1920929e-7f);
                    if (Vector2.DistanceSquared(candy.Position, star.Position) <= radius * radius)
                    {
                        collector = candyIndex;
                        break;
                    }
                }
                if (collector < 0 && Characters.Any(c => c.PolygonFixtures.Any(f => f.OverlapsCircle(star.Position, star.Radius, c.Origin, c.Angle))))
                {
                    collector = -1;
                }
                if (collector == -2)
                {
                    continue;
                }
                stars[starIndex] = star with
                {
                    CollectedAt = Tick
                };
                starCollections.Add(new(Tick, starIndex, collector));
                if (collector >= 0)
                {
                    Gameplay?.Excite();
                }
            }
        }
    }
}
