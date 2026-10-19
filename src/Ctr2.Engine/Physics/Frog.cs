using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>Native frog tongue states.</summary>
    public enum FrogState
    {
        /// <summary>Tongue retracted.</summary>
        Idle,
        /// <summary>Tongue extending as a growing static sensor.</summary>
        Opening,
        /// <summary>Fully extended this update; becomes active on the next.</summary>
        ChangeToActive,
        /// <summary>Tongue extended as a jointed bridge.</summary>
        Active,
        /// <summary>Tongue retracting as a shrinking static sensor.</summary>
        Closing,
        /// <summary>Fully retracted this update; becomes idle on the next.</summary>
        ChangeToIdle
    }

    /// <summary>One authored frog: a tap-toggled tongue toward its path target, built from physics bodies.</summary>
    public sealed class SimulationFrog
    {
        internal readonly List<PhysicsBody> TongueBodies = [];
        internal float PreviousOpen;

        internal SimulationFrog(Vector2 position, Vector2 finish)
        {
            Position = position;
            TongueFinish = finish;
            Vector2 toFinish = finish - position;
            Angle = MathF.Atan2(toFinish.Y, toFinish.X);
            // Native narrows double-precision cosine and sine of the float angle.
            Direction = new((float)Math.Cos(Angle), (float)Math.Sin(Angle));
            TongueStart = position + (Direction * 40);
            Vector2 tongue = finish - TongueStart;
            Length = MathF.Sqrt((tongue.X * tongue.X) + (tongue.Y * tongue.Y));
        }

        /// <summary>Gets the frog position in logical units.</summary>
        public Vector2 Position
        {
            get;
        }
        /// <summary>Gets the tongue root, 40 logical units from the frog toward the target.</summary>
        public Vector2 TongueStart
        {
            get;
        }
        /// <summary>Gets the authored tongue target in logical units.</summary>
        public Vector2 TongueFinish
        {
            get;
        }
        /// <summary>Gets the unit direction toward the target.</summary>
        public Vector2 Direction
        {
            get;
        }
        /// <summary>Gets the tongue angle in radians.</summary>
        public float Angle
        {
            get;
        }
        /// <summary>Gets the full tongue length in logical units.</summary>
        public float Length
        {
            get;
        }
        /// <summary>Gets the current extension in logical units.</summary>
        public float Open
        {
            get; internal set;
        }
        /// <summary>Gets the tongue state.</summary>
        public FrogState State
        {
            get; internal set;
        }
        /// <summary>Gets the current tongue bodies: one sensor while moving, or end anchors and segments when extended.</summary>
        public IReadOnlyList<PhysicsBody> Bodies => TongueBodies.AsReadOnly();
    }

    public sealed partial class CandySimulation
    {
        private readonly List<SimulationFrog> frogs = [];
        /// <summary>Static tongue end anchors; like the native ones they carry no game object, so they never close a tongue.</summary>
        private readonly HashSet<PhysicsBody> tongueAnchors = [];

        /// <summary>Gets the authored frogs in creation order.</summary>
        public IReadOnlyList<SimulationFrog> Frogs => frogs.AsReadOnly();

        /// <summary>Applies a tap like the scene: frogs newest first (within 78 logical units a frog toggles), then balloons.</summary>
        /// <param name="point">Tap position in logical units.</param>
        /// <returns>Whether a frog consumed the tap.</returns>
        public bool Tap(Vector2 point)
        {
            for (int i = frogs.Count - 1; i >= 0; i--)
            {
                SimulationFrog frog = frogs[i];
                Vector2 d = frog.Position - point;
                if (MathF.Sqrt((d.X * d.X) + (d.Y * d.Y)) >= 78)
                {
                    continue;
                }
                if (frog.State == FrogState.Idle)
                {
                    frog.State = FrogState.Opening;
                }
                else if (frog.State == FrogState.Active)
                {
                    frog.State = FrogState.Closing;
                }
                return true;
            }
            // Balloons, then pumps, were added to the scene before frogs, so they see the tap afterwards in reverse order.
            return TapBalloon(point) || TapPump(point);
        }

        /// <summary>Creates an authored frog; a frog with an authored open tongue extends it during construction.</summary>
        /// <param name="placed">Authored frog.</param>
        private void AddFrog(PlacedLevelObject placed)
        {
            string[] path = placed.Source.Attributes.GetValueOrDefault("path", "").Split(',');
            if (path.Length < 2 || path[0].Length == 0)
            {
                throw new InvalidDataException("Frogs without a tongue path are not supported.");
            }
            float x = Value(placed.Source, "x", 0), y = Value(placed.Source, "y", 0);
            Vector2 position = new Vector2(x, y) * 2;
            Vector2 finish = position + (new Vector2(Number(path[0]), Number(path[1])) * 2);
            SimulationFrog frog = new(position, finish);
            frogs.Add(frog);
            World.RecordContactEvents = true;
            if (placed.Source.Attributes.GetValueOrDefault("tongueOut") == "true")
            {
                frog.State = FrogState.Opening;
                UpdateFrog(frog, 1);
            }
        }

        /// <summary>Closes opening tongues touched by a solid game object during the step, as the frog's contact handler does.</summary>
        private void CloseTouchedTongues()
        {
            if (frogs.Count == 0)
            {
                return;
            }
            foreach (PhysicsContactEvent contact in World.DrainContactEvents())
            {
                if (contact.Kind != PhysicsContactEventKind.Begin)
                {
                    continue;
                }
                PhysicsBody? a = World.GetFixtureBody(contact.FirstFixture), b = World.GetFixtureBody(contact.SecondFixture);
                // Both bodies need a game object; terrain, tongue anchors and the main static body have none here.
                if (a == null || b == null || tongueAnchors.Contains(a) || tongueAnchors.Contains(b))
                {
                    continue;
                }
                foreach (SimulationFrog frog in frogs.Where(f => f.State is FrogState.Opening or FrogState.ChangeToActive))
                {
                    // Native checks the other body's first listed fixture, which is its newest.
                    if ((frog.TongueBodies.Contains(b) && !a.SensorFlags[^1] && !IsTongue(a)) || (frog.TongueBodies.Contains(a) && !b.SensorFlags[^1] && !IsTongue(b)))
                    {
                        frog.State = FrogState.Closing;
                    }
                }
            }
        }

        private bool IsTongue(PhysicsBody body)
        {
            return frogs.Any(f => f.TongueBodies.Contains(body));
        }

        /// <summary>Advances a tongue and rebuilds its bodies whenever the extension changes, as the native frog update does.</summary>
        /// <param name="frog">Frog to update.</param>
        /// <param name="delta">Elapsed seconds.</param>
        private void UpdateFrog(SimulationFrog frog, float delta)
        {
            if (frog.State == FrogState.ChangeToIdle)
            {
                frog.State = FrogState.Idle;
            }
            else if (frog.State == FrogState.ChangeToActive)
            {
                frog.State = FrogState.Active;
            }
            float open = frog.Open;
            if (frog.State == FrogState.Opening)
            {
                if (MoveToTarget(ref open, frog.Length, 3400, delta))
                {
                    frog.State = FrogState.ChangeToActive;
                }
            }
            else if (frog.State == FrogState.Closing && MoveToTarget(ref open, 0, 3000, delta))
            {
                frog.State = FrogState.ChangeToIdle;
            }
            frog.Open = open;
            if (frog.PreviousOpen == open)
            {
                return;
            }
            foreach (PhysicsBody body in frog.TongueBodies)
            {
                _ = World.RemoveBody(body);
                _ = tongueAnchors.Remove(body);
            }
            frog.TongueBodies.Clear();
            if (open > 0)
            {
                float halfWidth = (float)((float)(9 / 200.0) / 2.0);
                if ((float)((frog.Length - open) / 2.0) == 0)
                {
                    BuildBridge(frog, halfWidth);
                }
                else
                {
                    Vector2 middle = frog.TongueStart + (frog.Direction * open / 2);
                    PhysicsBody sensor = World.AddPolygon(ConvexPolygon.Box((float)((float)(open / 200.0) / 2.0), halfWidth, Vector2.Zero, frog.Angle),
                        Meters(middle), 1, 1, 0, false, false, PhysicsBodyType.Static);
                    sensor.IsSensor = true;
                    frog.TongueBodies.Add(sensor);
                }
            }
            frog.PreviousOpen = open;
        }

        /// <summary>Builds the extended tongue: static end anchors and a revolute chain of dynamic segments.</summary>
        /// <param name="frog">Fully extended frog.</param>
        /// <param name="halfWidth">Tongue half width in meters.</param>
        private void BuildBridge(SimulationFrog frog, float halfWidth)
        {
            PhysicsBody head = World.AddCircle(Meters(frog.TongueStart), halfWidth, 1, 1, 0, PhysicsBodyType.Static);
            PhysicsBody tail = World.AddCircle(Meters(frog.TongueFinish), halfWidth, 1, 1, 0, PhysicsBodyType.Static);
            frog.TongueBodies.Add(head);
            frog.TongueBodies.Add(tail);
            _ = tongueAnchors.Add(head);
            _ = tongueAnchors.Add(tail);
            int parts = (int)(float)(frog.Length / 80.0);
            if (parts <= 2)
            {
                parts = 3;
            }
            float size = frog.Length / parts;
            // Tongue segments use category 2 and ignore categories 2 and 14.
            PhysicsCollisionFilter filter = new(2, unchecked((ushort)-16387), 0);
            PhysicsBody? previous = null;
            for (int i = 0; i < parts; i++)
            {
                float offset = (float)(size / 2.0) + (size * i);
                PhysicsBody segment = World.AddPolygon(ConvexPolygon.Box((float)((float)(size / 200.0) / 2.0), halfWidth, Vector2.Zero, frog.Angle),
                    Meters(frog.TongueStart + (frog.Direction * offset)), 1, 1, 0, false, false, PhysicsBodyType.Dynamic);
                segment.LinearDamping = 2;
                segment.AngularDamping = 2;
                World.SetFixtureFilter(World.GetFixtureLabels(segment)[0], filter);
                frog.TongueBodies.Add(segment);
                if (i == 0)
                {
                    _ = World.AddRevolute(head, segment, head.Position);
                }
                if (i == parts - 1)
                {
                    _ = World.AddRevolute(tail, segment, tail.Position);
                }
                if (previous != null)
                {
                    _ = World.AddRevolute(previous, segment, Meters(frog.TongueStart + (frog.Direction * size * i)));
                }
                previous = segment;
            }
        }

        /// <summary>Native <c>moveVariableToTarget</c>: no move when already there; reports reaching the target.</summary>
        private static bool MoveToTarget(ref float variable, float target, float speed, float delta)
        {
            if (target == variable)
            {
                return false;
            }
            if (target <= variable)
            {
                variable -= speed * delta;
                if (variable < target)
                {
                    variable = target;
                }
            }
            else
            {
                variable += speed * delta;
                if (variable > target)
                {
                    variable = target;
                }
            }
            return target == variable;
        }

        private static float Number(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && MathF.Abs(value) <= 1000000
                ? value : throw new InvalidDataException("Invalid frog path number: " + text);
        }
    }
}
