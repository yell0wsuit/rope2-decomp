// Altered C# circle/circle subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A persistent one-point solid circle manifold.</summary>
    internal sealed class CircleContact : SinglePointContact
    {
        private CircleContact(PhysicsBody first, PhysicsBody second, float friction, float restitution)
            : base(first, second, friction, restitution) { }

        internal static CircleContact? Create(PhysicsBody a, PhysicsBody b, float friction, float restitution)
        {
            float radius = a.Radius + b.Radius;
            return Vector2.DistanceSquared(a.Position, b.Position) > radius * radius ? null : new(a, b, friction, restitution);
        }

        protected override (Vector2 Normal, Vector2 Point) VelocityGeometry()
        {
            Vector2 delta = b.Position - a.Position;
            Vector2 normal = delta.LengthSquared() > Epsilon * Epsilon ? PhysicsMath.Normalize(delta) : Vector2.UnitX;
            Vector2 pointA = a.Position + (a.Radius * normal);
            Vector2 pointB = b.Position - (b.Radius * normal);
            return (normal, .5f * (pointA + pointB));
        }

        protected override (Vector2 Normal, Vector2 Point, float Separation) PositionGeometry()
        {
            Vector2 delta = b.Position - a.Position;
            Vector2 normal = PhysicsMath.Normalize(delta);
            return (normal, .5f * (a.Position + b.Position), Vector2.Dot(delta, normal) - a.Radius - b.Radius);
        }
    }
}
