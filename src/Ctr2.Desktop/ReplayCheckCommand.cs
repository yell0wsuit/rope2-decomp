using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

namespace Ctr2.Desktop
{
    /// <summary>Replays an original developer hint journal against the port and reports drift.</summary>
    internal static class ReplayCheckCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length < 4)
            {
                throw new ArgumentException("replay-check requires a cache, pack and entry.");
            }
            string cache = args[1], variant = "";
            int pack = int.Parse(args[2], CultureInfo.InvariantCulture), entry = int.Parse(args[3], CultureInfo.InvariantCulture);
            float threshold = .05f;
            bool trace = false;
            for (int i = 4; i < args.Length; i += 2)
            {
                if (args[i] == "--trace")
                {
                    trace = true;
                    i--;
                    continue;
                }
                string value = i + 1 < args.Length ? args[i + 1] : throw new ArgumentException("Missing value for " + args[i]);
                switch (args[i])
                {
                    case "--variant":
                        variant = value is "challenge" or "fruit_challenge" ? "." + value : throw new ArgumentException("Variant must be challenge or fruit_challenge.");
                        break;
                    case "--threshold":
                        threshold = float.Parse(value, CultureInfo.InvariantCulture);
                        if (!float.IsFinite(threshold) || threshold <= 0)
                        {
                            throw new ArgumentException("Threshold must be positive.");
                        }
                        break;
                    default:
                        throw new ArgumentException("Unknown replay-check option: " + args[i]);
                }
            }
            LevelDefinition level = LevelReader.LoadWindows(cache, pack, entry);
            LevelDefinition design = LevelReader.LoadWindowsDesign(cache);
            LevelVariant mode = variant.Length == 0 ? LevelVariant.Main : LevelVariants.Parse(variant.TrimStart('.'));
            ReplayJournal journal = JournalInputPlayer.LoadHint(cache, level, mode);
            string levelHash = journal.LevelHash;
            CandySimulation scene = new(level, design, play: true, firstLevelArrival: pack == 1 && entry == 1, SceneTimings.Load(cache, level, mode), mode);
            JournalReplayResult result = JournalReplay.Run(scene, journal, threshold);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Scope = "Open-loop replay of the original hint journal: inputs only, snapshots compared, never applied.",
                Level = level.FileName,
                Variant = variant.TrimStart('.'),
                LevelHash = levelHash,
                ThresholdMeters = threshold,
                result.Frames,
                Result = result.Result?.ToString(),
                RecordedFrames = journal.Snapshots.Count == 0 ? 0 : journal.Snapshots.Max(s => s.Frame),
                result.Objects,
                result.Stars,
                result.Fruits,
                result.Unsupported,
                Samples = trace ? result.Samples.Select(s => new { s.Frame, s.Index, RecordedX = s.Recorded.X, RecordedY = s.Recorded.Y, PortX = s.Port.X, PortY = s.Port.Y, s.RecordedAngle, s.PortAngle }) : null
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
    }
}
