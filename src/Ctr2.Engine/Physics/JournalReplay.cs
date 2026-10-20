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

    /// <summary>Replays an original hint journal against a scene without forcing its snapshots, measuring drift.</summary>
    /// <remarks>The original hint playback re-applies every snapshot, so it never relies on determinism. This replay applies
    /// only inputs: cuts by journal rope index with the live swipe's tip impulse, touch presses as scene taps, and touch
    /// movement as balloon swipes (replayed touches never cut ropes). Events recorded at frame F apply before step F + 1; a snapshot at frame N is
    /// compared after step N.</remarks>
    public static class JournalReplay
    {
        /// <summary>Runs the journal and compares every snapshot state with the matching port body.</summary>
        /// <param name="scene">A freshly constructed scene for the journal's level.</param>
        /// <param name="journal">Parsed journal.</param>
        /// <param name="threshold">Origin distance in meters that counts as divergence.</param>
        /// <returns>Per-object drift, star catch timing and the final result.</returns>
        public static JournalReplayResult Run(CandySimulation scene, ReplayJournal journal, float threshold = .05f)
        {
            ArgumentNullException.ThrowIfNull(scene);
            ArgumentNullException.ThrowIfNull(journal);
            List<(string Kind, Func<PhysicsBody?> Body)> objects = Registry(scene);
            List<string> unsupported = [];
            Dictionary<int, Accumulator> drift = [];
            Dictionary<int, int> catches = [], fruitCatches = [];
            List<JournalSample> samples = [];
            int frames = Math.Max(journal.Events.Count == 0 ? 0 : journal.Events.Max(e => e.Frame), journal.Snapshots.Count == 0 ? 0 : journal.Snapshots.Max(s => s.Frame));
            ILookup<int, JournalEvent> events = journal.Events.ToLookup(e => e.Frame);
            ILookup<int, JournalSnapshot> snapshots = journal.Snapshots.ToLookup(s => s.Frame);
            Vector2? touch = null;
            while (scene.Tick < frames && (scene.Gameplay == null || scene.Gameplay.Result == LevelResult.Running))
            {
                JournalEvent[] frame = [.. events[scene.Tick]];
                for (int k = 0; k < frame.Length; k++)
                {
                    JournalEvent e = frame[k];
                    Vector2 point = new(e.X, e.Y);
                    switch (e.Type)
                    {
                        case JournalEventType.TouchDown:
                            scene.Gameplay?.NotifyTouchDown();
                            _ = scene.Tap(point);
                            touch = point;
                            break;
                        case JournalEventType.TouchMove:
                            if (touch is Vector2 start)
                            {
                                scene.SwipeBalloons(start, point);
                            }
                            touch = point;
                            break;
                        case JournalEventType.TouchUp:
                            touch = null;
                            break;
                        case JournalEventType.RopeCut:
                            if (e.Index < 0 || e.Index >= scene.RopeCount || objects[e.Index].Kind != "rope")
                            {
                                unsupported.Add($"Frame {e.Frame}: cut of journal object {e.Index}");
                                break;
                            }
                            // A live cut gave the new tips the swipe's impulse. The recorder logs the cutting move after its
                            // cuts, so the swipe runs from the previous touch to the next move in this frame.
                            JournalEvent? move = frame.Skip(k + 1).FirstOrDefault(m => m.Type == JournalEventType.TouchMove);
                            _ = scene.Cut(e.Index, e.Segment, point, touch is Vector2 from && move != null ? CandySimulation.SwipeImpulse(from, new(move.X, move.Y)) : Vector2.Zero);
                            break;
                        case JournalEventType.StarCatch:
                            _ = catches.TryAdd(e.Index, e.Frame);
                            break;
                        case JournalEventType.None:
                            break;
                        case JournalEventType.PauseTap:
                            break;
                        case JournalEventType.PauseSwipe:
                            break;
                        case JournalEventType.FruitCatch:
                            _ = fruitCatches.TryAdd(e.Index, e.Frame);
                            break;
                        default:
                            break;
                    }
                }
                scene.Step();
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
                unsupported.AsReadOnly(), samples.AsReadOnly());
        }

        /// <summary>Journal registration order: ropes, dynamic polygons, balloons, candies, fireballs, frogs, Bookas, throwers, Om Nom,
        /// helicopters, stars (a fruit challenge registers them before they are removed), then fruits. Removed objects leave the
        /// journal.</summary>
        private static List<(string, Func<PhysicsBody?>)> Registry(CandySimulation scene)
        {
            List<(string, Func<PhysicsBody?>)> objects = [];
            for (int i = 0; i < scene.RopeCount; i++)
            {
                objects.Add(("rope", () => null));
            }
            foreach (GeneralShape shape in scene.Shapes.Where(s => s.Body.Type == PhysicsBodyType.Dynamic))
            {
                objects.Add(("shape", () => scene.Shapes.Contains(shape) ? shape.Body : null));
            }
            foreach (SimulationBalloon balloon in scene.Balloons)
            {
                objects.Add(("balloon", () => balloon.Body));
            }
            foreach (PhysicsBody candy in scene.Candies)
            {
                objects.Add(("candy", () => scene.World.Bodies.Contains(candy) ? candy : null));
            }
            // Snails, still pending, register between candies and fireballs.
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
