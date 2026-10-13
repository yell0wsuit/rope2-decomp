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
        /// <summary>Gets or sets whether this circle skips solid terrain contacts.</summary>
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
        internal float InverseInertia => Mass <= 0 || FixedRotation ? 0 : PolygonFixtures.Count > 0 ? 1 / (Mass * field) : 2 / (Mass * Radius * Radius);
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
            get;
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

    /// <summary>A Box2D 2.2.1 rope or soft distance constraint restricted to body centers.</summary>
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
        /// <summary>Gets the rope limit or spring rest length in meters.</summary>
        /// <value>The configured nonnegative length.</value>
        public float Length
        {
            get;
        }
        /// <summary>Gets the reaction force on body B from the latest solver initialization.</summary>
        /// <value>A force vector in newtons, based on the fixed timestep.</value>
        public Vector2 ReactionForce => impulse / PhysicsWorld.TimeStep * direction;

        /// <summary>Creates a center-anchor constraint.</summary>
        /// <param name="a">First body.</param>
        /// <param name="b">Second body.</param>
        /// <param name="length">Positive rest length or upper limit.</param>
        /// <param name="frequency">Zero for rope; positive for spring.</param>
        /// <param name="damping">Spring damping ratio.</param>
        internal PhysicsJoint(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping)
        {
            A = a;
            B = b;
            Length = length;
            this.frequency = frequency;
            this.damping = damping;
        }

        /// <summary>Computes effective mass and applies last step's accumulated impulse.</summary>
        /// <param name="firstStep">Whether previous-step impulses must be zeroed.</param>
        internal void Initialize(bool firstStep)
        {
            direction = B.Position - A.Position;
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
            float inverseMass = A.InverseMass + B.InverseMass;
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
            float speed = Vector2.Dot(direction, B.Velocity - A.Velocity);
            if (frequency == 0 && separation < Length)
            {
                speed += 1 / PhysicsWorld.TimeStep * (separation - Length);
            }
            float change = -mass * (speed + bias + (gamma * impulse));
            float previous = impulse;
            impulse = frequency == 0 ? MathF.Min(0, impulse + change) : impulse + change;
            ApplyVelocity((impulse - previous) * direction);
        }

        /// <summary>Corrects excessive rope length; soft springs defer correction to the velocity solver.</summary>
        /// <returns>Whether the uncorrected rope error is below the CTR2 linear slop.</returns>
        internal bool SolvePosition()
        {
            if (frequency > 0)
            {
                return true;
            }
            Vector2 u = B.Position - A.Position;
            float length = u.Length();
            if (length >= 1.1920929e-7f)
            {
                u *= 1 / length;
            }
            Vector2 correction = -mass * Math.Clamp(length - Length, 0, 0.2f) * u;
            A.Position -= A.InverseMass * correction;
            B.Position += B.InverseMass * correction;
            return length - Length < PhysicsWorld.LinearSlop;
        }

        /// <summary>Applies equal and opposite center impulses.</summary>
        /// <param name="value">Impulse vector on body B.</param>
        private void ApplyVelocity(Vector2 value)
        {
            A.Velocity -= A.InverseMass * value;
            B.Velocity += B.InverseMass * value;
        }
    }

    /// <summary>Always-awake Box2D 2.2.1 subset with circle center joints and circle/polygon contacts against static terrain.</summary>
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

        /// <summary>Removes a constraint before the next step.</summary>
        /// <param name="joint">Previously created constraint.</param>
        /// <returns>True if this call removed an active constraint.</returns>
        public bool Remove(PhysicsJoint joint)
        {
            return joints.Remove(joint);
        }

        /// <summary>Advances exactly one original game tick using 15 velocity and 30 position iterations.</summary>
        public void Step()
        {
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
            List<List<PhysicsJoint>> islands = SolverOrder(out Dictionary<PhysicsBody, (int Island, int Order)>? bodyOrder);
            List<List<PhysicsContact>> contactIslands = BuildContacts(islands, bodyOrder);
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
            stepped = true;
        }

        /// <summary>Validates and adds one center constraint within the world budget.</summary>
        /// <param name="a">First body.</param>
        /// <param name="b">Second body.</param>
        /// <param name="length">Positive length.</param>
        /// <param name="frequency">Zero for rope or positive for spring.</param>
        /// <param name="damping">Nonnegative spring damping ratio.</param>
        /// <returns>The owned constraint.</returns>
        private PhysicsJoint AddJoint(PhysicsBody a, PhysicsBody b, float length, float frequency, float damping)
        {
            if (joints.Count >= 8192 || a.PolygonFixtures.Count > 0 || b.PolygonFixtures.Count > 0 || a == b || !bodies.Contains(a) || !bodies.Contains(b) || !float.IsFinite(length) || length < 0 || (frequency > 0 && length == 0) ||
                !float.IsFinite(frequency) || frequency < 0 || !float.IsFinite(damping) || damping < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }
            PhysicsJoint joint = new(a, b, length, frequency, damping);
            joints.Add(joint);
            return joint;
        }

        /// <summary>Builds Box2D's depth-first joint order, without traversing through static bodies.</summary>
        /// <param name="bodyOrder">Dynamic-body island memberships and depth-first traversal ranks.</param>
        /// <returns>The ordered constraints for all independent awake islands.</returns>
        private List<List<PhysicsJoint>> SolverOrder(out Dictionary<PhysicsBody, (int Island, int Order)> bodyOrder)
        {
            bodyOrder = [];
            Dictionary<PhysicsBody, List<PhysicsJoint>> edges = [];
            foreach (PhysicsBody body in bodies)
            {
                edges.Add(body, []);
            }
            foreach (PhysicsJoint joint in joints)
            {
                edges[joint.A].Add(joint);
                edges[joint.B].Add(joint);
            }
            HashSet<PhysicsBody> visited = [];
            HashSet<PhysicsJoint> seen = [];
            List<List<PhysicsJoint>> islands = [];
            for (int i = bodies.Count - 1; i >= 0; i--)
            {
                PhysicsBody seed = bodies[i];
                if (seed.Mass == 0 || !visited.Add(seed))
                {
                    continue;
                }
                List<PhysicsJoint> ordered = [];
                islands.Add(ordered);
                Stack<PhysicsBody> stack = new();
                stack.Push(seed);
                while (stack.TryPop(out PhysicsBody? body))
                {
                    if (body.Mass == 0)
                    {
                        continue;
                    }
                    bodyOrder.Add(body, (islands.Count - 1, bodyOrder.Count));
                    List<PhysicsJoint> connected = edges[body];
                    for (int j = connected.Count - 1; j >= 0; j--)
                    {
                        PhysicsJoint joint = connected[j];
                        if (!seen.Add(joint))
                        {
                            continue;
                        }
                        ordered.Add(joint);
                        PhysicsBody other = joint.A == body ? joint.B : joint.A;
                        if (visited.Add(other))
                        {
                            stack.Push(other);
                        }
                    }
                }
            }
            return islands;
        }
    }
}
