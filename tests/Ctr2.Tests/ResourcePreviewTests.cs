using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;
using Ctr2.Desktop;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks resource selection and cache binding using synthetic catalogs and image bytes.</summary>
    public sealed class ResourcePreviewTests : IDisposable
    {
        /// <summary>Temporary cache directory owned by the test instance.</summary>
        private readonly string root = Path.Combine(Path.GetTempPath(), "ctr2-resource-" + Guid.NewGuid());

        /// <summary>Deletes the synthetic cache after each test.</summary>
        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Verifies unavailable placeholder quads keep their index and cannot be previewed.</summary>
        [Fact]
        public void PreservesUnavailablePlaceholders()
        {
            WriteCatalog();
            ResourceCatalog catalog = JsonSerializer.Deserialize<ResourceCatalog>(File.ReadAllText(Path.Combine(root, "resources.json")))!;
            ResourceCatalog bound = catalog.BindToCache(Path.Combine(root, "assets"),
                [new("synthetic.png", new string('b', 64), 2, 2, 0, 1)], TestContext.Current.CancellationToken);
            Assert.False(Assert.Single(Assert.Single(bound.Resources).Quads).IsAvailable);
            Assert.Equal(3, bound.Resources[0].Quads[0].Width);
            File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(bound));
            _ = Assert.Throws<InvalidDataException>(() => ResourcePreview.Load(root, 7, 4, 0));
        }

        /// <summary>Verifies ID, resolution, and quad lookup produces the intended pixel and logical geometry.</summary>
        [Fact]
        public void SelectsMatchingResourceAndResolution()
        {
            WriteCatalog();
            SelectedResource selected = ResourcePreview.Load(root, 7, 4, 0);
            Assert.Equal(Path.Combine(root, "assets", "synthetic.png"), selected.TexturePath);
            Assert.Equal(1, selected.Frame.PixelX);
            Assert.Equal(3 / 1.56f, selected.Frame.LogicalWidth);
            Assert.Equal(5 / 1.56f, selected.Frame.LogicalOffset.X);
            Assert.Equal(40 / 1.56f, selected.Frame.LogicalCanvas.X);
        }

        /// <summary>Verifies catalog lookup does not silently substitute an ID, resolution, or quad.</summary>
        /// <param name="id">Requested resource ID.</param>
        /// <param name="resolution">Requested original resolution enum.</param>
        /// <param name="quad">Requested quad index.</param>
        [Theory]
        [InlineData(8, 4, 0)]
        [InlineData(7, 2, 0)]
        [InlineData(7, 4, 1)]
        [InlineData(7, 4, -1)]
        public void RejectsMissingSelection(int id, int resolution, int quad)
        {
            WriteCatalog();
            _ = Assert.Throws<InvalidDataException>(() => ResourcePreview.Load(root, id, resolution, quad));
        }

        /// <summary>Verifies a modified cached image cannot use previously bound sprite metadata.</summary>
        [Fact]
        public void RejectsChangedTexture()
        {
            WriteCatalog();
            File.AppendAllText(Path.Combine(root, "assets", "synthetic.png"), "changed");
            _ = Assert.Throws<InvalidDataException>(() => ResourcePreview.Load(root, 7, 4, 0));
        }

        /// <summary>Verifies Windows-specific resolutions retain unknown density rather than guessing.</summary>
        [Fact]
        public void RejectsUnknownDensity()
        {
            WriteCatalog(density: null);
            _ = Assert.Throws<InvalidDataException>(() => ResourcePreview.Load(root, 7, 4, 0));
        }

        /// <summary>Verifies a sidecar cannot select a texture outside its cache assets directory.</summary>
        [Fact]
        public void RejectsUnsafeTexturePath()
        {
            WriteCatalog(path: "../escaped.png");
            _ = Assert.Throws<InvalidDataException>(() => ResourcePreview.Load(root, 7, 4, 0));
        }

        /// <summary>Writes one versioned synthetic resource and a corresponding texture file.</summary>
        /// <param name="density">Explicit resource density, or null for an unknown variant.</param>
        /// <param name="path">Catalog texture path, including invalid values for negative tests.</param>
        private void WriteCatalog(float? density = 1.56f, string path = "synthetic.png")
        {
            _ = Directory.CreateDirectory(Path.Combine(root, "assets"));
            byte[] bytes = [1, 2, 3, 4];
            File.WriteAllBytes(Path.Combine(root, "assets", "synthetic.png"), bytes);
            ResourceEntry entry = new(7, path, 0, 4, density, 40, 60,
                [new(1, 2, 3, 4, 5, 6)], Convert.ToHexStringLower(SHA256.HashData(bytes)));
            ResourceCatalog catalog = new(1, "synthetic", new string('a', 64), [entry]);
            File.WriteAllText(Path.Combine(root, "resources.json"), JsonSerializer.Serialize(catalog));
        }
    }
}
