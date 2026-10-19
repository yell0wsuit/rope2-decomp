using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Ctr2.Assets;

namespace Ctr2.Engine.Physics
{
    /// <summary>How far one hint journal replays bit-exactly, identified by desktop pack, entry and variant only.</summary>
    /// <param name="Pack">One-based desktop pack.</param>
    /// <param name="Entry">One-based entry within the pack.</param>
    /// <param name="Variant"><c>main</c>, <c>challenge</c> or <c>fruit_challenge</c>.</param>
    /// <param name="Status"><c>ok</c>, <c>unsupported</c> when the scene uses unported mechanics, or <c>hash-mismatch</c>.</param>
    /// <param name="FirstInexactFrame">First snapshot frame with any component differing from the port, or null when all match.</param>
    /// <param name="ExactSamples">Compared states before the first inexact one.</param>
    /// <param name="Samples">All compared states.</param>
    public sealed record JournalOutcome(int Pack, int Entry, string Variant, string Status, int? FirstInexactFrame, int ExactSamples, int Samples)
    {
        /// <summary>Gets the tab-separated baseline line.</summary>
        /// <value>Pack, entry, variant, status, first inexact frame (<c>-</c> for none), exact and total samples.</value>
        public string Line => string.Join('\t', Pack, Entry, Variant, Status, FirstInexactFrame?.ToString(CultureInfo.InvariantCulture) ?? "-", ExactSamples, Samples);

        /// <summary>Parses a baseline line.</summary>
        /// <param name="line">Tab-separated line written by <see cref="Line"/>.</param>
        /// <returns>The outcome.</returns>
        /// <exception cref="FormatException">The line is malformed.</exception>
        public static JournalOutcome Parse(string line)
        {
            ArgumentNullException.ThrowIfNull(line);
            string[] f = line.Split('\t');
            return f.Length != 7 ? throw new FormatException("Expected seven baseline fields.") :
                new(int.Parse(f[0], CultureInfo.InvariantCulture), int.Parse(f[1], CultureInfo.InvariantCulture), f[2], f[3],
                    f[4] == "-" ? null : int.Parse(f[4], CultureInfo.InvariantCulture), int.Parse(f[5], CultureInfo.InvariantCulture), int.Parse(f[6], CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Replays every developer hint journal in a local cache and measures its bit-exact prefix, for regression baselines.</summary>
    /// <remarks>Exact frames are a floor, not parity: the recording platform's libm rounds a few trigonometric results differently
    /// from the host's, so ulp-level divergence eventually appears even though the engine itself matches native Box2D.</remarks>
    public static class JournalBaseline
    {
        /// <summary>Journal variants in baseline order.</summary>
        public static readonly IReadOnlyList<string> Variants = ["main", "challenge", "fruit_challenge"];

        /// <summary>Replays every journal present in the cache, in pack, entry and variant order.</summary>
        /// <param name="cache">Imported Windows cache root.</param>
        /// <returns>One outcome per journal file.</returns>
        public static IReadOnlyList<JournalOutcome> RunAll(string cache)
        {
            LevelDefinition design = LevelReader.LoadWindowsDesign(cache);
            (WindowsLevelEntry Entry, string Variant)[] work = [.. LevelReader.LoadWindowsIndex(cache).SelectMany(e => Variants.Select(v => (e, v)))];
            JournalOutcome?[] results = new JournalOutcome?[work.Length];
            _ = Parallel.For(0, work.Length, i => results[i] = Run(cache, work[i].Entry, work[i].Variant, design));
            return [.. results.OfType<JournalOutcome>()];
        }

        /// <summary>Replays one journal open-loop and measures its bit-exact prefix.</summary>
        /// <param name="cache">Imported Windows cache root.</param>
        /// <param name="entry">Desktop level entry.</param>
        /// <param name="variant">Journal variant.</param>
        /// <param name="design">Master design document.</param>
        /// <returns>The outcome, or null when the level has no journal for the variant.</returns>
        public static JournalOutcome? Run(string cache, WindowsLevelEntry entry, string variant, LevelDefinition design)
        {
            ArgumentNullException.ThrowIfNull(entry);
            string assets = Path.Combine(Path.GetFullPath(cache), "assets");
            FileInfo file = new(Path.Combine(assets, "replays", "lightweight", entry.FileName + (variant == "main" ? "" : "." + variant) + ".rep.lw"));
            if (!file.Exists || file.Length > 16 * 1024 * 1024)
            {
                return null;
            }
            ReplayJournal journal = ReplayJournal.Read(File.ReadAllBytes(file.FullName));
            if (ReplayJournal.ComputeLevelHash(File.ReadAllText(Path.Combine(assets, "maps", entry.FileName))) != journal.LevelHash)
            {
                return new(entry.Pack, entry.Entry, variant, "hash-mismatch", null, 0, 0);
            }
            CandySimulation scene;
            try
            {
                scene = new(LevelReader.LoadWindows(cache, entry.Pack, entry.Entry), design, play: true, firstLevelArrival: entry.Pack == 1 && entry.Entry == 1);
            }
            catch (InvalidDataException)
            {
                return new(entry.Pack, entry.Entry, variant, "unsupported", null, 0, 0);
            }
            IReadOnlyList<JournalSample> samples = JournalReplay.Run(scene, journal).Samples;
            int exact = samples.TakeWhile(s => s.Recorded == s.Port && (s.RecordedAngle is not float angle || angle == s.PortAngle)).Count();
            return new(entry.Pack, entry.Entry, variant, "ok", exact < samples.Count ? samples[exact].Frame : null, exact, samples.Count);
        }
    }
}
