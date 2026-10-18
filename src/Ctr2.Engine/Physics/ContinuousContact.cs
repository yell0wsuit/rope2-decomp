// Altered C# continuous collision subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    public sealed partial class PhysicsBody
    {
        /// <summary>Native sweep start center, captured when the step's island solve begins.</summary>
        internal Vector2 Center0;
        /// <summary>Native sweep start angle.</summary>
        internal float Angle0;
        /// <summary>Native sweep start fraction within the step.</summary>
        internal float Alpha0;

        /// <summary>Gets the native sweep from the start state to the current center and angle.</summary>
        internal PhysicsSweep Sweep => new()
        {
            LocalCenter = LocalCenter,
            Center0 = Center0,
            Center = Position,
            Angle0 = Angle0,
            Angle = Angle,
            Alpha0 = Alpha0
        };

        /// <summary>Native <c>b2Sweep::Advance</c> on this body's sweep start; the current state is unchanged.</summary>
        internal void AdvanceSweep(float alpha)
        {
            PhysicsSweep sweep = Sweep;
            sweep.Advance(alpha);
            (Center0, Angle0, Alpha0) = (sweep.Center0, sweep.Angle0, sweep.Alpha0);
        }

        /// <summary>Native <c>b2Body::Advance</c>: advances the sweep start and moves the body to it.</summary>
        internal void Advance(float alpha)
        {
            AdvanceSweep(alpha);
            Position = Center0;
            Angle = Angle0;
        }

        /// <summary>Captures the native sweep for a later restore.</summary>
        internal (Vector2 Center0, float Angle0, float Alpha0, Vector2 Center, float Angle) SaveSweep()
        {
            return (Center0, Angle0, Alpha0, Position, Angle);
        }

        /// <summary>Restores a saved sweep and resynchronizes the transform.</summary>
        internal void RestoreSweep((Vector2 Center0, float Angle0, float Alpha0, Vector2 Center, float Angle) saved)
        {
            (Center0, Angle0, Alpha0, Position, Angle) = saved;
        }
    }

    public sealed partial class PhysicsWorld
    {
        private const int MaxSubSteps = 8;
        private const int MaxToiContacts = 32;

        /// <summary>Gets or sets whether native time-of-impact solving follows each discrete step.</summary>
        /// <value>True by default, as in the game; false isolates the discrete solver for reference tests.</value>
        public bool ContinuousPhysics
        {
            get; set;
        } = true;

        /// <summary>All candidates in native world contact-list order, newest first.</summary>
        private List<ContactCandidate> CandidatesNewestFirst()
        {
            return [.. bodyContacts.Values.Concat<ContactCandidate>(contacts.Values).OrderByDescending(c => c.CreationOrder)];
        }

        /// <summary>Native <c>b2World::SolveTOI</c> without bullets or sub-stepping, after the discrete step's proxy synchronization.</summary>
        private void SolveToi()
        {
            foreach (PhysicsBody body in bodies)
            {
                body.Alpha0 = 0;
            }
            foreach (TerrainFixture fixture in terrain)
            {
                fixture.Frame.Alpha0 = 0;
            }
            foreach (ContactCandidate candidate in CandidatesNewestFirst())
            {
                candidate.ToiFlag = false;
                candidate.ToiCount = 0;
                candidate.Toi = 1;
            }
            while (true)
            {
                ContactCandidate? minContact = null;
                float minAlpha = 1;
                foreach (ContactCandidate candidate in CandidatesNewestFirst())
                {
                    if (!candidate.Enabled || candidate.ToiCount > MaxSubSteps)
                    {
                        continue;
                    }
                    float alpha = 1;
                    if (candidate.ToiFlag)
                    {
                        alpha = candidate.Toi;
                    }
                    else
                    {
                        if (IsSensor(candidate.A, candidate.B))
                        {
                            continue;
                        }
                        PhysicsBody a = candidate.BodyA, b = candidate.BodyB;
                        // At least one body must move, and only pairs involving a non-dynamic body are swept.
                        if ((a.Type == PhysicsBodyType.Static && b.Type == PhysicsBodyType.Static) ||
                            (a.Type == PhysicsBodyType.Dynamic && b.Type == PhysicsBodyType.Dynamic))
                        {
                            continue;
                        }
                        // Put the sweeps onto the same time interval.
                        float alpha0 = a.Alpha0;
                        if (a.Alpha0 < b.Alpha0)
                        {
                            alpha0 = b.Alpha0;
                            a.AdvanceSweep(alpha0);
                        }
                        else if (b.Alpha0 < a.Alpha0)
                        {
                            alpha0 = a.Alpha0;
                            b.AdvanceSweep(alpha0);
                        }
                        (PhysicsToiState state, float beta) = PhysicsTimeOfImpact.Compute(DistanceProxy(candidate.A), a.Sweep, DistanceProxy(candidate.B), b.Sweep, 1);
                        alpha = state == PhysicsToiState.Touching ? MathF.Min(alpha0 + ((1 - alpha0) * beta), 1) : 1;
                        candidate.Toi = alpha;
                        candidate.ToiFlag = true;
                    }
                    if (alpha < minAlpha)
                    {
                        minContact = candidate;
                        minAlpha = alpha;
                    }
                }
                if (minContact == null || 1 - (10 * 1.1920929e-7f) < minAlpha)
                {
                    return;
                }
                PhysicsBody bodyA = minContact.BodyA, bodyB = minContact.BodyB;
                (Vector2 Center0, float Angle0, float Alpha0, Vector2 Center, float Angle) backupA = bodyA.SaveSweep();
                (Vector2 Center0, float Angle0, float Alpha0, Vector2 Center, float Angle) backupB = bodyB.SaveSweep();
                bodyA.Advance(minAlpha);
                bodyB.Advance(minAlpha);
                // The TOI contact likely has some new contact points.
                _ = UpdateCandidate(minContact);
                minContact.ToiFlag = false;
                ++minContact.ToiCount;
                if (!minContact.Enabled || !minContact.Touching)
                {
                    minContact.Enabled = false;
                    bodyA.RestoreSweep(backupA);
                    bodyB.RestoreSweep(backupB);
                    continue;
                }
                List<PhysicsBody> islandBodies = [bodyA, bodyB];
                List<ContactCandidate> islandContacts = [minContact];
                foreach (PhysicsBody body in new[] { bodyA, bodyB })
                {
                    if (body.Type != PhysicsBodyType.Dynamic)
                    {
                        continue;
                    }
                    foreach (ContactCandidate candidate in CandidatesNewestFirst())
                    {
                        if (candidate.BodyA != body && candidate.BodyB != body)
                        {
                            continue;
                        }
                        if (islandBodies.Count == 2 * MaxToiContacts || islandContacts.Count == MaxToiContacts)
                        {
                            break;
                        }
                        // Only static and kinematic neighbours join without bullets.
                        PhysicsBody other = candidate.BodyA == body ? candidate.BodyB : candidate.BodyA;
                        if (islandContacts.Contains(candidate) || other.Type == PhysicsBodyType.Dynamic || IsSensor(candidate.A, candidate.B))
                        {
                            continue;
                        }
                        // Tentatively advance the other body to the TOI.
                        (Vector2 Center0, float Angle0, float Alpha0, Vector2 Center, float Angle) backup = other.SaveSweep();
                        if (!islandBodies.Contains(other))
                        {
                            other.Advance(minAlpha);
                        }
                        _ = UpdateCandidate(candidate);
                        if (!candidate.Enabled || !candidate.Touching)
                        {
                            other.RestoreSweep(backup);
                            continue;
                        }
                        islandContacts.Add(candidate);
                        if (!islandBodies.Contains(other))
                        {
                            islandBodies.Add(other);
                        }
                    }
                }
                SolveToiIsland(islandBodies, islandContacts, minAlpha);
                foreach (PhysicsBody body in islandBodies)
                {
                    if (body.Type != PhysicsBodyType.Dynamic)
                    {
                        continue;
                    }
                    SynchronizeFixtures(body);
                    // Invalidate all contact TOIs on this displaced body.
                    foreach (ContactCandidate candidate in CandidatesNewestFirst())
                    {
                        if (candidate.BodyA == body || candidate.BodyB == body)
                        {
                            candidate.ToiFlag = false;
                        }
                    }
                }
                // Commit proxy movements so that new contacts are created.
                FindNewContacts();
            }
        }

        /// <summary>Native <c>b2Island::SolveTOI</c>: TOI position correction, leap of faith, unwarmed velocity solve and integration.</summary>
        private void SolveToiIsland(List<PhysicsBody> islandBodies, List<ContactCandidate> islandContacts, float minAlpha)
        {
            // Solve fresh constraints so the stored warm-start impulses stay as the discrete step left them.
            List<PhysicsContact> solvers = [.. islandContacts.Select(c => Narrowphase(c)!)];
            for (int i = 0; i < 20; i++)
            {
                bool solved = true;
                foreach (PhysicsContact contact in solvers)
                {
                    solved = contact.SolvePosition(true) && solved;
                }
                if (solved)
                {
                    break;
                }
            }
            // Leap of faith to the new safe state.
            for (int i = 0; i < 2; i++)
            {
                islandBodies[i].Center0 = islandBodies[i].Position;
                islandBodies[i].Angle0 = islandBodies[i].Angle;
            }
            foreach (PhysicsContact contact in solvers)
            {
                contact.Initialize();
            }
            for (int i = 0; i < 15; i++)
            {
                foreach (PhysicsContact contact in solvers)
                {
                    contact.SolveVelocity();
                }
            }
            float h = (1 - minAlpha) * TimeStep;
            foreach (PhysicsBody body in islandBodies)
            {
                Integrate(body, h);
            }
            ReportPostSolve(solvers);
        }

        /// <summary>Native position integration with translation and rotation clamps.</summary>
        private static void Integrate(PhysicsBody body, float h)
        {
            Vector2 translation = h * body.Velocity;
            if (Vector2.Dot(translation, translation) > 4)
            {
                body.Velocity *= 2 / translation.Length();
            }
            float rotation = h * body.AngularVelocity;
            if (rotation * rotation > MathF.PI * MathF.PI / 4)
            {
                body.AngularVelocity *= MathF.PI / 2 / MathF.Abs(rotation);
            }
            body.Position += h * body.Velocity;
            body.Angle += h * body.AngularVelocity;
        }

        /// <summary>Native <c>b2Body::SynchronizeFixtures</c> from the sweep start to the current transform.</summary>
        private void SynchronizeFixtures(PhysicsBody body)
        {
            Vector2 start = body.Center0 - PhysicsMath.Rotate(body.LocalCenter, body.Angle0), origin = body.Origin;
            List<FixtureProxy> fixtures = bodyProxies[body];
            for (int index = fixtures.Count - 1; index >= 0; index--)
            {
                FixtureProxy proxy = fixtures[index];
                proxy.Bounds = PhysicsAabb.Combine(BodyBounds(body, index, start, body.Angle0), BodyBounds(body, index, origin, body.Angle));
                _ = broadPhase.MoveProxy(proxy.Id, proxy.Bounds, origin - start);
            }
        }
    }
}
