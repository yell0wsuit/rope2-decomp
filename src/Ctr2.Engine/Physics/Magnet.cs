using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>An authored magnet: a static body that turns toward the candy and, while active, pulls it.</summary>
    public sealed class SimulationMagnet
    {
        internal SimulationMagnet(PhysicsBody body)
        {
            Body = body;
            CachePose();
        }

        /// <summary>Gets the static body built from the merged magnet design.</summary>
        public PhysicsBody Body
        {
            get;
        }

        /// <summary>Gets whether presses have switched the magnet on.</summary>
        public bool Active
        {
            get; internal set;
        }

        /// <summary>Logical position and degree rotation cached at the start of the last update, which presses use.</summary>
        internal Vector2 Position;
        internal float Rotation;

        internal void CachePose()
        {
            Position = Body.Origin * 200;
            Rotation = (float)((float)(Body.Angle * 180.0) * 0.3183098861837907);
        }
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationMagnet> magnets = [];
        private float magnetForce;
        private readonly float candyDefaultDamping;
        /// <summary>The object registered as <c>candy</c>, which magnets track, or null when no candy has that name.</summary>
        private readonly PhysicsBody? magnetCandy;
        /// <summary>Native <c>Candy::magnets</c>: the count of magnets switched on while the candy existed.</summary>
        private int activeMagnets;

        /// <summary>Gets magnets in authored order; like triggers, they are not registered in hint journals.</summary>
        public IReadOnlyList<SimulationMagnet> Magnets => magnets.AsReadOnly();

        /// <summary>Windows Magnet::initWith (0x494420): the master <c>magnet</c> design merged with the placement, plus the
        /// shared <c>magnet_design</c> force.</summary>
        private void AddMagnet(PlacedLevelObject placed, LevelDefinition design)
        {
            Dictionary<string, string> fields = new(Fields(Find(design, "magnet")), StringComparer.Ordinal);
            foreach ((string key, string value) in placed.Source.Attributes)
            {
                fields[key] = value;
            }
            magnets.Add(new(BodyFactory.Create(World, fields)));
            magnetForce = Value(Find(design, "magnet_design"), "force");
        }

        /// <summary>Windows Magnet::processTouchDown (0x494AA0): a press within 95 logical units of the point 25 units along the
        /// cached facing toggles the newest such magnet, counting it on the candy.</summary>
        private bool TapMagnets(Vector2 point)
        {
            for (int i = magnets.Count - 1; i >= 0; i--)
            {
                SimulationMagnet magnet = magnets[i];
                float radians = (float)((magnet.Rotation + 90f) * 3.141592653589793 * 0.005555555555555556);
                float s = MathF.Sin(radians), c = MathF.Cos(radians);
                float dx = (c * 25) + magnet.Position.X - point.X, dy = (s * 25) + magnet.Position.Y - point.Y;
                if (MathF.Sqrt((dy * dy) + (dx * dx)) >= 95)
                {
                    continue;
                }
                magnet.Active = !magnet.Active;
                if (MagnetCandy() != null)
                {
                    activeMagnets += magnet.Active ? 1 : -1;
                }
                return true;
            }
            return false;
        }

        /// <summary>Windows Magnet::update (0x494D80): the magnet faces the candy; an active one pulls it with the design force and
        /// damping 3, while an inactive one restores the candy's design damping once no magnet is active.</summary>
        private void UpdateMagnets()
        {
            foreach (SimulationMagnet magnet in magnets)
            {
                magnet.CachePose();
                if (MagnetCandy() is not PhysicsBody candy)
                {
                    continue;
                }
                Vector2 direction = candy.Position - magnet.Body.Position;
                // The x87 fpatan rounds an extended-precision result to float.
                float angle = (float)Math.Atan2(direction.Y, direction.X);
                World.SetTransform(magnet.Body, magnet.Body.Origin, angle + 1.5707964f);
                if (magnet.Active)
                {
                    float length = MathF.Sqrt((direction.X * direction.X) + (direction.Y * direction.Y));
                    if (length >= 1.1920929e-7f)
                    {
                        float inverse = 1 / length;
                        direction = new(direction.X * inverse, direction.Y * inverse);
                    }
                    candy.ApplyForceToCenter(new(-magnetForce * direction.X, -magnetForce * direction.Y));
                    candy.LinearDamping = 3;
                }
                else if (activeMagnets == 0)
                {
                    candy.LinearDamping = candyDefaultDamping;
                }
            }
        }

        /// <summary>Gets the named candy while its body remains in the world.</summary>
        private PhysicsBody? MagnetCandy()
        {
            return magnetCandy != null && World.Bodies.Contains(magnetCandy) ? magnetCandy : null;
        }
    }
}
