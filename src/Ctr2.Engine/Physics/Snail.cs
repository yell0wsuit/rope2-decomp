using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>A snail body that follows a named polygon's cyclic spline when activated.</summary>
    public sealed class SimulationSnail
    {
        internal SimulationSnail(PhysicsBody body, string polygonName, float speed, float parameter, bool active)
        {
            Body = body;
            PolygonName = polygonName;
            EditorSpeed = speed;
            Parameter = parameter;
            Active = active;
            Position = body.Origin * 200;
        }

        /// <summary>Gets the snail's body.</summary>
        public PhysicsBody Body
        {
            get;
        }
        /// <summary>Gets whether the snail is walking.</summary>
        public bool Active
        {
            get; internal set;
        }
        /// <summary>Gets the cyclic track parameter, in the inclusive interval [0, 1].</summary>
        public float Parameter
        {
            get; internal set;
        }
        /// <summary>Gets the touch center: the last child-update pose plus the table-rotated visual offset.</summary>
        public Vector2 VisualCenter => Position + CandySimulation.SnailVisualOffset(Body.Angle);

        internal readonly string PolygonName;
        internal readonly float EditorSpeed;
        internal SmoothCyclePath Path = null!;
        internal Vector2 Position;
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationSnail> snails = [];

        /// <summary>Gets snails in authored and initial journal order.</summary>
        public IReadOnlyList<SimulationSnail> Snails => snails.AsReadOnly();

        /// <summary>The Windows snail inlines the table lookup with a full reciprocal of pi (0x4A7460).</summary>
        /// <param name="angle">Live body angle in radians.</param>
        /// <returns>The rotated (0,-57) logical-unit offset.</returns>
        internal static Vector2 SnailVisualOffset(float angle)
        {
            int index = (int)((float)(angle * 1024.0) * 0.3183098861837907 * .5) & 0x3FF;
            return new(57 * Sines[index], -57 * Cosines[index]);
        }

        /// <summary>Windows 0x4A69F0 merges the design and mirrors the first four polygon points for negative speeds.</summary>
        private void AddSnail(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "snail")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            float speed = NativeText.FloatValue(fields.GetValueOrDefault("speed"));
            if (speed < 0)
            {
                string[] components = fields.GetValueOrDefault("polygon", "").Split(',');
                if (components.Length < 8)
                {
                    throw new InvalidDataException("A reversed snail requires four authored polygon vertices.");
                }
                for (int i = 0; i < 8; i += 2)
                {
                    components[i] = Printed(-NativeText.FloatValue(components[i]));
                }
                fields["polygon"] = string.Join(',', components.Take(8)) + ",";
            }
            PhysicsBody body = BodyFactory.Create(World, fields);
            snails.Add(new(body, fields.GetValueOrDefault("attachTo", ""), speed,
                Math.Clamp(NativeText.FloatValue(fields.GetValueOrDefault("percent")) * .01f, 0, 1),
                NativeText.BoolValue(fields.GetValueOrDefault("activeOnStart"))));
        }

        /// <summary>Native loader post-pass (before pins), then Snail::attachToPolygon (0x4A6FF0).</summary>
        private void AttachSnails(LevelLayout layout, IReadOnlyDictionary<PlacedLevelObject, PhysicsBody> shapeBodies)
        {
            foreach (SimulationSnail snail in snails)
            {
                if (!layout.Named.TryGetValue(snail.PolygonName, out PlacedLevelObject? target) ||
                    !shapeBodies.TryGetValue(target, out PhysicsBody? body) || shapes.FirstOrDefault(s => s.Body == body) is not GeneralShape shape ||
                    shape.Design.GetValueOrDefault("shapeType") != "polygon")
                {
                    throw new InvalidDataException("A snail requires a named polygon track.");
                }
                IReadOnlyDictionary<string, string> fields = shape.Design;
                float x = NativeText.FloatValue(fields.GetValueOrDefault("x")), y = NativeText.FloatValue(fields.GetValueOrDefault("y"));
                int factor = fields.TryGetValue("smoothFactor", out string? text) ? NativeText.IntValue(text) : 1;
                SmoothCyclePath path = new(new(x + x, y + y), fields.GetValueOrDefault("polygon", ""),
                    NativeText.BoolValue(fields.GetValueOrDefault("normalizeShape")), factor);
                snail.Path = path;
                Vector2 p = path.VectorAt(snail.Parameter);
                // The source adds local spline coordinates without rotating them by the target body's angle.
                World.SetTransform(snail.Body, new(body.Origin.X + (p.X * .005f), body.Origin.Y + (p.Y * .005f)), SnailAngle(path, snail.Parameter));
            }
        }

        private static float SnailAngle(SmoothCyclePath path, float t)
        {
            Vector2 d = path.DerivativeAt(t);
            return (float)Math.Atan2(d.Y, d.X);
        }

        /// <summary>Windows 0x4A7570 sets velocities after the step from the spline derivative and position correction.</summary>
        private void UpdateSnails()
        {
            foreach (SimulationSnail snail in snails)
            {
                PhysicsBody body = snail.Body;
                snail.Position = body.Origin * 200;
                if (!snail.Active)
                {
                    continue;
                }
                SmoothCyclePath path = snail.Path;
                float t = snail.Parameter, speed = (float)(snail.EditorSpeed * .06);
                Vector2 d = path.DerivativeAt(t);
                Vector2 velocity = new(d.X * speed * path.MaxRealT, d.Y * speed * path.MaxRealT);
                Vector2 point = path.VectorAt(t);
                Vector2 correction = new(path.Position.X + point.X - (body.Origin.X * 200), path.Position.Y + point.Y - (body.Origin.Y * 200));
                float next = CycleSnailParameter(t + (PhysicsWorld.TimeStep * speed));
                float angular = SnailAngle(path, next) - SnailAngle(path, t);
                // Windows uses pi, not the rounded 3.1416/6.2832 constants in the Android decompile.
                while (angular < MathF.PI)
                {
                    angular += 2 * MathF.PI;
                }
                while (angular > MathF.PI)
                {
                    angular -= 2 * MathF.PI;
                }
                body.SetLinearVelocity(new((velocity.X + correction.X) * .005f, (velocity.Y + correction.Y) * .005f));
                body.SetAngularVelocity(angular / PhysicsWorld.TimeStep);
                snail.Parameter = next;
            }
        }

        private static float CycleSnailParameter(float value)
        {
            while (value < 0)
            {
                value += 1;
            }
            while (value > 1)
            {
                value -= 1;
            }
            return value;
        }

        /// <summary>Windows 0x4A7460 uses the table-rotated visual center and a strict 80-unit press radius.</summary>
        private bool TapSnails(Vector2 point)
        {
            for (int i = snails.Count - 1; i >= 0; i--)
            {
                SimulationSnail snail = snails[i];
                Vector2 d = snail.VisualCenter - point;
                if (MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) >= 80)
                {
                    continue;
                }
                snail.Body.SetLinearVelocity(Vector2.Zero);
                snail.Body.SetAngularVelocity(0);
                if (snail.Active)
                {
                    World.SetTransform(snail.Body, snail.Body.Origin, SnailAngle(snail.Path, snail.Parameter));
                }
                snail.Active = !snail.Active;
                return true;
            }
            return false;
        }
    }
}
