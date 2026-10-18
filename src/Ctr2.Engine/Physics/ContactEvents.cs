// Altered C# discrete contact lifecycle subset of Box2D 2.2.1. See BOX2D_LICENSE.txt and PHYSICS.md.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;

namespace Ctr2.Engine.Physics
{
    /// <summary>A discrete touching transition.</summary>
    public enum PhysicsContactEventKind
    {
        /// <summary>The latest update began touching.</summary>
        Begin,
        /// <summary>An update or contact destruction ended touching.</summary>
        End
    }
    /// <summary>The native stage that emitted a touching transition.</summary>
    public enum PhysicsContactEventPhase
    {
        /// <summary>Narrowphase updated the touching flag before emission.</summary>
        Update,
        /// <summary>A touching contact is being retired with its touching flag still set.</summary>
        Destroy
    }
    /// <summary>An immutable Begin/End snapshot, including sensor contacts.</summary>
    /// <param name="Kind">Begin or End transition.</param>
    /// <param name="Phase">Update or destruction emission stage.</param>
    /// <param name="FirstFixture">Native fixture A's stable world-local label.</param>
    /// <param name="SecondFixture">Native fixture B's stable world-local label.</param>
    /// <param name="CreationOrder">Persistent candidate creation sequence.</param>
    /// <param name="IsSensor">Whether either fixture is a sensor at emission.</param>
    /// <param name="Touching">Native touching flag at emission; true for destruction End.</param>
    /// <param name="Manifold">Begin-time world manifold when <see cref="PhysicsWorld.RecordContactManifolds"/> is set; otherwise empty.</param>
    public readonly record struct PhysicsContactEvent(PhysicsContactEventKind Kind, PhysicsContactEventPhase Phase,
        int FirstFixture, int SecondFixture, long CreationOrder, bool IsSensor, bool Touching, PhysicsWorldManifold Manifold = default);

    /// <summary>A native <c>b2WorldManifold</c> snapshot.</summary>
    /// <param name="Normal">World normal from fixture A to fixture B.</param>
    /// <param name="PointCount">Manifold point count; zero for sensors and separated pairs.</param>
    /// <param name="FirstPoint">First world contact point midway between the surfaces.</param>
    /// <param name="SecondPoint">Second world contact point, or zero.</param>
    public readonly record struct PhysicsWorldManifold(Vector2 Normal, int PointCount, Vector2 FirstPoint, Vector2 SecondPoint);

    /// <summary>A native <c>PostSolve</c> report for one touching solid contact.</summary>
    /// <param name="FirstFixture">Native fixture A's stable label.</param>
    /// <param name="SecondFixture">Native fixture B's stable label.</param>
    /// <param name="CreationOrder">Persistent candidate creation sequence.</param>
    /// <param name="ImpulseCount">Velocity constraint point count; one for a redundant two-point manifold.</param>
    /// <param name="FirstNormalImpulse">First point's accumulated normal impulse.</param>
    /// <param name="SecondNormalImpulse">Second point's accumulated normal impulse, or zero.</param>
    /// <param name="FirstTangentImpulse">First point's accumulated tangent impulse.</param>
    /// <param name="SecondTangentImpulse">Second point's accumulated tangent impulse, or zero.</param>
    /// <param name="Manifold">World manifold from transforms synchronized after the island's solve.</param>
    public readonly record struct PhysicsPostSolve(int FirstFixture, int SecondFixture, long CreationOrder, int ImpulseCount,
        float FirstNormalImpulse, float SecondNormalImpulse, float FirstTangentImpulse, float SecondTangentImpulse, PhysicsWorldManifold Manifold);

    /// <summary>An immutable candidate diagnostic, including sensors and implicit terrain.</summary>
    /// <param name="FirstFixture">Native fixture A's stable world-local label.</param>
    /// <param name="SecondFixture">Native fixture B's stable world-local label.</param>
    /// <param name="CreationOrder">Persistent candidate creation sequence.</param>
    /// <param name="Touching">Touching state from the latest narrowphase update.</param>
    /// <param name="IsSensor">Whether either fixture currently acts as a sensor.</param>
    /// <param name="ManifoldPoints">Stored solid manifold point count; sensors have none after updating.</param>
    public readonly record struct PhysicsFixtureContactState(int FirstFixture, int SecondFixture, long CreationOrder,
        bool Touching, bool IsSensor, int ManifoldPoints);

    public sealed partial class PhysicsWorld
    {
        /// <summary>Gets or sets whether future Begin/End transitions are queued; false by default.</summary>
        /// <remarks>Drain regularly while recording. Disabling recording retains any pending records.</remarks>
        public bool RecordContactEvents
        {
            get; set;
        }
        /// <summary>Gets or sets whether queued Begin events carry their narrowphase-time world manifold; false by default.</summary>
        public bool RecordContactManifolds
        {
            get; set;
        }
        /// <summary>Gets or sets whether native PostSolve reports are queued after each island solve; false by default.</summary>
        /// <remarks>Reports follow native island and island-contact order. Disabling recording retains pending reports.</remarks>
        public bool RecordPostSolve
        {
            get; set;
        }
        /// <summary>Returns detached PostSolve reports in emission order and empties the pending queue.</summary>
        public IReadOnlyList<PhysicsPostSolve> DrainPostSolve()
        {
            ReadOnlyCollection<PhysicsPostSolve> result = Array.AsReadOnly(postSolves.ToArray());
            postSolves.Clear();
            return result;
        }
        /// <summary>Returns a detached immutable event batch in emission order and empties the pending queue.</summary>
        public IReadOnlyList<PhysicsContactEvent> DrainContactEvents()
        {
            ReadOnlyCollection<PhysicsContactEvent> result = Array.AsReadOnly(contactEvents.ToArray());
            contactEvents.Clear();
            return result;
        }
        /// <summary>Captures all supported candidates in reverse global creation order, including sensors.</summary>
        public IReadOnlyList<PhysicsFixtureContactState> CaptureFixtureContacts()
        {
            return Array.AsReadOnly(bodyContacts.Values.Select(c => new PhysicsFixtureContactState(c.A.Label, c.B.Label, c.CreationOrder,
                    c.Touching, IsSensor(c.A, c.B), c.Manifold?.PointCount ?? 0))
                .Concat(contacts.Values.Select(c => new PhysicsFixtureContactState(c.A.Label, c.B.Label, c.CreationOrder,
                    c.Touching, IsSensor(c.A, c.B), c.Manifold?.PointCount ?? 0)))
                .OrderByDescending(c => c.CreationOrder).ToArray());
        }

        private readonly List<PhysicsContactEvent> contactEvents = [];
        private readonly List<PhysicsPostSolve> postSolves = [];

        /// <summary>Native <c>b2Island::Report</c>, for discrete and TOI islands alike.</summary>
        private void ReportPostSolve(IEnumerable<PhysicsContact> island)
        {
            foreach (PhysicsContact contact in island)
            {
                // Character landing reactions read the same post-solve impulses as the game's Omnom handler.
                if (contact is PolygonBodyContact { Terrain: true } polygon)
                {
                    polygonImpacts.Add(polygon.CaptureImpact());
                }
                if (!RecordPostSolve)
                {
                    continue;
                }
                (int count, float firstNormal, float secondNormal, float firstTangent, float secondTangent) = contact.Impulses();
                postSolves.Add(new(contact.FirstFixture, contact.SecondFixture, contact.CreationOrder, count,
                    firstNormal, secondNormal, firstTangent, secondTangent, contact.WorldManifold()));
            }
        }
        private static readonly IComparer<PhysicsContactEvent> ContactEventOrder =
            Comparer<PhysicsContactEvent>.Create((a, b) => b.CreationOrder.CompareTo(a.CreationOrder));
        private static bool IsSensor(FixtureProxy a, FixtureProxy b)
        {
            return a.IsSensor || b.IsSensor;
        }

        private void RecordContactChange(FixtureProxy a, FixtureProxy b, long order, bool previous, bool touching, bool destroying,
            PhysicsContact? manifold = null)
        {
            if (!RecordContactEvents || previous == touching)
            {
                return;
            }

            PhysicsWorldManifold snapshot = RecordContactManifolds && touching && manifold != null ? manifold.WorldManifold() : default;
            contactEvents.Add(new(touching ? PhysicsContactEventKind.Begin : PhysicsContactEventKind.End,
                destroying ? PhysicsContactEventPhase.Destroy : PhysicsContactEventPhase.Update,
                a.Label, b.Label, order, IsSensor(a, b), destroying ? previous : touching, snapshot));
        }
        private static PhysicsDistanceProxy DistanceProxy(FixtureProxy fixture)
        {
            if (fixture.Distance != null)
            {
                return fixture.Distance;
            }

            if (fixture.Terrain != null)
            {
                return fixture.Distance = new(fixture.Terrain.Points, 2 * LinearSlop);
            }

            PhysicsBody body = fixture.Body!;
            return fixture.Distance = body.PolygonFixtures.Count == 0 ? new([Vector2.Zero], body.Radius) :
                new(body.PolygonFixtures[fixture.LocalIndex].Vertices, 2 * LinearSlop);
        }
        private static PhysicsDistanceTransform DistanceTransform(FixtureProxy fixture)
        {
            return fixture.Body is PhysicsBody body ? new(body.Origin, body.Angle) : default;
        }

        private static bool SensorOverlap(FixtureProxy a, FixtureProxy b)
        {
            return PhysicsDistance.Overlaps(DistanceProxy(a), DistanceTransform(a), DistanceProxy(b), DistanceTransform(b));
        }

        private void RetireBodyContacts(PhysicsBody body)
        {
            KeyValuePair<(int, int), BodyCandidate>[] bodyEntries = [.. bodyContacts.Where(c => c.Value.A.Body == body || c.Value.B.Body == body)];
            KeyValuePair<(PhysicsBody, TerrainFixture, int), TerrainCandidate>[] terrainEntries = [.. contacts.Where(c => c.Value.Body == body)];
            IEnumerable<(FixtureProxy A, FixtureProxy B, long CreationOrder, bool Touching)> retired = bodyEntries.Select(c => (c.Value.A, c.Value.B, c.Value.CreationOrder, c.Value.Touching))
                .Concat(terrainEntries.Select(c => (c.Value.A, c.Value.B, c.Value.CreationOrder, c.Value.Touching)));
            foreach ((FixtureProxy A, FixtureProxy B, long CreationOrder, bool Touching) in retired.OrderByDescending(c => c.CreationOrder))
            {
                RecordContactChange(A, B, CreationOrder, Touching, false, true);
            }

            foreach (KeyValuePair<(int, int), BodyCandidate> entry in bodyEntries)
            {
                _ = bodyContacts.Remove(entry.Key);
            }

            foreach (KeyValuePair<(PhysicsBody, TerrainFixture, int), TerrainCandidate> entry in terrainEntries)
            {
                _ = contacts.Remove(entry.Key);
            }
        }
    }
}
