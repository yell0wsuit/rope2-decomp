using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>A gremlin body, authored or cloned by a press. Retired clones retain their journal identity.</summary>
    public sealed class SimulationGremlin
    {
        internal SimulationGremlin(PhysicsBody body, bool spawned)
        {
            Body = body;
            Spawned = spawned;
            CachePose();
        }

        /// <summary>Gets the body, retained after removal for diagnostics.</summary>
        public PhysicsBody Body
        {
            get;
        }
        /// <summary>Gets whether a tap created this gremlin after the initial scene objects.</summary>
        public bool Spawned
        {
            get;
        }
        /// <summary>Gets whether the gremlin remains eligible when enforcing the clone limit.</summary>
        public bool Active { get; internal set; } = true;
        /// <summary>Gets whether the scene destroyed this gremlin.</summary>
        public bool Removed
        {
            get; internal set;
        }
        /// <summary>Gets whether the gremlin has completed its first child update.</summary>
        public bool Cloneable
        {
            get; internal set;
        }
        /// <summary>Gets the remaining cloning cooldown in seconds.</summary>
        public float Cooldown
        {
            get; internal set;
        }

        internal Vector2 Position;
        internal float Rotation;

        internal void CachePose()
        {
            Position = Body.Origin * 200;
            Rotation = (float)((float)(Body.Angle * 180.0) * 0.3183098861837907);
        }
    }

    public sealed partial class CandySimulation
    {
        private const int GremlinLimit = 10;
        private readonly List<SimulationGremlin> gremlins = [];
        private IReadOnlyDictionary<string, string>? gremlinDesign;
        private int initialGremlinCount;

        /// <summary>Gets authored gremlins followed by clones in creation order, including retired journal slots.</summary>
        public IReadOnlyList<SimulationGremlin> Gremlins => gremlins.AsReadOnly();

        /// <summary>Windows 0x492740 merges placements, while loadMap accepts at most ten authored gremlins.</summary>
        private void AddGremlin(PlacedLevelObject placed, LevelDefinition design)
        {
            if (initialGremlinCount >= GremlinLimit)
            {
                return;
            }
            gremlinDesign ??= Fields(Find(design, "gremlin"));
            Dictionary<string, string> fields = new(gremlinDesign, StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            PhysicsBody body = BodyFactory.Create(World, fields);
            gremlins.Add(new(body, false));
            initialGremlinCount++;
        }

        /// <summary>Windows 0x492DD0 unrotates a touch around the last child-update pose, then tests a half-open rectangle.</summary>
        private bool TapGremlins(Vector2 point, bool spawned)
        {
            for (int i = gremlins.Count - 1; i >= 0; i--)
            {
                SimulationGremlin parent = gremlins[i];
                if (parent.Spawned != spawned || parent.Removed || !parent.Cloneable || parent.Cooldown > 0)
                {
                    continue;
                }
                float radians = (float)((float)(-0.0 - parent.Rotation) * 3.14159265 / 180.0);
                float c = MathF.Cos(radians), s = MathF.Sin(radians);
                Vector2 p = parent.Position, d = point - p;
                float x = p.X + (d.X * c) - (d.Y * s), y = p.Y + (d.X * s) + (d.Y * c);
                float left = p.X - 81, top = p.Y - 57.5f;
                if (x < left || x >= left + 162 || y < top || y >= top + 115)
                {
                    continue;
                }
                // The clone factory (0x4928C0) uses the master body design at an explicit position and angle:
                // no placement overrides, parent velocities or parent attachments are copied.
                float height = NativeText.FloatValue(gremlinDesign!.GetValueOrDefault("height"));
                float offset = height * 2 * .005f * -.5f;
                Vector2 position = parent.Body.Position + PhysicsMath.Rotate(new(0, offset), parent.Body.Angle);
                PhysicsBody clone = BodyFactory.Create(World, gremlinDesign!, position, parent.Body.Angle);
                gremlins.Add(new(clone, true));
                parent.Cooldown = .1f;
                LimitGremlins();
                return true;
            }
            return false;
        }

        /// <summary>Windows GameScene::addGremlin (0x4F3910) protects authored and held gremlins, retiring old clones first.</summary>
        private void LimitGremlins()
        {
            List<SimulationGremlin> live = [.. gremlins.Where(g => !g.Removed)];
            int count = live.Count;
            while (count > GremlinLimit)
            {
                SimulationGremlin? oldest = live.Skip(initialGremlinCount).Take(count - 1 - initialGremlinCount)
                    .FirstOrDefault(g => g.Active && !capturedBy.ContainsKey(g.Body));
                if (oldest == null)
                {
                    break;
                }
                oldest.Active = false;
                count--;
            }
        }

        private void RemoveGremlins()
        {
            foreach (SimulationGremlin gremlin in gremlins.Where(g => !g.Active && !g.Removed))
            {
                DestroyBody(gremlin.Body);
                gremlin.Removed = true;
            }
        }

        /// <summary>Initial gremlins update before throwers; newly added children update after all initial children.</summary>
        private void UpdateGremlins(bool spawned)
        {
            foreach (SimulationGremlin gremlin in gremlins.Where(g => g.Spawned == spawned && !g.Removed))
            {
                gremlin.CachePose();
                gremlin.Cloneable = true;
                gremlin.Cooldown = MathF.Max(0, gremlin.Cooldown - PhysicsWorld.TimeStep);
            }
        }
    }
}
