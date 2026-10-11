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
        /// <summary>Previous active contacts, retaining accumulated normal and tangent impulses.</summary>
        private Dictionary<(PhysicsBody, TerrainFixture), TerrainContact> contacts = [];

        /// <summary>Adds one convex static terrain fixture; vertices are copied into the world.</summary>
        /// <param name="vertices">Three to eight counterclockwise world-space vertices in meters.</param>
        /// <param name="friction">Finite nonnegative friction coefficient.</param>
        /// <param name="restitution">Finite nonnegative restitution coefficient.</param>
        /// <exception cref="ArgumentException">Geometry or coefficients are invalid or exceed budgets.</exception>
        public void AddTerrain(IReadOnlyList<Vector2> vertices, float friction, float restitution)
        {
            if (terrain.Count >= 4096 || vertices.Count is < 3 or > 8 ||
                !float.IsFinite(friction) || friction < 0 || friction > 100 ||
                !float.IsFinite(restitution) || restitution < 0 || restitution > 10)
            {
                throw new ArgumentException("Invalid convex terrain fixture.");
            }
            Vector2[] points = [.. vertices];
            Vector2[] normals = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                Vector2 p = points[i], edge = points[(i + 1) % points.Length] - p;
                if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) || MathF.Abs(p.X) > 10000 || MathF.Abs(p.Y) > 10000 || edge.LengthSquared() < 1e-10f)
                {
                    throw new ArgumentException("Invalid terrain vertex.");
                }
                normals[i] = Vector2.Normalize(new(edge.Y, -edge.X));
                for (int j = 0; j < points.Length; j++)
                {
                    if (j != i && j != (i + 1) % points.Length && Vector2.Dot(normals[i], points[j] - p) >= -1e-7f)
                    {
                        throw new ArgumentException("Terrain fixture must be strictly convex and counterclockwise.");
                    }
                }
            }
            terrain.Add(new(points, normals, friction, restitution));
        }

        /// <summary>Rebuilds supported manifolds and assigns contacts to independent dynamic-body islands.</summary>
        /// <param name="islands">Independent joint groups, including free-body groups.</param>
        /// <param name="bodyOrder">Dynamic-body group memberships and traversal ranks.</param>
        /// <returns>Contact groups parallel to the joint groups.</returns>
        private List<List<TerrainContact>> BuildContacts(List<List<PhysicsJoint>> islands, Dictionary<PhysicsBody, (int Island, int Order)> bodyOrder)
        {
            List<List<TerrainContact>> groups = [.. islands.Select(_ => new List<TerrainContact>())];
            Dictionary<(PhysicsBody, TerrainFixture), TerrainContact> active = [];
            foreach (PhysicsBody body in bodies)
            {
                if (body.Mass == 0 || body.IsSensor)
                {
                    continue;
                }

                foreach (TerrainFixture fixture in terrain)
                {
                    TerrainContact? contact = TerrainContact.Create(body, fixture);
                    if (contact == null)
                    {
                        continue;
                    }

                    (PhysicsBody body, TerrainFixture fixture) key = (body, fixture);
                    if (contacts.TryGetValue(key, out TerrainContact? previous))
                    {
                        contact.NormalImpulse = previous.NormalImpulse;
                        contact.TangentImpulse = previous.TangentImpulse;
                        contact.CreationOrder = previous.CreationOrder;
                    }
                    if (contact.CreationOrder == 0)
                    {
                        contact.CreationOrder = ++contactSequence;
                    }

                    active.Add(key, contact);
                    int index = bodyOrder[body].Island;
                    groups[index].Add(contact);
                }
            }
            foreach (List<TerrainContact> group in groups)
            {
                group.Sort((a, b) => bodyOrder[a.Body].Order != bodyOrder[b.Body].Order ?
                    bodyOrder[a.Body].Order.CompareTo(bodyOrder[b.Body].Order) : b.CreationOrder.CompareTo(a.CreationOrder));
            }
            contacts = active;
            return groups;
        }
    }

    /// <summary>Immutable convex static geometry and contact material.</summary>
    /// <param name="Points">Counterclockwise vertices in meters.</param>
    /// <param name="Normals">Outward face normals.</param>
    /// <param name="Friction">Fixture friction.</param>
    /// <param name="Restitution">Fixture restitution.</param>
    internal sealed record TerrainFixture(Vector2[] Points, Vector2[] Normals, float Friction, float Restitution);

    /// <summary>A single-point static convex polygon/circle manifold and sequential impulse constraint.</summary>
    internal sealed class TerrainContact
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
        internal PhysicsBody Body
        {
            get;
        }
        /// <summary>Original creation sequence retained across touching frames.</summary>
        internal long CreationOrder;
        /// <summary>Selected narrowphase feature for diagnostics; polygon/circle impulse ID remains zero.</summary>
        internal readonly int Feature;
        /// <summary>Accumulated nonnegative normal impulse.</summary>
        internal float NormalImpulse;
        /// <summary>Accumulated Coulomb friction impulse.</summary>
        internal float TangentImpulse;

        /// <summary>Creates a manifold after geometry overlap has been confirmed.</summary>
        /// <param name="body">Dynamic circle.</param>
        /// <param name="fixture">Static material.</param>
        /// <param name="point">Face midpoint or vertex.</param>
        /// <param name="normal">Outward normal.</param>
        /// <param name="feature">Stable feature identifier.</param>
        private TerrainContact(PhysicsBody body, TerrainFixture fixture, Vector2 point, Vector2 normal, int feature)
        {
            Body = body;
            this.point = point;
            faceNormal = normal;
            friction = MathF.Sqrt(body.Friction * fixture.Friction);
            restitution = MathF.Max(body.Restitution, fixture.Restitution);
            Feature = feature;
        }

        /// <summary>Finds the maximum separating face and its circle Voronoi region.</summary>
        /// <param name="body">Dynamic circle.</param>
        /// <param name="fixture">Static polygon.</param>
        /// <returns>A touching manifold, or null when separated.</returns>
        internal static TerrainContact? Create(PhysicsBody body, TerrainFixture fixture)
        {
            float separation = float.NegativeInfinity;
            int face = 0;
            float radius = body.Radius + Skin;
            for (int i = 0; i < fixture.Points.Length; i++)
            {
                float distance = Vector2.Dot(fixture.Normals[i], body.Position - fixture.Points[i]);
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
            Vector2 a = fixture.Points[face], b = fixture.Points[(face + 1) % fixture.Points.Length];
            if (separation >= 1.1920929e-7f)
            {
                bool first = Vector2.Dot(body.Position - a, b - a) <= 0;
                bool second = Vector2.Dot(body.Position - b, a - b) <= 0;
                if (first || second)
                {
                    int index = first ? face : (face + 1) % fixture.Points.Length;
                    Vector2 p = fixture.Points[index];
                    return Vector2.DistanceSquared(p, body.Position) > radius * radius ? null :
                        new(body, fixture, p, Vector2.Normalize(body.Position - p), index + 8);
                }
            }
            return new(body, fixture, (a + b) * .5f, fixture.Normals[face], face);
        }

        /// <summary>Computes effective masses, restitution and warm-start velocity changes.</summary>
        internal void Initialize()
        {
            normal = faceNormal;
            tangent = new(normal.Y, -normal.X);
            float separation = Vector2.Dot(Body.Position - point, normal) - (Body.Radius + Skin);
            arm = -(Body.Radius + (.5f * separation)) * normal;
            float rn = Cross(arm, normal), rt = Cross(arm, tangent);
            normalMass = 1 / (Body.InverseMass + (Body.InverseInertia * rn * rn));
            tangentMass = 1 / (Body.InverseMass + (Body.InverseInertia * rt * rt));
            float speed = Vector2.Dot(ContactVelocity(), normal);
            bias = speed < -1 ? -restitution * speed : 0;
        }

        /// <summary>Applies retained contact impulses before joint warm starting.</summary>
        internal void WarmStart()
        {
            Apply((NormalImpulse * normal) + (TangentImpulse * tangent));
        }

        /// <summary>Solves tangent friction before the unilateral normal constraint.</summary>
        internal void SolveVelocity()
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
        internal bool SolvePosition(bool continuous = false)
        {
            Vector2 n = faceNormal;
            float separation = Vector2.Dot(Body.Position - point, n) - (Body.Radius + Skin);
            float correction = Math.Clamp((continuous ? .75f : .2f) * (separation + PhysicsWorld.LinearSlop), -.2f, 0);
            Body.Position -= correction * n;
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
