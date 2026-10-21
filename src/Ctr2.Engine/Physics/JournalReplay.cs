using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Drift of one journaled object between the original recording and the port.</summary>
    /// <param name="Index">Journal object index.</param>
    /// <param name="Kind">Object kind in journal registration order.</param>
    /// <param name="Samples">Compared snapshot states.</param>
    /// <param name="Missing">Snapshot states for which the port had no body.</param>
    /// <param name="MaxPosition">Largest origin distance in meters.</param>
    /// <param name="MeanPosition">Mean origin distance in meters.</param>
    /// <param name="MaxAngle">Largest angle difference in radians.</param>
    /// <param name="FirstDivergence">First frame whose origin distance exceeded the threshold, if any.</param>
    public sealed record JournalDrift(int Index, string Kind, int Samples, int Missing, float MaxPosition, float MeanPosition, float MaxAngle, int? FirstDivergence);

    /// <summary>One compared snapshot state.</summary>
    /// <param name="Frame">Snapshot frame.</param>
    /// <param name="Index">Journal object index.</param>
    /// <param name="Recorded">Recorded origin in meters.</param>
    /// <param name="Port">Port origin in meters.</param>
    /// <param name="RecordedAngle">Recorded angle, if any.</param>
    /// <param name="PortAngle">Port angle.</param>
    public sealed record JournalSample(int Frame, int Index, Vector2 Recorded, Vector2 Port, float? RecordedAngle, float PortAngle);

    /// <summary>A recorded star catch compared with the port's collection.</summary>
    /// <param name="Star">Zero-based star index.</param>
    /// <param name="RecordedFrame">Frame recorded by the original.</param>
    /// <param name="PortTick">Tick at which the port collected it, if it did.</param>
    public sealed record JournalStarCatch(int Star, int RecordedFrame, int? PortTick);

    /// <summary>One recorded fruit catch and the port's collection tick for the same fruit.</summary>
    /// <param name="Fruit">Zero-based fruit index in creation order, or -1 when the journal names an object that is not a port fruit.</param>
    /// <param name="RecordedFrame">Recorded catch frame.</param>
    /// <param name="PortTick">Port contact tick, or null when the port never collected it.</param>
    public sealed record JournalFruitCatch(int Fruit, int RecordedFrame, int? PortTick);

    /// <summary>The outcome of replaying a journal open-loop against the port.</summary>
    /// <param name="Frames">Steps simulated.</param>
    /// <param name="Objects">Per-object drift.</param>
    /// <param name="Stars">Recorded star catches.</param>
    /// <param name="Fruits">Recorded fruit catches.</param>
    /// <param name="Result">Port gameplay result after the last step.</param>
    /// <param name="Unsupported">Events or objects the port could not apply.</param>
    /// <param name="Samples">Every compared state, in frame order.</param>
    public sealed record JournalReplayResult(int Frames, IReadOnlyList<JournalDrift> Objects, IReadOnlyList<JournalStarCatch> Stars, IReadOnlyList<JournalFruitCatch> Fruits, LevelResult? Result,
        IReadOnlyList<string> Unsupported, IReadOnlyList<JournalSample> Samples);

    /// <summary>Replays an original hint journal against a scene, measuring drift, either open loop or snapshot-corrected.</summary>
    /// <remarks>The original hint playback re-applies every snapshot, so it never relies on determinism. Open-loop replay applies
    /// only inputs: cuts by journal rope index with the live swipe's tip impulse, touch presses as scene taps, and touch
    /// movement as balloon swipes (replayed touches never cut ropes). Events recorded at frame F apply before step F + 1; a snapshot at frame N is
    /// compared after step N. Corrected replay then applies each compared state as <c>SingleBodyObject::applyObjectState</c> does,
    /// so each comparison measures only the drift since that object's previous state.</remarks>
    public static class JournalReplay
    {
        /// <summary>Runs the journal and compares every snapshot state with the matching port body.</summary>
        /// <param name="scene">A freshly constructed scene for the journal's level.</param>
        /// <param name="journal">Parsed journal.</param>
        /// <param name="threshold">Origin distance in meters that counts as divergence.</param>
        /// <param name="corrected">Whether to apply every compared snapshot state after measuring it.</param>
        /// <param name="follow">Optional per-step observer receiving the scene and the journal slot bodies, before any correction.</param>
        /// <returns>Per-object drift, star catch timing and the final result.</returns>
        public static JournalReplayResult Run(CandySimulation scene, ReplayJournal journal, float threshold = .05f, bool corrected = false,
            Action<CandySimulation, IReadOnlyList<PhysicsBody?>>? follow = null)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(journal);
            List<(string Kind, Func<PhysicsBody?> Body)> objects = Registry(scene);
            int appended = 0;
            // Native addGremlin, addRope and addBalloon append journal slots as they create late children.
            void Append()
            {
                for (; appended < scene.SpawnedCount; appended++)
                {
                    (string kind, Func<PhysicsBody?> body, _) = scene.SpawnedJournalSlot(appended);
                    objects.Add((kind, body));
                }
            }
            List<string> unsupported = [];
            Dictionary<int, Accumulator> drift = [];
            Dictionary<int, int> catches = [], fruitCatches = [];
            List<JournalSample> samples = [];
            JournalInputPlayer player = new(journal);
            ILookup<int, JournalSnapshot> snapshots = journal.Snapshots.ToLookup(s => s.Frame);
            while (scene.Tick < player.LastFrame && (scene.Gameplay == null || scene.Gameplay.Result == LevelResult.Running))
            {
                foreach (JournalEvent e in player.Apply(scene))
                {
                    if (e.Type == JournalEventType.StarCatch)
                    {
                        _ = catches.TryAdd(e.Index, e.Frame);
                    }
                    else if (e.Type == JournalEventType.FruitCatch)
                    {
                        _ = fruitCatches.TryAdd(e.Index, e.Frame);
                    }
                }
                Append();
                scene.Step();
                Append();
                follow?.Invoke(scene, [.. objects.Select(o => o.Body())]);
                foreach (JournalObjectState state in snapshots[scene.Tick].SelectMany(s => s.Objects))
                {
                    if (state.Index < 0 || state.Index >= objects.Count)
                    {
                        unsupported.Add($"Frame {scene.Tick}: snapshot of unknown journal object {state.Index}");
                        continue;
                    }
                    (string kind, Func<PhysicsBody?> body) = objects[state.Index];
                    if (kind == "rope" || state.X is not float x || state.Y is not float y)
                    {
                        continue;
                    }
                    Accumulator a = drift.TryGetValue(state.Index, out Accumulator? found) ? found : drift[state.Index] = new(kind);
                    if (body() is not PhysicsBody b)
                    {
                        a.Missing++;
                        continue;
                    }
                    float distance = Vector2.Distance(b.Origin, new(x, y));
                    samples.Add(new(scene.Tick, state.Index, new(x, y), b.Origin, state.Angle, b.Angle));
                    a.Samples++;
                    a.Sum += distance;
                    a.Max = MathF.Max(a.Max, distance);
                    if (state.Angle is float angle)
                    {
                        a.MaxAngle = MathF.Max(a.MaxAngle, MathF.Abs(b.Angle - angle));
                    }
                    if (distance > threshold)
                    {
                        a.First ??= scene.Tick;
                    }
                    if (corrected)
                    {
                        Apply(scene, b, state);
                    }
                }
            }
            List<int> starIndices = [.. Enumerable.Range(0, objects.Count).Where(i => objects[i].Kind == "star")];
            JournalStarCatch[] stars = [.. catches.OrderBy(c => c.Value).Select(c =>
            {
                int star = starIndices.IndexOf(c.Key);
                StarCollection? collected = star < 0 ? null : scene.StarCollections.FirstOrDefault(s => s.Star == star);
                return new JournalStarCatch(star, c.Value, collected?.Tick);
            })];
            List<int> fruitIndices = [.. Enumerable.Range(0, objects.Count).Where(i => objects[i].Kind == "fruit")];
            JournalFruitCatch[] fruits = [.. fruitCatches.OrderBy(c => c.Value).Select(c =>
            {
                int fruit = fruitIndices.IndexOf(c.Key);
                FruitCollection? collected = fruit < 0 ? null : scene.FruitCollections.FirstOrDefault(f => f.Fruit == fruit);
                return new JournalFruitCatch(fruit, c.Value, collected?.Tick);
            })];
            return new(scene.Tick, Array.AsReadOnly(drift.OrderBy(d => d.Key).Select(d => new JournalDrift(d.Key, d.Value.Kind, d.Value.Samples, d.Value.Missing, d.Value.Max,
                d.Value.Samples == 0 ? 0 : d.Value.Sum / d.Value.Samples, d.Value.MaxAngle, d.Value.First)).ToArray()), Array.AsReadOnly(stars), Array.AsReadOnly(fruits), scene.Gameplay?.Result,
                Array.AsReadOnly([.. player.Unsupported, .. unsupported]), samples.AsReadOnly());
        }

        /// <summary>Native <c>SingleBodyObject::applyObjectState</c>: the origin and angle when all three are recorded, then the linear and
        /// angular velocities when recorded (only full syncs record them). <c>Booka::applyObjectState</c> ignores a caught Booka.</summary>
        private static void Apply(CandySimulation scene, PhysicsBody body, JournalObjectState state)
        {
            if (!scene.World.Bodies.Contains(body) || scene.Bookas.Any(b => b.Body == body && b.Booed))
            {
                return;
            }
            if (state is { X: float x, Y: float y, Angle: float angle })
            {
                scene.World.SetTransform(body, new(x, y), angle);
            }
            if (state is { VelocityX: float vx, VelocityY: float vy })
            {
                body.SetLinearVelocity(new(vx, vy));
            }
            if (state.AngularVelocity is float spin)
            {
                body.SetAngularVelocity(spin);
            }
        }

        /// <summary>Maps a journal object index to a scene rope: loaded ropes take the first indices, and generated ropes take the
        /// slots appended after the initial registry.</summary>
        /// <param name="scene">Scene playing the journal.</param>
        /// <param name="index">Journal object index.</param>
        /// <returns>The scene rope index, or -1 when the slot is not a rope.</returns>
        internal static int RopeIndex(CandySimulation scene, int index)
        {
            if (index >= 0 && index < scene.InitialRopeCount)
            {
                return index;
            }
            int late = index - scene.InitialJournalCount;
            return late >= 0 && late < scene.SpawnedCount ? scene.SpawnedJournalSlot(late).Rope : -1;
        }

        /// <summary>Journal registration order while loading: ropes, dynamic polygons, balloons, candies, snails, fireballs, frogs,
        /// Bookas, gremlins, throwers, Om Nom, helicopters, stars (a fruit challenge registers them before they are removed), then
        /// fruits. Removed objects leave the journal. Late children append their slots afterwards.</summary>
        internal static List<(string, Func<PhysicsBody?>)> Registry(CandySimulation scene)
        {
            // Native loadMap never registers triggers or balloon generators; their bodies must not shift journal object indices.
            List<(string, Func<PhysicsBody?>)> objects = [];
            for (int i = 0; i < scene.InitialRopeCount; i++)
            {
                objects.Add(("rope", () => null));
            }
            foreach (GeneralShape shape in scene.Shapes.Where(s => s.Body.Type == PhysicsBodyType.Dynamic))
            {
                objects.Add(("shape", () => scene.Shapes.Contains(shape) ? shape.Body : null));
            }
            foreach (SimulationBalloon balloon in scene.Balloons.Where(b => !b.Spawned))
            {
                objects.Add(("balloon", () => balloon.Body));
            }
            foreach (PhysicsBody candy in scene.Candies)
            {
                objects.Add(("candy", () => scene.World.Bodies.Contains(candy) ? candy : null));
            }
            foreach (SimulationSnail snail in scene.Snails)
            {
                objects.Add(("snail", () => snail.Body));
            }
            foreach (SimulationFireball fireball in scene.Fireballs)
            {
                objects.Add(("fireball", () => scene.World.Bodies.Contains(fireball.Body) ? fireball.Body : null));
            }
            foreach (SimulationFrog frog in scene.Frogs)
            {
                // The frog records its newest tongue body.
                objects.Add(("frog", () => frog.Bodies.Count == 0 ? null : frog.Bodies[^1]));
            }
            foreach (SimulationBooka booka in scene.Bookas)
            {
                objects.Add(("booka", () => booka.Removed ? null : booka.Body));
            }
            foreach (SimulationGremlin gremlin in scene.Gremlins.Where(g => !g.Spawned))
            {
                objects.Add(("gremlin", () => gremlin.Removed ? null : gremlin.Body));
            }
            foreach (SimulationThrower thrower in scene.Throwers)
            {
                objects.Add(("thrower", () => thrower.Body));
            }
            foreach (PhysicsBody character in scene.Characters)
            {
                objects.Add(("omnom", () => character));
            }
            foreach (SimulationHelicopter helicopter in scene.Helicopters)
            {
                objects.Add(("helicopter", () => helicopter.Body));
            }
            for (int i = 0; i < scene.Stars.Count; i++)
            {
                int star = i;
                objects.Add(("star", () => scene.Stars[star].Removed ? null : scene.Stars[star].Body));
            }
            for (int i = 0; i < scene.Fruits.Count; i++)
            {
                int fruit = i;
                objects.Add(("fruit", () => scene.Fruits[fruit].Removed ? null : scene.Fruits[fruit].Body));
            }
            return objects;
        }

        private sealed class Accumulator(string kind)
        {
            internal readonly string Kind = kind;
            internal int Samples, Missing;
            internal float Sum, Max, MaxAngle;
            internal int? First;
        }
    }
}
