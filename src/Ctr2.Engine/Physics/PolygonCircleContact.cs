// Altered C# polygon/circle subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native face-A manifold, including polygon vertex Voronoi regions.</summary>
    internal sealed class PolygonCircleContact : SinglePointContact
    {
        private const float Skin = 2 * PhysicsWorld.LinearSlop;
        private readonly Vector2 localPoint;
        private readonly Vector2 localNormal;

        private PolygonCircleContact(PhysicsBody polygon, PhysicsBody circle, Vector2 point, Vector2 normal, float friction, float restitution)
            : base(polygon, circle, friction, restitution)
        {
            localPoint = point;
            localNormal = normal;
        }

        internal static PolygonCircleContact? Create(PhysicsBody polygon, ConvexPolygon shape, PhysicsBody circle, float friction, float restitution)
        {
            Vector2 center = PhysicsMath.InverseRotate(circle.Position - polygon.Origin, polygon.Angle);
            float radius = Skin + circle.Radius;
            float separation = -float.MaxValue;
            int face = 0;
            for (int i = 0; i < shape.Vertices.Count; i++)
            {
                float distance = Vector2.Dot(shape.Normals[i], center - shape.Vertices[i]);
                if (distance > radius)
                    return null;
                if (distance > separation)
                {
                    separation = distance;
                    face = i;
                }
            }
            Vector2 first = shape.Vertices[face], second = shape.Vertices[(face + 1) % shape.Vertices.Count];
            if (separation < Epsilon)
                return new(polygon, circle, .5f * (first + second), shape.Normals[face], friction, restitution);

            float u1 = Vector2.Dot(center - first, second - first);
            float u2 = Vector2.Dot(center - second, first - second);
            if (u1 <= 0)
                return Vector2.DistanceSquared(center, first) > radius * radius ? null :
                    new(polygon, circle, first, Normalize(center - first), friction, restitution);
            if (u2 <= 0)
                return Vector2.DistanceSquared(center, second) > radius * radius ? null :
                    new(polygon, circle, second, Normalize(center - second), friction, restitution);

            Vector2 midpoint = .5f * (first + second);
            return Vector2.Dot(center - midpoint, shape.Normals[face]) > radius ? null :
                new(polygon, circle, midpoint, shape.Normals[face], friction, restitution);
        }

        protected override (Vector2 Normal, Vector2 Point) VelocityGeometry()
        {
            Vector2 normal = PhysicsMath.Rotate(localNormal, a.Angle);
            Vector2 plane = a.Origin + PhysicsMath.Rotate(localPoint, a.Angle);
            Vector2 clip = b.Position;
            Vector2 pointA = clip + ((Skin - Vector2.Dot(clip - plane, normal)) * normal);
            Vector2 pointB = clip - (b.Radius * normal);
            return (normal, .5f * (pointA + pointB));
        }

        protected override (Vector2 Normal, Vector2 Point, float Separation) PositionGeometry()
        {
            Vector2 normal = PhysicsMath.Rotate(localNormal, a.Angle);
            Vector2 plane = a.Origin + PhysicsMath.Rotate(localPoint, a.Angle);
            return (normal, b.Position, Vector2.Dot(b.Position - plane, normal) - Skin - b.Radius);
        }
    }
}
