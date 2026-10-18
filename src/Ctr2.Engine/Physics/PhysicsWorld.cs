// Altered C# subset of Box2D 2.2.1 by Erin Catto. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A circle or compound polygon body for supported fixed-step physics.</summary>
    public sealed partial class PhysicsBody
    {
        /// <summary>Gets the center in meters.</summary>
        /// <value>The current world-space center.</value>
        public Vector2 Position
        {
            get; internal set;
        }
        /// <summary>Gets the velocity in meters per second.</summary>
        /// <value>The current linear velocity.</value>
        public Vector2 Velocity
        {
            get; internal set;
        }
        /// <summary>Gets or sets whether this body skips supported solid contacts.</summary>
        /// <value>True for rope material points and grab sensors.</value>
        public bool IsSensor
        {
            get; set;
        }
        /// <summary>Gets the clockwise orientation in radians.</summary>
        /// <value>The integrated circle angle.</value>
        public float Angle
        {
            get; internal set;
        }
        /// <summary>Gets the clockwise angular velocity in radians per second.</summary>
        /// <value>The velocity produced by friction impulses.</value>
        public float AngularVelocity
        {
            get; internal set;
        }
        /// <summary>Gets or sets the nonnegative angular damping coefficient.</summary>
        /// <value>The coefficient applied each fixed tick.</value>
        public float AngularDamping
        {
            get;
            set => field = Coefficient(value, 1000000);
        }
        /// <summary>Gets or sets the nonnegative fixture friction coefficient.</summary>
        /// <value>The coefficient mixed with terrain using the geometric mean.</value>
        public float Friction
        {
            get;
            set => field = Coefficient(value, 100);
        } = .2f;
        /// <summary>Gets or sets the nonnegative coefficient of restitution.</summary>
        /// <value>The coefficient mixed with terrain using the maximum.</value>
        public float Restitution
        {
            get;
            set => field = Coefficient(value, 10);
        }

        /// <summary>Rejects invalid public contact coefficients before assignment.</summary>
        /// <param name="value">Requested coefficient.</param>
        /// <param name="maximum">Supported upper bound.</param>
        /// <returns>The validated coefficient.</returns>
        /// <exception cref="ArgumentOutOfRangeException">The value is nonfinite or outside bounds.</exception>
        private static float Coefficient(float value, float maximum)
        {
            return float.IsFinite(value) && value >= 0 && value <= maximum ? value : throw new ArgumentOutOfRangeException(nameof(value));
        }

        /// <summary>Gets the inverse centroid moment of inertia of the owned fixtures.</summary>
        /// <value>Zero for static bodies.</value>
        internal float InverseInertia => Mass <= 0 || FixedRotation ? 0 : PolygonFixtures.Count > 0 ? polygonInverseInertia * (polygonInitialMass / Mass) : 2 / (Mass * Radius * Radius);
        /// <summary>Gets the circular fixture mass in kilograms; zero denotes a static body.</summary>
        /// <value>Density times circle area for a dynamic body.</value>
        public float Mass
        {
            get; internal set;
        }
        /// <summary>Gets the inverse mass used by constraints.</summary>
        /// <value>Zero for static bodies.</value>
        internal float InverseMass => Mass > 0 ? 1 / Mass : 0;
        /// <summary>Gets the multiplier applied to world gravity.</summary>
        /// <value>The authored gravity scale.</value>
        public float GravityScale
        {
            get; internal set;
        }
        /// <summary>Gets the circle radius in meters, or zero for a compound polygon.</summary>
        /// <value>The authored radius, independent of visual dimensions.</value>
        public float Radius
        {
            get;
        }

        /// <summary>Gets or sets nonnegative linear damping in inverse seconds.</summary>
        /// <value>The coefficient in Box2D 2.2.1's clamped linear damping formula.</value>
        /// <exception cref="ArgumentOutOfRangeException">The coefficient is negative or nonfinite.</exception>
        public float LinearDamping
        {
            get;
            set => field = !float.IsFinite(value) || value < 0 ? throw new ArgumentOutOfRangeException(nameof(value)) : value;
        }

        /// <summary>Creates a validated body; construction is owned by its world.</summary>
        /// <param name="position">Initial center in meters.</param>
        /// <param name="radius">Positive fixture radius.</param>
        /// <param name="density">Fixture density, or zero for a static body.</param>
        /// <param name="gravityScale">Gravity multiplier.</param>
        internal PhysicsBody(Vector2 position, float radius, float density, float gravityScale)
        {
            Position = position;
            Radius = radius;
            Mass = density > 0 ? density * MathF.PI * radius * radius : 0;
            GravityScale = gravityScale;
        }
    }

    /// <summary>A Box2D 2.2.1 rope or soft distance constraint with rotating local anchors.</summary>
    public sealed class PhysicsJoint
    {
        /// <summary>First body.</summary>
        internal readonly PhysicsBody A;
        /// <summary>Second body.</summary>
        internal readonly PhysicsBody B;
        /// <summary>Positive spring frequency, or zero for a unilateral rope limit.</summary>
        private readonly float frequency;
        /// <summary>Spring damping ratio.</summary>
        private readonly float damping;
        /// <summary>Cached constraint direction.</summary>
        private Vector2 direction;
        /// <summary>Effective constraint mass.</summary>
        private float mass;
        /// <summary>Initial separation for predictive rope constraints.</summary>
        private float separation;
        /// <summary>Spring softness.</summary>
        private float gamma;
        /// <summary>Spring position bias.</summary>
        private float bias;
        /// <summary>Accumulated warm-start impulse.</summary>
        private float impulse;
        private readonly Vector2 localAnchorA;
        private readonly Vector2 localAnchorB;
        private Vector2 armA;
        private Vector2 armB;
        /// <summary>Gets the rope limit or spring rest length in meters.</summary>
        /// <value>The configured nonnegative length.</value>
        public float Length
        {
            get;
        }
        /// <summary>Gets the reaction force on body B from the latest solver initialization.</summary>
        /// <value>A force vector in newtons, based on the fixed timestep.</value>
        public Vector2 ReactionForce => impulse / PhysicsWorld.TimeStep * direction;

        /// <summary>Gets the current world-space first anchor in meters.</summary>
        public Vector2 AnchorA => A.Position + Rotate(localAnchorA - A.LocalCenter, A.Angle);
        /// <summary>Gets the current world-space second anchor in meters.</summary>
        public Vector2 AnchorB => B.Position + Rotate(localAnchorB - B.LocalCenter, B.Angle);

        /// <summary>Creates a constraint with body-origin local anchors.</summary>
        /// <param name="a">First body.</param>
        /// <param name="b">Second body.</param>
        /// <param name="length">Positive rest length or upper limit.</param>
        /// <param name="frequency">Zero for rope; positive for spring.</param>
        /// <param name="damping">Spring damping ratio.</param>
        /// <param name="localAnchorA">First body-local anchor.</param>
        /// <param name="localAnchorB">Second body-local anchor.</param>
        internal PhysicsJoint(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping, Vector2 localAnchorA, Vector2 localAnchorB)
        {
            A = a;
            B = b;
            Length = length;
            this.frequency = frequency;
            this.damping = damping;
            this.localAnchorA = localAnchorA;
            this.localAnchorB = localAnchorB;
        }

        /// <summary>Computes effective mass and applies last step's accumulated impulse.</summary>
        /// <param name="firstStep">Whether previous-step impulses must be zeroed.</param>
        internal void Initialize(bool firstStep)
        {
            armA = Rotate(localAnchorA - A.LocalCenter, A.Angle);
            armB = Rotate(localAnchorB - B.LocalCenter, B.Angle);
            direction = B.Position + armB - A.Position - armA;
            separation = direction.Length();
            if (separation > PhysicsWorld.LinearSlop)
            {
                direction *= 1 / separation;
            }
            else
            {
                direction = Vector2.Zero;
                if (frequency == 0)
                {
                    mass = impulse = 0;
                    return;
                }
            }
            float crossA = Cross(armA, direction), crossB = Cross(armB, direction);
            float inverseMass = A.InverseMass + (A.InverseInertia * crossA * crossA) +
                B.InverseMass + (B.InverseInertia * crossB * crossB);
            mass = inverseMass != 0 ? 1 / inverseMass : 0;
            gamma = bias = 0;
            if (frequency > 0)
            {
                float omega = 2 * MathF.PI * frequency;
                float d = 2 * mass * damping * omega;
                float k = mass * omega * omega;
                float h = PhysicsWorld.TimeStep;
                gamma = h * (d + (h * k));
                gamma = gamma != 0 ? 1 / gamma : 0;
                bias = (separation - Length) * h * k * gamma;
                inverseMass += gamma;
                mass = inverseMass != 0 ? 1 / inverseMass : 0;
            }
            if (firstStep)
            {
                impulse = 0;
            }
            ApplyVelocity(impulse * direction);
        }

        /// <summary>Solves the predictive rope limit or soft distance velocity constraint.</summary>
        internal void SolveVelocity()
        {
            Vector2 velocityA = A.Velocity + (A.AngularVelocity * new Vector2(-armA.Y, armA.X));
            Vector2 velocityB = B.Velocity + (B.AngularVelocity * new Vector2(-armB.Y, armB.X));
            float speed = Vector2.Dot(direction, velocityB - velocityA);
            if (frequency == 0 && separation < Length)
            {
                speed += 1 / PhysicsWorld.TimeStep * (separation - Length);
            }
            float change = -mass * (speed + bias + (gamma * impulse));
            float previous = impulse;
            impulse = frequency == 0 ? MathF.Min(0, impulse + change) : impulse + change;
            // Rope impulses clamp the accumulated limit; native distance joints apply the raw increment.
            ApplyVelocity((frequency == 0 ? impulse - previous : change) * direction);
        }

        /// <summary>Corrects excessive rope length; soft springs defer correction to the velocity solver.</summary>
        /// <returns>Whether the uncorrected rope error is below the CTR2 linear slop.</returns>
        internal bool SolvePosition()
        {
            if (frequency > 0)
            {
                return true;
            }
            Vector2 rA = Rotate(localAnchorA - A.LocalCenter, A.Angle);
            Vector2 rB = Rotate(localAnchorB - B.LocalCenter, B.Angle);
            Vector2 u = B.Position + rB - A.Position - rA;
            float length = u.Length();
            if (length >= 1.1920929e-7f)
            {
                u *= 1 / length;
            }
            Vector2 correction = -mass * Math.Clamp(length - Length, 0, 0.2f) * u;
            A.Position -= A.InverseMass * correction;
            A.Angle -= A.InverseInertia * Cross(rA, correction);
            B.Position += B.InverseMass * correction;
            B.Angle += B.InverseInertia * Cross(rB, correction);
            return length - Length < PhysicsWorld.LinearSlop;
        }

        /// <summary>Applies equal and opposite impulses at the rotating anchors.</summary>
        /// <param name="value">Impulse vector on body B.</param>
        private void ApplyVelocity(Vector2 value)
        {
            A.Velocity -= A.InverseMass * value;
            A.AngularVelocity -= A.InverseInertia * Cross(armA, value);
            B.Velocity += B.InverseMass * value;
            B.AngularVelocity += B.InverseInertia * Cross(armB, value);
        }

        private static float Cross(Vector2 a, Vector2 b) => (a.X * b.Y) - (a.Y * b.X);

        private static Vector2 Rotate(Vector2 value, float angle)
        {
            float sine = MathF.Sin(angle), cosine = MathF.Cos(angle);
            return new((cosine * value.X) - (sine * value.Y), (sine * value.X) + (cosine * value.Y));
        }
    }

    /// <summary>Always-awake Box2D subset with local-anchor joints, circle pairs and circle/polygon terrain contacts.</summary>
    public sealed partial class PhysicsWorld
    {
        /// <summary>Original game fixed timestep in seconds.</summary>
        public const float TimeStep = 0.0167f;
        /// <summary>CTR2's tightened rope position tolerance in meters.</summary>
        public const float LinearSlop = 0.001f;
        /// <summary>Body creation order; Box2D traverses its linked body list in reverse order.</summary>
        private readonly List<PhysicsBody> bodies = [];
        /// <summary>Joint creation order; each body traverses its joint edges in reverse order.</summary>
        private readonly List<PhysicsJoint> joints = [];
        /// <summary>Whether an earlier world step initialized warm starting.</summary>
        private bool stepped;

        /// <summary>Checks whether a topology change fits before mutating the world.</summary>
        /// <param name="extraBodies">Additional circle count.</param>
        /// <param name="extraJoints">Additional constraint count.</param>
        /// <returns>Whether both world budgets permit the addition.</returns>
        internal bool CanAdd(int extraBodies, int extraJoints)
        {
            return bodies.Count + extraBodies <= 4096 && joints.Count + extraJoints <= 8192;
        }

        /// <summary>Adds a circular body, using zero density to request a static anchor.</summary>
        /// <param name="position">Initial center in meters.</param>
        /// <param name="radius">Positive radius in meters.</param>
        /// <param name="density">Positive dynamic fixture density, or zero for static.</param>
        /// <param name="gravityScale">Finite world gravity multiplier.</param>
        /// <returns>The newly owned body.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Values or the body budget are invalid.</exception>
        public PhysicsBody AddCircle(Vector2 position, float radius, float density, float gravityScale)
        {
            float mass = density * MathF.PI * radius * radius;
            if (bodies.Count >= 4096 || !float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius) || radius <= 0 ||
                !float.IsFinite(density) || density < 0 || !float.IsFinite(gravityScale) || !float.IsFinite(mass) || (density > 0 && mass < 1e-12f))
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }
            PhysicsBody body = new(position, radius, density, gravityScale);
            RegisterBodyProxies(body);
            bodies.Add(body);
            return body;
        }

        /// <summary>Adds a center-anchor maximum-length constraint.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="length">Nonnegative maximum separation in meters.</param>
        /// <returns>The new rope constraint.</returns>
        public PhysicsJoint AddRope(PhysicsBody a, PhysicsBody b, float length)
        {
            return AddJoint(a, b, length, 0, 0);
        }

        /// <summary>Adds a rope limit between local anchors measured from the body origins.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="length">Nonnegative maximum separation in meters.</param>
        /// <param name="localAnchorA">First body-local anchor in meters.</param>
        /// <param name="localAnchorB">Second body-local anchor in meters.</param>
        /// <returns>The owned rope constraint.</returns>
        public PhysicsJoint AddRope(PhysicsBody a, PhysicsBody b, float length, Vector2 localAnchorA, Vector2 localAnchorB)
        {
            return AddJoint(a, b, length, 0, 0, localAnchorA, localAnchorB);
        }

        /// <summary>Adds a soft center-anchor distance constraint.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="length">Positive rest length in meters.</param>
        /// <param name="frequency">Positive spring frequency in hertz.</param>
        /// <param name="damping">Nonnegative damping ratio.</param>
        /// <returns>The new soft distance constraint.</returns>
        public PhysicsJoint AddSpring(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping)
        {
            return frequency <= 0 ? throw new ArgumentOutOfRangeException(nameof(frequency)) : AddJoint(a, b, length, frequency, damping);
        }

        /// <summary>Adds a soft distance constraint between local body-origin anchors.</summary>
        /// <param name="a">First owned body.</param>
        /// <param name="b">Second owned body.</param>
        /// <param name="length">Positive rest length in meters.</param>
        /// <param name="frequency">Positive frequency in hertz.</param>
        /// <param name="damping">Nonnegative damping ratio.</param>
        /// <param name="localAnchorA">First body-local anchor in meters.</param>
        /// <param name="localAnchorB">Second body-local anchor in meters.</param>
        /// <returns>The owned distance constraint.</returns>
        public PhysicsJoint AddSpring(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping, Vector2 localAnchorA, Vector2 localAnchorB)
        {
            return frequency <= 0 ? throw new ArgumentOutOfRangeException(nameof(frequency)) :
                AddJoint(a, b, length, frequency, damping, localAnchorA, localAnchorB);
        }

        /// <summary>Removes a constraint before the next step.</summary>
        /// <param name="joint">Previously created constraint.</param>
        /// <returns>True if this call removed an active constraint.</returns>
        public bool Remove(PhysicsJoint joint)
        {
            if (!joints.Remove(joint))
            {
                return false;
            }
            ChangeCollisionBlock(joint.A, joint.B, -1);
            return true;
        }

        /// <summary>Removes an owned body and every attached constraint and cached terrain contact between steps.</summary>
        /// <param name="body">Body to retire.</param>
        /// <returns>Whether the body belonged to this world.</returns>
        public bool RemoveBody(PhysicsBody body)
        {
            if (!bodies.Remove(body))
            {
                return false;
            }
            RemoveBodyProxies(body);
            foreach (PhysicsJoint joint in joints.FindAll(j => j.A == body || j.B == body))
            {
                _ = Remove(joint);
            }
            foreach ((PhysicsBody, TerrainFixture, int) key in new List<(PhysicsBody, TerrainFixture, int)>(contacts.Keys))
            {
                if (key.Item1 == body)
                {
                    _ = contacts.Remove(key);
                }
            }
            polygonFixtureCount -= body.PolygonFixtures.Count;
            return true;
        }

        /// <summary>Advances exactly one original game tick using 15 velocity and 30 position iterations.</summary>
        public void Step()
        {
            Dictionary<PhysicsBody, (Vector2 Origin, float Angle)> proxyStarts = BeginBroadPhaseStep();
            Dictionary<PhysicsBody, (Vector2 Position, float Angle)>? starts = ContinuousPhysics && terrain.Count > 0 ? new() : null;
            if (starts != null)
            {
                foreach (PhysicsBody body in bodies)
                {
                    if (body.Mass > 0 && !body.IsSensor && body.PolygonFixtures.Count == 0)
                    {
                        starts.Add(body, (body.Position, body.Angle));
                    }
                }
            }
            List<PhysicsContact> touching = UpdateTerrainContacts();
            touching.AddRange(UpdateCircleContacts());
            List<List<PhysicsJoint>> islands = SolverOrder(touching, out List<List<PhysicsContact>> contactIslands);
            List<PhysicsJoint> ordered = [];
            foreach (List<PhysicsJoint> island in islands)
            {
                ordered.AddRange(island);
            }
            foreach (PhysicsBody body in bodies)
            {
                if (body.Mass > 0)
                {
                    body.Velocity += TimeStep * new Vector2(0, body.GravityScale * 10);
                    body.Velocity *= Math.Clamp(1 - (TimeStep * body.LinearDamping), 0, 1);
                    body.AngularVelocity *= Math.Clamp(1 - (TimeStep * body.AngularDamping), 0, 1);
                }
            }
            foreach (List<PhysicsContact> contacts in contactIslands)
            {
                foreach (PhysicsContact contact in contacts)
                {
                    contact.Initialize();
                }
                foreach (PhysicsContact contact in contacts)
                {
                    contact.WarmStart();
                }
            }
            foreach (PhysicsJoint joint in ordered)
            {
                joint.Initialize(!stepped);
            }
            for (int i = 0; i < 15; i++)
            {
                foreach (PhysicsJoint joint in ordered)
                {
                    joint.SolveVelocity();
                }
                foreach (List<PhysicsContact> contacts in contactIslands)
                {
                    foreach (PhysicsContact contact in contacts)
                    {
                        contact.SolveVelocity();
                    }
                }
            }
            foreach (PhysicsBody body in bodies)
            {
                Vector2 translation = TimeStep * body.Velocity;
                if (Vector2.Dot(translation, translation) > 4)
                {
                    body.Velocity *= 2 / translation.Length();
                }
                body.Position += TimeStep * body.Velocity;
                float rotation = TimeStep * body.AngularVelocity;
                if (rotation * rotation > MathF.PI * MathF.PI / 4)
                {
                    body.AngularVelocity *= MathF.PI / 2 / MathF.Abs(rotation);
                }
                body.Angle += TimeStep * body.AngularVelocity;
            }
            for (int islandIndex = 0; islandIndex < islands.Count; islandIndex++)
            {
                List<PhysicsJoint> island = islands[islandIndex];
                for (int i = 0; i < 30; i++)
                {
                    bool solved = true;
                    foreach (PhysicsContact contact in contactIslands[islandIndex])
                    {
                        solved = contact.SolvePosition() && solved;
                    }
                    foreach (PhysicsJoint joint in island)
                    {
                        solved = joint.SolvePosition() && solved;
                    }
                    if (solved)
                    {
                        break;
                    }
                }
            }
            if (starts != null)
            {
                SolveContinuous(starts);
            }
            polygonImpacts.Clear();
            foreach (List<PhysicsContact> island in contactIslands)
            {
                foreach (PhysicsContact contact in island)
                {
                    if (contact is PolygonContact polygon)
                    {
                        polygonImpacts.Add(polygon.CaptureImpact());
                    }
                }
            }
            SynchronizeBodyProxies(proxyStarts);
            stepped = true;
        }

        /// <summary>Validates and adds one local-anchor constraint within the world budget.</summary>
        /// <param name="a">First body.</param>
        /// <param name="b">Second body.</param>
        /// <param name="length">Positive length.</param>
        /// <param name="frequency">Zero for rope or positive for spring.</param>
        /// <param name="damping">Nonnegative spring damping ratio.</param>
        /// <param name="localAnchorA">First body-origin anchor, or null for the center of mass.</param>
        /// <param name="localAnchorB">Second body-origin anchor, or null for the center of mass.</param>
        /// <returns>The owned constraint.</returns>
        private PhysicsJoint AddJoint(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping,
            Vector2? localAnchorA = null, Vector2? localAnchorB = null)
        {
            Vector2 anchorA = localAnchorA ?? a.LocalCenter, anchorB = localAnchorB ?? b.LocalCenter;
            if (!ValidAnchor(anchorA) || !ValidAnchor(anchorB) || joints.Count >= 8192 || a == b || !bodies.Contains(a) || !bodies.Contains(b) || !float.IsFinite(length) || length < 0 || (frequency > 0 && length == 0) ||
                !float.IsFinite(frequency) || frequency < 0 || !float.IsFinite(damping) || damping < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            PhysicsJoint joint = new(a, b, length, frequency, damping, anchorA, anchorB);
            joints.Add(joint);
            ChangeCollisionBlock(a, b, 1);
            return joint;
        }

        private static bool ValidAnchor(Vector2 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) &&
            MathF.Abs(value.X) <= 10000 && MathF.Abs(value.Y) <= 10000;

        /// <summary>Traverses solid contacts before joints, preserving native body-edge and LIFO order.</summary>
        private List<List<PhysicsJoint>> SolverOrder(IReadOnlyList<PhysicsContact> active, out List<List<PhysicsContact>> contactIslands)
        {
            Dictionary<PhysicsBody, List<PhysicsJoint>> jointEdges = [];
            Dictionary<PhysicsBody, List<PhysicsContact>> contactEdges = [];
            foreach (PhysicsBody body in bodies)
            {
                jointEdges.Add(body, []);
                contactEdges.Add(body, []);
            }
            foreach (PhysicsJoint joint in joints)
            {
                jointEdges[joint.A].Add(joint);
                jointEdges[joint.B].Add(joint);
            }
            foreach (PhysicsContact contact in active)
            {
                contactEdges[contact.Body].Add(contact);
                if (contact.OtherBody != null)
                    contactEdges[contact.OtherBody].Add(contact);
            }
            foreach (List<PhysicsContact> edges in contactEdges.Values)
            {
                edges.Sort((a, b) => b.CreationOrder.CompareTo(a.CreationOrder));
            }
            HashSet<PhysicsBody> visited = [];
            HashSet<PhysicsJoint> seenJoints = [];
            HashSet<PhysicsContact> seenContacts = [];
            List<List<PhysicsJoint>> islands = [];
            contactIslands = [];
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                PhysicsBody seed = bodies[i];
                if (seed.Mass == 0 || !visited.Add(seed))
                    continue;
                List<PhysicsJoint> ordered = [];
                List<PhysicsContact> islandContacts = [];
                islands.Add(ordered);
                contactIslands.Add(islandContacts);
                Stack<PhysicsBody> stack = new();
                stack.Push(seed);
                while (stack.TryPop(out PhysicsBody? body))
                {
                    foreach (PhysicsContact contact in contactEdges[body])
                    {
                        if (!seenContacts.Add(contact))
                            continue;
                        islandContacts.Add(contact);
                        PhysicsBody? other = contact.Body == body ? contact.OtherBody : contact.Body;
                        if (other != null && other.Mass > 0 && visited.Add(other))
                            stack.Push(other);
                    }
                    List<PhysicsJoint> connected = jointEdges[body];
                    for (int j = connected.Count - 1; j >= 0; j--)
                    {
                        PhysicsJoint joint = connected[j];
                        if (!seenJoints.Add(joint))
                            continue;
                        ordered.Add(joint);
                        PhysicsBody other = joint.A == body ? joint.B : joint.A;
                        if (other.Mass > 0 && visited.Add(other))
                            stack.Push(other);
                    }
                }
            }
            return islands;
        }
    }
}
