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

    public sealed partial class PhysicsWorld
    {
        /// <summary>Captures current proxy IDs and predicted bounds ordered by stable fixture label.</summary>
        public IReadOnlyList<PhysicsProxyState> CaptureProxies() =>
            Array.AsReadOnly(proxies.Values.OrderBy(p => p.Label)
                .Select(p => new PhysicsProxyState(p.Label, p.Id, broadPhase.GetBounds(p.Id))).ToArray());

        /// <summary>Captures supported solid terrain candidates; labels are never reused within a world.</summary>
        /// <remarks>Newly discovered candidates have no touching manifold until the next discrete step.</remarks>
        public IReadOnlyList<PhysicsContactState> CaptureTerrainContacts() =>
            Array.AsReadOnly(contacts.Values.Where(c => !c.Body.IsSensor).OrderByDescending(c => c.CreationOrder)
                .Select(c => new PhysicsContactState(c.BodyProxy.Label, c.TerrainProxy.Label, c.CreationOrder, c.Manifold != null)).ToArray());

        private readonly PhysicsBroadPhase broadPhase = new();
        private readonly Dictionary<int, FixtureProxy> proxies = [];
        private readonly Dictionary<PhysicsBody, List<FixtureProxy>> bodyProxies = [];
        private int fixtureLabel;
        private bool newFixtures;

        private sealed class FixtureProxy(int label, PhysicsBody? body, TerrainFixture? terrain, int localIndex)
        {
            internal readonly int Label = label;
            internal readonly PhysicsBody? Body = body;
            internal readonly TerrainFixture? Terrain = terrain;
            internal readonly int LocalIndex = localIndex;
            internal int Id;
        }

        private sealed class TerrainCandidate(FixtureProxy body, FixtureProxy terrain, long creationOrder)
        {
            internal readonly FixtureProxy BodyProxy = body;
            internal readonly FixtureProxy TerrainProxy = terrain;
            internal PhysicsBody Body => BodyProxy.Body!;
            internal readonly long CreationOrder = creationOrder;
            internal PhysicsContact? Manifold;
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
                FixtureProxy proxy = new(++fixtureLabel, body, null, i);
                proxy.Id = broadPhase.CreateProxy(bounds[i], proxy.Label);
                proxies.Add(proxy.Label, proxy);
                fixtures.Add(proxy);
            }
            bodyProxies.Add(body, fixtures);
            newFixtures = true;
        }

        private void RegisterTerrainProxy(TerrainFixture fixture)
        {
            PhysicsAabb bounds = PolygonBounds(fixture.Points, Vector2.Zero, 0);
            bounds.Validate();
            FixtureProxy proxy = new(++fixtureLabel, null, fixture, 0);
            proxy.Id = broadPhase.CreateProxy(bounds, proxy.Label);
            proxies.Add(proxy.Label, proxy);
            newFixtures = true;
        }

        private void RemoveBodyProxies(PhysicsBody body)
        {
            List<FixtureProxy> fixtures = bodyProxies[body];
            // Native body fixture lists prepend creation; destruction traverses that list.
            for (int i = fixtures.Count - 1; i >= 0; i--)
            {
                broadPhase.DestroyProxy(fixtures[i].Id);
                proxies.Remove(fixtures[i].Label);
            }
            bodyProxies.Remove(body);
        }

        private void FindNewTerrainContacts()
        {
            foreach (BroadPhasePair pair in broadPhase.UpdatePairs())
            {
                FixtureProxy a = proxies[pair.First], b = proxies[pair.Second];
                if (a.Terrain == null && b.Terrain != null)
                {
                    (a, b) = (b, a);
                }
                if (a.Terrain == null || b.Body == null || b.Body.Mass == 0)
                {
                    continue;
                }
                var key = (b.Body, a.Terrain, b.LocalIndex);
                if (!contacts.ContainsKey(key))
                {
                    contacts.Add(key, new(b, a, ++contactSequence));
                }
            }
        }

        private Dictionary<PhysicsBody, (Vector2 Origin, float Angle)> BeginBroadPhaseStep()
        {
            if (newFixtures)
            {
                FindNewTerrainContacts();
                newFixtures = false;
            }
            Dictionary<PhysicsBody, (Vector2, float)> starts = [];
            foreach (PhysicsBody body in bodies)
            {
                if (body.Mass > 0)
                {
                    starts.Add(body, (body.Origin, body.Angle));
                }
            }
            return starts;
        }

        private void SynchronizeBodyProxies(Dictionary<PhysicsBody, (Vector2 Origin, float Angle)> starts)
        {
            for (int bodyIndex = bodies.Count - 1; bodyIndex >= 0; bodyIndex--)
            {
                PhysicsBody body = bodies[bodyIndex];
                if (!starts.TryGetValue(body, out var start))
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
                    broadPhase.MoveProxy(proxy.Id, PhysicsAabb.Combine(previous, current), origin - start.Origin);
                }
            }
            FindNewTerrainContacts();
        }

        private static PhysicsAabb BodyBounds(PhysicsBody body, int index, Vector2 origin, float angle) =>
            body.PolygonFixtures.Count == 0 ? new(origin - new Vector2(body.Radius), origin + new Vector2(body.Radius)) :
            PolygonBounds(body.PolygonFixtures[index].Vertices, origin, angle);

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
