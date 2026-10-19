using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Engine.Physics;

namespace Ctr2.Desktop
{
    /// <summary>Audits imported desktop content against the actual managed session constructors.</summary>
    internal static class LevelAuditCommand
    {
        internal static int Run(string[] args)
        {
            if (args.Length != 2)
            {
                throw new ArgumentException("audit-levels requires an imported Windows cache.");
            }
            string cache = args[1];
            IReadOnlyList<WindowsLevelEntry> index = LevelReader.LoadWindowsIndex(cache);
            LevelDefinition design = LevelReader.LoadWindowsDesign(cache);
            List<object> results = [];
            int startable = 0;
            foreach (WindowsLevelEntry entry in index)
            {
                Dictionary<string, int> objects = [];
                string? location = null;
                string? reason = null;
                bool supported = false;
                try
                {
                    LevelDefinition level = LevelReader.LoadWindows(cache, entry.Pack, entry.Entry);
                    objects = level.Objects.GroupBy(o => o.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count());
                    location = level.Settings.FirstOrDefault(s => s.Kind == "gameDesign")?.Attributes.GetValueOrDefault("location");
                    _ = new GameSession(level, design, firstLevelArrival: entry.Pack == 1 && entry.Entry == 1);
                    supported = true;
                    startable++;
                }
                catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    reason = error.Message;
                }
                results.Add(new
                {
                    entry.Pack,
                    entry.Entry,
                    entry.FileName,
                    Location = location,
                    Objects = objects,
                    CanStartSession = supported,
                    Reason = reason
                });
            }
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                SchemaVersion = 2,
                Scope = "Session construction only; level completion, rendered assets, original-game parity and unsupported attributes are not verified.",
                LevelCount = index.Count,
                StartableLevels = startable,
                Levels = results
            }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
    }
}
