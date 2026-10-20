using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Verifies native forest artwork with synthetic textures and fixed simulation time.</summary>
    public sealed class ForestArtworkTests
    {
        /// <summary>Background fitting and star collection preserve logical geometry at both densities.</summary>
        [Theory]
        [InlineData(2, 640, 960)]
        [InlineData(4, 640, 960)]
        [InlineData(4, 1920, 720)]
        public void ArtworkFollowsFixedTicksCollectionPauseAndRestart(int resolution, int width, int height)
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-art-" + Guid.NewGuid());
            string assets = Path.Combine(root, "assets"), maps = Path.Combine(assets, "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='a.xml'/><file short='a.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "a.xml"), "<map><layer name='settings'><map width='320' height='480'/><gameDesign location='forest'/></layer><layer name='Objects'><candy id='c' x='200' y='60'/><star x='200' y='100'/><star x='200' y='150'/><star x='200' y='200'/><omnom x='100' y='350'/></layer></map>");
                string design = CandySimulationTests.Design.Replace("</layer></map>", CharacterBodyDefinitionTests.Master + "<circle id='star' body_type='kinematic' isSensor='true' radius='8'/></layer></map>");
                File.WriteAllText(Path.Combine(maps, "!_MasterGameDesignDocument.xml"), design);
                foreach (string name in new[] { "star", "star-hd", "candy", "back" })
                {
                    int density = name == "star-hd" ? 2 : 1;
                    using SKBitmap bitmap = new(name == "back" ? 24 : name.StartsWith("star", StringComparison.Ordinal) ? 16 * density : 8, 8 * density);
                    bitmap.Erase(name switch
                    {
                        "star" or "star-hd" => SKColors.Lime,
                        "candy" => SKColors.Red,
                        _ => SKColors.Blue
                    });
                    if (name.StartsWith("star", StringComparison.Ordinal))
                    {
                        using SKCanvas canvas = new(bitmap);
                        using SKPaint effect = new()
                        {
                            Color = SKColors.Magenta
                        };
                        canvas.DrawRect(8 * density, 0, 8 * density, 8 * density, effect);
                    }
                    if (name == "back")
                    {
                        using SKCanvas canvas = new(bitmap);
                        using SKPaint edge = new()
                        {
                            Color = SKColors.Yellow
                        };
                        canvas.DrawRect(0, 0, 8, 8, edge);
                        canvas.DrawRect(16, 0, 8, 8, edge);
                    }
                    using SKData png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                    File.WriteAllBytes(Path.Combine(assets, name + ".png"), png.ToArray());
                }
                string Hash(string name)
                {
                    return Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(assets, name + ".png"))));
                }

                ResourceEntry Image(int id, string name, int selected, float density, int quads = 1)
                {
                    return new(id, name + ".png", 0, selected, density, name == "back" ? 24 : 8, 8,
                                        [.. Enumerable.Range(0, quads).Select(_ => new ResourceQuad(0, 0, name == "back" ? 24 : 8, 8, 0, 0))], Hash(name));
                }

                ResourceCatalog catalog = new(1, "synthetic", "", [
                    new(85, "character.pb", 7, 2, null, 0, 0, []), new(95, "star.pb", 7, 2, null, 0, 0, []),
                    new(7, "star.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0), new(8, 0, 8, 8, 0, 0)], Hash("star")),
                    new(7, "star-hd.png", 0, 4, 2, 16, 16, [new(0, 0, 16, 16, 0, 0), new(16, 0, 16, 16, 0, 0)], Hash("star-hd")),
                    Image(133, "candy", 2, 1, 2), Image(133, "candy", 4, 2, 2), Image(160, "back", 2, 1)]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                void Animation(string file, string[] scenes)
                {
                    byte[] track = AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                        (2, AnimationPreviewTests.Message((1, .1f), (2, 0f), (3, 0f))));
                    // Center the trimmed sprite inside the authored root canvas at both densities.
                    byte[] moving = AnimationPreviewTests.Message((1, 0), (2, AnimationPreviewTests.Message((1, 0f), (2, 0f), (3, 0f))),
                        (2, AnimationPreviewTests.Message((1, 1f), (2, 40f), (3, 0f))));
                    byte[] effect = AnimationPreviewTests.Message((1, 4), (2, AnimationPreviewTests.Message((1, 0f), (2, 1f), (6, 1))));
                    byte[] part = AnimationPreviewTests.Message([(1, 1), (3, 7), (5, 18), (6, 18), .. scenes.Select((name, i) =>
                        (10, (object)(file == "star.pb" && name == "FX_PARTICLE"
                            ? AnimationPreviewTests.Message((1, i), (5, track), (5, effect))
                            : AnimationPreviewTests.Message((1, i), (5, file == "star.pb" ? moving : track)))))]);
                    File.WriteAllBytes(Path.Combine(assets, file), AnimationPreviewTests.Message([(1, 2), (7, 8f), (8, 8f), (9, part),
                        .. scenes.Select((name, i) => (10, (object)AnimationPreviewTests.Message((1, i), (2, name))))]));
                }
                Animation("character.pb", ["IDLE", "MOUTH_OPEN", "MOUTH_CLOSE", "CHEWING", "PUZZLED", "FAIL", "FALLING", "FALLING_MOUTH_OPEN", "FALLING_MOUTH_CLOSE", "FALLING_CHEWING", "ROLLING", "STAND_UP", "EXCITED", "EXCITED_MORE", "EXCITED_MOST"]);
                Animation("star.pb", ["IDLE", "FLARE", "FX_PARTICLE"]);
                using LevelPreview preview = LevelPreview.Load(root, 1, 2, resolution, play: true).Preview;
                using SKBitmap pixels = new(width, height);
                using SKCanvas destination = new(pixels);
                Viewport viewport = Viewport.Fit(width, height, 640, 960);
                SKColor Pixel(float x, float y)
                {
                    Vector2 point = viewport.ToScreen(new(x, y));
                    return pixels.GetPixel((int)point.X, (int)point.Y);
                }
                preview.Draw(destination, width, height, 0);
                Assert.Equal(SKColors.Blue, Pixel(10, 100));
                Assert.Equal(SKColors.Blue, Pixel(630, 100));
                Assert.Equal(SKColors.Lime, Pixel(400, 200));
                Assert.Equal(SKColors.Blue, Pixel(405, 200));
                Assert.Equal(SKColors.Lime, Pixel(414, 300));
                Assert.Equal(SKColors.Lime, Pixel(424, 400));
                Assert.Equal(SKColors.Blue, Pixel(400, 300));
                byte[] initial = pixels.Bytes;
                preview.Session!.Paused = true;
                preview.Draw(destination, width, height, 1000.043);
                Assert.Equal(SKColors.Lime, Pixel(400, 200));
                Assert.Equal(initial, pixels.Bytes);
                Assert.Equal(0, preview.Simulation!.Tick);
                preview.Session.Paused = false;
                for (int i = 0; i < 100 && preview.Simulation.StarCollections.Count == 0; i++)
                {
                    preview.Session.Step();
                }
                _ = Assert.Single(preview.Simulation.StarCollections);
                preview.Draw(destination, width, height, 1000.043);
                Assert.Equal(SKColors.Magenta, Pixel(400, 200));
                for (int i = 0; i < 30; i++)
                {
                    preview.Session.Step();
                }
                preview.Draw(destination, width, height, 0);
                Assert.Equal(SKColors.Blue, Pixel(400, 200));
                preview.Session.Restart();
                preview.Draw(destination, width, height, 1000.043);
                Assert.Equal(SKColors.Lime, Pixel(400, 200));
                Assert.Empty(preview.Simulation.StarCollections);
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
