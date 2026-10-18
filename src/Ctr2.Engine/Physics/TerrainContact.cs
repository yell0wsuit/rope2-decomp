// Altered C# circle/static-polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsWorld
    {
        /// <summary>Owned static convex fixtures in creation order.</summary>
        private readonly List<TerrainFixture> terrain = [];
        /// <summary>Monotonic contact creation sequence, matching prepended body contact edges.</summary>
        private long contactSequence;
        /// <summary>Persistent broadphase candidates; touching manifolds retain compatible impulses.</summary>
        private readonly Dictionary<(PhysicsBody, TerrainFixture, int), TerrainCandidate> contacts = [];

        /// <summary>Adds one convex static terrain fixture; vertices are copied into the world.</summary>
        /// <param name="vertices">Three to eight counterclockwise world-space vertices in meters.</param>
        /// <param name="friction">Finite nonnegative friction coefficient.</param>
        /// <param name="restitution">Finite nonnegative restitution coefficient.</param>
        /// <param name="isSensor">Whether to initialize the terrain fixture as a sensor.</param>
        /// <exception cref="ArgumentException">Geometry or coefficients are invalid or exceed budgets.</exception>
        public void AddTerrain(IReadOnlyList<Vector2> vertices, float friction, float restitution, bool isSensor = false)
        {
            if (terrain.Count >= 4096 || vertices.Count is < 3 or > 8 ||
                !float.IsFinite(friction) || friction < 0 || friction > 100 ||
                !float.IsFinite(restitution) || restitution < 0 || restitution > 10)
            {
                throw new ArgumentException("Invalid convex terrain fixture.");
            }
            ConvexPolygon polygon = new(vertices);
            TerrainFixture fixture = new([.. polygon.Vertices], [.. polygon.Normals], friction, restitution, polygon.ComputeMass(0).Center, polygon);
            RegisterTerrainProxy(fixture, isSensor);
            terrain.Add(fixture);
        }

        /// <summary>Updates supported terrain manifolds before assembling contact-connected islands.</summary>
        private List<PhysicsContact> UpdateTerrainContacts()
        {
            List<PhysicsContact> active = [];
            foreach (KeyValuePair<(PhysicsBody, TerrainFixture, int), TerrainCandidate> entry in contacts.ToArray())
            {
                TerrainCandidate candidate = entry.Value;
                if (candidate.Body.Mass == 0 || !ShouldCollide(candidate.BodyProxy.Filter, candidate.TerrainProxy.Filter) ||
                    !broadPhase.GetBounds(candidate.BodyProxy.Id).Overlaps(broadPhase.GetBounds(candidate.TerrainProxy.Id)))
                {
                    RecordContactChange(candidate.A, candidate.B, candidate.CreationOrder, candidate.Touching, false, true);
                    _ = contacts.Remove(entry.Key);
                    continue;
                }
                PhysicsBody body = candidate.Body;
                TerrainFixture fixture = candidate.TerrainProxy.Terrain!;
                int localIndex = candidate.BodyProxy.LocalIndex;
                bool sensor = IsSensor(candidate.A, candidate.B);
                // Polygon bodies collide with terrain through the native polygon pair path, using
                // the terrain's static identity frame in native fixture orientation.
                PhysicsContact? contact = sensor ? null : body.PolygonFixtures.Count == 0 ?
                    TerrainContact.Create(body, fixture, candidate.Friction, candidate.Restitution) :
                    candidate.A.Terrain != null ?
                    PolygonBodyContact.Create(fixture.Frame, fixture.Shape, body, body.PolygonFixtures[localIndex], candidate.Friction, candidate.Restitution, true) :
                    PolygonBodyContact.Create(body, body.PolygonFixtures[localIndex], fixture.Frame, fixture.Shape, candidate.Friction, candidate.Restitution, true);
                if (contact != null)
                {
                    if (candidate.Manifold != null)
                    {
                        contact.Restore(candidate.Manifold);
                    }
                    contact.CreationOrder = candidate.CreationOrder;
                    active.Add(contact);
                }
                // Losing the manifold keeps candidate order, but clears warm-start impulses.
                bool touching = sensor ? SensorOverlap(candidate.A, candidate.B) : contact != null;
                RecordContactChange(candidate.A, candidate.B, candidate.CreationOrder, candidate.Touching, touching, false);
                candidate.Touching = touching;
                candidate.Manifold = contact;
            }
            return active;
        }
    }

    /// <summary>Immutable convex static geometry and contact material.</summary>
    /// <param name="Points">Counterclockwise vertices in meters.</param>
    /// <param name="Normals">Outward face normals.</param>
    /// <param name="Friction">Fixture friction.</param>
    /// <param name="Restitution">Fixture restitution.</param>
    /// <param name="Center">World-space polygon centroid.</param>
    /// <param name="Shape">The same world-space geometry as a convex polygon.</param>
    internal sealed record TerrainFixture(Vector2[] Points, Vector2[] Normals, float Friction, float Restitution, Vector2 Center, ConvexPolygon Shape)
    {
        /// <summary>Gets the static identity-frame body that native terrain fixtures belong to.</summary>
        /// <value>Never world-owned; used only as the static side of polygon pair manifolds.</value>
        internal PhysicsBody Frame
        {
            get;
        } = new([Shape], Vector2.Zero, 0, 0, 0, false, PhysicsBodyType.Static);
    }

    /// <summary>A single-point static convex polygon/circle manifold and sequential impulse constraint.</summary>
    internal sealed class TerrainContact : PhysicsContact
    {
        /// <summary>CTR2 polygon skin radius, twice its linear slop.</summary>
        private const float Skin = 2 * PhysicsWorld.LinearSlop;

        /// <summary>Polygon face midpoint or nearest vertex.</summary>
        private readonly Vector2 point;
        /// <summary>Manifold normal, fixed until the next narrowphase update.</summary>
        private readonly Vector2 faceNormal;
        /// <summary>Mixed coefficient of friction.</summary>
        private readonly float friction;
        /// <summary>Mixed coefficient of restitution.</summary>
        private readonly float restitution;
        /// <summary>Current world-space contact normal.</summary>
        private Vector2 normal;
        /// <summary>Current world-space tangent.</summary>
        private Vector2 tangent;
        /// <summary>Lever arm from the circle center to the averaged surface contact point.</summary>
        private Vector2 arm;
        /// <summary>Normal effective mass.</summary>
        private float normalMass;
        /// <summary>Tangent effective mass.</summary>
        private float tangentMass;
        /// <summary>Restitution velocity bias.</summary>
        private float bias;
        /// <summary>Gets the dynamic circle for body-edge traversal ordering.</summary>
        /// <value>The contacted circle.</value>
        internal override PhysicsBody Body
        {
            get;
        }
        /// <summary>Selected narrowphase feature for diagnostics; polygon/circle impulse ID remains zero.</summary>
        internal readonly int Feature;
        /// <summary>Accumulated nonnegative normal impulse.</summary>
        internal float NormalImpulse;
        /// <summary>Accumulated Coulomb friction impulse.</summary>
        internal float TangentImpulse;

        /// <summary>Creates a manifold after geometry overlap has been confirmed.</summary>
        /// <param name="body">Dynamic circle.</param>
        /// <param name="friction">Mixed friction.</param>
        /// <param name="restitution">Mixed restitution.</param>
        /// <param name="point">Face midpoint or vertex.</param>
        /// <param name="normal">Outward normal.</param>
        /// <param name="feature">Stable feature identifier.</param>
        private TerrainContact(PhysicsBody body, float friction, float restitution, Vector2 point, Vector2 normal, int feature)
        {
            Body = body;
            this.point = point;
            faceNormal = normal;
            this.friction = friction;
            this.restitution = restitution;
            Feature = feature;
        }

        /// <summary>Finds the maximum separating face and its circle Voronoi region, as native <c>b2CollidePolygonAndCircle</c>.</summary>
        /// <param name="body">Dynamic circle.</param>
        /// <param name="fixture">Static polygon.</param>
        /// <param name="friction">Friction mixed when the native contact was constructed, or null to mix now.</param>
        /// <param name="restitution">Restitution mixed when the native contact was constructed, or null to mix now.</param>
        /// <returns>A touching manifold, or null when separated.</returns>
        internal static TerrainContact? Create(PhysicsBody body, TerrainFixture fixture, float? friction = null, float? restitution = null)
        {
            float mixedFriction = friction ?? MathF.Sqrt(body.Friction * fixture.Friction);
            float mixedRestitution = restitution ?? MathF.Max(body.Restitution, fixture.Restitution);
            // Terrain vertices are stored in world space, so the polygon frame is the identity.
            Vector2 center = body.Origin;
            int face = 0, count = fixture.Points.Length;
            float separation = -float.MaxValue;
            float radius = Skin + body.Radius;
            for (int i = 0; i < count; i++)
            {
                float distance = Vector2.Dot(fixture.Normals[i], center - fixture.Points[i]);
                if (distance > radius)
                {
                    return null;
                }

                if (distance > separation)
                {
                    separation = distance;
                    face = i;
                }
            }
            int next = face + 1 < count ? face + 1 : 0;
            Vector2 a = fixture.Points[face], b = fixture.Points[next];
            if (separation < 1.1920929e-7f)
            {
                return new(body, mixedFriction, mixedRestitution, .5f * (a + b), fixture.Normals[face], face);
            }
            float first = Vector2.Dot(center - a, b - a), second = Vector2.Dot(center - b, a - b);
            if (first <= 0 || second <= 0)
            {
                int index = first <= 0 ? face : next;
                Vector2 vertex = fixture.Points[index], offset = center - vertex;
                return Vector2.Dot(offset, offset) > radius * radius ? null : new(body, mixedFriction, mixedRestitution, vertex, PhysicsMath.Normalize(offset), index + 8);
            }
            Vector2 faceCenter = .5f * (a + b);
            return Vector2.Dot(center - faceCenter, fixture.Normals[face]) > radius ? null :
                new(body, mixedFriction, mixedRestitution, faceCenter, fixture.Normals[face], face);
        }


        /// <summary>Restores the native polygon/circle feature-zero accumulated impulses.</summary>
        /// <param name="previous">Earlier contact for the same fixture pair.</param>
        internal override void Restore(PhysicsContact previous)
        {
            if (previous is TerrainContact contact)
            {
                NormalImpulse = contact.NormalImpulse;
                TangentImpulse = contact.TangentImpulse;
            }
        }

        /// <summary>Computes effective masses, restitution and warm-start velocity changes.</summary>
        /// <remarks>Uses native <c>b2WorldManifold</c> face-A point averaging and contact solver mass grouping.</remarks>
        internal override void Initialize()
        {
            normal = faceNormal;
            tangent = new(normal.Y, -normal.X);
            // The circle's solver transform origin is its center; its local point is the circle center.
            Vector2 clip = Body.Position;
            Vector2 surfaceA = clip + ((Skin - Vector2.Dot(clip - point, normal)) * normal);
            Vector2 surfaceB = clip - (Body.Radius * normal);
            arm = (.5f * (surfaceA + surfaceB)) - Body.Position;
            float rn = Cross(arm, normal), rt = Cross(arm, tangent);
            float kNormal = Body.InverseMass + (Body.InverseInertia * rn * rn);
            float kTangent = Body.InverseMass + (Body.InverseInertia * rt * rt);
            normalMass = kNormal > 0 ? 1 / kNormal : 0;
            tangentMass = kTangent > 0 ? 1 / kTangent : 0;
            float speed = Vector2.Dot(ContactVelocity(), normal);
            bias = speed < -1 ? -restitution * speed : 0;
        }

        /// <summary>Applies retained contact impulses before joint warm starting.</summary>
        internal override void WarmStart()
        {
            Apply((NormalImpulse * normal) + (TangentImpulse * tangent));
        }

        /// <summary>Solves tangent friction before the unilateral normal constraint.</summary>
        internal override void SolveVelocity()
        {
            float oldTangent = TangentImpulse;
            TangentImpulse = Math.Clamp(oldTangent - (tangentMass * Vector2.Dot(ContactVelocity(), tangent)), -friction * NormalImpulse, friction * NormalImpulse);
            Apply((TangentImpulse - oldTangent) * tangent);
            float previous = NormalImpulse;
            NormalImpulse = MathF.Max(0, previous - (normalMass * (Vector2.Dot(ContactVelocity(), normal) - bias)));
            Apply((NormalImpulse - previous) * normal);
        }

        /// <summary>Corrects penetration using the original Baumgarte factor and maximum correction.</summary>
        /// <param name="continuous">Whether to use time-of-impact correction strength and tolerance.</param>
        /// <returns>Whether penetration is within the selected slop tolerance.</returns>
        internal override bool SolvePosition(bool continuous = false)
        {
            Vector2 n = faceNormal;
            // Native b2PositionSolverManifold face A: the clip point is the circle center, so its arm is zero.
            float separation = Vector2.Dot(Body.Position - point, n) - Skin - Body.Radius;
            float correction = Math.Clamp((continuous ? .75f : .2f) * (separation + PhysicsWorld.LinearSlop), -.2f, 0);
            float k = Body.InverseMass;
            float impulse = k > 0 ? -correction / k : 0;
            Body.Position += Body.InverseMass * (impulse * n);
            return separation >= -(continuous ? 1.5f : 3) * PhysicsWorld.LinearSlop;
        }

        /// <summary>Gets velocity at the friction application point.</summary>
        /// <returns>Linear velocity plus clockwise angular contribution.</returns>
        private Vector2 ContactVelocity()
        {
            return Body.Velocity + (Body.AngularVelocity * new Vector2(-arm.Y, arm.X));
        }

        /// <summary>Applies a contact impulse at the manifold lever arm.</summary>
        /// <param name="impulse">World-space impulse on the circle.</param>
        private void Apply(Vector2 impulse)
        {
            Body.Velocity += Body.InverseMass * impulse;
            Body.AngularVelocity += Body.InverseInertia * Cross(arm, impulse);
        }

        /// <summary>Computes the scalar cross product.</summary>
        /// <param name="a">First vector.</param>
        /// <param name="b">Second vector.</param>
        /// <returns>The signed determinant.</returns>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }
    }
}
