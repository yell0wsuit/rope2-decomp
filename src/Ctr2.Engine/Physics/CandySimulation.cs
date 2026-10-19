using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>An authored circle or rectangle shape and its body.</summary>
    /// <param name="Kind">Authored element name, such as <c>rectangle_materialized</c>.</param>
    /// <param name="Design">Merged material and authored design.</param>
    /// <param name="Body">Static, kinematic or dynamic body.</param>
    public sealed record GeneralShape(string Kind, IReadOnlyDictionary<string, string> Design, PhysicsBody Body);

    /// <summary>A diagnostic scene with authored candy, grab, rope, stationary star and static terrain design data.</summary>
    public sealed partial class CandySimulation
    {
        /// <summary>Gets the world owning every scene body and constraint.</summary>
        internal PhysicsWorld World { get; } = new();
        /// <summary>Authored rope chains in grab order.</summary>
        private readonly List<RopeChain> ropes = [];
        /// <summary>Gets optional gameplay state; null retains diagnostic behavior.</summary>
        public LevelGameplay? Gameplay
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

        /// <summary>Gets authored circle and rectangle shapes, static or dynamic, in authored order.</summary>
        /// <value>Bodies built by the design body factory, with their merged designs.</value>
        public IReadOnlyList<GeneralShape> Shapes
        {
            get;
        }

        /// <summary>Gets the number of authored grab ropes.</summary>
        /// <value>The number of independent chains, including released chains.</value>
        public int RopeCount => ropes.Count;

        /// <summary>Creates a center-attachment simulation using a parsed level and matching master design.</summary>
        /// <param name="level">Original editor-space level.</param>
        /// <param name="design">Matching master design document.</param>
        /// <param name="play">Whether to enable the supported gameplay slice.</param>
        /// <param name="firstLevelArrival">Whether the host selected the first production level, which has a special arrival.</param>
        /// <exception cref="InvalidDataException">A required design, target or supported field is invalid.</exception>
        /// <remarks>Circle and compound-polygon/static-terrain contacts are supported; polygon CCD, sleeping, dynamic-body collisions and gameplay state remain pending.</remarks>
        public CandySimulation(LevelDefinition level, LevelDefinition design, bool play = false, bool firstLevelArrival = false)
        {
            LevelLayout layout = LevelLayout.Create(level);
            CharacterDefinitions = PolygonBodyDefinition.LoadCharacters(level, design);
            Terrain = TerrainGeometry.Build(level, design);
            IReadOnlyDictionary<string, string> candyDesign = Fields(Find(design, "candy")), grabDesign = Fields(Find(design, "grab"));
            pointDesign = Fields(Find(design, "ropeMaterialPoint"));
            LevelElement ropeDesign = Find(design, "rope_design");
            float bone = Value(ropeDesign, "bone_length") / 100;
            float frequency = Value(ropeDesign, "stretch_hertz");
            if (bone <= 0 || frequency <= 0)
            {
                throw new InvalidDataException("Rope bone length and stretch frequency must be positive.");
            }
            Dictionary<PlacedLevelObject, PhysicsBody> candyBodies = new(ReferenceEqualityComparer.Instance), shapeBodies = new(ReferenceEqualityComparer.Instance);
            List<PhysicsBody> candies = [], characters = [];
            List<GeneralShape> shapes = [];
            Dictionary<PlacedLevelObject, PhysicsBody> grabs = [];
            int terrainIndex = 0, characterIndex = 0;
            // Like the scene loader, every object creates its body in document order; ropes follow in a post-pass.
            foreach (PlacedLevelObject placed in layout.Objects)
            {
                string kind = placed.Source.Kind;
                switch (kind)
                {
                    case "polygon_materialized":
                        TerrainShape shape = Terrain[terrainIndex++];
                        if (!shape.IsSensor)
                        {
                            foreach (IReadOnlyList<Vector2> triangle in shape.Triangles)
                            {
                                World.AddTerrain(triangle, shape.Friction, shape.Restitution);
                            }
                        }
                        break;
                    case "candy":
                        PhysicsBody candy = BodyFactory.Create(World, candyDesign, placed.Position!.Value / 200, 0);
                        candies.Add(candy);
                        candyBodies.Add(placed, candy);
                        break;
                    case "grab":
                        if (placed.Source.Attributes.TryGetValue("balloons", out string? balloons) && balloons != "0")
                        {
                            throw new InvalidDataException("Balloon-generating grabs are not supported by this physics slice.");
                        }
                        grabs.Add(placed, BodyFactory.Create(World, grabDesign, placed.Position!.Value / 200, 0));
                        break;
                    case "omnom":
                        characters.Add(AddCharacter(CharacterDefinitions[characterIndex++]));
                        break;
                    case "star":
                        AddStar(placed, design);
                        break;
                    default:
                        if (kind.StartsWith("circle", StringComparison.Ordinal) || kind.StartsWith("rectangle", StringComparison.Ordinal))
                        {
                            IReadOnlyDictionary<string, string> fields = BodyFactory.MaterializedDesign(placed.Source, design);
                            GeneralShape general = new(kind, fields, BodyFactory.Create(World, fields));
                            shapes.Add(general);
                            shapeBodies.Add(placed, general.Body);
                        }
                        break;
                }
            }
            if (candies.Count == 0)
            {
                throw new InvalidDataException("Physics preview requires at least one candy.");
            }
            foreach (PlacedLevelObject grab in layout.Objects.Where(o => o.Source.Kind == "grab"))
            {
                // Like the scene, an absent target name means "candy" and an unregistered name keeps no rope.
                if (!layout.Named.TryGetValue(grab.Source.Attributes.GetValueOrDefault("target_id", "candy"), out PlacedLevelObject? named))
                {
                    continue;
                }
                bool candy = candyBodies.TryGetValue(named, out PhysicsBody? target);
                if (!candy && !shapeBodies.TryGetValue(named, out target))
                {
                    throw new InvalidDataException("Ropes support candy and circle/rectangle shape targets only.");
                }
                float x = Value(grab.Source, "target_x", 0), y = Value(grab.Source, "target_y", 0), lengthValue = Value(grab.Source, "length");
                ropes.Add(CreateRope(grabs[grab], target!, lengthValue + lengthValue, Vector2.Zero, new(x + x, y + y), false, candy, bone, frequency));
            }
            Characters = characters.AsReadOnly();
            Candies = candies.AsReadOnly();
            Shapes = shapes.AsReadOnly();
            Stars = stars.AsReadOnly();
            StarCollections = starCollections.AsReadOnly();
            Gameplay = play ? new LevelGameplay(this, layout, firstLevelArrival) : null;
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
                    _ = World.Remove(rope.Joints[^1]);
                    if (rope.Stretch != null)
                    {
                        _ = World.Remove(rope.Stretch);
                    }
                    rope.Released = true;
                }
            }
            CollectStars();
            Gameplay?.BeforeStep();
            World.Step();
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
            RopeChain rope = ropes[index];
            return Array.AsReadOnly(Enumerable.Range(0, rope.Points.Count).Select(i => BodyPosition(rope, i)).ToArray());
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

        /// <summary>Creates a scene rope between two body origins, with logical attach shifts as end anchors.</summary>
        /// <param name="head">Head body, normally a grab.</param>
        /// <param name="tail">Tail body.</param>
        /// <param name="lengthPx">Rope length in logical units.</param>
        /// <param name="headShift">Head attach shift in logical units, in the head's local frame.</param>
        /// <param name="tailShift">Tail attach shift in logical units, in the tail's local frame.</param>
        /// <param name="slowHead">Whether rope tension damps the head, as candy and Bookas do.</param>
        /// <param name="slowTail">Whether rope tension damps the tail.</param>
        /// <param name="bone">Bone length in meters.</param>
        /// <param name="frequency">Whole-rope stretch frequency.</param>
        /// <returns>The owned chain.</returns>
        private RopeChain CreateRope(PhysicsBody head, PhysicsBody tail, float lengthPx, Vector2 headShift, Vector2 tailShift, bool slowHead, bool slowTail,
            float bone, float frequency)
        {
            float length = lengthPx / 200;
            int generated = (int)(length / bone);
            if (length <= 0 || generated > 1024)
            {
                throw new InvalidDataException("Rope length or generated point count exceeds physics bounds.");
            }
            float remainder = length - (generated * bone);
            // The native comparison runs in double precision.
            if (Math.Abs((double)remainder) < 0.00000011920929)
            {
                generated--;
                remainder = bone;
            }
            // Material points are spaced between the body origins, not the anchors.
            List<PhysicsBody> points = [head];
            Vector2 position = head.Origin;
            Vector2 shift = generated > 0 ? (tail.Origin - head.Origin) / generated : Vector2.Zero;
            for (int i = 0; i < generated; i++)
            {
                position += shift;
                points.Add(BodyFactory.Create(World, pointDesign, position, 0));
            }
            points.Add(tail);
            List<PhysicsJoint> constraints = [];
            for (int i = 0; i < points.Count - 1; i++)
            {
                bool last = i == points.Count - 2;
                constraints.Add(World.AddRope(points[i], points[i + 1], new PhysicsRopeDefinition(i == 0 ? Meters(headShift) : Vector2.Zero,
                    last ? Meters(tailShift) : Vector2.Zero, last ? remainder : bone, i == 0 && last)));
            }
            return new(points, constraints, length, bone, frequency, headShift, tailShift, slowHead, slowTail);
        }

        /// <summary>Applies the scene rope's damping and stretch-joint transitions after the world step.</summary>
        /// <param name="rope">Unreleased, uncut chain.</param>
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
            bool stretched = rope.Stretch != null;
            float slow = stretched && average > 2 ? .3f : stretched ? .1f : 0;
            if (rope.SlowHead)
            {
                head.LinearDamping = slow;
            }
            if (rope.SlowTail)
            {
                tail.LinearDamping = slow;
            }
            float actual = ActualLength(rope);
            // The native lookup returns the newest joint between the last point and the tail: the whole-rope spring
            // when a rope without material points is stretched.
            PhysicsJoint measured = rope.Points.Count == 2 && rope.Stretch != null ? rope.Stretch : rope.Joints[^1];
            float force = measured.ReactionForce.Length();
            float lastLength = rope.Length - ((rope.Points.Count - 2) * rope.Bone);
            Vector2 lastAnchor = rope.Points.Count == 2 ? Meters(rope.HeadShift) : Vector2.Zero;
            if (!stretched && ((force > 0.001 && actual > rope.Length) || actual > rope.Length * 1.5))
            {
                _ = World.Remove(rope.Joints[^1]);
                rope.Joints[^1] = World.AddDistance(rope.Points[^2], tail, new PhysicsDistanceDefinition(lastAnchor, Meters(rope.TailShift), lastLength, 10, .3f));
                rope.Stretch = World.AddDistance(head, tail, new PhysicsDistanceDefinition(Meters(rope.HeadShift), Meters(rope.TailShift), rope.Length,
                    rope.Frequency, 0, true));
            }
            // Only the sign matters; native measures this force at 60 Hz and the origins rather than the centers.
            if (rope.Stretch != null && Vector2.Dot(rope.Stretch.ReactionForce, head.Origin - tail.Origin) < 0)
            {
                _ = World.Remove(rope.Stretch);
                rope.Stretch = null;
                _ = World.Remove(rope.Joints[^1]);
                rope.Joints[^1] = World.AddRope(rope.Points[^2], tail, new PhysicsRopeDefinition(lastAnchor, Meters(rope.TailShift), lastLength));
            }
        }

        /// <summary>Measures between the rotated attach points of the head and tail, like the native rope.</summary>
        /// <param name="rope">Chain to measure.</param>
        /// <returns>The anchor separation in meters.</returns>
        private static float ActualLength(RopeChain rope)
        {
            PhysicsBody head = rope.Points[0], tail = rope.Points[^1];
            Vector2 a = head.Position + PhysicsMath.Rotate(Meters(rope.HeadShift), head.Angle);
            Vector2 b = tail.Position + PhysicsMath.Rotate(Meters(rope.TailShift), tail.Angle);
            return (b - a).Length();
        }

        /// <summary>Gets a rope body's drawn and cut position in logical units, with the table-rotated attach shift.</summary>
        /// <param name="rope">Owning chain.</param>
        /// <param name="index">Index into the chain's bodies.</param>
        /// <returns>The center, plus the rotated shift for the head or tail.</returns>
        private static Vector2 BodyPosition(RopeChain rope, int index)
        {
            PhysicsBody body = rope.Points[index];
            Vector2 position = body.Position * 200;
            if (index == 0)
            {
                position += TableRotate(rope.HeadShift, body.Angle);
            }
            if (index == rope.Points.Count - 1)
            {
                position += TableRotate(rope.TailShift, body.Angle);
            }
            return position;
        }

        /// <summary>Converts logical units to meters as native <c>toB2Vector</c> does.</summary>
        /// <param name="value">Logical vector.</param>
        /// <returns>The vector divided by 200.</returns>
        private static Vector2 Meters(Vector2 value)
        {
            return value / 200;
        }

        /// <summary>Creates one simulated character from its validated authored definition.</summary>
        /// <param name="definition">Authored compound polygon definition.</param>
        /// <returns>The owned dynamic body.</returns>
        private PhysicsBody AddCharacter(PolygonBodyDefinition definition)
        {
            PhysicsBody character = World.AddPolygon(definition.Shape, definition.Origin, definition.Density,
                definition.GravityScale, definition.Angle, definition.FixedRotation);
            character.LinearDamping = definition.LinearDamping;
            character.AngularDamping = definition.AngularDamping;
            character.Friction = definition.Friction;
            character.Restitution = definition.Restitution;
            return character;
        }

        /// <summary>Copies a master design like the master loader: its attributes plus its tag as <c>shapeType</c>.</summary>
        /// <param name="design">Master design record.</param>
        /// <returns>A read-only attribute dictionary.</returns>
        private static IReadOnlyDictionary<string, string> Fields(LevelElement design)
        {
            return new Dictionary<string, string>(design.Attributes, StringComparer.Ordinal) { ["shapeType"] = design.Kind }.AsReadOnly();
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
        private sealed class RopeChain(List<PhysicsBody> points, List<PhysicsJoint> joints, float length, float bone, float frequency,
            Vector2 headShift, Vector2 tailShift, bool slowHead, bool slowTail)
        {
            /// <summary>Head, intermediate material points and tail.</summary>
            internal readonly List<PhysicsBody> Points = points;
            /// <summary>One active constraint per adjacent pair, except after diagnostic release.</summary>
            internal readonly List<PhysicsJoint> Joints = joints;
            /// <summary>Native 60-sample endpoint relative-speed history.</summary>
            internal readonly Queue<float> Speeds = new();
            /// <summary>Whole-rope authored rest length in meters.</summary>
            internal readonly float Length = length;
            /// <summary>Bone length in meters.</summary>
            internal readonly float Bone = bone;
            /// <summary>Whole-rope stretch spring frequency.</summary>
            internal readonly float Frequency = frequency;
            /// <summary>Head attach shift in logical units, in the head's local frame.</summary>
            internal readonly Vector2 HeadShift = headShift;
            /// <summary>Tail attach shift in logical units, in the tail's local frame.</summary>
            internal readonly Vector2 TailShift = tailShift;
            /// <summary>Whether rope tension damps the head.</summary>
            internal readonly bool SlowHead = slowHead;
            /// <summary>Whether rope tension damps the tail.</summary>
            internal readonly bool SlowTail = slowTail;
            /// <summary>Whole-rope stretch spring, if active.</summary>
            internal PhysicsJoint? Stretch;
            /// <summary>Whether the final attachment has been released diagnostically.</summary>
            internal bool Released;
            /// <summary>Disconnected edge between two cut tips, or -1 before cutting.</summary>
            internal int CutGap = -1;
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
