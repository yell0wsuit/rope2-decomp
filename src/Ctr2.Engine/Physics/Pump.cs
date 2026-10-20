using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>One authored pump: a bodiless touch target that blows a cone of impulses along its rotated up axis.</summary>
    /// <param name="Position">Logical-space position.</param>
    /// <param name="Angle">Authored clockwise rotation in degrees.</param>
    public sealed record SimulationPump(Vector2 Position, float Angle);

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationPump> pumps = [];
        /// <summary>Rope material points, which carry the native material-point body type that pumps ignore.</summary>
        private readonly HashSet<PhysicsBody> materialPoints = [];
        private float pumpPower, pumpLength, pumpWidth;

        /// <summary>Gets the authored pumps in scene order.</summary>
        /// <value>Pumps never own a body.</value>
        public IReadOnlyList<SimulationPump> Pumps => pumps.AsReadOnly();

        private void AddPump(PlacedLevelObject placed, LevelDefinition design)
        {
            if (pumps.Count == 0)
            {
                // The pump design supplies every pump's power and cone; an authored power attribute is ignored.
                LevelElement pump = Find(design, "pump_design");
                pumpPower = Value(pump, "power");
                pumpLength = Value(pump, "cone_height");
                pumpWidth = Value(pump, "cone_width");
                if (pumpPower < 0 || pumpLength <= 0 || pumpWidth < 0)
                {
                    throw new InvalidDataException("Invalid pump design.");
                }
            }
            pumps.Add(new(placed.Position!.Value, Value(placed.Source, "angle")));
        }

        /// <summary>Native <c>Pump::processTouchDown</c> for the newest pump within 90 logical units of the press.</summary>
        private bool TapPump(Vector2 point)
        {
            for (int i = pumps.Count - 1; i >= 0; i--)
            {
                SimulationPump pump = pumps[i];
                Vector2 d = point - pump.Position;
                if (MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) < 90)
                {
                    Blow(pump);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Casts the five cone rays, then pushes each distinct game body found, scaled down with distance.</summary>
        private void Blow(SimulationPump pump)
        {
            Vector2 origin = new((float)(pump.Position.X / 200.0), (float)(pump.Position.Y / 200.0));
            float angle = (float)(pump.Angle * 3.14159265 / 180.0);
            List<PhysicsBody> found = [];
            float halfWidth = (float)(pumpWidth / 2.0), halfLength = (float)(pumpLength / 2.0);
            foreach (Vector2 end in new Vector2[] { new(0, -pumpLength), new(-halfWidth, -pumpLength), new(-halfWidth, -halfLength), new(halfWidth, -pumpLength), new(halfWidth, -halfLength) })
            {
                World.RayCast(origin, origin + PhysicsMath.Rotate(end, angle), hit =>
                {
                    // Bodies without a game object (frog tongue anchors) and rope material points are ignored; the ray continues.
                    if (hit.Body is PhysicsBody body && !materialPoints.Contains(body) && !tongueAnchors.Contains(body) && !found.Contains(body) && found.Count < 9)
                    {
                        found.Add(body);
                    }
                    return -1;
                });
            }
            foreach (PhysicsBody body in found)
            {
                Vector2 impulse = PhysicsMath.Rotate(new(0, -pumpPower), angle);
                Vector2 center = body.Position, offset = center - origin;
                float t = MathF.Sqrt((offset.X * offset.X) + (offset.Y * offset.Y)) / pumpLength;
                if (t <= 1)
                {
                    t = 1 - t;
                    body.ApplyLinearImpulse(new(impulse.X * t, impulse.Y * t), center);
                }
            }
        }
    }
}
