using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>Feeds a hint journal's recorded inputs to a scene, one frame before each step: touch presses become scene taps,
    /// touch movement swipes balloons, and journaled cuts cut ropes with the live swipe's tip impulse. Snapshots are never
    /// applied, so the scene plays the recording open loop.</summary>
    public sealed class JournalInputPlayer
    {
        private readonly ILookup<int, JournalEvent> events;
        private readonly List<string> unsupported = [];
        private Vector2? touch;

        /// <summary>Prepares the journal's inputs.</summary>
        /// <param name="journal">Parsed journal.</param>
        public JournalInputPlayer(ReplayJournal journal)
        {
            ArgumentNullException.ThrowIfNull(journal);
            events = journal.Events.ToLookup(e => e.Frame);
            LastFrame = Math.Max(journal.Events.Count == 0 ? 0 : journal.Events.Max(e => e.Frame),
                journal.Snapshots.Count == 0 ? 0 : journal.Snapshots.Max(s => s.Frame));
        }

        /// <summary>Gets the last recorded frame, of either an event or a snapshot.</summary>
        public int LastFrame
        {
            get;
        }

        /// <summary>Gets the inputs the scene could not apply, such as cuts of objects that are not ropes.</summary>
        public IReadOnlyList<string> Unsupported => unsupported.AsReadOnly();

        /// <summary>Applies the inputs recorded at the scene's current tick; call it just before the scene steps. Events
        /// recorded at frame F apply before step F + 1, so a snapshot at frame N compares after step N.</summary>
        /// <param name="scene">Scene playing the journal's level.</param>
        /// <returns>Every event recorded at this frame, inputs and catches alike.</returns>
        public IReadOnlyList<JournalEvent> Apply(CandySimulation scene)
        {
            ArgumentNullException.ThrowIfNull(scene);
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
                        int rope = JournalReplay.RopeIndex(scene, e.Index);
                        if (rope < 0)
                        {
                            unsupported.Add($"Frame {e.Frame}: cut of journal object {e.Index}");
                            break;
                        }
                        // A live cut gave the new tips the swipe's impulse. The recorder logs the cutting move after its cuts,
                        // so the swipe runs from the previous touch to the next move in this frame.
                        JournalEvent? move = frame.Skip(k + 1).FirstOrDefault(m => m.Type == JournalEventType.TouchMove);
                        _ = scene.Cut(rope, e.Segment, point, touch is Vector2 from && move != null ? CandySimulation.SwipeImpulse(from, new(move.X, move.Y)) : Vector2.Zero);
                        break;
                    case JournalEventType.StarCatch:
                        break;
                    case JournalEventType.FruitCatch:
                        break;
                    case JournalEventType.None:
                        break;
                    case JournalEventType.PauseTap:
                        break;
                    case JournalEventType.PauseSwipe:
                        break;
                    default:
                        break;
                }
            }
            return frame;
        }

        /// <summary>Loads a level's developer hint journal from a local cache and checks that it was recorded for this level.</summary>
        /// <param name="cache">Local game cache.</param>
        /// <param name="level">Level definition from the same cache.</param>
        /// <param name="variant">Game mode, which selects the journal file.</param>
        /// <returns>The parsed journal.</returns>
        /// <exception cref="InvalidDataException">The level has no bounded journal for the variant, or the journal's level hash differs.</exception>
        public static ReplayJournal LoadHint(string cache, LevelDefinition level, LevelVariant variant)
        {
            ArgumentNullException.ThrowIfNull(level);
            string assets = Path.Combine(Path.GetFullPath(cache), "assets");
            string suffix = variant == LevelVariant.Main ? "" : "." + variant.Name();
            FileInfo file = new(Path.Combine(assets, "replays", "lightweight", level.FileName + suffix + ".rep.lw"));
            if (!file.Exists || file.Length > 16 * 1024 * 1024)
            {
                throw new InvalidDataException("No bounded replay journal for this level.");
            }
            ReplayJournal journal = ReplayJournal.Read(File.ReadAllBytes(file.FullName));
            string levelHash = ReplayJournal.ComputeLevelHash(File.ReadAllText(Path.Combine(assets, "maps", level.FileName)));
            return string.Equals(levelHash, journal.LevelHash, StringComparison.Ordinal) ? journal
                : throw new InvalidDataException($"Journal level hash {journal.LevelHash} does not match the level ({levelHash}).");
        }
    }
}
