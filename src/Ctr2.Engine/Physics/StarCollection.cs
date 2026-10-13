using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Immutable stationary star geometry and collection state.</summary>
    /// <param name="Position">Authored center in meters.</param>
    /// <param name="Radius">Sensor radius in meters, independent of artwork.</param>
    /// <param name="CollectedAt">Zero-based contact tick, or null before collection.</param>
    public sealed record SimulationStar(Vector2 Position, float Radius, int? CollectedAt);

    /// <summary>One accepted candy/star contact in the fixed-step replay.</summary>
    /// <param name="Tick">Zero-based tick before world integration.</param>
    /// <param name="Star">Zero-based star index in authored order.</param>
    /// <param name="Candy">Zero-based candy index in authored order.</param>
    public sealed record StarCollection(int Tick, int Star, int Candy);

    public sealed partial class CandySimulation
    {
        /// <summary>Owned star snapshots, replaced when a sensor is collected.</summary>
        private readonly List<SimulationStar> stars = [];
        /// <summary>Accepted one-time sensor contacts in deterministic authored order.</summary>
        private readonly List<StarCollection> starCollections = [];

        /// <summary>Gets the stationary stars in authored order.</summary>
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

        /// <summary>Loads stationary sensor geometry without adding solver bodies or changing island order.</summary>
        /// <param name="layout">Logical authored object positions.</param>
        /// <param name="design">Matching master document.</param>
        /// <exception cref="InvalidDataException">A star has unsupported motion, geometry or collision filtering.</exception>
        private void LoadStars(LevelLayout layout, LevelDefinition design)
        {
            foreach (PlacedLevelObject placed in layout.Objects.Where(o => o.Source.Kind == "star"))
            {
                LevelElement master = Find(design, "star");
                Dictionary<string, string> attributes = new(master.Attributes, StringComparer.Ordinal);
                foreach ((string key, string value) in placed.Source.Attributes)
                {
                    attributes[key] = value;
                }
                LevelElement merged = new(master.Kind, attributes);
                if (master.Kind != "circle" || attributes.GetValueOrDefault("body_type") != "kinematic" ||
                    attributes.GetValueOrDefault("isSensor") is not ("true" or "1") ||
                    Value(merged, "linearVelocityX", 0) != 0 || Value(merged, "linearVelocityY", 0) != 0 || Value(merged, "angularVelocity", 0) != 0 ||
                    !string.IsNullOrEmpty(attributes.GetValueOrDefault("path")) || !string.IsNullOrEmpty(attributes.GetValueOrDefault("normalizedPath")) ||
                    attributes.GetValueOrDefault("filter.collideWith", "all") != "all" || Value(merged, "filter.category", 0) != 0)
                {
                    throw new InvalidDataException("Star collection supports stationary kinematic circle sensors with default collision filtering only.");
                }
                float radius = Value(merged, "radius") / 100;
                if (radius is <= 0 or > 10000)
                {
                    throw new InvalidDataException("Star radius must be positive and within physics bounds.");
                }
                stars.Add(new(placed.Position!.Value / 200, radius, null));
            }
        }

        /// <summary>Collects overlapping candy sensors at contact-begin time before this tick's integration.</summary>
        /// <remarks>Sensor contacts use current transforms rather than a swept path; rope material points cannot collect stars.</remarks>
        private void CollectStars()
        {
            if (Gameplay != null && Gameplay.CandyState != ForestCandyState.Active)
            {
                return;
            }
            for (int starIndex = 0; starIndex < stars.Count; starIndex++)
            {
                SimulationStar star = stars[starIndex];
                if (star.CollectedAt.HasValue)
                {
                    continue;
                }
                for (int candyIndex = 0; candyIndex < Candies.Count; candyIndex++)
                {
                    PhysicsBody candy = Candies[candyIndex];
                    // Box2D sensor overlap accepts ten single-precision epsilons of distance.
                    float radius = candy.Radius + star.Radius + (10 * 1.1920929e-7f);
                    if (Vector2.DistanceSquared(candy.Position, star.Position) <= radius * radius)
                    {
                        stars[starIndex] = star with
                        {
                            CollectedAt = Tick
                        };
                        starCollections.Add(new(Tick, starIndex, candyIndex));
                        break;
                    }
                }
            }
        }
    }
}
