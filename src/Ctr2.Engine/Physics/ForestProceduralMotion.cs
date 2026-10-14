using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    internal sealed record PolygonImpact(PhysicsBody Body, Vector2 Point, float FirstNormalImpulse);

    internal enum ForestProcedure
    {
        None, Bottom, Top, Left, Right
    }

    public sealed partial class PhysicsWorld
    {
        private readonly List<PolygonImpact> polygonImpacts = [];
        internal IReadOnlyList<PolygonImpact> PolygonImpacts => polygonImpacts;
    }

    public sealed partial class CandySimulation
    {
        internal IReadOnlyList<PolygonImpact> CharacterImpacts => world.PolygonImpacts;
    }

    public sealed partial class ForestGameplay
    {
        private ForestProcedure procedure;
        private int procedureStartedAt;
        private float procedureImpulse;

        /// <summary>Gets whether a procedural root timeline owns animation completion.</summary>
        public bool ProcedureActive => procedure != ForestProcedure.None;

        /// <summary>Gets the fixed-tick root scale independently of the authored child scene.</summary>
        public Vector2 CharacterScale
        {
            get
            {
                float squash = ProcedureWeight * procedureImpulse * .75f;
                return procedure is ForestProcedure.Top or ForestProcedure.Bottom ? new(1 + squash, 1 - squash) : new(1 - squash, 1 + squash);
            }
        }

        /// <summary>Gets the procedural root displacement in logical art units.</summary>
        public Vector2 CharacterOffset => ProcedureWeight * procedureImpulse * 60 * (procedure switch
        {
            ForestProcedure.Bottom => Vector2.UnitY,
            ForestProcedure.Top => -Vector2.UnitY,
            ForestProcedure.Left => -Vector2.UnitX,
            ForestProcedure.Right => Vector2.UnitX,
            ForestProcedure.None => Vector2.Zero,
            _ => Vector2.Zero
        });

        private float ProcedureWeight
        {
            get
            {
                if (!ProcedureActive)
                {
                    return 0;
                }

                float time = (simulation.Tick - procedureStartedAt) * PhysicsWorld.TimeStep;
                return Math.Clamp(time <= .03f ? time / .03f : 1 - ((time - .03f) / .06f), 0, 1);
            }
        }

        private static int BodyDegrees(PhysicsBody body)
        {
            int degrees = (int)(body.Angle * 180 / MathF.PI) % 360;
            return degrees > 180 ? degrees - 360 : degrees < -180 ? degrees + 360 : degrees;
        }

        private void ProcessImpacts()
        {
            PhysicsBody body = simulation.Characters[0];
            foreach (PolygonImpact impact in simulation.CharacterImpacts)
            {
                if (impact.Body != body)
                {
                    continue;
                }

                float impulse = MathF.Min(impact.FirstNormalImpulse, .5f);
                int angle = BodyDegrees(body);
                ForestProcedure direction;
                if (CharacterState == ForestCharacterState.Idle && Math.Abs(angle) < 20 && impact.FirstNormalImpulse > .1f)
                {
                    direction = ForestProcedure.Bottom;
                    impulse *= 1.4f;
                }
                else if ((CharacterState is ForestCharacterState.Falling or ForestCharacterState.Rolling or ForestCharacterState.FallingMouthClose) && impulse > .2f)
                {
                    Vector2 relative = impact.Point - body.Position;
                    float diff = (angle + 90 - (MathF.Atan2(relative.Y, relative.X) * 180 / MathF.PI)) % 360;
                    if (diff < 0)
                    {
                        diff += 360;
                    }

                    direction = diff is (>= 60 and <= 120) or (>= 240 and <= 300) ? diff < 180 ? ForestProcedure.Right : ForestProcedure.Left
                        : diff is < 60 or > 300 ? ForestProcedure.Bottom : ForestProcedure.Top;
                }
                else
                {
                    continue;
                }

                CharacterState = ForestCharacterState.Bumping;
                StartProcedure(direction, impulse);
            }
        }

        private void StartProcedure(ForestProcedure value, float impulse)
        {
            procedure = value;
            procedureImpulse = impulse;
            procedureStartedAt = simulation.Tick;
            // Original procedural timelines replace only the root; child clocks keep running.
            SceneRevision++;
        }

        private void AdvanceProcedure()
        {
            if (!ProcedureActive || (simulation.Tick - procedureStartedAt) * PhysicsWorld.TimeStep < .09f)
            {
                return;
            }

            bool rolling = MathF.Abs(motion.ShortAngular) > 1 && motion.Short.Length() > .5f;
            CharacterState = rolling ? ForestCharacterState.Rolling : ForestCharacterState.Idle;
            SetScene(rolling ? "ROLLING" : "IDLE");
        }
    }
}
