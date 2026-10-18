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
    public readonly record struct PhysicsContactEvent(PhysicsContactEventKind Kind, PhysicsContactEventPhase Phase,
        int FirstFixture, int SecondFixture, long CreationOrder, bool IsSensor, bool Touching);

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
        private static readonly IComparer<PhysicsContactEvent> ContactEventOrder =
            Comparer<PhysicsContactEvent>.Create((a, b) => b.CreationOrder.CompareTo(a.CreationOrder));
        private static bool IsSensor(FixtureProxy a, FixtureProxy b)
        {
            return a.IsSensor || b.IsSensor;
        }

        private void RecordContactChange(FixtureProxy a, FixtureProxy b, long order, bool previous, bool touching, bool destroying)
        {
            if (!RecordContactEvents || previous == touching)
            {
                return;
            }

            contactEvents.Add(new(touching ? PhysicsContactEventKind.Begin : PhysicsContactEventKind.End,
                destroying ? PhysicsContactEventPhase.Destroy : PhysicsContactEventPhase.Update,
                a.Label, b.Label, order, IsSensor(a, b), destroying ? previous : touching));
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
