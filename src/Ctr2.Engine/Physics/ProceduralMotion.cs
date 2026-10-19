using System;
using System.Collections.Generic;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    internal sealed record PolygonImpact(PhysicsBody Body, Vector2 Point, float FirstNormalImpulse);

    internal enum CharacterProcedure
    {
        None, Bottom, Top, Left, Right, StandLeft, StandRight
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

    public sealed partial class LevelGameplay
    {
        private CharacterProcedure procedure;
        private int procedureStartedAt;
        private float procedureImpulse;

        /// <summary>Gets whether a procedural root timeline owns animation completion.</summary>
        public bool ProcedureActive => procedure != CharacterProcedure.None;

        /// <summary>Gets the fixed-tick root scale independently of the authored child scene.</summary>
        public Vector2 CharacterScale
        {
            get
            {
                if (procedure is CharacterProcedure.StandLeft or CharacterProcedure.StandRight)
                {
                    return Vector2.Lerp(Vector2.One, new(.85f, 1.3f), ProcedureWeight);
                }
                float squash = ProcedureWeight * procedureImpulse * .75f;
                return procedure is CharacterProcedure.Top or CharacterProcedure.Bottom ? new(1 + squash, 1 - squash) : new(1 - squash, 1 + squash);
            }
        }

        /// <summary>Gets the procedural root displacement in logical art units.</summary>
        public Vector2 CharacterOffset => ProcedureWeight * (procedure switch
        {
            CharacterProcedure.Bottom => procedureImpulse * 60 * Vector2.UnitY,
            CharacterProcedure.Top => -procedureImpulse * 60 * Vector2.UnitY,
            CharacterProcedure.Left => -procedureImpulse * 60 * Vector2.UnitX,
            CharacterProcedure.Right => procedureImpulse * 60 * Vector2.UnitX,
            CharacterProcedure.StandLeft => new Vector2(-10, 0),
            CharacterProcedure.StandRight => new Vector2(10, 0),
            CharacterProcedure.None => Vector2.Zero,
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
                bool standing = procedure is CharacterProcedure.StandLeft or CharacterProcedure.StandRight;
                float peak = standing ? .1f : .03f;
                float recovery = standing ? .05f : .06f;
                return Math.Clamp(time <= peak ? time / peak : 1 - ((time - peak) / recovery), 0, 1);
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
                CharacterProcedure direction;
                if (CharacterState == CharacterState.Idle && Math.Abs(angle) < 20 && impact.FirstNormalImpulse > .1f)
                {
                    direction = CharacterProcedure.Bottom;
                    impulse *= 1.4f;
                }
                else if ((CharacterState is CharacterState.Falling or CharacterState.Rolling or CharacterState.FallingMouthClose) && impulse > .2f)
                {
                    Vector2 relative = impact.Point - body.Position;
                    float diff = (angle + 90 - (MathF.Atan2(relative.Y, relative.X) * 180 / MathF.PI)) % 360;
                    if (diff < 0)
                    {
                        diff += 360;
                    }

                    direction = diff is (>= 60 and <= 120) or (>= 240 and <= 300) ? diff < 180 ? CharacterProcedure.Right : CharacterProcedure.Left
                        : diff is < 60 or > 300 ? CharacterProcedure.Bottom : CharacterProcedure.Top;
                }
                else
                {
                    continue;
                }

                CharacterState = CharacterState.Bumping;
                StartProcedure(direction, impulse);
            }
        }

        private void StartProcedure(CharacterProcedure value, float impulse)
        {
            procedure = value;
            procedureImpulse = impulse;
            procedureStartedAt = simulation.Tick;
            // Original procedural timelines replace only the root; child clocks keep running.
            SceneRevision++;
        }

        private void UpdateStandingState()
        {
            if (Result != LevelResult.Running || CharacterState is not (CharacterState.Idle or CharacterState.Chewing))
            {
                return;
            }

            PhysicsBody body = simulation.Characters[0];
            int angle = BodyDegrees(body);
            if (body.Velocity.Length() >= .01f || (angle >= -60 && angle <= 60))
            {
                return;
            }

            CharacterState = CharacterState == CharacterState.Chewing ? CharacterState.StandUpChewing : CharacterState.StandUp;
            StartProcedure(angle < -60 ? CharacterProcedure.StandLeft : CharacterProcedure.StandRight, 0);
        }

        private void AdvanceProcedure()
        {
            bool standing = procedure is CharacterProcedure.StandLeft or CharacterProcedure.StandRight;
            if (!ProcedureActive || (simulation.Tick - procedureStartedAt) * PhysicsWorld.TimeStep < (standing ? .15f : .09f))
            {
                return;
            }

            if (standing)
            {
                PhysicsBody body = simulation.Characters[0];
                int angle = BodyDegrees(body);
                float angularImpulse = procedure == CharacterProcedure.StandLeft ? angle >= -110 ? .008f : .014f : angle <= 110 ? -.008f : -.014f;
                body.Velocity += new Vector2(0, -.19f * body.InverseMass);
                body.AngularVelocity += angularImpulse * body.InverseInertia;
                procedure = CharacterProcedure.None;
                if (CharacterState == CharacterState.StandUp)
                {
                    SetScene("STAND_UP");
                }

                return;
            }
            bool rolling = MathF.Abs(motion.ShortAngular) > 1 && motion.Short.Length() > .5f;
            CharacterState = rolling ? CharacterState.Rolling : CharacterState.Idle;
            SetScene(rolling ? "ROLLING" : "IDLE");
        }
    }
}
