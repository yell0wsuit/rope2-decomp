using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Desktop;
using Ctr2.Engine;
using Ctr2.Engine.Physics;

using SkiaSharp;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks character draw/readback from simulated authored origins using generated cache assets.</summary>
    public sealed class DynamicCharacterPreviewTests
    {
        /// <summary>Artwork follows the fallen body origin rather than its center or initial placement.</summary>
        /// <remarks>The invented placement polygon's area centroid lies away from its vertex mean, so normalization keeps the center of mass offset.</remarks>
        /// <param name="width">Output pixel width.</param>
        /// <param name="height">Output pixel height.</param>
        /// <param name="resolution">Generated texture density enum.</param>
        [Theory]
        [InlineData(640, 960, 2)]
        [InlineData(3440, 1440, 4)]
        public void CharacterArtworkFollowsBodyOrigin(int width, int height, int resolution)
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-character-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='test.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "test.xml"), "<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy id='c' x='260' y='0'/><omnom x='100' y='0' polygon='60,0,60,6,54,12,0,60,'/><polygon_materialized x='0' y='300' material='wood' polygon='320,0,320,40,0,40,' smoothFactor='1'/></layer></map>");
                File.WriteAllText(Path.Combine(maps, "%21_MasterGameDesignDocument.xml"), CandySimulationTests.Design.Replace("</layer></map>",
                    CharacterBodyDefinitionTests.Master + "<polygon id='wood' body_type='static' friction='0.3' restitution='0' isSensor='false'/></layer></map>"));
                WriteTexture(Path.Combine(root, "assets", "red.png"), false);
                WriteTexture(Path.Combine(root, "assets", "green.png"), true);
                string redHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "assets", "red.png"))));
                string greenHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "assets", "green.png"))));
                ResourceCatalog catalog = new(1, "synthetic", "", [
                    new(85, "character.pb", 7, 2, null, 0, 0, []),
                    new(133, "red.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0), new(0, 0, 8, 8, 0, 0)], redHash),
                    new(133, "red.png", 0, 4, 2, 16, 16, [new(0, 0, 16, 16, 0, 0), new(0, 0, 16, 16, 0, 0)], redHash),
                    new(7, "green.png", 0, 2, 1, 8, 8, [new(0, 0, 8, 8, 0, 0)], greenHash),
                    new(7, "green.png", 0, 4, 2, 16, 16, [new(0, 0, 16, 16, 0, 0)], greenHash)]);
                File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
                byte[] part = AnimationPreviewTests.Message((1, 1), (3, 7), (10, AnimationPreviewTests.Message((1, 0))));
                byte[] animation = AnimationPreviewTests.Message((1, 2), (7, 8f), (8, 8f), (9, part), (10, AnimationPreviewTests.Message((1, 0), (2, "IDLE"))));
                File.WriteAllBytes(Path.Combine(root, "assets", "character.pb"), animation);
                (LevelDefinition _, LevelPreview preview) = LevelPreview.Load(root, 1, 1, resolution, simulate: true);
                using (preview)
                {
                    for (int tick = 0; tick < 180; tick++)
                    {
                        preview.Simulation!.Step();
                    }
                    PhysicsBody body = Assert.Single(preview.Simulation!.Characters);
                    using SKBitmap bitmap = new(width, height);
                    using SKCanvas canvas = new(bitmap);
                    preview.Draw(canvas, width, height, 0);
                    // Left of center the character faces right, so the mirrored scale also mirrors the art offset.
                    Vector2 artworkCenter = (body.Origin * 200) + Vector2.Transform(new(8, -11), Matrix3x2.CreateRotation(body.Angle));
                    Viewport viewport = Viewport.Fit(width, height, 640, 960);
                    Vector2 expected = viewport.ToScreen(artworkCenter);
                    Assert.Equal(SKColors.Lime, bitmap.GetPixel((int)expected.X, (int)expected.Y));
                    Vector2 wrongCenter = viewport.ToScreen((body.Position * 200) + new Vector2(-8, -11));
                    Assert.NotEqual(SKColors.Lime, bitmap.GetPixel((int)wrongCenter.X, (int)wrongCenter.Y));
                }
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Writes a generated opaque red or green atlas without original artwork.</summary>
        /// <param name="path">New texture path.</param>
        /// <param name="green">Whether to write green pixels instead of red.</param>
        private static void WriteTexture(string path, bool green)
        {
            byte[] pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < 256; i++)
            {
                pixels[(i * 4) + (green ? 1 : 0)] = 255;
                pixels[(i * 4) + 3] = 255;
            }
            using FileStream file = File.Create(path);
            PngWriter.Write(file, 16, 16, pixels);
        }
    }
}
