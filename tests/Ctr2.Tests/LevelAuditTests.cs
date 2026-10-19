using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Ctr2.Desktop;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks authored-order content auditing through the real desktop command.</summary>
    public sealed class LevelAuditTests
    {
        /// <summary>Unsupported and missing levels are reported without hiding later entries.</summary>
        [Fact]
        public async Task AuditReportsConstructionCoverageInDesktopOrder()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-audit-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='b.xml'/><file short='missing.xml'/><file short='a.xml'/><file short='bad.xml'/><file short='a.xml'/></pack></filelist>");
                const string map = "<map><layer name='settings'><map width='320' height='480'/><gameDesign location='forest'/></layer><layer name='Objects'><candy id='c' x='100' y='100'/><omnom x='100' y='350'/></layer></map>";
                File.WriteAllText(Path.Combine(maps, "a.xml"), map);
                File.WriteAllText(Path.Combine(maps, "b.xml"), map.Replace("</layer></map>", "<balloon x='0' y='0'/></layer></map>"));
                File.WriteAllText(Path.Combine(maps, "bad.xml"), map.Replace("id='c'", "id=''").Replace("</layer></map>", "<candy id='' x='200' y='100'/></layer></map>"));
                File.WriteAllText(Path.Combine(maps, "!_MasterGameDesignDocument.xml"), CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>"));
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string arg in new[] { typeof(LevelPreview).Assembly.Location, "audit-levels", root })
                {
                    start.ArgumentList.Add(arg);
                }
                using Process process = Process.Start(start)!;
                Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                Task<string> errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.True(process.ExitCode == 0, await errors);
                using JsonDocument report = JsonDocument.Parse(await output);
                JsonElement json = report.RootElement;
                Assert.Equal(5, json.GetProperty("LevelCount").GetInt32());
                Assert.True(json.GetProperty("StartableLevels").GetInt32() == 2, json.ToString());
                JsonElement levels = json.GetProperty("Levels");
                Assert.Equal("b.xml", levels[0].GetProperty("FileName").GetString());
                Assert.False(levels[0].GetProperty("CanStartSession").GetBoolean());
                Assert.Equal(1, levels[0].GetProperty("Objects").GetProperty("balloon").GetInt32());
                Assert.NotEmpty(levels[0].GetProperty("Reason").GetString()!);
                Assert.Equal("missing.xml", levels[1].GetProperty("FileName").GetString());
                Assert.False(levels[1].GetProperty("CanStartSession").GetBoolean());
                Assert.Equal("a.xml", levels[2].GetProperty("FileName").GetString());
                Assert.Equal(3, levels[2].GetProperty("Entry").GetInt32());
                Assert.True(levels[2].GetProperty("CanStartSession").GetBoolean());
                Assert.False(levels[3].GetProperty("CanStartSession").GetBoolean());
                Assert.True(levels[4].GetProperty("CanStartSession").GetBoolean());
                Assert.Contains("completion", json.GetProperty("Scope").GetString()!, StringComparison.OrdinalIgnoreCase);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
