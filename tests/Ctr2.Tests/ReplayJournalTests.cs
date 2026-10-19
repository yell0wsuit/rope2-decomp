using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks the lightweight journal reader, level hash and open-loop replay with invented data only.</summary>
    public sealed class ReplayJournalTests
    {
        private static void Varint(List<byte> output, ulong value)
        {
            while (value >= 0x80)
            {
                output.Add((byte)(value | 0x80));
                value >>= 7;
            }
            output.Add((byte)value);
        }

        private static void VarintField(List<byte> output, int number, ulong value)
        {
            Varint(output, (ulong)(number << 3));
            Varint(output, value);
        }

        private static void FloatField(List<byte> output, int number, float value)
        {
            Varint(output, (ulong)((number << 3) | 5));
            output.AddRange(BitConverter.GetBytes(value));
        }

        private static void BytesField(List<byte> output, int number, IReadOnlyCollection<byte> value)
        {
            Varint(output, (ulong)((number << 3) | 2));
            Varint(output, (ulong)value.Count);
            output.AddRange(value);
        }

        private static byte[] Snapshot(int frame, int index, float x, float y, float angle)
        {
            List<byte> state = [], physical = [], snapshot = [];
            FloatField(physical, 1, x);
            FloatField(physical, 2, y);
            FloatField(physical, 3, angle);
            VarintField(state, 1, (ulong)index);
            BytesField(state, 2, physical);
            VarintField(snapshot, 1, (ulong)frame);
            BytesField(snapshot, 2, state);
            return [.. snapshot];
        }

        private static byte[] Cut(int frame, int rope, int segment, float x, float y)
        {
            List<byte> e = [];
            VarintField(e, 1, (ulong)frame);
            VarintField(e, 2, 6);
            FloatField(e, 3, x);
            FloatField(e, 4, y);
            VarintField(e, 6, (ulong)rope);
            VarintField(e, 7, (ulong)segment);
            return [.. e];
        }

        private static byte[] Journal(string hash, IEnumerable<byte[]> snapshots, IEnumerable<byte[]> events)
        {
            List<byte> journal = [];
            BytesField(journal, 1, Encoding.UTF8.GetBytes(hash));
            VarintField(journal, 2, 2);
            foreach (byte[] snapshot in snapshots)
            {
                BytesField(journal, 3, snapshot);
            }
            foreach (byte[] e in events)
            {
                BytesField(journal, 4, e);
            }
            return [.. journal];
        }

        /// <summary>Reads hash, version, snapshot states and events, with absent fields reading as unset or zero.</summary>
        [Fact]
        public void ReadsJournalFields()
        {
            ReplayJournal journal = ReplayJournal.Read(Journal("abc", [Snapshot(7, 3, 1.5f, -2, .25f)], [Cut(4, 1, 2, 10, 20)]));
            Assert.Equal(("abc", 2), (journal.LevelHash, journal.FormatVersion));
            JournalSnapshot snapshot = Assert.Single(journal.Snapshots);
            Assert.Equal(new JournalObjectState(3, 1.5f, -2, .25f, null, null, null), Assert.Single(snapshot.Objects));
            Assert.Equal(7, snapshot.Frame);
            Assert.Equal(new JournalEvent(4, JournalEventType.RopeCut, 10, 20, 0, 1, 2), Assert.Single(journal.Events));
            _ = Assert.Throws<InvalidDataException>(() => ReplayJournal.Read([0x0a, 0x05, 0x61]));
        }

        /// <summary>Hashes the object layer breadth-first with reversed attribute order, seeded like the original.</summary>
        [Fact]
        public void LevelHashFollowsOriginalWalk()
        {
            const string xml = "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='1' y='2' id='c'><star x='3'/></candy><grab x='4'/></layer></map>";
            string expected = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("old_style_levellayernameObjectscandyidyxc21grabx4starx3")));
            Assert.Equal(expected, ReplayJournal.ComputeLevelHash(xml));
            Assert.Equal(Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("old_style_level"))),
                ReplayJournal.ComputeLevelHash(xml.Replace("'Objects'", "'Layer 4'", StringComparison.Ordinal)));
        }

        /// <summary>Applies a journaled cut before the matching step and measures snapshot drift without applying it.</summary>
        [Fact]
        public void ReplayAppliesCutsAndMeasuresDrift()
        {
            const string level = "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='200' y='147'/><grab x='200' y='43' length='105' target_id='c'/></layer></map>";
            CandySimulation manual = new(LevelReader.Read(level), LevelReader.Read(CandySimulationTests.Design));
            while (manual.Tick < 5)
            {
                manual.Step();
            }
            Vector2 cut = manual.RopePoints(0)[0] + new Vector2(0, 20);
            Assert.True(manual.Cut(0, 0, cut));
            while (manual.Tick < 30)
            {
                manual.Step();
            }
            Vector2 candy = manual.Candies[0].Origin;
            // Journal index 0 is the rope and 1 the candy, in registration order.
            ReplayJournal journal = ReplayJournal.Read(Journal("h", [Snapshot(30, 1, candy.X, candy.Y, 0), Snapshot(31, 1, candy.X + 1, candy.Y, 0)], [Cut(5, 0, 0, cut.X, cut.Y)]));
            JournalReplayResult result = JournalReplay.Run(new CandySimulation(LevelReader.Read(level), LevelReader.Read(CandySimulationTests.Design)), journal, .05f);
            JournalDrift drift = Assert.Single(result.Objects);
            Assert.Equal(("candy", 2, 0f), (drift.Kind, drift.Samples, result.Samples[0].Port.X - candy.X));
            Assert.Equal(31, drift.FirstDivergence);
            Assert.Empty(result.Unsupported);
        }
    }
}
