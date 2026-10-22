using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Generated art verifies fixed-clock water animation selection, looping and restart through the renderer.</summary>
    public sealed class WaterPreviewTests
    {
        /// <summary>The swim loop continues past its duration; drawing and pausing cannot advance its clock.</summary>
        [Fact]
        public void SwimmingArtworkLoopsFromFixedClockAndRestartReturnsToIdle()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-water-art-" + Guid.NewGuid());
            string assets = Path.Combine(root, "assets"), maps = Path.Combine(assets, "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='2'><file short='water.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "water.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='20' y='20'/><omnom x='250' y='250'/><water y='100'/></layer></map>");
                string design = RopeTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master +
                    "<water_design id='water_design' gravity_multiplier='-0.6' water_damping='3' water_angular_damping='1'/></layer></map>", StringComparison.Ordinal)
                    .Replace("gravityScale=\"0.7\"", "gravityScale=\"0\"", StringComparison.Ordinal)
                    .Replace("gravityScale='0.7'", "gravityScale='0'", StringComparison.Ordinal);
                File.WriteAllText(Path.Combine(maps, "!_MasterGameDesignDocument.xml"), design);
                foreach (string color in new[] { "red", "green", "clear" })
                {
                    using SKBitmap texture = new(8, 8);
                    texture.Erase(color == "red" ? SKColors.Red : color == "green" ? SKColors.Lime : SKColors.Transparent);
                    using SKData png = texture.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(assets, color + ".png"), png.ToArray());
                }
                string Hash(string color)
                {
                    return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets, color + ".png"))));
                }
                ResourceCatalog catalog = new(1, "synthetic", "", [new(85, "water.pb", 7, 2, null, 0, 0, []),
                    new(133, "red.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0), new(0, 0, 8, 8, 0, 0)], Hash("red")),
                    new(250, "clear.png", 0, 2, 1, 8, 8, [.. Enumerable.Range(0, 12).Select(_ => new ResourceQuad(0, 0, 8, 8, 0, 0))], Hash("clear")),
                    new(7, "green.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0)], Hash("green"))]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                string[] names = ["IDLE", "MOUTH_OPEN", "MOUTH_CLOSE", "CHEWING", "PUZZLED", "FAIL", "FALLING", "FALLING_MOUTH_OPEN",
                    "FALLING_MOUTH_CLOSE", "FALLING_CHEWING", "ROLLING", "STAND_UP", "WATER_DIVE", "WATER_SWIM", "WATER_MOUTH_OPEN",
                    "WATER_MOUTH_CLOSE", "WATER_CHEWING", "WATER_FAIL"];
                byte[] Track(string name)
                {
                    return name == "WATER_SWIM"
                        ? AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                            (2, AnimationPreviewTests.Message((1, .1f), (2, 20f), (3, 0f))), (2, AnimationPreviewTests.Message((1, .1f), (2, 0f), (3, 0f))))
                        : AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                            (2, AnimationPreviewTests.Message((1, .5f), (2, 0f), (3, 0f))));
                }
                byte[] part = AnimationPreviewTests.Message([(1, 1), (3, 7), .. names.Select((name, i) => (10, (object)AnimationPreviewTests.Message((1, i), (5, Track(name)))))]);
                File.WriteAllBytes(Path.Combine(assets, "water.pb"), AnimationPreviewTests.Message([(1, 2), (7, 8f), (8, 8f), (9, part),
                    .. names.Select((name, i) => (10, (object)AnimationPreviewTests.Message((1, i), (2, name))))]));
                using LevelPreview preview = LevelPreview.Load(root, 2, 1, 2, play: true).Preview;
                GameSession session = preview.Session!;
                while (session.Simulation.Gameplay!.Scene != "WATER_SWIM" && session.Simulation.Tick < 100)
                {
                    session.Step();
                }
                Assert.Equal("WATER_SWIM", session.Simulation.Gameplay!.Scene);
                using SKBitmap bitmap = new(640, 960);
                using SKCanvas canvas = new(bitmap);
                int GreenLeft()
                {
                    preview.Draw(canvas, 640, 960, 1000);
                    return Enumerable.Range(450, 100).First(x => Enumerable.Range(450, 100).Any(y => bitmap.GetPixel(x, y) == SKColors.Lime));
                }
                int initial = GreenLeft();
                for (int i = 0; i < 15; i++)
                {
                    session.Step();
                }
                int looped = GreenLeft();
                Assert.InRange(looped - initial, 9, 11); // .2505 seconds wraps to .0505, halfway along the first segment.
                session.Paused = true;
                session.Step();
                Assert.Equal(looped, GreenLeft());
                session.Restart();
                Assert.Equal("IDLE", session.Simulation.Gameplay!.Scene);
                Assert.Equal(initial, GreenLeft());
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
