using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ctr2.Assets
{
    /// <summary>Describes a converted RAW texture in an imported cache.</summary>
    /// <param name="Path">PNG path relative to the cache assets directory, using forward slashes.</param>
    /// <param name="SourceSha256">
    /// Lowercase SHA-256 of the original RAW file and its consecutive continuation files.
    /// </param>
    /// <param name="Width">Decoded width in source pixels.</param>
    /// <param name="Height">Decoded height in source pixels.</param>
    /// <param name="PixelFormat">Original format: 0 RGBA8888, 1 RGB565, 2 RGBA4444, 3 RGB5A1, or 4 A8.</param>
    /// <param name="PixelsPerLogicalUnit">
    /// Resource density, or null when the filename does not identify a known density.
    /// </param>
    public sealed record ImportedTexture(string Path, string SourceSha256, int Width, int Height,
        byte PixelFormat, float? PixelsPerLogicalUnit);

    /// <summary>Describes the source and converted textures of a published asset cache.</summary>
    /// <param name="SchemaVersion">Manifest layout version; the current importer writes version 1.</param>
    /// <param name="SourceKind">Source platform identifier; the current importer writes windows.</param>
    /// <param name="Textures">Converted RAW textures; copied PNGs and other unchanged files are excluded.</param>
    public sealed record ImportManifest(int SchemaVersion, string SourceKind, IReadOnlyList<ImportedTexture> Textures);

    /// <summary>First-run processing shared by the desktop host and development CLI.</summary>
    public static class GameImport
    {
        /// <summary>Maximum bytes permitted in an individual source file or archive entry.</summary>
        private const long MaxFile = 256L * 1024 * 1024;

        /// <summary>Maximum combined bytes permitted in a dump or extracted archive.</summary>
        private const long MaxTotal = 4L * 1024 * 1024 * 1024;

        /// <summary>Serialization settings for readable cache manifests.</summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>Converts a user-supplied Windows game dump into a new asset cache.</summary>
        /// <param name="source">Extracted package root, asset directory, or ZIP, APPX, or MSIX package path.</param>
        /// <param name="destination">New cache directory, outside the source tree; it must not already exist.</param>
        /// <param name="progress">
        /// Optional synchronous callback receiving each relative source path before conversion or copying, and
        /// metadata extraction status messages.
        /// </param>
        /// <param name="cancellationToken">
        /// Cancellation checked between files and while extracting archive chunks.
        /// </param>
        /// <returns>The manifest written alongside the converted assets.</returns>
        /// <remarks>
        /// The source is read without modification. Files are staged in a sibling directory and published by
        /// directory move. On failure, staging cleanup is attempted. RAW continuations are joined, textures
        /// become PNGs, and other assets are copied unchanged. Unknown density remains null. If Game.exe is
        /// present, a verified executable profile supplies
        /// resources.json; unsupported builds fail instead of guessing metadata. Callback exceptions propagate
        /// and trigger cleanup.
        /// </remarks>
        /// <exception cref="ArgumentException">Source and destination overlap, or a path is invalid.</exception>
        /// <exception cref="IOException">
        /// The destination exists, a file cannot be read or written, or the cache cannot be published.
        /// </exception>
        /// <exception cref="InvalidDataException">
        /// The dump, archive, or RAW data is unsupported, malformed, linked, unsafe, or exceeds import limits.
        /// </exception>
        /// <exception cref="UnauthorizedAccessException">
        /// The process lacks permission to access an input or output path.
        /// </exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested before publication.</exception>
        public static ImportManifest ImportWindows(string source, string destination,
            Action<string>? progress = null, CancellationToken cancellationToken = default)
        {
            source = CanonicalPath(source);
            destination = CanonicalPath(destination);
            if (Inside(source, destination) || Inside(destination, source))
            {
                throw new ArgumentException("Source dump and cache must be separate directories.");
            }

            if (Path.Exists(destination))
            {
                throw new IOException("Cache already exists; choose a new cache directory.");
            }

            string parent = Path.GetDirectoryName(destination) ?? throw new ArgumentException("Cache needs a parent directory.");
            _ = Directory.CreateDirectory(parent);
            string staging = Path.Combine(parent, ".ctr2-import-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(staging);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dump = source;
                if (File.Exists(source))
                {
                    string extension = Path.GetExtension(source).ToLowerInvariant();
                    if (extension is not (".zip" or ".appx" or ".msix"))
                    {
                        throw new InvalidDataException("Select an extracted Windows dump or a ZIP, APPX, or MSIX dump.");
                    }

                    dump = Path.Combine(staging, "unpacked");
                    Extract(source, dump, cancellationToken);
                }
                string assets = FindWindowsAssets(dump);
                for (string? current = assets; current != null && Inside(dump, current); current = Path.GetDirectoryName(current))
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("Linked asset paths are unsupported: " + current);
                    }
                }

                string target = Path.Combine(staging, "assets");
                _ = Directory.CreateDirectory(target);
                List<ImportedTexture> textures = [];
                long total = 0;
                // Walk explicitly: recursive enumeration could traverse a link before it is checked.
                foreach (string file in FilesWithoutLinks(assets))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    long size = new FileInfo(file).Length;
                    total += size;
                    if (size > MaxFile || total > MaxTotal)
                    {
                        throw new InvalidDataException("Asset dump exceeds import size limits.");
                    }

                    string relative = Path.GetRelativePath(assets, file);
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (Regex.IsMatch(extension, @"^\.raw\d+$"))
                    {
                        continue;
                    }

                    string outputRelative = extension == ".raw" ? Path.ChangeExtension(relative, ".png") : relative;
                    string output = Path.Combine(target, outputRelative);
                    _ = Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                    progress?.Invoke(relative);
                    if (extension == ".raw")
                    {
                        RawTexture decoded = RawTexture.Load(file);
                        using (FileStream png = new(output, FileMode.CreateNew))
                        {
                            PngWriter.Write(png, decoded.Width, decoded.Height, decoded.Rgba);
                        }

                        textures.Add(new(outputRelative.Replace('\\', '/'), HashRawParts(file), decoded.Width,
                            decoded.Height, decoded.PixelFormat, DensityForName(file)));
                    }
                    else
                    {
                        File.Copy(file, output, false);
                    }
                }
                if (textures.Count == 0)
                {
                    throw new InvalidDataException("No RAW textures found in the selected Windows assets.");
                }

                string executable = Path.Combine(dump, "Game.exe");
                if (File.Exists(executable))
                {
                    if ((File.GetAttributes(executable) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidDataException("Linked Windows executables are unsupported.");
                    }
                    progress?.Invoke("Reading Windows resource metadata");
                    ResourceCatalog catalog = WindowsMetadata.Load(executable, cancellationToken);
                    catalog = catalog.BindToCache(target, textures, cancellationToken);
                    int unavailable = catalog.Resources.Sum(r => r.Quads.Count(q => !q.IsAvailable));
                    progress?.Invoke($"Resource metadata: {catalog.Resources.Count} variants; {unavailable} unavailable placeholder quads.");
                    File.WriteAllText(Path.Combine(staging, "resources.json"), JsonSerializer.Serialize(catalog, JsonOptions));
                }

                ImportManifest manifest = new(1, "windows", textures);
                File.WriteAllText(Path.Combine(staging, "manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions));
                string unpacked = Path.Combine(staging, "unpacked");
                if (Directory.Exists(unpacked))
                {
                    Directory.Delete(unpacked, true);
                }

                cancellationToken.ThrowIfCancellationRequested();
                Directory.Move(staging, destination);
                return manifest;
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, true);
                }
            }
        }

        /// <summary>Looks up the original resource density encoded by a recognized filename suffix.</summary>
        /// <param name="file">Texture filename or path; the extension is ignored.</param>
        /// <returns>1 for _640_960, 1.56 for _1200_1600, or null for an unknown variant.</returns>
        /// <remarks>
        /// Comparison ignores case. These values come from the resource density table, rather than ratios of
        /// the resolution labels. Windows landscape and UI variants require separate metadata.
        /// </remarks>
        public static float? DensityForName(string file)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (name.EndsWith("_640_960", StringComparison.OrdinalIgnoreCase))
            {
                return 1f;
            }

            if (name.EndsWith("_1200_1600", StringComparison.OrdinalIgnoreCase))
            {
                return 1.56f;
            }
            // Windows landscape/UI and unknown variants need their own resource metadata.
            return null;
        }

        /// <summary>Locates the RAW asset directory in an extracted Windows dump.</summary>
        /// <param name="dump">Extracted package root or asset directory.</param>
        /// <returns>
        /// The Assets/assets child directory or the supplied directory containing top-level RAW files.
        /// </returns>
        /// <exception cref="InvalidDataException">Neither candidate contains RAW textures.</exception>
        private static string FindWindowsAssets(string dump)
        {
            foreach (string candidate in new[] { Path.Combine(dump, "Assets", "assets"), dump })
            {
                if (Directory.Exists(candidate) && Directory.EnumerateFiles(candidate, "*.raw").Any())
                {
                    return candidate;
                }
            }

            throw new InvalidDataException("Expected a Windows package root containing Assets/assets, or that asset directory itself. Bundles must be unpacked to their individual package first.");
        }

        /// <summary>Walks an asset tree in ordinal entry order while rejecting links before descending.</summary>
        /// <param name="directory">Directory whose files and nested directories are inspected.</param>
        /// <returns>A lazy sequence of file paths; validation occurs during enumeration.</returns>
        /// <exception cref="InvalidDataException">A directory or entry is a reparse point.</exception>
        private static IEnumerable<string> FilesWithoutLinks(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked asset directories are unsupported: " + directory);
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("Linked dump paths are unsupported: " + entry);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    foreach (string nested in FilesWithoutLinks(entry))
                    {
                        yield return nested;
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        /// <summary>Resolves an absolute path and any existing linked ancestors.</summary>
        /// <param name="path">Relative or absolute input path; the final components may not exist.</param>
        /// <returns>An absolute path with existing links resolved and any nonexistent tail preserved.</returns>
        /// <remarks>
        /// Resolving ancestors handles macOS aliases such as /var and /tmp and makes source/cache overlap
        /// checks compare their real locations.
        /// </remarks>
        private static string CanonicalPath(string path)
        {
            string full = Path.GetFullPath(path);
            string current = Path.GetPathRoot(full)!;
            foreach (string part in full[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    current = new FileInfo(current).ResolveLinkTarget(true)!.FullName;
                }
            }
            return current;
        }

        /// <summary>Tests path equality or directory containment using a separator boundary.</summary>
        /// <param name="parent">Canonical absolute parent path.</param>
        /// <param name="child">Canonical absolute candidate path.</param>
        /// <returns>
        /// True when the paths match or the child is below the parent, using case-insensitive comparison.
        /// </returns>
        private static bool Inside(string parent, string child)
        {
            return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Extracts a bounded package archive while rejecting traversal and symbolic links.</summary>
        /// <param name="source">ZIP-compatible package file to read.</param>
        /// <param name="destination">Absolute staging directory receiving archive entries.</param>
        /// <param name="token">Cancellation checked between entries and copied chunks.</param>
        /// <remarks>
        /// Existing output files are never overwritten. The caller owns cleanup of a partially extracted
        /// directory.
        /// </remarks>
        /// <exception cref="InvalidDataException">
        /// An entry is unsafe, oversized, truncated, or exceeds the archive limits.
        /// </exception>
        /// <exception cref="IOException">
        /// An input or output operation fails, including an output path collision.
        /// </exception>
        /// <exception cref="OperationCanceledException">Cancellation is requested during extraction.</exception>
        private static void Extract(string source, string destination, CancellationToken token)
        {
            _ = Directory.CreateDirectory(destination);
            using ZipArchive archive = ZipFile.OpenRead(source);
            long total = 0;
            if (archive.Entries.Count > 20000)
            {
                throw new InvalidDataException("Too many package entries.");
            }

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string name = entry.FullName.Replace('\\', '/');
                string path = Path.GetFullPath(Path.Combine(destination, name));
                if (!Inside(destination, path) || name.Contains(':') ||
                    name.Split('/').Any(part => part is "." or "..") ||
                    ((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                {
                    throw new InvalidDataException("Unsafe package entry: " + name);
                }

                total += entry.Length;
                if (entry.Length > MaxFile || total > MaxTotal)
                {
                    throw new InvalidDataException("Package exceeds import size limits.");
                }

                if (name.EndsWith('/'))
                {
                    _ = Directory.CreateDirectory(path);
                    continue;
                }
                _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using Stream input = entry.Open();
                using FileStream output = new(path, FileMode.CreateNew);
                byte[] buffer = new byte[81920];
                long written = 0;
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    token.ThrowIfCancellationRequested();
                    written += read;
                    if (written > entry.Length)
                    {
                        throw new InvalidDataException("Package entry exceeds declared size.");
                    }

                    output.Write(buffer, 0, read);
                }
                if (written != entry.Length)
                {
                    throw new InvalidDataException("Truncated package entry.");
                }
            }
        }

        /// <summary>Hashes the bytes of a RAW file followed by consecutive numbered continuations.</summary>
        /// <param name="path">Base RAW path; continuation names append 1, 2, and so on.</param>
        /// <returns>
        /// The 64-character lowercase SHA-256 digest; continuation scanning stops at the first missing file.
        /// </returns>
        private static string HashRawParts(string path)
        {
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            for (int part = 0; ; part++)
            {
                string file = part == 0 ? path : path + part;
                if (!File.Exists(file))
                {
                    break;
                }

                using FileStream input = File.OpenRead(file);
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }
            }
            return Convert.ToHexStringLower(hash.GetHashAndReset());
        }
    }
}
