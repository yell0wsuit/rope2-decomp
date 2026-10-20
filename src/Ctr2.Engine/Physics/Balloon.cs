using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One balloon popped by a swipe, for exact replay.</summary>
    /// <param name="Tick">Zero-based tick before which the pop applied.</param>
    /// <param name="Balloon">Zero-based balloon index in creation order.</param>
    public sealed record BalloonPop(int Tick, int Balloon);

    /// <summary>One authored or generated balloon: a buoyant body tied to its target by a rope.</summary>
    public sealed class SimulationBalloon
    {
        internal SimulationBalloon(PlacedLevelObject? placed, PhysicsBody body, bool spawned = false)
        {
            Placed = placed;
            Body = body;
            Spawned = spawned;
            InitialGravity = body.GravityScale;
            InitialDamping = body.LinearDamping;
        }

        /// <summary>Gets the authored placement, or null for a generated balloon.</summary>
        internal PlacedLevelObject? Placed
        {
            get;
        }
        /// <summary>Gets whether a generator created this balloon after the initial scene objects.</summary>
        public bool Spawned
        {
            get;
        }
        internal float InitialGravity
        {
            get;
        }
        internal float InitialDamping
        {
            get;
        }

        /// <summary>Gets the balloon body, or null once it has popped or flown away.</summary>
        public PhysicsBody? Body
        {
            get; internal set;
        }

        /// <summary>Gets whether the balloon can still pop or fly away.</summary>
        public bool Active
        {
            get; internal set;
        } = true;
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationBalloon> balloons = [];
        private readonly List<BalloonPop> balloonPops = [];
        private float stratosphere, stratosphereGravity, stratosphereDamping;
        private bool balloonDesignLoaded;

        /// <summary>Gets the authored balloons followed by generated ones, in creation order.</summary>
        public IReadOnlyList<SimulationBalloon> Balloons => balloons.AsReadOnly();

        /// <summary>Gets balloons popped by swipes, in application order.</summary>
        public IReadOnlyList<BalloonPop> BalloonPops => balloonPops.AsReadOnly();

        /// <summary>Pops a balloon by index, as a recorded swipe pop does.</summary>
        /// <param name="index">Zero-based balloon index.</param>
        /// <returns>Whether the balloon was active and popped.</returns>
        public bool PopBalloon(int index)
        {
            return (uint)index >= balloons.Count ? throw new ArgumentOutOfRangeException(nameof(index)) : Pop(balloons[index]);
        }

        /// <summary>Creates a balloon body from the master balloon design merged with its authored attributes.</summary>
        /// <param name="placed">Authored balloon.</param>
        /// <param name="design">Matching master document.</param>
        /// <remarks>The scene loads at most ten authored balloons; later ones are ignored.</remarks>
        private void AddBalloon(PlacedLevelObject placed, LevelDefinition design)
        {
            if (balloons.Count >= 10)
            {
                return;
            }
            LoadBalloonDesign(design);
            Dictionary<string, string> fields = new(Fields(Find(design, "balloon")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            balloons.Add(new(placed, BodyFactory.Create(World, fields.AsReadOnly())));
        }

        /// <summary>Reads the stratosphere settings every balloon initializer reads, once per scene.</summary>
        /// <param name="design">Matching master document.</param>
        private void LoadBalloonDesign(LevelDefinition design)
        {
            if (balloonDesignLoaded)
            {
                return;
            }
            LevelElement settings = Find(design, "balloon_design");
            stratosphere = Value(settings, "stratosphere_y");
            stratosphereGravity = Value(settings, "stratosphere_koeff");
            stratosphereDamping = Value(settings, "stratosphere_damping_koeff");
            balloonDesignLoaded = true;
        }

        /// <summary>Scales gravity and damping below the stratosphere line from the body height, as the balloon update does.</summary>
        /// <param name="balloon">Balloon to update.</param>
        private void UpdateBalloon(SimulationBalloon balloon)
        {
            if (balloon.Body is not PhysicsBody body)
            {
                return;
            }
            float y = (float)(body.Origin.Y * 200.0);
            if (y > 0)
            {
                if (y >= stratosphere)
                {
                    body.GravityScale = balloon.InitialGravity;
                    body.LinearDamping = balloon.InitialDamping;
                }
                else
                {
                    body.GravityScale = (stratosphereGravity + (y * (float)(1.0 - stratosphereGravity) / (float)(stratosphere - 0.0))) * balloon.InitialGravity;
                    body.LinearDamping = (stratosphereDamping + (y * (float)(1.0 - stratosphereDamping) / (float)(stratosphere - 0.0))) * balloon.InitialDamping;
                }
            }
            else
            {
                body.GravityScale = stratosphereGravity * balloon.InitialGravity;
                body.LinearDamping = stratosphereDamping * balloon.InitialDamping;
            }
        }

        /// <summary>Pops an active balloon: it stops being active and its body is destroyed.</summary>
        private bool Pop(SimulationBalloon balloon)
        {
            if (!balloon.Active)
            {
                return false;
            }
            balloon.Active = false;
            DestroyBalloonBody(balloon);
            return true;
        }

        /// <summary>Tries a tap on authored balloons, newest first; generated balloons see presses as late children.</summary>
        private bool TapBalloon(Vector2 point)
        {
            for (int i = balloons.Count - 1; i >= 0; i--)
            {
                if (!balloons[i].Spawned && TapBalloon(balloons[i], point))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Native <c>Balloon::processTouchDown</c>: a press within 70 logical units pops an active balloon.</summary>
        private bool TapBalloon(SimulationBalloon balloon, Vector2 point)
        {
            return balloon.Body is PhysicsBody body && Vector2.Distance(body.Origin * 200, point) < 70 && Pop(balloon);
        }

        /// <summary>Pops every balloon within 50 logical units of a swipe segment, after ropes, as the finger trace does.</summary>
        internal void SwipeBalloons(Vector2 start, Vector2 end)
        {
            for (int i = 0; i < balloons.Count; i++)
            {
                if (balloons[i].Body is PhysicsBody body && SegmentDistance(start, end, body.Origin * 200) < 50 && Pop(balloons[i]))
                {
                    balloonPops.Add(new(Tick, i));
                }
            }
        }

        /// <summary>Native <c>minimum_distance</c> from a point to a segment.</summary>
        private static float SegmentDistance(Vector2 v, Vector2 w, Vector2 p)
        {
            float l2 = ((w.X - v.X) * (w.X - v.X)) + ((w.Y - v.Y) * (w.Y - v.Y));
            if (l2 == 0)
            {
                return Vector2.Distance(p, v);
            }
            float t = Vector2.Dot(p - v, w - v) / l2;
            return t < 0 ? Vector2.Distance(p, v) : t > 1 ? Vector2.Distance(p, w) : Vector2.Distance(p, v + ((w - v) * t));
        }

        /// <summary>Destroys a balloon body after cutting every intact rope attached to it, like the native body destruction.</summary>
        private void DestroyBalloonBody(SimulationBalloon balloon)
        {
            if (balloon.Body is not PhysicsBody body)
            {
                return;
            }
            for (int r = 0; r < ropes.Count; r++)
            {
                CutWithBodyDestroyed(r, body);
            }
            _ = World.RemoveBody(body);
            balloon.Body = null;
        }

        /// <summary>Native <c>Rope::cutWithBodyDestroyed</c>: an intact rope is cut at the joint next to the body, which then leaves the rope.</summary>
        private void CutWithBodyDestroyed(int ropeIndex, PhysicsBody body)
        {
            RopeChain rope = ropes[ropeIndex];
            bool head = rope.Points[0] == body;
            if (!head && rope.Points[^1] != body)
            {
                return;
            }
            if (rope.CutGap < 0 && !rope.Released)
            {
                int index = head ? 0 : rope.Points.Count - 2;
                _ = Cut(ropeIndex, index, BodyPosition(rope, index), Vector2.Zero);
            }
            if (head)
            {
                rope.Points.RemoveAt(0);
                rope.Joints.RemoveAt(0);
                rope.CutGap--;
            }
            else
            {
                rope.Points.RemoveAt(rope.Points.Count - 1);
                rope.Joints.RemoveAt(rope.Joints.Count - 1);
            }
        }

        /// <summary>Native <c>Rope::notifyAttachedObjects</c>: a balloon at either end of a cut rope flies away.</summary>
        private void NotifyAttached(RopeChain rope)
        {
            foreach (SimulationBalloon balloon in balloons.Where(b => b.Active && b.Body != null && (rope.Points[0] == b.Body || rope.Points[^1] == b.Body)))
            {
                balloon.Active = false;
                DestroyBalloonBody(balloon);
            }
        }
    }
}
