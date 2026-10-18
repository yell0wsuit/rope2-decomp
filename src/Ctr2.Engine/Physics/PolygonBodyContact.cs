// Altered C# polygon/polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native local-frame clipping and one/two-point constraints on two polygon bodies.</summary>
    internal sealed class PolygonBodyContact : PhysicsContact
    {
        private const float Radius = 2 * PhysicsWorld.LinearSlop;
        private const float TotalRadius = 2 * Radius;
        private readonly PhysicsBody a;
        private readonly PhysicsBody b;
        private readonly bool faceB;
        private readonly Vector2 localNormal;
        private readonly Vector2 localPlane;
        private readonly List<Point> points;
        internal override int PointCount => points.Count;
        private readonly float friction;
        private readonly float restitution;
        private Vector2 normal;
        private Vector2 tangent;
        private int velocityCount;
        private float k11, k12, k22, m11, m12, m22;

        internal override PhysicsBody Body => a;
        internal override PhysicsBody? OtherBody => b;

        /// <summary>Whether one side is a terrain fixture's static identity frame.</summary>
        internal bool Terrain
        {
            get;
        }

        /// <summary>Captures the first point's native world-manifold location and solved normal impulse.</summary>
        /// <returns>The impact on the world-owned body.</returns>
        internal PolygonImpact CaptureImpact()
        {
            Point point = points[0];
            return new(a.Type == PhysicsBodyType.Static && Terrain ? b : a, WorldPoint(point), point.NormalImpulse);
        }

        /// <summary>Native <c>b2WorldManifold</c> point: the midpoint of both radius-offset surfaces.</summary>
        private Vector2 WorldPoint(Point point)
        {
            (Vector2 Clip, Vector2 Normal, float Distance) geometry = Geometry(point);
            Vector2 reference = geometry.Clip + ((Radius - geometry.Distance) * geometry.Normal);
            Vector2 incident = geometry.Clip - (Radius * geometry.Normal);
            return .5f * (faceB ? incident + reference : reference + incident);
        }
        internal override int[] CaptureFeatures()
        {
            return [.. points.Select(p => p.Feature)];
        }

        private PolygonBodyContact(PhysicsBody a, PhysicsBody b, bool flip, Vector2 normal, Vector2 plane,
            List<Point> points, float friction, float restitution, bool terrain)
        {
            Terrain = terrain;
            this.a = a;
            this.b = b;
            faceB = flip;
            localNormal = normal;
            localPlane = plane;
            this.points = points;
            this.friction = friction;
            this.restitution = restitution;
        }

        internal static PolygonBodyContact? Create(PhysicsBody a, ConvexPolygon shapeA, PhysicsBody b, ConvexPolygon shapeB, float friction, float restitution,
            bool terrain = false)
        {
            Shape first = new(shapeA), second = new(shapeB);
            (float Separation, int Edge) = FindSeparation(first, a, second, b);
            if (Separation > TotalRadius)
            {
                return null;
            }

            (float Separation, int Edge) sb = FindSeparation(second, b, first, a);
            if (sb.Separation > TotalRadius)
            {
                return null;
            }

            bool flip = sb.Separation > (.98f * Separation) + .001f;
            Shape reference = flip ? second : first, incident = flip ? first : second;
            PhysicsBody referenceBody = flip ? b : a, incidentBody = flip ? a : b;
            int edge = flip ? sb.Edge : Edge;
            Vector2 incidentNormal = PhysicsMath.InverseRotate(PhysicsMath.Rotate(reference.Normals[edge], referenceBody.Angle), incidentBody.Angle);
            int incidentEdge = 0;
            float minimum = float.MaxValue;
            for (int i = 0; i < incident.Normals.Length; i++)
            {
                float dot = Vector2.Dot(incidentNormal, incident.Normals[i]);
                if (dot < minimum)
                {
                    minimum = dot;
                    incidentEdge = i;
                }
            }
            int incidentNext = (incidentEdge + 1) % incident.Vertices.Count;
            List<ClipVertex> clipped = [new(ToWorld(incidentBody, incident.Vertices[incidentEdge]), edge | (incidentEdge << 8) | (1 << 16)),
                new(ToWorld(incidentBody, incident.Vertices[incidentNext]), edge | (incidentNext << 8) | (1 << 16))];
            int next = (edge + 1) % reference.Vertices.Count;
            Vector2 v1 = reference.Vertices[edge], v2 = reference.Vertices[next];
            Vector2 localTangent = PhysicsMath.Normalize(v2 - v1);
            Vector2 localNormal = new(localTangent.Y, -localTangent.X);
            Vector2 plane = .5f * (v1 + v2);
            Vector2 tangent = PhysicsMath.Rotate(localTangent, referenceBody.Angle);
            Vector2 normal = new(tangent.Y, -tangent.X);
            v1 = ToWorld(referenceBody, v1);
            v2 = ToWorld(referenceBody, v2);
            float front = Vector2.Dot(normal, v1);
            clipped = Clip(clipped, -tangent, -Vector2.Dot(tangent, v1) + TotalRadius, edge);
            if (clipped.Count < 2)
            {
                return null;
            }

            clipped = Clip(clipped, tangent, Vector2.Dot(tangent, v2) + TotalRadius, next);
            if (clipped.Count < 2)
            {
                return null;
            }

            List<Point> points = [];
            foreach (ClipVertex vertex in clipped)
            {
                if (Vector2.Dot(normal, vertex.Position) - front <= TotalRadius)
                {
                    points.Add(new(ToLocal(incidentBody, vertex.Position), flip ? FlipFeature(vertex.Feature) : vertex.Feature));
                }
            }
            return points.Count == 0 ? null : new(a, b, flip, localNormal, plane, points, friction, restitution, terrain);
        }

        internal override void Restore(PhysicsContact previous)
        {
            if (previous is not PolygonBodyContact old)
            {
                return;
            }

            foreach (Point point in points)
            {
                Point? match = old.points.Find(p => p.Feature == point.Feature);
                if (match == null)
                {
                    continue;
                }

                point.NormalImpulse = match.NormalImpulse;
                point.TangentImpulse = match.TangentImpulse;
            }
        }

        internal override void Initialize()
        {
            foreach (Point point in points)
            {
                (Vector2 Clip, Vector2 Normal, float Distance) geometry = Geometry(point);
                normal = faceB ? -geometry.Normal : geometry.Normal;
                tangent = new(normal.Y, -normal.X);
                Vector2 world = WorldPoint(point);
                point.ArmA = world - a.Position;
                point.ArmB = world - b.Position;
                float rnA = Cross(point.ArmA, normal), rnB = Cross(point.ArmB, normal);
                float rtA = Cross(point.ArmA, tangent), rtB = Cross(point.ArmB, tangent);
                float kn = a.InverseMass + b.InverseMass + (a.InverseInertia * rnA * rnA) + (b.InverseInertia * rnB * rnB);
                float kt = a.InverseMass + b.InverseMass + (a.InverseInertia * rtA * rtA) + (b.InverseInertia * rtB * rtB);
                point.NormalMass = kn > 0 ? 1 / kn : 0;
                point.TangentMass = kt > 0 ? 1 / kt : 0;
                float speed = Vector2.Dot(RelativeVelocity(point), normal);
                point.Bias = speed < -1 ? -restitution * speed : 0;
            }
            velocityCount = points.Count;
            if (velocityCount != 2)
            {
                return;
            }

            float a1 = Cross(points[0].ArmA, normal), a2 = Cross(points[1].ArmA, normal);
            float b1 = Cross(points[0].ArmB, normal), b2 = Cross(points[1].ArmB, normal);
            k11 = a.InverseMass + b.InverseMass + (a.InverseInertia * a1 * a1) + (b.InverseInertia * b1 * b1);
            k22 = a.InverseMass + b.InverseMass + (a.InverseInertia * a2 * a2) + (b.InverseInertia * b2 * b2);
            k12 = a.InverseMass + b.InverseMass + (a.InverseInertia * a1 * a2) + (b.InverseInertia * b1 * b2);
            float determinant = (k11 * k22) - (k12 * k12);
            if (k11 * k11 < 1000 * determinant)
            {
                float inverse = 1 / determinant;
                m11 = inverse * k22;
                m12 = -inverse * k12;
                m22 = inverse * k11;
            }
            else
            {
                velocityCount = 1;
            }
        }

        internal override void WarmStart()
        {
            for (int i = 0; i < velocityCount; i++)
            {
                Point point = points[i];
                Apply(point, (point.NormalImpulse * normal) + (point.TangentImpulse * tangent));
            }
        }

        internal override void SolveVelocity()
        {
            for (int i = 0; i < velocityCount; i++)
            {
                Point point = points[i];
                float lambda = point.TangentMass * -Vector2.Dot(RelativeVelocity(point), tangent);
                float maximum = friction * point.NormalImpulse;
                float next = Math.Clamp(point.TangentImpulse + lambda, -maximum, maximum);
                lambda = next - point.TangentImpulse;
                point.TangentImpulse = next;
                Apply(point, lambda * tangent);
            }
            if (velocityCount == 1)
            {
                Point point = points[0];
                float lambda = -point.NormalMass * (Vector2.Dot(RelativeVelocity(point), normal) - point.Bias);
                float next = MathF.Max(point.NormalImpulse + lambda, 0);
                lambda = next - point.NormalImpulse;
                point.NormalImpulse = next;
                Apply(point, lambda * normal);
                return;
            }
            Point first = points[0], second = points[1];
            float b1 = Vector2.Dot(RelativeVelocity(first), normal) - first.Bias;
            float b2 = Vector2.Dot(RelativeVelocity(second), normal) - second.Bias;
            b1 -= (k11 * first.NormalImpulse) + (k12 * second.NormalImpulse);
            b2 -= (k12 * first.NormalImpulse) + (k22 * second.NormalImpulse);
            float x1 = -((m11 * b1) + (m12 * b2)), x2 = -((m12 * b1) + (m22 * b2));
            if (!(x1 >= 0 && x2 >= 0))
            {
                x1 = -first.NormalMass * b1;
                x2 = 0;
                if (!(x1 >= 0 && (k12 * x1) + b2 >= 0))
                {
                    x1 = 0;
                    x2 = -second.NormalMass * b2;
                    if (!(x2 >= 0 && (k12 * x2) + b1 >= 0))
                    {
                        x1 = x2 = 0;
                        if (!(b1 >= 0 && b2 >= 0))
                        {
                            return;
                        }
                    }
                }
            }
            Vector2 p1 = (x1 - first.NormalImpulse) * normal, p2 = (x2 - second.NormalImpulse) * normal;
            a.Velocity -= a.InverseMass * (p1 + p2);
            a.AngularVelocity -= a.InverseInertia * (Cross(first.ArmA, p1) + Cross(second.ArmA, p2));
            b.Velocity += b.InverseMass * (p1 + p2);
            b.AngularVelocity += b.InverseInertia * (Cross(first.ArmB, p1) + Cross(second.ArmB, p2));
            first.NormalImpulse = x1;
            second.NormalImpulse = x2;
        }

        internal override bool SolvePosition(bool continuous = false)
        {
            float minimum = 0;
            foreach (Point point in points)
            {
                (Vector2 Clip, Vector2 Normal, float Distance) geometry = Geometry(point);
                Vector2 n = faceB ? -geometry.Normal : geometry.Normal;
                float separation = geometry.Distance - Radius - Radius;
                minimum = MathF.Min(minimum, separation);
                Vector2 rA = geometry.Clip - a.Position, rB = geometry.Clip - b.Position;
                float rnA = Cross(rA, n), rnB = Cross(rB, n);
                float mass = a.InverseMass + b.InverseMass + (a.InverseInertia * rnA * rnA) + (b.InverseInertia * rnB * rnB);
                float correction = Math.Clamp((continuous ? .75f : .2f) * (separation + PhysicsWorld.LinearSlop), -.2f, 0);
                float impulse = mass > 0 ? -correction / mass : 0;
                Vector2 p = impulse * n;
                a.Position -= a.InverseMass * p;
                a.Angle -= a.InverseInertia * Cross(rA, p);
                b.Position += b.InverseMass * p;
                b.Angle += b.InverseInertia * Cross(rB, p);
            }
            return minimum >= -(continuous ? 1.5f : 3) * PhysicsWorld.LinearSlop;
        }

        private (Vector2 Clip, Vector2 Normal, float Distance) Geometry(Point point)
        {
            PhysicsBody reference = faceB ? b : a, incident = faceB ? a : b;
            Vector2 normal = PhysicsMath.Rotate(localNormal, reference.Angle);
            Vector2 plane = ToWorld(reference, localPlane), clip = ToWorld(incident, point.Local);
            return (clip, normal, Vector2.Dot(clip - plane, normal));
        }

        private Vector2 RelativeVelocity(Point point)
        {
            return b.Velocity + Cross(b.AngularVelocity, point.ArmB) - a.Velocity - Cross(a.AngularVelocity, point.ArmA);
        }

        private void Apply(Point point, Vector2 impulse)
        {
            a.Velocity -= a.InverseMass * impulse;
            a.AngularVelocity -= a.InverseInertia * Cross(point.ArmA, impulse);
            b.Velocity += b.InverseMass * impulse;
            b.AngularVelocity += b.InverseInertia * Cross(point.ArmB, impulse);
        }

        private static (float Separation, int Edge) FindSeparation(Shape a, PhysicsBody bodyA, Shape b, PhysicsBody bodyB)
        {
            Vector2 direction = PhysicsMath.InverseRotate(ToWorld(bodyB, b.Center) - ToWorld(bodyA, a.Center), bodyA.Angle);
            float maximum = -float.MaxValue;
            int edge = 0;
            for (int i = 0; i < a.Normals.Length; i++)
            {
                float dot = Vector2.Dot(a.Normals[i], direction);
                if (dot > maximum)
                {
                    maximum = dot;
                    edge = i;
                }
            }
            float current = EdgeSeparation(a, bodyA, edge, b, bodyB);
            int previous = (edge + a.Vertices.Count - 1) % a.Vertices.Count, next = (edge + 1) % a.Vertices.Count;
            float before = EdgeSeparation(a, bodyA, previous, b, bodyB), after = EdgeSeparation(a, bodyA, next, b, bodyB);
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

            for (int count = 0; count < a.Vertices.Count; count++)
            {
                next = (edge + increment + a.Vertices.Count) % a.Vertices.Count;
                float separation = EdgeSeparation(a, bodyA, next, b, bodyB);
                if (separation <= current)
                {
                    break;
                }

                edge = next;
                current = separation;
            }
            return (current, edge);
        }

        private static float EdgeSeparation(Shape a, PhysicsBody bodyA, int edge, Shape b, PhysicsBody bodyB)
        {
            Vector2 worldNormal = PhysicsMath.Rotate(a.Normals[edge], bodyA.Angle);
            Vector2 normal = PhysicsMath.InverseRotate(worldNormal, bodyB.Angle);
            float minimum = float.MaxValue;
            int index = 0;
            for (int i = 0; i < b.Vertices.Count; i++)
            {
                float dot = Vector2.Dot(b.Vertices[i], normal);
                if (dot < minimum)
                {
                    minimum = dot;
                    index = i;
                }
            }
            return Vector2.Dot(ToWorld(bodyB, b.Vertices[index]) - ToWorld(bodyA, a.Vertices[edge]), worldNormal);
        }

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
                output.Add(new(input[0].Position + (fraction * (input[1].Position - input[0].Position)), referenceVertex | (input[0].Feature & 0xff00) | (1 << 24)));
            }
            return output;
        }

        private static int FlipFeature(int value)
        {
            return ((value & 255) << 8) | ((value >> 8) & 255) | ((value & 0xff0000) << 8) | ((value >> 8) & 0xff0000);
        }

        private static Vector2 ToWorld(PhysicsBody body, Vector2 local)
        {
            return body.Origin + PhysicsMath.Rotate(local, body.Angle);
        }

        private static Vector2 ToLocal(PhysicsBody body, Vector2 world)
        {
            return PhysicsMath.InverseRotate(world - body.Origin, body.Angle);
        }


        private static float Cross(Vector2 a, Vector2 b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }

        private static Vector2 Cross(float value, Vector2 point)
        {
            return new(-value * point.Y, value * point.X);
        }

        private sealed record ClipVertex(Vector2 Position, int Feature);
        private sealed class Point(Vector2 local, int feature)
        {
            internal readonly Vector2 Local = local;
            internal readonly int Feature = feature;
            internal Vector2 ArmA, ArmB;
            internal float NormalMass, TangentMass, Bias, NormalImpulse, TangentImpulse;
        }

        // Shape's cached centroid uses the native polygon Set formula, distinct from mass integration.
        private sealed class Shape
        {
            internal readonly IReadOnlyList<Vector2> Vertices;
            internal readonly Vector2[] Normals;
            internal readonly Vector2 Center;
            internal Shape(ConvexPolygon shape)
            {
                Vertices = shape.Vertices;
                Normals = new Vector2[Vertices.Count];
                Vector2 center = Vector2.Zero;
                float area = 0;
                for (int i = 0; i < Vertices.Count; i++)
                {
                    Vector2 first = Vertices[i], second = Vertices[(i + 1) % Vertices.Count], edge = second - first;
                    Normals[i] = PhysicsMath.Normalize(new(edge.Y, -edge.X));
                    float triangle = .5f * Cross(first, second);
                    area += triangle;
                    center += triangle * (1f / 3) * (Vector2.Zero + first + second);
                }
                Center = 1 / area * center;
            }
        }
    }
}
