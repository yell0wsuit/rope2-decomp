using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    public sealed partial class CandySimulation
    {
        /// <summary>Gets water decoration, independent of bodies and gameplay randomness.</summary>
        public WaterEffects? WaterEffects
        {
            get; private set;
        }

        private float? waterLevel;
        private float waterFlow;
        private LevelDefinition? waterMaster;
        private readonly Dictionary<PhysicsBody, WaterParameters?> waterParameters = [];
        private readonly HashSet<PhysicsBody> underwater = [];
        private readonly Dictionary<PhysicsBody, List<int>> underwaterHistory = [];

        private sealed record WaterParameters(float Gravity, float Linear, float Angular, float BelowGravity, float BelowLinear, float BelowAngular);

        private void AddWater(PlacedLevelObject placed, LevelDefinition design)
        {
            waterLevel = Value(placed.Source, "y") * 2;
            waterFlow = Value(placed.Source, "flowspeed");
            waterMaster = design;
            WaterEffects = new(Screen, waterLevel.Value);
        }

        private WaterParameters? WaterDesign(PhysicsBody body)
        {
            LevelDefinition design = waterMaster!;
            string? kind = Candies.Contains(body) ? "candy" : Characters.Contains(body) ? "omnom" :
                fireballs.Any(f => f.Body == body) ? "fireball" : bookas.Any(b => b.Body == body) ? "booka" :
                gremlins.Any(g => g.Body == body) ? "gremlin" : throwers.Any(t => t.Body == body) ? "thrower" : null;
            IReadOnlyDictionary<string, string>? fields = kind != null ? Fields(Find(design, kind)) : shapes.FirstOrDefault(s => s.Body == body)?.Design;
            if (fields == null)
            {
                return null;
            }
            float gravity = NativeText.FloatValue(fields.GetValueOrDefault("gravityScale"));
            LevelElement water = Find(design, "water_design");
            // Windows 0x48D6B0 uses a default ostringstream: six significant digits, then floatValue reads it back.
            static float StreamFloat(float value)
            {
                return NativeText.FloatValue(value.ToString("G6", CultureInfo.InvariantCulture));
            }
            return new(gravity, NativeText.FloatValue(fields.GetValueOrDefault("linearDamping")), NativeText.FloatValue(fields.GetValueOrDefault("angularDamping")),
                StreamFloat(gravity * Value(water, "gravity_multiplier")), StreamFloat(Value(water, "water_damping")), StreamFloat(Value(water, "water_angular_damping")));
        }

        /// <summary>Windows Water::update (0x4AF1C0), after magnets and before appended children. Bodies are newest first;
        /// callbacks run every update and design parameters are cached on the first sighting.</summary>
        private void UpdateWater()
        {
            if (waterLevel is not float level)
            {
                return;
            }
            WaterEffects!.Step();
            foreach (PhysicsBody body in World.Bodies)
            {
                if (body.Type != PhysicsBodyType.Dynamic)
                {
                    continue;
                }
                if (!waterParameters.TryGetValue(body, out WaterParameters? parameters))
                {
                    parameters = WaterDesign(body);
                    waterParameters.Add(body, parameters);
                }
                if (parameters == null)
                {
                    continue;
                }
                float center = body.Position.Y * 200;
                bool below = center > level;
                _ = below ? underwater.Add(body) : underwater.Remove(body);
                if (Characters.Contains(body))
                {
                    if (!underwaterHistory.TryGetValue(body, out List<int>? history))
                    {
                        history = [];
                        underwaterHistory.Add(body, history);
                    }
                    history.Add(below ? 1 : 0);
                    if (history.Count > 60)
                    {
                        history.RemoveAt(0);
                    }
                }
                if (below && fireballs.FirstOrDefault(f => f.Body == body && f.State == SimulationFireball.FireballState.Idle) is SimulationFireball fireball)
                {
                    body.IsSensor = true;
                    body.SetLinearVelocity(System.Numerics.Vector2.Zero);
                    fireball.State = SimulationFireball.FireballState.FadingWater;
                    fireball.FadeStartedAt = Tick;
                }
                float depth = MathF.Min(center - level, 80);
                body.GravityScale = below ? (float)(depth * .0125) * parameters.BelowGravity : parameters.Gravity;
                body.LinearDamping = below ? parameters.BelowLinear : parameters.Linear;
                body.AngularDamping = below ? parameters.BelowAngular : parameters.Angular;
                WaterEffects.Observe(body, below);
            }
            WaterEffects.EndStep();
            waterLevel = level + (waterFlow * PhysicsWorld.TimeStep);
        }

        internal bool IsUnderwater(PhysicsBody body)
        {
            return underwater.Contains(body);
        }

        internal float UnderwaterAverage(PhysicsBody body, int samples)
        {
            return underwaterHistory.TryGetValue(body, out List<int>? history) ?
                (float)(history.TakeLast(samples).Sum() * (samples == 5 ? .2 : 0.01694915254237288)) : 0;
        }

        /// <summary>The character child consumes the previous water callbacks, before this tick's late water update.</summary>
        private void UpdateUnderwaterCharacters()
        {
            foreach (PhysicsBody body in underwaterHistory.Keys)
            {
                // Fixed denominator even while the history is still filling; Windows rounds the double product to float.
                float average = UnderwaterAverage(body, 59);
                if (average <= .5f || body.Type != PhysicsBodyType.Dynamic || !World.Bodies.Contains(body))
                {
                    continue;
                }
                float degrees = (float)((float)(body.Angle * 180.0) * 0.3183098861837907);
                while (MathF.Abs(degrees) > 360)
                {
                    degrees = degrees > 0 ? degrees - 360 : degrees + 360;
                }
                if (degrees < 0)
                {
                    degrees += 360;
                }
                if (degrees >= 180)
                {
                    float distance = 360 - degrees;
                    body.ApplyAngularImpulse((float)(distance * .0000001) * distance);
                }
                else
                {
                    body.ApplyAngularImpulse(-(float)(degrees * .0000001) * degrees);
                }
            }

        }
    }
}
