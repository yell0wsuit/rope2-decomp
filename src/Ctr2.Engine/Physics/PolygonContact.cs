// Altered C# static/dynamic polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A clipped static/dynamic convex polygon manifold with one or two constraint points.</summary>
    internal sealed class PolygonContact : PhysicsContact
    {
        /// <summary>Combined skin radius of two CTR2 polygon fixtures.</summary>
        private const float Skin = 4 * PhysicsWorld.LinearSlop;
        /// <summary>Whether the reference face belongs to the moving body.</summary>
        private readonly bool movingReference;
        /// <summary>Reference face normal in its own frame.</summary>
        private readonly Vector2 localNormal;
        /// <summary>Reference face midpoint in its own frame.</summary>
        private readonly Vector2 localPlane;
        /// <summary>Clipped incident points in the incident polygon's frame.</summary>
        private readonly List<Point> points;
        /// <summary>Mixed friction coefficient.</summary>
        private readonly float friction;
        /// <summary>Mixed restitution coefficient.</summary>
        private readonly float restitution;
        /// <summary>World-space normal pointing from terrain toward the moving body.</summary>
        private Vector2 normal;
        /// <summary>Clockwise tangent of the world normal.</summary>
        private Vector2 tangent;
        /// <summary>Number of well-conditioned velocity points.</summary>
        private int velocityCount;
        /// <summary>First diagonal element of the two-point inverse-mass matrix.</summary>
        private float k11;
        /// <summary>Off-diagonal element of the two-point inverse-mass matrix.</summary>
        private float k12;
        /// <summary>Second diagonal element of the two-point inverse-mass matrix.</summary>
        private float k22;
        /// <summary>Inverse determinant of the two-point matrix.</summary>
        private float inverseDeterminant;
        /// <summary>Gets the moving polygon body.</summary>
        /// <value>The body whose compound fixture generated this manifold.</value>
        internal override PhysicsBody Body
        {
            get;
        }

        /// <summary>Captures the first solved impulse and current world manifold point.</summary>
        internal PolygonImpact CaptureImpact()
        {
            (Vector2 clip, Vector2 n, float distance) = Geometry(points[0]);
            return new(Body, clip - (.5f * distance * n), points[0].NormalImpulse);
        }

        /// <summary>Creates a clipped manifold with local reference data.</summary>
        /// <param name="body">Dynamic polygon body.</param>
        /// <param name="terrain">Static contact material.</param>
        /// <param name="movingReference">Whether the reference face moves with the body.</param>
        /// <param name="localNormal">Reference-frame outward normal.</param>
        /// <param name="localPlane">Reference-frame face midpoint.</param>
        /// <param name="points">Owned incident points and feature IDs.</param>
        private PolygonContact(PhysicsBody body, TerrainFixture terrain, bool movingReference, Vector2 localNormal, Vector2 localPlane, List<Point> points)
        {
            Body = body;
            this.movingReference = movingReference;
            this.localNormal = localNormal;
            this.localPlane = localPlane;
            this.points = points;
            friction = MathF.Sqrt(body.Friction * terrain.Friction);
            restitution = MathF.Max(body.Restitution, terrain.Restitution);
        }

        /// <summary>Finds separating axes, chooses a reference face and clips the incident edge.</summary>
        /// <param name="body">Dynamic polygon body.</param>
        /// <param name="shape">One local convex compound fixture.</param>
        /// <param name="terrain">Static convex fixture.</param>
        /// <returns>A touching manifold, or null when the fixtures are separated.</returns>
        internal static PolygonContact? Create(PhysicsBody body, ConvexPolygon shape, TerrainFixture terrain)
        {
            Vector2[] moving = [.. shape.Vertices.Select(p => ToWorld(body, p))];
            Vector2[] movingNormals = [.. shape.Normals.Select(n => Rotate(n, body.Angle))];
            (float separationA, int edgeA) = FindSeparation(terrain.Points, terrain.Normals, terrain.Center, moving, ToWorld(body, shape.ComputeMass(0).Center));
            if (separationA > Skin)
            {
                return null;
            }
            (float separationB, int edgeB) = FindSeparation(moving, movingNormals, ToWorld(body, shape.ComputeMass(0).Center), terrain.Points, terrain.Center);
            if (separationB > Skin)
            {
                return null;
            }
            bool flip = separationB > (.98f * separationA) + .001f;
            Vector2[] reference = flip ? moving : terrain.Points, incident = flip ? terrain.Points : moving;
            Vector2[] referenceNormals = flip ? movingNormals : terrain.Normals, incidentNormals = flip ? terrain.Normals : movingNormals;
            int edge = flip ? edgeB : edgeA;
            Vector2 referenceNormal = referenceNormals[edge];
            int incidentEdge = 0;
            float minimum = float.PositiveInfinity;
            for (int i = 0; i < incidentNormals.Length; i++)
            {
                float dot = Vector2.Dot(referenceNormal, incidentNormals[i]);
                if (dot < minimum)
                {
                    minimum = dot;
                    incidentEdge = i;
                }
            }
            int nextIncident = (incidentEdge + 1) % incident.Length, nextReference = (edge + 1) % reference.Length;
            List<ClipVertex> clipped = [new(incident[incidentEdge], edge | (incidentEdge << 8) | (1 << 16)),
                new(incident[nextIncident], edge | (nextIncident << 8) | (1 << 16))];
            Vector2 a = reference[edge], b = reference[nextReference], tangent = Vector2.Normalize(b - a);
            clipped = Clip(clipped, -tangent, -Vector2.Dot(tangent, a) + Skin, edge);
            if (clipped.Count < 2)
            {
                return null;
            }
            clipped = Clip(clipped, tangent, Vector2.Dot(tangent, b) + Skin, nextReference);
            if (clipped.Count < 2)
            {
                return null;
            }
            List<Point> points = [];
            foreach (ClipVertex vertex in clipped)
            {
                if (Vector2.Dot(referenceNormal, vertex.Position - a) <= Skin)
                {
                    int feature = flip ? FlipFeature(vertex.Feature) : vertex.Feature;
                    points.Add(new(flip ? vertex.Position : ToLocal(body, vertex.Position), feature));
                }
            }
            if (points.Count == 0)
            {
                return null;
            }
            Vector2 localNormal = flip ? shape.Normals[edge] : terrain.Normals[edge];
            Vector2 plane = flip ? (shape.Vertices[edge] + shape.Vertices[nextReference]) * .5f : (a + b) * .5f;
            return new(body, terrain, flip, localNormal, plane, points);
        }

        /// <summary>Restores impulses only for matching native contact feature identifiers.</summary>
        /// <param name="previous">Previous contact for this fixture pair.</param>
        internal override void Restore(PhysicsContact previous)
        {
            if (previous is not PolygonContact contact)
            {
                return;
            }
            foreach (Point point in points)
            {
                Point? old = contact.points.Find(p => p.Feature == point.Feature);
                if (old != null)
                {
                    point.NormalImpulse = old.NormalImpulse;
                    point.TangentImpulse = old.TangentImpulse;
                }
            }
        }

        /// <summary>Initializes world normals, effective masses, restitution and two-point conditioning.</summary>
        internal override void Initialize()
        {
            normal = movingReference ? -Rotate(localNormal, Body.Angle) : localNormal;
            tangent = new(normal.Y, -normal.X);
            foreach (Point point in points)
            {
                (Vector2 clip, Vector2 n, float distance) = Geometry(point);
                // Preserve native world-manifold rounding; cancelling equal radii changes float bits.
                float radius = 2 * PhysicsWorld.LinearSlop;
                Vector2 referencePoint = clip + ((radius - distance) * n);
                Vector2 incidentPoint = clip - (radius * n);
                point.Arm = (.5f * (referencePoint + incidentPoint)) - Body.Position;
                float rn = Cross(point.Arm, normal), rt = Cross(point.Arm, tangent);
                point.NormalMass = 1 / (Body.InverseMass + (Body.InverseInertia * rn * rn));
                point.TangentMass = 1 / (Body.InverseMass + (Body.InverseInertia * rt * rt));
                float speed = Vector2.Dot(Velocity(point), normal);
                point.Bias = speed < -1 ? -restitution * speed : 0;
            }
            velocityCount = points.Count;
            if (velocityCount == 2)
            {
                float a = Cross(points[0].Arm, normal), b = Cross(points[1].Arm, normal);
                k11 = Body.InverseMass + (Body.InverseInertia * a * a);
                k22 = Body.InverseMass + (Body.InverseInertia * b * b);
                k12 = Body.InverseMass + (Body.InverseInertia * a * b);
                float determinant = (k11 * k22) - (k12 * k12);
                if (k11 * k11 < 1000 * determinant)
                {
                    inverseDeterminant = 1 / determinant;
                }
                else
                {
                    velocityCount = 1;
                }
            }
        }

        /// <summary>Applies retained normal and tangent impulses at each active contact point.</summary>
        internal override void WarmStart()
        {
            for (int i = 0; i < velocityCount; i++)
            {
                Point point = points[i];
                Apply(point, (point.NormalImpulse * normal) + (point.TangentImpulse * tangent));
            }
        }

        /// <summary>Solves friction, then the one-point or coupled two-point normal complementarity problem.</summary>
        internal override void SolveVelocity()
        {
            for (int i = 0; i < velocityCount; i++)
            {
                Point point = points[i];
                float previous = point.TangentImpulse;
                float maximum = friction * point.NormalImpulse;
                point.TangentImpulse = Math.Clamp(previous - (point.TangentMass * Vector2.Dot(Velocity(point), tangent)), -maximum, maximum);
                Apply(point, (point.TangentImpulse - previous) * tangent);
            }
            if (velocityCount == 1)
            {
                Point point = points[0];
                float previous = point.NormalImpulse;
                point.NormalImpulse = MathF.Max(0, previous - (point.NormalMass * (Vector2.Dot(Velocity(point), normal) - point.Bias)));
                Apply(point, (point.NormalImpulse - previous) * normal);
                return;
            }
            Point first = points[0], second = points[1];
            float b1 = Vector2.Dot(Velocity(first), normal) - first.Bias - ((k11 * first.NormalImpulse) + (k12 * second.NormalImpulse));
            float b2 = Vector2.Dot(Velocity(second), normal) - second.Bias - ((k12 * first.NormalImpulse) + (k22 * second.NormalImpulse));
            // Native GetInverse rounds each matrix entry before the matrix/vector product.
            float inverse11 = inverseDeterminant * k22, inverse12 = -inverseDeterminant * k12, inverse22 = inverseDeterminant * k11;
            float x1 = -((inverse11 * b1) + (inverse12 * b2));
            float x2 = -((inverse12 * b1) + (inverse22 * b2));
            if (x1 < 0 || x2 < 0)
            {
                x1 = -first.NormalMass * b1;
                x2 = 0;
                if (x1 < 0 || (k12 * x1) + b2 < 0)
                {
                    x1 = 0;
                    x2 = -second.NormalMass * b2;
                    if (x2 < 0 || (k12 * x2) + b1 < 0)
                    {
                        x1 = x2 = 0;
                        if (b1 < 0 || b2 < 0)
                        {
                            return;
                        }
                    }
                }
            }
            Vector2 impulse1 = (x1 - first.NormalImpulse) * normal, impulse2 = (x2 - second.NormalImpulse) * normal;
            Body.Velocity += Body.InverseMass * (impulse1 + impulse2);
            Body.AngularVelocity += Body.InverseInertia * (Cross(first.Arm, impulse1) + Cross(second.Arm, impulse2));
            first.NormalImpulse = x1;
            second.NormalImpulse = x2;
        }

        /// <summary>Corrects each point using its updated reference face and angular effective mass.</summary>
        /// <param name="continuous">Whether to select time-of-impact correction parameters.</param>
        /// <returns>Whether all points are within the selected position slop.</returns>
        internal override bool SolvePosition(bool continuous = false)
        {
            float minimum = 0;
            foreach (Point point in points)
            {
                (Vector2 clip, Vector2 faceNormal, float distance) = Geometry(point);
                Vector2 n = movingReference ? -faceNormal : faceNormal;
                float separation = distance - Skin;
                minimum = MathF.Min(minimum, separation);
                float correction = Math.Clamp((continuous ? .75f : .2f) * (separation + PhysicsWorld.LinearSlop), -.2f, 0);
                Vector2 arm = clip - Body.Position;
                float rn = Cross(arm, n);
                float mass = Body.InverseMass + (Body.InverseInertia * rn * rn);
                Vector2 impulse = -correction / mass * n;
                Body.Position += Body.InverseMass * impulse;
                Body.Angle += Body.InverseInertia * Cross(arm, impulse);
            }
            return minimum >= -(continuous ? 1.5f : 3) * PhysicsWorld.LinearSlop;
        }

        /// <summary>Computes a point's current incident location and reference-plane distance.</summary>
        /// <param name="point">Clipped incident point.</param>
        /// <returns>World incident point, outward reference normal and core separation.</returns>
        private (Vector2 Clip, Vector2 Normal, float Distance) Geometry(Point point)
        {
            Vector2 n = movingReference ? Rotate(localNormal, Body.Angle) : localNormal;
            Vector2 plane = movingReference ? ToWorld(Body, localPlane) : localPlane;
            Vector2 clip = movingReference ? point.Local : ToWorld(Body, point.Local);
            return (clip, n, Vector2.Dot(clip - plane, n));
        }

        /// <summary>Computes current contact-point velocity.</summary>
        /// <param name="point">Initialized velocity constraint point.</param>
        /// <returns>Linear plus angular point velocity.</returns>
        private Vector2 Velocity(Point point)
        {
            return Body.Velocity + (Body.AngularVelocity * new Vector2(-point.Arm.Y, point.Arm.X));
        }

        /// <summary>Applies a world impulse at the initialized contact arm.</summary>
        /// <param name="point">Contact arm.</param>
        /// <param name="impulse">Impulse on the dynamic body.</param>
        private void Apply(Point point, Vector2 impulse)
        {
            Body.Velocity += Body.InverseMass * impulse;
            Body.AngularVelocity += Body.InverseInertia * Cross(point.Arm, impulse);
        }

        /// <summary>Finds the greatest face separation with centroid-directed local edge search.</summary>
        /// <param name="reference">Reference world vertices.</param>
        /// <param name="normals">Reference world normals.</param>
        /// <param name="center">Reference world centroid.</param>
        /// <param name="incident">Incident world vertices.</param>
        /// <param name="otherCenter">Incident world centroid.</param>
        /// <returns>Greatest separation and selected reference edge.</returns>
        private static (float Separation, int Edge) FindSeparation(Vector2[] reference, Vector2[] normals, Vector2 center, Vector2[] incident, Vector2 otherCenter)
        {
            Vector2 direction = otherCenter - center;
            int edge = 0;
            float maximum = float.NegativeInfinity;
            for (int i = 0; i < normals.Length; i++)
            {
                float dot = Vector2.Dot(normals[i], direction);
                if (dot > maximum)
                {
                    maximum = dot;
                    edge = i;
                }
            }
            // Minimum incident vertex separation from the selected reference edge, in meters.
            float separation(int index)
            {
                float result = float.PositiveInfinity;
                foreach (Vector2 vertex in incident)
                {
                    result = MathF.Min(result, Vector2.Dot(vertex - reference[index], normals[index]));
                }
                return result;
            }
            float current = separation(edge);
            int previous = (edge + reference.Length - 1) % reference.Length, next = (edge + 1) % reference.Length;
            float before = separation(previous), after = separation(next);
            int increment;
            if (before > current && before > after)
            {
                increment = -1;
                edge = previous;
                current = before;
            }
            else if (after > current)
            {
                increment = 1;
                edge = next;
                current = after;
            }
            else
            {
                return (current, edge);
            }
            for (int count = 0; count < reference.Length; count++)
            {
                next = (edge + increment + reference.Length) % reference.Length;
                float candidate = separation(next);
                if (candidate <= current)
                {
                    break;
                }
                edge = next;
                current = candidate;
            }
            return (current, edge);
        }

        /// <summary>Clips a two-vertex incident edge against one reference side plane.</summary>
        /// <param name="input">Two incident vertices with contact features.</param>
        /// <param name="normal">Outward clipping normal.</param>
        /// <param name="offset">Plane offset.</param>
        /// <param name="referenceVertex">Reference endpoint feature index.</param>
        /// <returns>At most two clipped vertices in native insertion order.</returns>
        private static List<ClipVertex> Clip(List<ClipVertex> input, Vector2 normal, float offset, int referenceVertex)
        {
            List<ClipVertex> output = [];
            float first = Vector2.Dot(normal, input[0].Position) - offset, second = Vector2.Dot(normal, input[1].Position) - offset;
            if (first <= 0)
            {
                output.Add(input[0]);
            }
            if (second <= 0)
            {
                output.Add(input[1]);
            }
            if (first * second < 0)
            {
                float fraction = first / (first - second);
                int feature = referenceVertex | (input[0].Feature & 0xff00) | (1 << 24);
                output.Add(new(input[0].Position + (fraction * (input[1].Position - input[0].Position)), feature));
            }
            return output;
        }

        /// <summary>Swaps reference and incident features when the moving face is selected.</summary>
        /// <param name="feature">Packed native feature bytes.</param>
        /// <returns>The terrain/body-oriented feature ID.</returns>
        private static int FlipFeature(int feature)
        {
            return ((feature & 255) << 8) | ((feature >> 8) & 255) | ((feature & 0xff0000) << 8) | ((feature >> 8) & 0xff0000);
        }

        /// <summary>Rotates a vector by clockwise radians in the downward-positive world.</summary>
        /// <param name="point">Local vector.</param>
        /// <param name="angle">Clockwise radians.</param>
        /// <returns>The rotated vector.</returns>
        private static Vector2 Rotate(Vector2 point, float angle)
        {
            return PhysicsMath.Rotate(point, angle);
        }

        /// <summary>Converts a local polygon point using its authored body origin.</summary>
        /// <param name="body">Moving body.</param>
        /// <param name="point">Local fixture point.</param>
        /// <returns>The world point.</returns>
        private static Vector2 ToWorld(PhysicsBody body, Vector2 point)
        {
            return body.Origin + Rotate(point, body.Angle);
        }

        /// <summary>Converts a world point to the moving body's authored local frame.</summary>
        /// <param name="body">Moving body.</param>
        /// <param name="point">World point.</param>
        /// <returns>The local fixture point.</returns>
        private static Vector2 ToLocal(PhysicsBody body, Vector2 point)
        {
            return PhysicsMath.InverseRotate(point - body.Origin, body.Angle);
        }

        /// <summary>Computes a scalar cross product.</summary>
        /// <param name="a">First vector.</param>
        /// <param name="b">Second vector.</param>
        /// <returns>The signed determinant.</returns>
        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        /// <summary>A clipped vertex with native packed reference/incident feature bytes.</summary>
        /// <param name="Position">World incident position.</param>
        /// <param name="Feature">Packed contact feature ID.</param>
        private sealed record ClipVertex(Vector2 Position, int Feature);

        /// <summary>Mutable cached impulses and velocity masses for one clipped incident point.</summary>
        /// <param name="local">Incident-frame point.</param>
        /// <param name="feature">Native contact feature ID.</param>
        private sealed class Point(Vector2 local, int feature)
        {
            /// <summary>Incident point in the incident polygon frame.</summary>
            internal readonly Vector2 Local = local;
            /// <summary>Stable contact feature ID.</summary>
            internal readonly int Feature = feature;
            /// <summary>Initialized world-space contact lever arm.</summary>
            internal Vector2 Arm;
            /// <summary>Normal effective mass.</summary>
            internal float NormalMass;
            /// <summary>Tangent effective mass.</summary>
            internal float TangentMass;
            /// <summary>Restitution velocity bias.</summary>
            internal float Bias;
            /// <summary>Accumulated normal impulse.</summary>
            internal float NormalImpulse;
            /// <summary>Accumulated friction impulse.</summary>
            internal float TangentImpulse;
        }
    }
}
