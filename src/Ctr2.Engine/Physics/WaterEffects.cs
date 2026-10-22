using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A light trace uses a random horizontal sample resolved against its trimmed artwork width.</summary>
    public sealed record WaterLight(int Quad, uint HorizontalSample, float Opacity);

    /// <summary>Immutable logical-space pose: top-left wrapper for bubbles, center for drops; quad is an atlas 250 index.</summary>
    public sealed record WaterParticle(int Quad, Vector2 Position, Vector2 Scale, float Rotation, float Opacity, float HarmonicX = 0, float BounceY = 1);

    /// <summary>Windows water decoration on the fixed game clock, with a private random stream independent of physics.</summary>
    public sealed class WaterEffects
    {
        private readonly Vector2 screen;
        private readonly float level;
        private readonly List<Light> lights = [];
        private readonly List<BubbleSystem> systems = [];
        private readonly List<Particle> drops = [];
        private readonly Dictionary<PhysicsBody, float> immersion = [];
        private readonly List<Collision> collisions = [];
        private uint random = 0x57415452;
        private float nextBubbles, time;
        private bool firstFrame = true;

        /// <summary>Creates decoration for the initial logical screen and surface.</summary>
        public WaterEffects(Vector2 screen, float level)
        {
            if (!float.IsFinite(screen.X) || !float.IsFinite(screen.Y) || screen.X <= 0 || screen.Y <= 0 ||
                screen.X > 2000000 || screen.Y > 2000000 || !float.IsFinite(level))
            {
                throw new ArgumentOutOfRangeException(nameof(screen));
            }
            this.screen = screen;
            this.level = level;
            for (int i = 0; i < 7; i++)
            {
                lights.Add(new(3 + (int)(Next() & 1), Next() % 21 * .1f));
            }
            nextBubbles = NextDelay();
            Snapshot();
        }

        /// <summary>Gets seven immutable light snapshots.</summary>
        public IReadOnlyList<WaterLight> Lights { get; private set; } = [];
        /// <summary>Gets immutable rising bubble snapshots.</summary>
        public IReadOnlyList<WaterParticle> Bubbles { get; private set; } = [];
        /// <summary>Gets immutable foreground splash snapshots.</summary>
        public IReadOnlyList<WaterParticle> Drops { get; private set; } = [];

        /// <summary>Advances decoration by one fixed tick without touching a physics body.</summary>
        public void Step()
        {
            const float dt = PhysicsWorld.TimeStep;
            time += dt;
            // DeltaCollision only erases the expired prefix when an unexpired record follows it.
            int live = collisions.FindIndex(c => c.Expires >= time);
            if (live > 0)
            {
                collisions.RemoveRange(0, live);
            }
            foreach (PhysicsBody body in immersion.Keys.ToArray())
            {
                if (immersion[body] != -1)
                {
                    immersion[body] -= dt;
                    if (immersion[body] < 0)
                    {
                        _ = immersion.Remove(body);
                    }
                }
            }
            AdvanceDrops();
            nextBubbles -= dt;
            if (nextBubbles < 0)
            {
                SpawnBubbles(new(Next() % ((uint)screen.X + 1), screen.Y + 10), true);
                nextBubbles = NextDelay();
            }
            foreach (Light light in lights)
            {
                light.Elapsed += dt;
                if (light.Waiting)
                {
                    if (light.Elapsed >= light.Delay)
                    {
                        light.Waiting = false;
                        light.Elapsed = 0;
                        light.X = Next();
                    }
                }
                else if (light.Elapsed >= 2)
                {
                    light.Waiting = true;
                    light.Elapsed = 0;
                }
            }
            foreach (BubbleSystem system in systems)
            {
                foreach (Particle p in system.Particles)
                {
                    p.HarmonicX = MathF.Sin(p.Phase * 5) * 40;
                    p.Phase += dt;
                    p.Age += dt;
                }
                if (system.Elapsed <= system.Duration)
                {
                    system.Counter += dt;
                    while (system.Counter > .1f && system.Particles.Count < 3)
                    {
                        system.Particles.Add(NewParticle(system.Origin, false, system.Duration));
                        system.Counter -= .1f;
                    }
                    system.Elapsed += dt;
                }
                Advance(system.Particles, false, system.Origin);
            }
            _ = systems.RemoveAll(s => s.Elapsed > s.Duration && s.Particles.Count == 0);
            Snapshot();
        }

        /// <summary>Offers a non-consuming water touch, with bubbles only below the initial surface.</summary>
        public void Touch(Vector2 position)
        {
            if (float.IsFinite(position.X) && float.IsFinite(position.Y) && position.Y > level)
            {
                SpawnBubbles(position, false);
                Snapshot();
            }
        }

        internal event Action? Splashed;

        internal void Observe(PhysicsBody body, bool below)
        {
            if (!below)
            {
                if (immersion.TryGetValue(body, out float state) && state == -1)
                {
                    immersion[body] = .06f;
                }
                return;
            }
            Vector2 center = body.Position * 200;
            if (!immersion.ContainsKey(body) && !collisions.Any(c => c.Body == body && Vector2.Distance(c.Position, center) < 10))
            {
                collisions.Add(new(body, center, time + .2f));
                if (!firstFrame && body.Velocity.Length() > 1.7f)
                {
                    Splashed?.Invoke();
                    for (int i = 0; i < 10; i++)
                    {
                        drops.Add(NewParticle(center, true, 1.3f));
                    }
                }
            }
            immersion[body] = -1;
        }

        internal void EndStep()
        {
            firstFrame = false;
            Snapshot();
        }

        private void SpawnBubbles(Vector2 origin, bool longLife)
        {
            BubbleSystem system = new(origin, longLife ? 10.2f : 1.2f);
            system.Particles.Add(NewParticle(origin, false, system.Duration));
            systems.Add(system);
        }

        private Particle NewParticle(Vector2 origin, bool drop, float life)
        {
            Vector2 pos = origin + new Vector2(30 * Signed(), (drop ? 10 : 30) * Signed());
            float degrees = -90 + ((drop ? 90 : 12) * Signed());
            float angle = (float)(degrees * Math.PI / 180);
            float speed = (drop ? 280 : 120) + ((drop ? 60 : 80) * Signed());
            float start = (drop ? .9f : .75f) + ((drop ? .4f : .25f) * Signed());
            Vector2 scale = new(start);
            float endScale = drop ? .5f + (.15f * Signed()) : .3f;
            Vector2 end = new(endScale);
            float alpha = drop ? 0 : .8f + (.4f * Signed());
            Particle particle = new(pos, origin, new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * speed, scale, (end - scale) / life,
                life, alpha, drop ? 160 + (30 * Signed()) : 0, drop ? 50 + (15 * Signed()) : 0,
                (drop ? 9 : 0) + (int)(Next() % 3), (float)(Next() / 4294967296.0 * 2))
            {
                Surface = level
            };
            if (drop)
            {
                particle.DrawAlpha = 0;
                particle.Alpha = pos.Y > origin.Y ? 0 : 1;
                particle.Rotation = (float)((float)Math.Atan2(particle.Velocity.Y, particle.Velocity.X) + (Math.PI / 2));
            }
            return particle;
        }

        private void AdvanceDrops()
        {
            Advance(drops, true, Vector2.Zero);
        }

        private static void Advance(List<Particle> particles, bool drop, Vector2 origin)
        {
            const float dt = PhysicsWorld.TimeStep;
            for (int i = 0; i < particles.Count; i++)
            {
                Particle p = particles[i];
                if (p.Life <= 0)
                {
                    particles[i] = particles[^1];
                    particles.RemoveAt(particles.Count - 1);
                    i--;
                    continue;
                }
                Vector2 offset = p.Position - p.Origin;
                Vector2 radial = offset.LengthSquared() > 0 ? Vector2.Normalize(offset) : Vector2.Zero;
                Vector2 tangent = new(-radial.Y, radial.X);
                p.Velocity += ((drop ? new Vector2(0, 600) : Vector2.Zero) + (radial * p.Radial) + (tangent * p.Tangent)) * dt;
                p.Position += p.Velocity * dt;
                p.Alpha -= p.AlphaRate * dt;
                p.Life -= dt;
                p.Scale += p.ScaleRate * dt;
                p.DrawAlpha = p.Alpha;
                p.DrawRotation = p.Rotation;
                if (drop)
                {
                    p.Rotation = (float)((float)Math.Atan2(p.Velocity.Y, p.Velocity.X) + (Math.PI / 2));
                    p.Alpha = p.Position.Y > p.Origin.Y ? 0 : p.Life / 1.3f;
                }
                else
                {
                    float cy = origin.Y - p.Position.Y;
                    float h = origin.Y - p.Surface - 5;
                    if (cy >= h - 12)
                    {
                        p.DrawAlpha *= MathF.Max(0, 1 - ((cy - h + 12) * .083333336f));
                    }
                }
            }
        }

        private void Snapshot()
        {
            static float Opacity(Light l)
            {
                float t = l.Elapsed;
                return l.Waiting ? 0 : t < .7f ? t / .7f * .3f : t < 1.3f ? .3f + ((t - .7f) / .6f * .1f) : (1 - ((t - 1.3f) / .7f)) * .4f;
            }
            Lights = Array.AsReadOnly(lights.Select(l => new WaterLight(l.Quad, l.X, Opacity(l))).ToArray());
            Bubbles = Array.AsReadOnly(systems.SelectMany(s => s.Particles.Select(p =>
            {
                float phase = p.Age % .4f;
                float t = (phase < .2f ? phase : .4f - phase) / .2f;
                float ease = t < .5f ? 2 * t * t : 1 - (2 * (1 - t) * (1 - t));
                return new WaterParticle(p.Quad, p.Position, p.Scale, 0, Math.Clamp(p.DrawAlpha, 0, 1), p.HarmonicX, 1 + (.2f * ease));
            })).ToArray());
            Drops = Array.AsReadOnly(drops.Select(p => new WaterParticle(p.Quad, p.Position, p.Scale, p.DrawRotation, Math.Clamp(p.DrawAlpha, 0, 1))).ToArray());
        }

        private uint Next()
        {
            random ^= random << 13;
            random ^= random >> 17;
            random ^= random << 5;
            return random;
        }
        private float Signed()
        {
            return (float)((Next() / 4294967296.0 * 2) - 1);
        }

        private float NextDelay()
        {
            return (Next() % 3) + 4 + (float)(Next() / 4294967296.0);
        }

        private sealed class Light(int quad, float delay)
        {
            internal readonly int Quad = quad;
            internal readonly float Delay = delay;
            internal float Elapsed;
            internal bool Waiting = true;
            internal uint X;
        }
        private sealed class BubbleSystem(Vector2 origin, float duration)
        {
            internal readonly Vector2 Origin = origin;
            internal readonly float Duration = duration;
            internal readonly List<Particle> Particles = [];
            internal float Counter, Elapsed;
        }
        private sealed class Particle(Vector2 position, Vector2 origin, Vector2 velocity, Vector2 scale, Vector2 scaleRate,
            float life, float alpha, float radial, float tangent, int quad, float phase)
        {
            internal Vector2 Position = position, Velocity = velocity, Scale = scale;
            internal readonly Vector2 Origin = origin, ScaleRate = scaleRate;
            internal readonly float AlphaRate = alpha / life, Radial = radial, Tangent = tangent;
            internal readonly int Quad = quad;
            internal float Life = life, Alpha = alpha, DrawAlpha = alpha, Rotation, DrawRotation, Age, Phase = phase;
            internal float Surface;
            internal float HarmonicX = MathF.Sin(phase * 5) * 40;
        }
        private sealed record Collision(PhysicsBody Body, Vector2 Position, float Expires);
    }
}
