using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

namespace Ctr2.Desktop
{
    /// <summary>Plays every supported hint journal to its end and compares the gameplay outcome with the recording.</summary>
    internal static class ReplayOutcomesCommand
    {
        /// <summary>Extra input-free ticks after the recording, so a recorded eat can finish chewing.</summary>
        private const int Grace = 600;

        internal static int Run(string[] args)
        {
            if (args.Length is not (2 or 3) || (args.Length == 3 && args[2] != "--corrected"))
            {
                throw new ArgumentException("replay-outcomes requires an imported Windows cache and optionally --corrected.");
            }
            bool corrected = args.Length == 3;
            string cache = args[1];
            LevelDefinition design = LevelReader.LoadWindowsDesign(cache);
            SceneTimings timings = SceneTimings.LoadAll(cache);
            (WindowsLevelEntry Entry, string Variant)[] work = [.. LevelReader.LoadWindowsIndex(cache).SelectMany(e => JournalBaseline.Variants.Select(v => (e, v)))];
            string?[] lines = new string?[work.Length];
            _ = Parallel.For(0, work.Length, i => lines[i] = Play(cache, work[i].Entry, work[i].Variant, design, timings, corrected));
            Console.WriteLine("pack\tentry\tvariant\tverdict\ttarget\trecordingPassed\trecordedEnd\trecordedCandyEnd\tportEat\tresult\tresultTick\tstarsRecorded\tstarsPort\tfruitsRecorded\tfruitsPort\tunsupported" +
                "\tsamplesOver5cm\tworstKind\tworstFrame\tworstMeters");
            foreach (string? line in lines.OfType<string>())
            {
                Console.WriteLine(line);
            }
            return 0;
        }

        private static string? Play(string cache, WindowsLevelEntry entry, string variant, LevelDefinition design, SceneTimings timings, bool corrected)
        {
            string assets = Path.Combine(Path.GetFullPath(cache), "assets");
            FileInfo file = new(Path.Combine(assets, "replays", "lightweight", entry.FileName + (variant == "main" ? "" : "." + variant) + ".rep.lw"));
            if (!file.Exists)
            {
                return null;
            }
            string prefix = $"{entry.Pack}\t{entry.Entry}\t{variant}";
            ReplayJournal journal = ReplayJournal.Read(File.ReadAllBytes(file.FullName));
            if (ReplayJournal.ComputeLevelHash(File.ReadAllText(Path.Combine(assets, "maps", entry.FileName))) != journal.LevelHash)
            {
                return prefix + "\thash-mismatch";
            }
            LevelDefinition level = LevelReader.LoadWindows(cache, entry.Pack, entry.Entry);
            CandySimulation scene;
            try
            {
                scene = new(level, design, play: true, firstLevelArrival: entry.Pack == 1 && entry.Entry == 1, timings,
                    LevelVariants.Parse(variant));
            }
            catch (InvalidDataException)
            {
                return prefix + "\tunsupported";
            }
            JournalReplayResult replay = JournalReplay.Run(scene, journal, corrected: corrected);
            Dictionary<int, string> kinds = replay.Objects.ToDictionary(o => o.Index, o => o.Kind);
            JournalSample[] over = [.. replay.Samples.Where(s => Vector2.Distance(s.Recorded, s.Port) > .05f)];
            JournalSample? worst = replay.Samples.MaxBy(s => Vector2.Distance(s.Recorded, s.Port));
            LevelGameplay gameplay = scene.Gameplay!;
            for (int i = 0; i < Grace && gameplay.Result == LevelResult.Running; i++)
            {
                scene.Step();
            }
            int recordedEnd = journal.Snapshots.Count == 0 ? 0 : journal.Snapshots.Max(s => s.Frame);
            // The candy's journal slot stops receiving snapshots once the original removes the eaten candy.
            int candySlot = replay.Objects.FirstOrDefault(o => o.Kind == "candy")?.Index ?? -1;
            int candyEnd = journal.Snapshots.Where(s => s.Objects.Any(o => o.Index == candySlot)).Select(s => s.Frame).DefaultIfEmpty(-1).Max();
            int? eat = gameplay.Consumptions.Count == 0 ? null : gameplay.Consumptions[0].Tick;
            HashSet<int> recordedStars = [.. replay.Stars.Select(s => s.Star)], portStars = [.. scene.StarCollections.Select(s => s.Star)];
            int recordedFruits = replay.Fruits.Count, portFruits = scene.FruitCollections.Count;
            // Challenges pass on their desired star count and fruit challenges on their minimum fruit count; main levels must
            // collect the recorded stars. Rope limits and forbidden elements constrain input, which the recording already obeys.
            int target = variant switch
            {
                "challenge" => Setting(level, "challenge", "desiredStars"),
                "fruit_challenge" => Setting(level, "fruit_challenge", "minFruits"),
                _ => recordedStars.Count
            };
            bool passed = variant switch
            {
                "challenge" => portStars.Count >= target,
                "fruit_challenge" => portFruits >= target,
                _ => recordedStars.SetEquals(portStars)
            };
            bool recordingPassed = variant switch
            {
                "challenge" => recordedStars.Count >= target,
                "fruit_challenge" => recordedFruits >= target,
                _ => true
            };
            string verdict = gameplay.Result != LevelResult.Won ? "not-won" : !passed ? (variant == "fruit_challenge" ? "fruits" : "stars") : "ok";
            return string.Join('\t', prefix, verdict, target, recordingPassed ? "yes" : "no", recordedEnd, candyEnd, eat?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-", gameplay.Result, scene.Tick,
                string.Join(',', recordedStars.Order()), string.Join(',', portStars.Order()), recordedFruits, portFruits, replay.Unsupported.Count,
                over.Length, worst == null ? "-" : kinds[worst.Index], worst?.Frame ?? 0,
                worst == null ? 0 : Vector2.Distance(worst.Recorded, worst.Port));
        }

        /// <summary>Reads an integer challenge setting, or -1 (no requirement) when absent.</summary>
        private static int Setting(LevelDefinition level, string kind, string name)
        {
            string? text = level.Settings.FirstOrDefault(s => s.Kind == kind)?.Attributes.GetValueOrDefault(name);
            return text == null ? -1 : int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
