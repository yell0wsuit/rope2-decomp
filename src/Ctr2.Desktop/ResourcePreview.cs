using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using Ctr2.Assets;

using Ctr2.Engine;

namespace Ctr2.Desktop
{
    /// <summary>Identifies a validated cached texture and its selected frame geometry.</summary>
    /// <param name="TexturePath">Absolute path of the cached texture.</param>
    /// <param name="Frame">Explicit sampling and layout geometry.</param>
    public sealed record SelectedResource(string TexturePath, AtlasFrame Frame);

    /// <summary>Resolves original Windows resource IDs into hash-bound preview frames.</summary>
    public static class ResourcePreview
    {
        /// <summary>Loads one exact resource, resolution, and quad from an imported cache sidecar.</summary>
        /// <param name="cache">Cache root containing resources.json and assets.</param>
        /// <param name="id">Original resource ID.</param>
        /// <param name="resolution">Original resolution enum.</param>
        /// <param name="quad">Zero-based quad index.</param>
        /// <returns>The cached image path and density-aware atlas geometry.</returns>
        /// <exception cref="InvalidDataException">
        /// The catalog, selection, density, path, or texture binding is invalid.
        /// </exception>
        /// <exception cref="IOException">The sidecar or selected texture cannot be read.</exception>
        public static SelectedResource Load(string cache, int id, int resolution, int quad)
        {
            ResourceCatalog catalog;
            try
            {
                using FileStream sidecar = File.OpenRead(Path.Combine(cache, "resources.json"));
                if (sidecar.Length > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException("Resource sidecar exceeds size limit.");
                }

                catalog = JsonSerializer.Deserialize<ResourceCatalog>(sidecar) ?? throw new InvalidDataException("Empty resource catalog.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("Malformed resource catalog.", error);
            }
            if (catalog.SchemaVersion != 1 || catalog.Resources == null || catalog.Resources.Any(r => r == null))
            {
                throw new InvalidDataException("Unsupported resource catalog schema or records.");
            }
            ResourceEntry[] matches = [.. catalog.Resources.Where(r => r.Id == id && r.Resolution == resolution)];
            if (matches.Length != 1)
            {
                throw new InvalidDataException($"Expected one resource {id} at resolution {resolution}; found {matches.Length}.");
            }

            ResourceEntry resource = matches[0];
            if (resource.Type != 0 || resource.Quads == null || quad < 0 || quad >= resource.Quads.Count)
            {
                throw new InvalidDataException($"Resource {id} has no image quad {quad}.");
            }
            if (resource.PixelsPerLogicalUnit is not float density || !float.IsFinite(density) || density <= 0)
            {
                throw new InvalidDataException("Resource density is unknown for this resolution; use an explicitly traced frame sidecar.");
            }
            if (string.IsNullOrEmpty(resource.Path) || resource.Path.Contains('/') || resource.Path.Contains('\\') || resource.Path.Contains(':') || resource.Path.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Unsafe resource texture path.");
            }
            string assets = Path.GetFullPath(Path.Combine(cache, "assets"));
            string texture = Path.Combine(assets, resource.Path);
            if ((File.GetAttributes(assets) & FileAttributes.ReparsePoint) != 0 || (File.GetAttributes(texture) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked cache textures are unsupported.");
            }
            using (FileStream file = File.OpenRead(texture))
            {
                if (file.Length > 256 * 1024 * 1024)
                {
                    throw new InvalidDataException("Resource texture exceeds size limit.");
                }

                string hash = Convert.ToHexStringLower(SHA256.HashData(file));
                if (resource.TextureSha256 != hash)
                {
                    throw new InvalidDataException("Cached texture no longer matches its resource metadata; import into a new cache.");
                }
            }
            ResourceQuad frame = resource.Quads[quad] ?? throw new InvalidDataException("Missing quad geometry.");
            if (!frame.IsAvailable)
            {
                throw new InvalidDataException("This quad is an unavailable placeholder in the source Windows build.");
            }
            try
            {
                AtlasFrame geometry = new(frame.X, frame.Y, frame.Width, frame.Height, frame.OffsetX, frame.OffsetY,
                    resource.PreCutWidth, resource.PreCutHeight, density);
                return geometry.LogicalCanvas.X <= 0 || geometry.LogicalCanvas.Y <= 0
                    ? throw new InvalidDataException("Resource has no positive pre-cut canvas.")
                    : new(texture, geometry);
            }
            catch (ArgumentOutOfRangeException error)
            {
                throw new InvalidDataException("Invalid or empty resource quad geometry.", error);
            }
        }
    }
}
