using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>A diagnostic scene with authored candy, grab, rope, stationary star and static terrain design data.</summary>
    public sealed partial class CandySimulation
    {
        /// <summary>World owning all center bodies and constraints.</summary>
        private readonly PhysicsWorld world = new();
        /// <summary>Authored rope chains in grab order.</summary>
        private readonly List<RopeChain> ropes = [];
        /// <summary>Gets optional ordinary forest gameplay state; null retains diagnostic behavior.</summary>
        public ForestGameplay? Gameplay
        {
            get;
        }
        /// <summary>Gets the simulated candies in authored order.</summary>
        /// <value>A read-only view of the dynamic candy bodies, in meters.</value>
        public IReadOnlyList<PhysicsBody> Candies
        {
            get;
        }
        /// <summary>Gets the completed fixed-step count.</summary>
        /// <value>Zero before the first step.</value>
        public int Tick
        {
            get; private set;
        }
        /// <summary>Gets static smoothed terrain shared by collision fixtures and diagnostic drawing.</summary>
        /// <value>Immutable material outlines and triangulated meter-space fixtures.</value>
        public IReadOnlyList<TerrainShape> Terrain
        {
            get;
        }

        /// <summary>Gets immutable authored polygon definitions for the simulated characters.</summary>
        /// <value>Initial authored data; dynamic transforms are available separately through Characters.</value>
        public IReadOnlyList<PolygonBodyDefinition> CharacterDefinitions
        {
            get;
        }

        /// <summary>Gets simulated compound polygon characters in authored definition order.</summary>
        /// <value>Dynamic bodies whose positions are centers of mass; artwork uses their origins.</value>
        public IReadOnlyList<PhysicsBody> Characters
        {
            get;
        }

        /// <summary>Gets the number of authored grab ropes.</summary>
        /// <value>The number of independent chains, including released chains.</value>
        public int RopeCount => ropes.Count;

        /// <summary>Creates a center-attachment simulation using a parsed level and matching master design.</summary>
        /// <param name="level">Original editor-space level.</param>
        /// <param name="design">Matching master design document.</param>
        /// <param name="play">Whether to enable the supported ordinary forest gameplay slice.</param>
        /// <param name="forestArrival">Whether the host selected the first production forest entry.</param>
        /// <exception cref="InvalidDataException">A required design, target or supported field is invalid.</exception>
        /// <remarks>Circle and compound-polygon/static-terrain contacts are supported; polygon CCD, sleeping, dynamic-body collisions and gameplay state remain pending.</remarks>
        public CandySimulation(LevelDefinition level, LevelDefinition design, bool play = false, bool forestArrival = false)
        {
            LevelLayout layout = LevelLayout.Create(level);
            CharacterDefinitions = PolygonBodyDefinition.LoadCharacters(level, design);
            Terrain = TerrainGeometry.Build(level, design);
            foreach (TerrainShape shape in Terrain.Where(s => !s.IsSensor))
            {
                foreach (IReadOnlyList<Vector2> triangle in shape.Triangles)
                {
                    world.AddTerrain(triangle, shape.Friction, shape.Restitution);
                }
            }
            LevelElement candyDesign = Find(design, "candy"), grabDesign = Find(design, "grab"), pointDesign = Find(design, "ropeMaterialPoint");
            this.pointDesign = pointDesign;
            LevelElement ropeDesign = Find(design, "rope_design");
            float bone = Value(ropeDesign, "bone_length") / 100;
            float frequency = Value(ropeDesign, "stretch_hertz");
            if (bone <= 0 || frequency <= 0)
            {
                throw new InvalidDataException("Rope bone length and stretch frequency must be positive.");
            }
            Dictionary<PlacedLevelObject, PhysicsBody> candyBodies = new(ReferenceEqualityComparer.Instance);
            List<PhysicsBody> candies = [];
            Dictionary<PlacedLevelObject, PhysicsBody> grabs = [];
            foreach (PlacedLevelObject placed in layout.Objects)
            {
                if (placed.Source.Kind is not ("candy" or "grab"))
                {
                    continue;
                }
                if (placed.Angle != 0)
                {
                    throw new InvalidDataException("Angular candy/grab placement is not supported by the center-body physics slice.");
                }
                bool isCandy = placed.Source.Kind == "candy";
                PhysicsBody body = CreateBody(isCandy ? candyDesign : grabDesign, placed.Position!.Value / 200, !isCandy);
                if (isCandy)
                {
                    candies.Add(body);
                    candyBodies.Add(placed, body);
                }
                else
                {
                    if (placed.Source.Attributes.TryGetValue("balloons", out string? balloons) && balloons != "0")
                    {
                        throw new InvalidDataException("Balloon-generating grabs are not supported by this physics slice.");
                    }
                    grabs.Add(placed, body);
                }
            }
            if (candies.Count == 0)
            {
                throw new InvalidDataException("Physics preview requires at least one candy.");
            }
            foreach (PlacedLevelObject grab in layout.Objects.Where(o => o.Source.Kind == "grab"))
            {
                // Like the scene loader, a grab whose target name is not registered keeps no rope.
                if (!grab.Source.Attributes.TryGetValue("target_id", out string? id) || !layout.Named.TryGetValue(id, out PlacedLevelObject? named))
                {
                    continue;
                }
                if (Value(grab.Source, "target_x", 0) != 0 || Value(grab.Source, "target_y", 0) != 0 ||
                    !candyBodies.TryGetValue(named, out PhysicsBody? target))
                {
                    throw new InvalidDataException("Physics preview supports zero-offset named candy targets only.");
                }
                float length = Value(grab.Source, "length") / 100;
                int generated = (int)(length / bone);
                if (length <= 0 || generated > 1024)
                {
                    throw new InvalidDataException("Rope length or generated point count exceeds physics bounds.");
                }
                float remainder = length - (generated * bone);
                if (MathF.Abs(remainder) < 1.1920929e-7f)
                {
                    generated--;
                    remainder = bone;
                }
                PhysicsBody head = grabs[grab];
                List<PhysicsBody> points = [head];
                Vector2 position = head.Position;
                Vector2 shift = generated > 0 ? (target.Position - head.Position) / generated : Vector2.Zero;
                for (int i = 0; i < generated; i++)
                {
                    position += shift;
                    points.Add(CreateBody(pointDesign, position, false));
                }
                points.Add(target);
                List<PhysicsJoint> constraints = [];
                for (int i = 0; i < points.Count - 1; i++)
                {
                    constraints.Add(world.AddRope(points[i], points[i + 1], i == points.Count - 2 ? remainder : bone));
                }
                ropes.Add(new(points, constraints, length, frequency));
            }
            List<PhysicsBody> characters = [];
            foreach (PolygonBodyDefinition definition in CharacterDefinitions)
            {
                PhysicsBody character = world.AddPolygon(definition.Shape, definition.Origin, definition.Density,
                    definition.GravityScale, definition.Angle, definition.FixedRotation);
                character.LinearDamping = definition.LinearDamping;
                character.AngularDamping = definition.AngularDamping;
                character.Friction = definition.Friction;
                character.Restitution = definition.Restitution;
                characters.Add(character);
            }
            Characters = characters.AsReadOnly();
            Candies = candies.AsReadOnly();
            LoadStars(layout, design);
            Stars = stars.AsReadOnly();
            StarCollections = starCollections.AsReadOnly();
            Gameplay = play ? new ForestGameplay(this, layout, level, forestArrival) : null;
        }

        /// <summary>Advances one tick, optionally applying a recorded diagnostic release before that tick.</summary>
        /// <param name="releaseAt">Zero-based tick to release all candy attachments, or -1 for no release.</param>
        /// <remarks>Release removes final links; it does not implement the native swipe/cut-fragment algorithm.</remarks>
        public void Step(int releaseAt = -1)
        {
            if (releaseAt < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(releaseAt));
            }
            if (Tick == releaseAt)
            {
                foreach (RopeChain rope in ropes.Where(r => !r.Released && r.CutGap < 0))
                {
                    _ = world.Remove(rope.Joints[^1]);
                    if (rope.Stretch != null)
                    {
                        _ = world.Remove(rope.Stretch);
                    }
                    rope.Released = true;
                }
            }
            CollectStars();
            Gameplay?.BeforeStep();
            world.Step();
            foreach (RopeChain rope in ropes.Where(r => !r.Released && r.CutGap < 0))
            {
                UpdateRope(rope);
            }
            Tick = checked(Tick + 1);
            Gameplay?.AfterStep();
        }

        /// <summary>Snapshots a chain's current world centers in logical scene units.</summary>
        /// <param name="index">Zero-based authored rope index.</param>
        /// <returns>A read-only polyline including the original endpoints.</returns>
        public IReadOnlyList<Vector2> RopePoints(int index)
        {
            return Array.AsReadOnly(ropes[index].Points.Select(p => p.Position * 200).ToArray());
        }

        /// <summary>Snapshots the chain's bone limits or spring rest lengths in meters.</summary>
        /// <param name="index">Zero-based authored rope index.</param>
        /// <returns>The per-bone lengths, excluding a whole-rope stretch spring.</returns>
        public IReadOnlyList<float> RopeLengths(int index)
        {
            return Array.AsReadOnly(ropes[index].Joints.Select(j => j.Length).ToArray());
        }

        /// <summary>Reports whether diagnostic release removed the candy attachment.</summary>
        /// <param name="index">Zero-based authored rope index.</param>
        /// <returns>True after release.</returns>
        public bool IsReleased(int index)
        {
            return ropes[index].Released;
        }

        /// <summary>Applies the traced material-point damping and stretch-joint transitions after the world step.</summary>
        /// <param name="rope">Unreleased chain.</param>
        private void UpdateRope(RopeChain rope)
        {
            PhysicsBody head = rope.Points[0], tail = rope.Points[^1];
            rope.Speeds.Enqueue((head.Velocity - tail.Velocity).Length());
            if (rope.Speeds.Count > 60)
            {
                _ = rope.Speeds.Dequeue();
            }
            float average = 0;
            foreach (float speed in rope.Speeds)
            {
                average += speed;
            }
            average /= rope.Speeds.Count;
            for (int i = 1; i < rope.Points.Count - 1; i++)
            {
                rope.Points[i].LinearDamping = average > 0.5f ? 0 : MathF.Exp(average * -3) * 5;
            }
            float distance = (tail.Position - head.Position).Length();
            PhysicsJoint last = rope.Joints[^1];
            if (rope.Stretch == null && ((last.ReactionForce.Length() > 0.001f && distance > rope.Length) || distance > rope.Length * 1.5))
            {
                _ = world.Remove(last);
                rope.Joints[^1] = world.AddSpring(rope.Points[^2], tail, last.Length, 10, 0.3f);
                rope.Stretch = world.AddSpring(head, tail, rope.Length, rope.Frequency, 0);
            }
            if (rope.Stretch != null && Vector2.Dot(rope.Stretch.ReactionForce, head.Position - tail.Position) < 0)
            {
                _ = world.Remove(rope.Stretch);
                rope.Stretch = null;
                last = rope.Joints[^1];
                _ = world.Remove(last);
                rope.Joints[^1] = world.AddRope(rope.Points[^2], tail, last.Length);
            }
        }

        /// <summary>Creates a supported zero-velocity circle body from the matching authored design.</summary>
        /// <param name="design">Circle design record.</param>
        /// <param name="position">Center in meters.</param>
        /// <param name="isStatic">Whether a static grab is expected.</param>
        /// <returns>The owned body.</returns>
        private PhysicsBody CreateBody(LevelElement design, Vector2 position, bool isStatic)
        {
            string expected = isStatic ? "static" : "dynamic";
            if (design.Kind != "circle" || !design.Attributes.TryGetValue("body_type", out string? type) || type != expected ||
                Value(design, "linearVelocityX") != 0 || Value(design, "linearVelocityY") != 0 || Value(design, "angularVelocity") != 0)
            {
                throw new InvalidDataException("Unsupported physics body design or nonzero initial velocity.");
            }
            float density = Value(design, "density"), radius = Value(design, "radius") / 100;
            if (density <= 0 || radius <= 0)
            {
                throw new InvalidDataException("Physics circle density and radius must be positive.");
            }
            PhysicsBody body = world.AddCircle(position, radius, isStatic ? 0 : density, Value(design, "gravityScale"));
            body.LinearDamping = Value(design, "linearDamping");
            body.AngularDamping = Value(design, "angularDamping");
            body.Friction = Value(design, "friction", .2f);
            body.Restitution = Value(design, "restitution", 0);
            body.IsSensor = isStatic || design.Attributes.GetValueOrDefault("id", "") == "ropeMaterialPoint";
            return body.AngularDamping < 0 || body.Friction < 0 || body.Friction > 100 || body.Restitution < 0 || body.Restitution > 10
                ? throw new InvalidDataException("Unsupported circle material coefficients.")
                : body;
        }

        /// <summary>Finds one uniquely named master design.</summary>
        /// <param name="design">Master design document.</param>
        /// <param name="id">Required design ID.</param>
        /// <returns>The matching design element.</returns>
        private static LevelElement Find(LevelDefinition design, string id)
        {
            LevelElement[] matches = [.. design.Objects.Where(o => o.Attributes.TryGetValue("id", out string? value) && value == id)];
            return matches.Length != 1 ? throw new InvalidDataException("Expected one master design: " + id) : matches[0];
        }

        /// <summary>Reads a finite bounded design value with an optional missing-value fallback.</summary>
        /// <param name="element">Authored object.</param>
        /// <param name="name">Attribute name.</param>
        /// <param name="fallback">Value for an absent optional attribute.</param>
        /// <returns>The parsed value.</returns>
        private static float Value(LevelElement element, string name, float? fallback = null)
        {
            return !element.Attributes.TryGetValue(name, out string? text) && fallback is float value
                ? value
                : !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) || !float.IsFinite(result) || MathF.Abs(result) > 1000000
                ? throw new InvalidDataException("Invalid physics design attribute: " + name) : result;
        }

        /// <summary>Owns mutable constraints and moving-average state for one authored rope.</summary>
        private sealed class RopeChain
        {
            /// <summary>Head, intermediate material points and candy tail.</summary>
            internal readonly List<PhysicsBody> Points;
            /// <summary>One active constraint per adjacent pair, except after diagnostic release.</summary>
            internal readonly List<PhysicsJoint> Joints;
            /// <summary>Native 60-sample endpoint relative-speed history.</summary>
            internal readonly Queue<float> Speeds = new();
            /// <summary>Whole-rope authored rest length in meters.</summary>
            internal readonly float Length;
            /// <summary>Whole-rope stretch spring frequency.</summary>
            internal readonly float Frequency;
            /// <summary>Whole-rope stretch spring, if active.</summary>
            internal PhysicsJoint? Stretch;
            /// <summary>Whether the final attachment has been released diagnostically.</summary>
            internal bool Released;
            /// <summary>Disconnected edge between two cut tips, or -1 before cutting.</summary>
            internal int CutGap = -1;

            /// <summary>Initializes chain ownership and authored spring parameters.</summary>
            /// <param name="points">Ordered bodies.</param>
            /// <param name="joints">Ordered segment constraints.</param>
            /// <param name="length">Whole-rope length in meters.</param>
            /// <param name="frequency">Whole-rope stretch frequency.</param>
            internal RopeChain(List<PhysicsBody> points, List<PhysicsJoint> joints, float length, float frequency)
            {
                Points = points;
                Joints = joints;
                Length = length;
                Frequency = frequency;
            }
        }
    }

    /// <summary>Converts monotonic host elapsed time into original fixed ticks without dropping elapsed time.</summary>
    public sealed class SimulationClock
    {
        /// <summary>Completed host-clock ticks.</summary>
        private long ticks;
        /// <summary>Last accepted absolute elapsed time.</summary>
        private double lastTime;

        /// <summary>Advances toward absolute elapsed host time, retaining any catch-up backlog.</summary>
        /// <param name="seconds">Finite nonnegative monotonic seconds since the preview began.</param>
        /// <param name="step">Callback performing one fixed simulation tick.</param>
        /// <returns>The number of callbacks performed, capped at 600 per call.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Time is invalid or moves backward.</exception>
        public int AdvanceTo(double seconds, Action step)
        {
            if (!double.IsFinite(seconds) || seconds < lastTime || seconds > int.MaxValue * (double)PhysicsWorld.TimeStep)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }
            lastTime = seconds;
            long target = (long)Math.Floor((seconds / PhysicsWorld.TimeStep) + 1e-9);
            int count = (int)Math.Min(600, target - ticks);
            for (int i = 0; i < count; i++)
            {
                step();
                ticks++;
            }
            return count;
        }
    }
}
