using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Ctr2.Assets
{
    /// <summary>Stores an ordered atlas quad and its trim offset in source pixels.</summary>
    /// <param name="X">Left sample edge.</param>
    /// <param name="Y">Top sample edge.</param>
    /// <param name="Width">Sample width.</param>
    /// <param name="Height">Sample height.</param>
    /// <param name="OffsetX">Horizontal trim offset within the original canvas.</param>
    /// <param name="OffsetY">Vertical trim offset within the original canvas.</param>
    /// <param name="IsAvailable">False when the quad is a known placeholder outside its imported texture.</param>
    public sealed record ResourceQuad(float X, float Y, float Width, float Height, float OffsetX, float OffsetY, bool IsAvailable = true);

    /// <summary>Maps one resource and resolution to an imported asset and optional sprite geometry.</summary>
    /// <param name="Id">Original resource table index used by animation source fields.</param>
    /// <param name="Path">Asset path relative to the cache assets directory.</param>
    /// <param name="Type">Original resource kind: zero for images, one for fonts, seven for animations.</param>
    /// <param name="Resolution">Original resolution enum, preserved without renumbering.</param>
    /// <param name="PixelsPerLogicalUnit">Known uniform resource density, or null when not yet established.</param>
    /// <param name="PreCutWidth">Original canvas width in source pixels, or zero for non-image resources.</param>
    /// <param name="PreCutHeight">Original canvas height in source pixels, or zero for non-image resources.</param>
    /// <param name="Quads">Sample rectangles and offsets in original quad order; empty for non-images.</param>
    /// <param name="TextureSha256">Lowercase hash of the cached image bytes, populated during import.</param>
    public sealed record ResourceEntry(int Id, string Path, int Type, int Resolution, float? PixelsPerLogicalUnit,
        float PreCutWidth, float PreCutHeight, IReadOnlyList<ResourceQuad> Quads, string? TextureSha256 = null);

    /// <summary>Contains executable-specific metadata generated locally from the user's package dump.</summary>
    /// <param name="SchemaVersion">Sidecar layout version, currently one.</param>
    /// <param name="Profile">Verified executable layout identifier.</param>
    /// <param name="ExecutableSha256">Lowercase SHA-256 of the executable that supplied the metadata.</param>
    /// <param name="Resources">Resource variants indexed by original ID and resolution.</param>
    public sealed record ResourceCatalog(int SchemaVersion, string Profile, string ExecutableSha256,
        IReadOnlyList<ResourceEntry> Resources)
    {
        /// <summary>Reads the bounded and versioned imported catalog.</summary>
        /// <param name="cache">Cache root.</param>
        /// <returns>The catalog.</returns>
        /// <exception cref="InvalidDataException">The sidecar is oversized, malformed or an unsupported schema.</exception>
        public static ResourceCatalog Load(string cache)
        {
            ResourceCatalog catalog;
            try
            {
                using FileStream sidecar = File.OpenRead(Path.Combine(cache, "resources.json"));
                if (sidecar.Length > 16 * 1024 * 1024)
                {
                    throw new InvalidDataException("Resource sidecar exceeds size limit.");
                }

                catalog = System.Text.Json.JsonSerializer.Deserialize<ResourceCatalog>(sidecar) ?? throw new InvalidDataException("Empty resource catalog.");
            }
            catch (System.Text.Json.JsonException error)
            {
                throw new InvalidDataException("Malformed resource catalog.", error);
            }
            return catalog.SchemaVersion != 1 || catalog.Resources == null || catalog.Resources.Any(r => r == null)
                ? throw new InvalidDataException("Unsupported resource catalog schema or records.")
                : catalog;
        }

        /// <summary>
        /// Binds resource image metadata to cached bytes and identifies out-of-bounds RAW placeholders.
        /// </summary>
        /// <param name="assets">Imported assets directory.</param>
        /// <param name="textures">Decoded RAW dimensions from the import manifest.</param>
        /// <param name="token">Cancellation checked between resource variants.</param>
        /// <returns>A catalog with image hashes and unavailable placeholder markers.</returns>
        internal ResourceCatalog BindToCache(string assets, IReadOnlyList<ImportedTexture> textures, CancellationToken token)
        {
            List<ResourceEntry> bound = [];
            foreach (ResourceEntry resource in Resources)
            {
                token.ThrowIfCancellationRequested();
                string asset = Path.Combine(assets, resource.Path);
                if (!File.Exists(asset))
                {
                    throw new InvalidDataException("Resource asset is missing from the dump: " + resource.Path);
                }

                if (resource.Type != 0)
                {
                    bound.Add(resource);
                    continue;
                }
                ImportedTexture? converted = textures.FirstOrDefault(t => t.Path == resource.Path);
                // Shipped Windows tables include unused UI/placeholder rectangles outside the actual atlas.
                // Preserve their original indices and geometry without allowing them to be sampled.
                ResourceQuad[] quads = [.. resource.Quads.Select(q => q with
                {
                    IsAvailable = q.IsAvailable && q.Width > 0 && q.Height > 0 &&
                        (converted == null || (q.X + q.Width <= converted.Width && q.Y + q.Height <= converted.Height))
                })];
                using FileStream input = File.OpenRead(asset);
                bound.Add(resource with
                {
                    Quads = quads,
                    TextureSha256 = Convert.ToHexStringLower(SHA256.HashData(input))
                });
            }
            return this with
            {
                Resources = bound
            };
        }
    }
}
