using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Guards how far each original developer hint journal replays bit-exactly against the port.</summary>
    /// <remarks>Needs a local imported cache (<c>CTR2_CACHE</c>, or <c>cache/windows-alpha</c> at the repository root) and is
    /// skipped without one. The tracked baseline holds only pack/entry/variant identities and frame and sample counts.
    /// Regenerate it with <c>replay-baseline &lt;cache&gt; --write tests/Ctr2.Tests/ReplayBaseline.tsv</c> after an improvement.</remarks>
    public sealed class ReplayBaselineTests
    {
        private static string? Cache()
        {
            string? configured = Environment.GetEnvironmentVariable("CTR2_CACHE");
            if (!string.IsNullOrEmpty(configured))
            {
                return Directory.Exists(configured) ? configured : null;
            }
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Ctr2.slnx")))
                {
                    string cache = Path.Combine(directory.FullName, "cache", "windows-alpha");
                    return Directory.Exists(cache) ? cache : null;
                }
            }
            return null;
        }

        /// <summary>No journal's exact prefix gets shorter, no supported journal becomes unsupported, and the journal set is unchanged.</summary>
        [Fact]
        public void NoJournalRegresses()
        {
            string? cache = Cache();
            Assert.SkipWhen(cache == null, "No local imported cache.");
            Dictionary<(int, int, string), JournalOutcome> baseline = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "ReplayBaseline.tsv"))
                .Where(l => !l.StartsWith('#')).Select(JournalOutcome.Parse).ToDictionary(o => (o.Pack, o.Entry, o.Variant));
            IReadOnlyList<JournalOutcome> current = JournalBaseline.RunAll(cache);
            Assert.Equal(baseline.Keys.Order(), current.Select(o => (o.Pack, o.Entry, o.Variant)).Order());
            List<string> regressions = [];
            foreach (JournalOutcome now in current)
            {
                JournalOutcome before = baseline[(now.Pack, now.Entry, now.Variant)];
                if (before.Status != "ok")
                {
                    continue;
                }
                if (now.Status != "ok" || (now.FirstInexactFrame ?? int.MaxValue) < (before.FirstInexactFrame ?? int.MaxValue) || now.ExactSamples < before.ExactSamples)
                {
                    regressions.Add($"{now.Pack}-{now.Entry} {now.Variant}: was {before.Line}, now {now.Line}");
                }
            }
            Assert.True(regressions.Count == 0, string.Join(Environment.NewLine, regressions));
        }
    }
}
