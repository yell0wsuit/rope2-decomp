using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Immutable logical-space arrival leaf snapshot.</summary>
    /// <param name="Position">Center in logical units.</param>
    /// <param name="Scale">Independent dimension scales.</param>
    /// <param name="Rotation">Clockwise angle in radians.</param>
    /// <param name="Opacity">Remaining premultiplied color intensity.</param>
    /// <param name="Variant">Zero-based leaf variant among the three Windows quads.</param>
    public sealed record ForestLeaf(Vector2 Position, Vector2 Scale, float Rotation, float Opacity, int Variant);

    /// <summary>Fixed-step decorative leaf burst, independent of world bodies and constraints.</summary>
    public sealed class ForestLeafBurst
    {
        private readonly Vector2 origin;
        private readonly List<Particle> particles = [];
        private uint randomState = 0x43545232;
        /// <summary>Creates the production first-level burst around the initial character origin.</summary>
        /// <param name="origin">Logical-space burst origin.</param>
        public ForestLeafBurst(Vector2 origin)
        {
            if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || MathF.Abs(origin.X) > 2000000 || MathF.Abs(origin.Y) > 2000000)
            {
                throw new ArgumentOutOfRangeException(nameof(origin));
            }
            this.origin = origin;
            for (int i = 0; i < 30; i++)
            {
                Vector2 position = origin + new Vector2(SignedRandom(), SignedRandom());
                float angle = (90 + (60 * SignedRandom())) * MathF.PI / 180;
                float speed = 280 + (120 * SignedRandom());
                float radial = 320 + (160 * SignedRandom()), tangential = 60 + (20 * SignedRandom());
                Vector2 scale = new(1.2f + (.3f * SignedRandom()), 1.2f + (.3f * SignedRandom()));
                Vector2 end = new(.4f + (.1f * SignedRandom()), .4f + (.1f * SignedRandom()));
                float rotation = SignedRandom() * 2 * MathF.PI;
                float spin = (200 + (300 * SignedRandom())) * MathF.PI / 180;
                int variant = (int)(NextRandom() % 3);
                particles.Add(new(position, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed, scale, (end - scale) / 1.5f,
                    rotation, spin, radial, tangential, variant));
            }
            Snapshot();
        }
        /// <summary>Gets immutable current particle snapshots.</summary>
        public IReadOnlyList<ForestLeaf> Particles { get; private set; } = [];
        /// <summary>Advances decorative motion one game tick.</summary>
        public void Step()
        {
            _ = particles.RemoveAll(p => p.Life <= 0);
            foreach (Particle particle in particles)
            {
                Vector2 offset = particle.Position - origin;
                Vector2 radial = offset.LengthSquared() > 0 ? Vector2.Normalize(offset) : Vector2.Zero;
                Vector2 tangent = new(-radial.Y, radial.X);
                particle.Velocity += (new Vector2(0, 400) + (radial * particle.Radial) + (tangent * particle.Tangential)) * PhysicsWorld.TimeStep;
                particle.Position += particle.Velocity * PhysicsWorld.TimeStep;
                particle.Scale += particle.ScaleRate * PhysicsWorld.TimeStep;
                particle.Rotation += particle.Spin * PhysicsWorld.TimeStep;
                particle.Life -= PhysicsWorld.TimeStep;
            }
            Snapshot();
        }

        private void Snapshot()
        {
            Particles = Array.AsReadOnly(particles.Select(p => new ForestLeaf(p.Position, p.Scale, p.Rotation, Math.Clamp(p.Life / 1.5f, 0, 1), p.Variant)).ToArray());
        }

        private uint NextRandom()
        {
            randomState ^= randomState << 13;
            randomState ^= randomState >> 17;
            randomState ^= randomState << 5;
            return randomState;
        }

        private float SignedRandom()
        {
            return (float)((NextRandom() / 4294967296.0 * 2) - 1);
        }

        private sealed class Particle(Vector2 position, Vector2 velocity, Vector2 scale, Vector2 scaleRate, float rotation, float spin, float radial, float tangential, int variant)
        {
            internal Vector2 Position = position;
            internal Vector2 Velocity = velocity;
            internal Vector2 Scale = scale;
            internal readonly Vector2 ScaleRate = scaleRate;
            internal float Rotation = rotation;
            internal readonly float Spin = spin;
            internal readonly float Radial = radial;
            internal readonly float Tangential = tangential;
            internal readonly int Variant = variant;
            internal float Life = 1.5f;
        }
    }
}
