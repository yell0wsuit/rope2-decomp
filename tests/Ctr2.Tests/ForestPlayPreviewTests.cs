using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks state-driven character playback and candy fade with generated cache assets.</summary>
    public sealed class ForestPlayPreviewTests
    {
        /// <summary>Rendering cannot advance game callbacks; fixed ticks complete feeding and restart restores candy.</summary>
        /// <param name="ticks">Fixed ticks to advance before the command renders its status.</param>
        /// <param name="result">Expected command result at that tick.</param>
        /// <param name="pack">First forest entry selects arrival; another pack keeps ordinary play.</param>
        [Theory]
        [InlineData(0, "Running", 1)]
        [InlineData(0, "Running", 2)]
        [InlineData(1, "Running", 2)]
        [InlineData(20, "Won", 2)]
        public async Task PlaybackCompletionUsesFixedTicksAndRestartRestoresArtwork(int ticks, string result, int pack)
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-play-" + Guid.NewGuid());
            string assets = Path.Combine(root, "assets"), maps = Path.Combine(assets, "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='test.xml'/></pack><pack id='2'><file short='test.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "test.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='100' y='100'/><omnom x='100' y='100'/><tutorial_swipe x='50' y='150' delay='0.05' angle='0'/></layer></map>");
                File.WriteAllText(Path.Combine(maps, "!_MasterGameDesignDocument.xml"), CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "</layer></map>").Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"").Replace("gravityScale='0.7'", "gravityScale='0'"));
                foreach (string color in new[] { "red", "green" })
                {
                    using SKBitmap texture = new(8, 8);
                    texture.Erase(color == "red" ? SKColors.Red : SKColors.Lime);
                    using SKData png = texture.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(assets, color + ".png"), png.ToArray());
                }
                string Hash(string color)
                {
                    return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets, color + ".png"))));
                }

                ResourceCatalog catalog = new(1, "synthetic", "", [new(85, "character.pb", 7, 2, null, 0, 0, []),
                    new(133, "red.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0), new(0, 0, 8, 8, 0, 0)], Hash("red")),
                    new(7, "green.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0)], Hash("green")),
                    new(163, "green.png", 0, 2, 1, 8, 8, [.. Enumerable.Range(0, 12).Select(_ => new ResourceQuad(0, 0, 8, 8, 0, 0))], Hash("green")),
                    new(104, "character.pb", 7, 2, null, 0, 0, [])]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                string[] names = ["IDLE", "MOUTH_OPEN", "MOUTH_CLOSE", "CHEWING", "PUZZLED", "FAIL", "FALLING", "FALLING_MOUTH_OPEN", "FALLING_MOUTH_CLOSE", "FALLING_CHEWING", "SWIPE_LOOP"];
                byte[] track = AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                    (2, AnimationPreviewTests.Message((1, .1f), (2, 0f), (3, 0f))));
                byte[] part = AnimationPreviewTests.Message([(1, 1), (3, 7), .. names.Select((_, i) => (10, (object)AnimationPreviewTests.Message((1, i), (5, track))))]);
                File.WriteAllBytes(Path.Combine(assets, "character.pb"), AnimationPreviewTests.Message([(1, 2), (7, 8f), (8, 8f), (9, part),
                    .. names.Select((name, i) => (10, (object)AnimationPreviewTests.Message((1, i), (2, name))))]));
                using LevelPreview preview = LevelPreview.Load(root, pack, 1, 2, play: true).Preview;
                ForestSession session = preview.Session!;
                Assert.Equal(pack == 1, session.Simulation.Gameplay!.ArrivalLeaves != null);
                using SKBitmap bitmap = new(640, 960);
                using SKCanvas canvas = new(bitmap);
                preview.Draw(canvas, 640, 960, 0);
                Assert.Equal(SKColors.Red, bitmap.GetPixel(200, 200));
                if (session.Simulation.Gameplay.ArrivalLeaves is ForestLeafBurst leaves)
                {
                    ForestLeaf leaf = leaves.Particles[0];
                    Assert.Equal(SKColors.Lime, bitmap.GetPixel((int)leaf.Position.X, (int)leaf.Position.Y));
                }
                session.Step();
                preview.Draw(canvas, 640, 960, 1000);
                Assert.Equal(ForestResult.Running, session.Simulation.Gameplay!.Result);
                string snapshot = Path.Combine(root, "play.png");
                ProcessStartInfo start = new("dotnet")
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                foreach (string arg in new[] { typeof(LevelPreview).Assembly.Location, "play-level", root, pack.ToString(System.Globalization.CultureInfo.InvariantCulture), "1", "--ticks", ticks.ToString(System.Globalization.CultureInfo.InvariantCulture), "--snapshot", snapshot })
                {
                    start.ArgumentList.Add(arg);
                }
                using Process process = Process.Start(start)!;
                Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
                Task<string> errors = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
                await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                string error = await errors;
                Assert.True(process.ExitCode == 0, error);
                Assert.Contains("Result: " + result, await output);
                Assert.True(File.Exists(snapshot));
                Assert.Equal("MOUTH_CLOSE", session.Simulation.Gameplay.Scene);
                Assert.NotEqual(SKColors.Red, bitmap.GetPixel(200, 200));
                for (int i = 0; i < 20; i++)
                {
                    session.Step();
                }
                Assert.Equal(ForestResult.Won, session.Simulation.Gameplay.Result);
                using (LevelPreview batched = LevelPreview.Load(root, pack, 1, 2, play: true).Preview)
                {
                    batched.Session!.AdvanceTo(1);
                    Assert.Equal(session.Simulation.Gameplay.FinishedAt, batched.Session.Simulation.Gameplay!.FinishedAt);
                    Assert.Equal(session.Simulation.Gameplay.Consumptions.ToArray(), batched.Session.Simulation.Gameplay.Consumptions.ToArray());
                }
                preview.Draw(canvas, 640, 960, 1000);
                Assert.NotEqual(SKColors.Red, bitmap.GetPixel(200, 200));
                Assert.Equal(SKColors.Lime, bitmap.GetPixel(190, 300));
                session.Restart();
                preview.Draw(canvas, 640, 960, 0);
                Assert.Equal(SKColors.Red, bitmap.GetPixel(200, 200));
                Assert.NotEqual(SKColors.Lime, bitmap.GetPixel(190, 300));
                Assert.Equal(ForestResult.Running, session.Simulation.Gameplay!.Result);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
