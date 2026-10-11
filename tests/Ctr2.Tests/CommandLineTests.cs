using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Ctr2.Desktop;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks user-facing command-line failures in a separate desktop-host process.</summary>
    public sealed class CommandLineTests
    {
        /// <summary>
        /// Verifies a missing dump reports a readable error, exits unsuccessfully, and leaves no cache.
        /// </summary>
        /// <returns>A task that completes after the child process exits and the assertions finish.</returns>
        [Fact]
        public async Task InvalidDumpReportsErrorWithoutCrashing()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-cli-" + Guid.NewGuid());
            _ = Directory.CreateDirectory(root);
            try
            {
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                start.ArgumentList.Add(typeof(AssetPreview).Assembly.Location);
                start.ArgumentList.Add("import");
                start.ArgumentList.Add(Path.Combine(root, "missing-dump"));
                start.ArgumentList.Add(Path.Combine(root, "cache"));
                using Process process = Process.Start(start)!;
                string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                Assert.Equal(1, process.ExitCode);
                Assert.Contains("Expected a Windows package root", error);
                Assert.DoesNotContain("Unhandled exception", error);
                Assert.False(Directory.Exists(Path.Combine(root, "cache")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
