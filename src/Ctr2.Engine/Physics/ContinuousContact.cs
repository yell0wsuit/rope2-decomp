// Altered C# circle/static-polygon subset of Box2D 2.2.1. See BOX2D_LICENSE.txt.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsWorld
    {
        /// <summary>Gets or sets whether circles use swept contacts against static terrain.</summary>
        /// <value>True by default; false isolates the discrete solver for reference tests.</value>
        public bool ContinuousPhysics
        {
            get; set;
        } = true;

        /// <summary>Resolves swept circle impacts with contact-only substeps, without reapplying gravity or joints.</summary>
        /// <param name="starts">Circle transforms before the discrete step.</param>
        private void SolveContinuous(Dictionary<PhysicsBody, (Vector2 Position, float Angle)> starts)
        {
            foreach ((PhysicsBody body, (Vector2 position, float angle)) in starts)
            {
                Vector2 start = position;
                float startAngle = angle;
                float remaining = TimeStep;
                for (int impact = 0; impact < 32; impact++)
                {
                    Vector2 end = body.Position;
                    float endAngle = body.Angle;
                    float fraction = 1;
                    TerrainFixture? selected = null;
                    foreach (TerrainFixture fixture in terrain)
                    {
                        if (!ShouldCollide(bodyProxies[body][0].Filter, terrainProxies[fixture].Filter))
                        {
                            continue;
                        }
                        float candidate = Sweep(start, end, body.Radius, fixture);
                        if (candidate < fraction)
                        {
                            fraction = candidate;
                            selected = fixture;
                        }
                    }
                    if (selected == null)
                    {
                        break;
                    }
                    body.Position = start + (fraction * (end - start));
                    body.Angle = startAngle + (fraction * (endAngle - startAngle));
                    List<TerrainContact> touching = [];
                    TerrainContact? first = TerrainContact.Create(body, selected);
                    if (first == null)
                    {
                        // Numerical sweep/manifold disagreement stops at the safe transform.
                        break;
                    }
                    touching.Add(first);
                    for (int index = terrain.Count - 1; index >= 0; index--)
                    {
                        TerrainFixture fixture = terrain[index];
                        if (fixture != selected && ShouldCollide(bodyProxies[body][0].Filter, terrainProxies[fixture].Filter) && TerrainContact.Create(body, fixture) is TerrainContact contact)
                        {
                            touching.Add(contact);
                        }
                    }
                    for (int iteration = 0; iteration < 20; iteration++)
                    {
                        bool solved = true;
                        foreach (TerrainContact contact in touching)
                        {
                            solved = contact.SolvePosition(true) && solved;
                        }
                        if (solved)
                        {
                            break;
                        }
                    }
                    foreach (TerrainContact contact in touching)
                    {
                        contact.Initialize();
                    }
                    for (int iteration = 0; iteration < 15; iteration++)
                    {
                        foreach (TerrainContact contact in touching)
                        {
                            contact.SolveVelocity();
                        }
                    }
                    remaining *= 1 - fraction;
                    start = body.Position;
                    startAngle = body.Angle;
                    if (impact == 31)
                    {
                        // Exhausted impact budget: retain the last safe position rather than tunnel.
                        break;
                    }
                    Vector2 translation = remaining * body.Velocity;
                    if (translation.LengthSquared() > 4)
                    {
                        body.Velocity *= 2 / translation.Length();
                    }
                    float rotation = remaining * body.AngularVelocity;
                    if (rotation * rotation > MathF.PI * MathF.PI / 4)
                    {
                        body.AngularVelocity *= MathF.PI / 2 / MathF.Abs(rotation);
                    }
                    body.Position += remaining * body.Velocity;
                    body.Angle += remaining * body.AngularVelocity;
                }
            }
        }

        /// <summary>Finds the first entry into a polygon expanded by the time-of-impact target radius.</summary>
        /// <param name="start">Initial circle center.</param>
        /// <param name="end">Discrete final center.</param>
        /// <param name="radius">Circle radius.</param>
        /// <param name="fixture">Static convex geometry.</param>
        /// <returns>The earliest normalized fraction, or one for no new impact.</returns>
        private static float Sweep(Vector2 start, Vector2 end, float radius, TerrainFixture fixture)
        {
            float target = MathF.Max(LinearSlop, radius - LinearSlop);
            Vector2 delta = end - start;
            float travel = delta.LengthSquared();
            if (travel < 1e-16f)
            {
                return 1;
            }
            float closest = float.PositiveInfinity;
            bool inside = true;
            for (int i = 0; i < fixture.Points.Length; i++)
            {
                Vector2 a = fixture.Points[i], edge = fixture.Points[(i + 1) % fixture.Points.Length] - a;
                inside &= Vector2.Dot(start - a, fixture.Normals[i]) <= 0;
                float projection = Math.Clamp(Vector2.Dot(start - a, edge) / edge.LengthSquared(), 0, 1);
                closest = MathF.Min(closest, Vector2.DistanceSquared(start, a + (projection * edge)));
            }
            if (inside || closest <= (target + (.25f * LinearSlop)) * (target + (.25f * LinearSlop)))
            {
                return 1;
            }
            float fraction = 1;
            for (int i = 0; i < fixture.Points.Length; i++)
            {
                Vector2 a = fixture.Points[i], edge = fixture.Points[(i + 1) % fixture.Points.Length] - a;
                Vector2 normal = fixture.Normals[i];
                float speed = Vector2.Dot(delta, normal);
                if (speed < 0)
                {
                    float t = (target - Vector2.Dot(start - a, normal)) / speed;
                    float projection = Vector2.Dot(start + (t * delta) - a, edge) / edge.LengthSquared();
                    if (t >= 0 && t < fraction && projection >= 0 && projection <= 1)
                    {
                        fraction = t;
                    }
                }
                Vector2 offset = start - a;
                float b = Vector2.Dot(offset, delta);
                float c = offset.LengthSquared() - (target * target);
                float discriminant = (b * b) - (travel * c);
                if (b < 0 && discriminant >= 0)
                {
                    float t = (-b - MathF.Sqrt(discriminant)) / travel;
                    if (t >= 0 && t < fraction)
                    {
                        fraction = t;
                    }
                }
            }
            return fraction;
        }
    }
}
