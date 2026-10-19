// Altered C# subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>An immutable diagnostic for a supported terrain contact, in reverse creation order.</summary>
    /// <param name="BodyFixture">Stable world-local dynamic fixture label, starting at one.</param>
    /// <param name="TerrainFixture">Stable world-local terrain fixture label.</param>
    /// <param name="CreationOrder">Monotonic candidate creation sequence, retained while proxy bounds overlap.</param>
    /// <param name="Touching">Whether the latest discrete narrowphase update produced a solid manifold.</param>
    public readonly record struct PhysicsContactState(int BodyFixture, int TerrainFixture, long CreationOrder, bool Touching);

    /// <summary>An immutable fixture-proxy diagnostic, including static and sensor fixtures.</summary>
    /// <param name="Fixture">Stable world-local fixture label, starting at one.</param>
    /// <param name="ProxyId">Reusable native-style tree node ID.</param>
    /// <param name="Bounds">Stored predicted bounds in meters.</param>
    public readonly record struct PhysicsProxyState(int Fixture, int ProxyId, PhysicsAabb Bounds);

    /// <summary>An immutable supported body-pair diagnostic, ordered by native fixture orientation.</summary>
    /// <param name="FirstFixture">Stable label of fixture A.</param>
    /// <param name="SecondFixture">Stable label of fixture B.</param>
    /// <param name="CreationOrder">Persistent candidate creation sequence.</param>
    /// <param name="Touching">Whether the latest update produced a solid manifold.</param>
    public readonly record struct PhysicsBodyContactState(int FirstFixture, int SecondFixture, long CreationOrder, bool Touching);

    /// <summary>An immutable feature ID for one point of a touching body-pair manifold.</summary>
    /// <param name="FirstFixture">Native fixture A's stable label.</param>
    /// <param name="SecondFixture">Native fixture B's stable label.</param>
    /// <param name="Point">Native manifold point index.</param>
    /// <param name="Feature">Packed native contact feature ID.</param>
    public readonly record struct PhysicsContactFeatureState(int FirstFixture, int SecondFixture, int Point, int Feature);

    public sealed partial class PhysicsWorld
    {
        /// <summary>Captures native feature IDs in reverse candidate creation order, then manifold point order.</summary>
        public IReadOnlyList<PhysicsContactFeatureState> CaptureBodyContactFeatures()
        {
            return Array.AsReadOnly(bodyContacts.Values.Where(c => c.Manifold != null && !IsSensor(c.A, c.B))
                .OrderByDescending(c => c.CreationOrder)
                .SelectMany(c => c.Manifold!.CaptureFeatures().Select((feature, index) =>
                    new PhysicsContactFeatureState(c.A.Label, c.B.Label, index, feature))).ToArray());
        }

        /// <summary>Captures supported solid body candidates in reverse creation order.</summary>
        public IReadOnlyList<PhysicsBodyContactState> CaptureBodyContacts()
        {
            return Array.AsReadOnly(bodyContacts.Values.Where(c => !IsSensor(c.A, c.B))
                .OrderByDescending(c => c.CreationOrder)
                .Select(c => new PhysicsBodyContactState(c.A.Label, c.B.Label, c.CreationOrder, c.Manifold != null)).ToArray());
        }

        /// <summary>Captures current proxy IDs and predicted bounds ordered by stable fixture label.</summary>
        public IReadOnlyList<PhysicsProxyState> CaptureProxies()
        {
            return Array.AsReadOnly(proxies.Values.OrderBy(p => p.Label)
                .Select(p => new PhysicsProxyState(p.Label, p.Id, broadPhase.GetBounds(p.Id))).ToArray());
        }

        /// <summary>Captures supported solid terrain candidates; labels are never reused within a world.</summary>
        /// <remarks>Newly discovered candidates have no touching manifold until the next discrete step.</remarks>
        public IReadOnlyList<PhysicsContactState> CaptureTerrainContacts()
        {
            return Array.AsReadOnly(contacts.Values.Where(c => !IsSensor(c.A, c.B)).OrderByDescending(c => c.CreationOrder)
                .Select(c => new PhysicsContactState(c.BodyProxy.Label, c.TerrainProxy.Label, c.CreationOrder, c.Manifold != null)).ToArray());
        }

        private readonly PhysicsBroadPhase broadPhase = new();
        private readonly Dictionary<int, FixtureProxy> proxies = [];
        private readonly Dictionary<PhysicsBody, List<FixtureProxy>> bodyProxies = [];
        private readonly Dictionary<TerrainFixture, FixtureProxy> terrainProxies = [];
        private readonly Dictionary<(int, int), BodyCandidate> bodyContacts = [];
        private readonly Dictionary<(PhysicsBody, PhysicsBody), int> collisionBlocks = [];
        private int fixtureLabel;
        private bool newFixtures;

        private sealed class FixtureProxy(int label, PhysicsBody? body, TerrainFixture? terrain, int localIndex)
        {
            internal readonly int Label = label;
            internal readonly PhysicsBody? Body = body;
            internal readonly TerrainFixture? Terrain = terrain;
            internal readonly int LocalIndex = localIndex;
            internal int Id;
            /// <summary>Native <c>b2FixtureProxy::aabb</c>: the swept tight box from the latest synchronization.</summary>
            internal PhysicsAabb Bounds;
            internal PhysicsDistanceProxy? Distance;
            internal PhysicsCollisionFilter Filter = PhysicsCollisionFilter.Default;

            internal bool IsSensor
            {
                get => Body != null ? Body.SensorFlags[LocalIndex] : field;
                set
                {
                    if (Body != null)
                    {
                        Body.SensorFlags[LocalIndex] = value;
                    }
                    else
                    {
                        field = value;
                    }
                }
            }
        }

        /// <summary>A native contact: fixtures in native orientation, mixed material, manifold and TOI state.</summary>
        private abstract class ContactCandidate(FixtureProxy a, FixtureProxy b, long order, float friction, float restitution)
        {
            internal readonly FixtureProxy A = a;
            internal readonly FixtureProxy B = b;
            internal readonly long CreationOrder = order;
            // Native contacts mix material once at construction, including before first touch.
            internal readonly float Friction = friction;
            internal readonly float Restitution = restitution;
            internal PhysicsContact? Manifold;
            internal bool Touching;
            /// <summary>Native enabled flag; cleared when a TOI update finds no touch, restored by the next update.</summary>
            internal bool Enabled = true;
            /// <summary>Whether <see cref="Toi"/> holds a valid cached time of impact.</summary>
            internal bool ToiFlag;
            internal float Toi = 1;
            internal int ToiCount;
            /// <summary>Gets native fixture A's body, or a terrain fixture's static frame.</summary>
            internal PhysicsBody BodyA => A.Body ?? A.Terrain!.Frame;
            /// <summary>Gets native fixture B's body, or a terrain fixture's static frame.</summary>
            internal PhysicsBody BodyB => B.Body ?? B.Terrain!.Frame;
        }

        private sealed class TerrainCandidate(FixtureProxy body, FixtureProxy terrain, long creationOrder, bool terrainFirst)
            : ContactCandidate(terrainFirst ? terrain : body, terrainFirst ? body : terrain, creationOrder,
                MathF.Sqrt(body.Body!.Friction * terrain.Terrain!.Friction), MathF.Max(body.Body.Restitution, terrain.Terrain.Restitution))
        {
            internal readonly FixtureProxy BodyProxy = body;
            internal readonly FixtureProxy TerrainProxy = terrain;
            internal PhysicsBody Body => BodyProxy.Body!;
        }

        private sealed class BodyCandidate(FixtureProxy a, FixtureProxy b, long order)
            : ContactCandidate(a, b, order, MathF.Sqrt(a.Body!.Friction * b.Body!.Friction), MathF.Max(a.Body.Restitution, b.Body.Restitution));

        private bool JointBlocksCollision(PhysicsBody a, PhysicsBody b)
        {
            return collisionBlocks.ContainsKey((a, b));
        }

        private void ChangeCollisionBlock(PhysicsBody a, PhysicsBody b, int change)
        {
            _ = collisionBlocks.TryGetValue((a, b), out int count);
            count += change;
            if (count == 0)
            {
                _ = collisionBlocks.Remove((a, b));
                _ = collisionBlocks.Remove((b, a));
            }
            else
            {
                collisionBlocks[(a, b)] = count;
                collisionBlocks[(b, a)] = count;
            }
        }

        private List<PhysicsContact> UpdateBodyContacts()
        {
            List<PhysicsContact> active = [];
            foreach (KeyValuePair<(int, int), BodyCandidate> entry in bodyContacts.ToArray())
            {
                BodyCandidate pair = entry.Value;
                PhysicsBody a = pair.A.Body!, b = pair.B.Body!;
                // SetBodyType refilters every contact of the changed body, so this matches native flagged filtering.
                bool filtered = (a.Mass == 0 && b.Mass == 0) || JointBlocksCollision(a, b) || !ShouldCollide(pair.A.Filter, pair.B.Filter);
                if (!filtered && !a.IsActiveMover && !b.IsActiveMover)
                {
                    // Sleeping pairs keep their manifold without updating.
                    if (pair.Manifold != null && pair.Enabled)
                    {
                        active.Add(pair.Manifold);
                    }
                    continue;
                }
                if (filtered || !broadPhase.GetBounds(pair.A.Id).Overlaps(broadPhase.GetBounds(pair.B.Id)))
                {
                    DestroyCandidate(pair);
                    _ = bodyContacts.Remove(entry.Key);
                    continue;
                }
                if (UpdateCandidate(pair) is PhysicsContact next)
                {
                    active.Add(next);
                }
            }
            return active;
        }

        /// <summary>Native contact destruction: emits a touching End and wakes both bodies when points existed.</summary>
        private void DestroyCandidate(ContactCandidate candidate)
        {
            RecordContactChange(candidate.A, candidate.B, candidate.CreationOrder, candidate.Touching, false, true);
            if (candidate.Manifold != null)
            {
                candidate.BodyA.SetAwake(true);
                candidate.BodyB.SetAwake(true);
            }
        }

        /// <summary>Computes a fresh solid manifold with zero impulses at the bodies' current transforms.</summary>
        private PhysicsContact? Narrowphase(ContactCandidate candidate)
        {
            if (IsSensor(candidate.A, candidate.B))
            {
                return null;
            }
            PhysicsContact? next;
            if (candidate is TerrainCandidate terrain)
            {
                PhysicsBody body = terrain.Body;
                TerrainFixture fixture = terrain.TerrainProxy.Terrain!;
                int localIndex = terrain.BodyProxy.LocalIndex;
                // Polygon bodies collide with terrain through the native polygon pair path, using
                // the terrain's static identity frame in native fixture orientation.
                next = body.PolygonFixtures.Count == 0 ?
                    TerrainContact.Create(body, fixture, candidate.Friction, candidate.Restitution) :
                    candidate.A.Terrain != null ?
                    PolygonBodyContact.Create(fixture.Frame, fixture.Shape, body, body.PolygonFixtures[localIndex], candidate.Friction, candidate.Restitution, true) :
                    PolygonBodyContact.Create(body, body.PolygonFixtures[localIndex], fixture.Frame, fixture.Shape, candidate.Friction, candidate.Restitution, true);
            }
            else
            {
                PhysicsBody a = candidate.A.Body!, b = candidate.B.Body!;
                next = a.PolygonFixtures.Count == 0 ?
                    CircleContact.Create(a, b, candidate.Friction, candidate.Restitution) :
                    b.PolygonFixtures.Count == 0 ?
                    PolygonCircleContact.Create(a, a.PolygonFixtures[candidate.A.LocalIndex], b, candidate.Friction, candidate.Restitution) :
                    PolygonBodyContact.Create(a, a.PolygonFixtures[candidate.A.LocalIndex], b, b.PolygonFixtures[candidate.B.LocalIndex], candidate.Friction, candidate.Restitution);
            }
            if (next != null)
            {
                next.CreationOrder = candidate.CreationOrder;
                next.FirstFixture = candidate.A.Label;
                next.SecondFixture = candidate.B.Label;
            }
            return next;
        }

        /// <summary>Native <c>b2Contact::Update</c>: re-enables, recomputes the manifold, carries matching impulses and emits transitions.</summary>
        /// <returns>The new solid manifold, or null when separated or a sensor.</returns>
        private PhysicsContact? UpdateCandidate(ContactCandidate candidate)
        {
            candidate.Enabled = true;
            bool sensor = IsSensor(candidate.A, candidate.B);
            PhysicsContact? next = Narrowphase(candidate);
            if (next != null && candidate.Manifold != null)
            {
                next.Restore(candidate.Manifold);
            }
            // Losing the manifold keeps candidate order, but clears warm-start impulses.
            bool touching = sensor ? SensorOverlap(candidate.A, candidate.B) : next != null;
            if (!sensor && touching != candidate.Touching)
            {
                candidate.BodyA.SetAwake(true);
                candidate.BodyB.SetAwake(true);
            }
            RecordContactChange(candidate.A, candidate.B, candidate.CreationOrder, candidate.Touching, touching, false, next);
            candidate.Touching = touching;
            candidate.Manifold = next;
            return next;
        }

        private void RegisterBodyProxies(PhysicsBody body)
        {
            int count = Math.Max(1, body.PolygonFixtures.Count);
            List<PhysicsAabb> bounds = [];
            for (int i = 0; i < count; i++)
            {
                PhysicsAabb box = BodyBounds(body, i, body.Origin, body.Angle);
                box.Validate();
                bounds.Add(box);
            }
            List<FixtureProxy> fixtures = [];
            for (int i = 0; i < count; i++)
            {
                FixtureProxy proxy = new(++fixtureLabel, body, null, i)
                {
                    Bounds = bounds[i]
                };
                proxy.Id = broadPhase.CreateProxy(bounds[i], proxy.Label);
                proxies.Add(proxy.Label, proxy);
                fixtures.Add(proxy);
            }
            bodyProxies.Add(body, fixtures);
            newFixtures = true;
        }

        private void RegisterTerrainProxy(TerrainFixture fixture, bool isSensor, PhysicsCollisionFilter filter)
        {
            PhysicsAabb bounds = PolygonBounds(fixture.Points, Vector2.Zero, 0);
            bounds.Validate();
            FixtureProxy proxy = new(++fixtureLabel, null, fixture, 0)
            {
                IsSensor = isSensor,
                Bounds = bounds,
                Filter = filter
            };
            proxy.Id = broadPhase.CreateProxy(bounds, proxy.Label);
            proxies.Add(proxy.Label, proxy);
            terrainProxies.Add(fixture, proxy);
            newFixtures = true;
        }

        private void RemoveBodyProxies(PhysicsBody body)
        {
            List<FixtureProxy> fixtures = bodyProxies[body];
            // Native body fixture lists prepend creation; destruction traverses that list.
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                broadPhase.DestroyProxy(fixtures[i].Id);
                _ = proxies.Remove(fixtures[i].Label);
            }
            _ = bodyProxies.Remove(body);
        }

        private void FindNewContacts()
        {
            foreach (BroadPhasePair pair in broadPhase.UpdatePairs())
            {
                FixtureProxy a = proxies[pair.First], b = proxies[pair.Second];
                FixtureProxy nativeA = a;
                if (!ShouldCollide(a.Filter, b.Filter))
                {
                    continue;
                }
                if (a.Body != null && b.Body != null && a.Body != b.Body &&
                    (a.Body.Mass > 0 || b.Body.Mass > 0) && !JointBlocksCollision(a.Body, b.Body))
                {
                    // The native factory swaps mixed pairs so the polygon is always fixture A.
                    if (a.Body.PolygonFixtures.Count == 0 && b.Body.PolygonFixtures.Count > 0)
                    {
                        (a, b) = (b, a);
                    }

                    (int, int) bodyKey = (a.Label, b.Label);
                    if (!bodyContacts.ContainsKey(bodyKey))
                    {
                        bodyContacts.Add(bodyKey, new(a, b, ++contactSequence));
                        a.Body.SetAwake(true);
                        b.Body.SetAwake(true);
                    }

                    continue;
                }
                if (a.Terrain == null && b.Terrain != null)
                {
                    (a, b) = (b, a);
                }
                if (a.Terrain == null || b.Body == null || b.Body.Mass == 0)
                {
                    continue;
                }
                (PhysicsBody Body, TerrainFixture Terrain, int LocalIndex) key = (b.Body, a.Terrain, b.LocalIndex);
                if (!contacts.ContainsKey(key))
                {
                    bool terrainFirst = b.Body.PolygonFixtures.Count == 0 || nativeA.Terrain != null;
                    contacts.Add(key, new(b, a, ++contactSequence, terrainFirst));
                    b.Body.SetAwake(true);
                }
            }
        }

        private Dictionary<PhysicsBody, (Vector2 Origin, float Angle)> BeginBroadPhaseStep()
        {
            if (newFixtures)
            {
                FindNewContacts();
                newFixtures = false;
            }
            Dictionary<PhysicsBody, (Vector2, float)> starts = [];
            foreach (PhysicsBody body in bodies)
            {
                if (body.Type != PhysicsBodyType.Static)
                {
                    // Native rebuilds the start transform from the sweep, not the cached teleport transform.
                    starts.Add(body, (body.ComputedOrigin, body.Angle));
                }
            }
            return starts;
        }

        private void SynchronizeBodyProxies(Dictionary<PhysicsBody, (Vector2 Origin, float Angle)> starts, HashSet<PhysicsBody> islandBodies)
        {
            for (int bodyIndex = bodies.Count - 1; bodyIndex >= 0; bodyIndex--)
            {
                PhysicsBody body = bodies[bodyIndex];
                // Bodies that sat out every island did not move.
                if (!islandBodies.Contains(body) || !starts.TryGetValue(body, out (Vector2 Origin, float Angle) start))
                {
                    continue;
                }
                Vector2 origin = body.Origin;
                List<FixtureProxy> fixtures = bodyProxies[body];
                for (int index = fixtures.Count - 1; index >= 0; index--)
                {
                    FixtureProxy proxy = fixtures[index];
                    PhysicsAabb previous = BodyBounds(body, index, start.Origin, start.Angle);
                    PhysicsAabb current = BodyBounds(body, index, origin, body.Angle);
                    proxy.Bounds = PhysicsAabb.Combine(previous, current);
                    _ = broadPhase.MoveProxy(proxy.Id, proxy.Bounds, origin - start.Origin);
                }
            }
            FindNewContacts();
        }

        private static PhysicsAabb BodyBounds(PhysicsBody body, int index, Vector2 origin, float angle)
        {
            return body.PolygonFixtures.Count == 0 ? new(origin - new Vector2(body.Radius), origin + new Vector2(body.Radius)) :
            PolygonBounds(body.PolygonFixtures[index].Vertices, origin, angle);
        }

        private static PhysicsAabb PolygonBounds(IReadOnlyList<Vector2> points, Vector2 origin, float angle)
        {
            float cosine = MathF.Cos(angle), sine = MathF.Sin(angle);
            Vector2 lower = new(float.PositiveInfinity), upper = new(float.NegativeInfinity);
            foreach (Vector2 p in points)
            {
                Vector2 world = origin + new Vector2((cosine * p.X) - (sine * p.Y), (sine * p.X) + (cosine * p.Y));
                lower = Vector2.Min(lower, world);
                upper = Vector2.Max(upper, world);
            }
            Vector2 skin = new(2 * LinearSlop);
            return new(lower - skin, upper + skin);
        }
    }
}
