using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Ctr2.Assets
{
    /// <summary>One authored XML element, retaining attributes for mechanics implemented later.</summary>
    /// <param name="Kind">Case-sensitive XML element name.</param>
    /// <param name="Attributes">Read-only authored attribute values, without unit conversion.</param>
    public sealed record LevelElement(string Kind, IReadOnlyDictionary<string, string> Attributes);

    /// <summary>Parsed editor-space level data, independent of graphics and physics.</summary>
    /// <param name="FileName">Selected flat filename, or empty for an in-memory document.</param>
    /// <param name="Width">Positive editor canvas width.</param>
    /// <param name="Height">Positive editor canvas height.</param>
    /// <param name="Settings">Authored settings elements in file order.</param>
    /// <param name="Objects">Authored object elements in file order, including unknown kinds.</param>
    public sealed record LevelDefinition(string FileName, int Width, int Height,
        IReadOnlyList<LevelElement> Settings, IReadOnlyList<LevelElement> Objects);

    /// <summary>One level in authored Windows desktop order, with one-based pack and entry numbers.</summary>
    /// <param name="Pack">Authored pack ID.</param>
    /// <param name="Entry">One-based position within its pack.</param>
    /// <param name="FileName">Validated flat XML filename.</param>
    public sealed record WindowsLevelEntry(int Pack, int Entry, string FileName);

    /// <summary>Reads bounded local level XML and the original Windows desktop pack order.</summary>
    public static class LevelReader
    {
        /// <summary>Maximum accepted document size in characters, and cached file size in bytes.</summary>
        private const int DocumentLimit = 4 * 1024 * 1024;

        /// <summary>Parses a level without resolving external entities or discarding unknown attributes.</summary>
        /// <param name="xml">Level XML, optionally preceded by a bracketed 32-digit hex hash line.</param>
        /// <returns>Read-only authored settings and objects in editor units.</returns>
        /// <exception cref="InvalidDataException">The document is malformed, too large, or structurally unsupported.</exception>
        public static LevelDefinition Read(string xml)
        {
            XDocument document = Document(xml);
            XElement root = document.Root!;
            if (root.Name != "map" || root.Elements().Any(e => e.Name != "layer"))
            {
                throw new InvalidDataException("Expected a map containing layers.");
            }
            XElement[] layers = [.. root.Elements("layer")];
            XElement[] settings = [.. layers.Where(e => (string?)e.Attribute("name") == "settings")];
            XElement[] objects = [.. layers.Where(e => (string?)e.Attribute("name") == "Objects")];
            if (settings.Length != 1 || objects.Length > 1 || layers.Length != settings.Length + objects.Length)
            {
                throw new InvalidDataException("Expected one settings layer and at most one Objects layer.");
            }
            XElement[] maps = [.. settings[0].Elements("map")];
            if (maps.Length != 1)
            {
                throw new InvalidDataException("Expected one canvas setting.");
            }
            int width = Dimension(maps[0], "width"), height = Dimension(maps[0], "height");
            return new("", width, height, Array.AsReadOnly(settings[0].Elements().Select(Element).ToArray()),
                Array.AsReadOnly(objects.SelectMany(e => e.Elements()).Select(Element).ToArray()));
        }

        /// <summary>Selects a level using sortedlist_desktop.xml instead of filename or mobile ordering.</summary>
        /// <param name="cache">User-selected imported Windows cache root.</param>
        /// <param name="pack">One-based authored pack ID.</param>
        /// <param name="level">One-based entry within the pack.</param>
        /// <returns>The selected parsed level with its original filename.</returns>
        /// <exception cref="InvalidDataException">The index, selection, path, or level is invalid.</exception>
        /// <exception cref="IOException">A cached file cannot be read.</exception>
        public static LevelDefinition LoadWindows(string cache, int pack, int level)
        {
            if (pack < 1 || level < 1)
            {
                throw new InvalidDataException("Pack and level numbers start at one.");
            }
            WindowsLevelEntry selected = LoadWindowsIndex(cache).SingleOrDefault(e => e.Pack == pack && e.Entry == level)
                ?? throw new InvalidDataException("Desktop pack/level selection is out of range.");
            return Read(ReadFile(Path.Combine(Path.GetFullPath(cache), "assets", "maps", selected.FileName))) with
            {
                FileName = selected.FileName
            };
        }

        /// <summary>Lists every validated desktop entry without opening its level file.</summary>
        /// <param name="cache">Imported Windows cache root.</param>
        /// <returns>Entries in authored pack and file order.</returns>
        /// <exception cref="InvalidDataException">The XML index, pack IDs or filenames are invalid.</exception>
        /// <exception cref="IOException">The index cannot be read.</exception>
        public static IReadOnlyList<WindowsLevelEntry> LoadWindowsIndex(string cache)
        {
            string assets = Path.GetFullPath(Path.Combine(cache, "assets")), maps = Path.Combine(assets, "maps");
            foreach (string directory in new[] { Path.GetFullPath(cache), assets, maps })
            {
                RejectLink(directory);
            }
            XDocument index = Document(ReadFile(Path.Combine(maps, "sortedlist_desktop.xml")));
            if (index.Root?.Name != "filelist")
            {
                throw new InvalidDataException("Expected a desktop filelist.");
            }
            Dictionary<int, string[]> packs = [];
            List<WindowsLevelEntry> entries = [];
            foreach (XElement entry in index.Root.Elements())
            {
                if (entry.Name != "pack" || !int.TryParse((string?)entry.Attribute("id"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out int id) || id < 1)
                {
                    throw new InvalidDataException("Invalid desktop pack ID.");
                }
                string[] files = [.. entry.Elements().Select(file =>
                {
                    string? name = (string?)file.Attribute("short");
                    return file.Name != "file" || file.HasElements || string.IsNullOrWhiteSpace(name) ||
                        name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains("..", StringComparison.Ordinal) ||
                        !name.EndsWith(".xml", StringComparison.Ordinal) || name.Length > 255
                        ? throw new InvalidDataException("Unsafe desktop level filename.")
                        : name; })];
                if (!packs.TryAdd(id, files))
                {
                    throw new InvalidDataException("Duplicate desktop pack ID.");
                }
                entries.AddRange(files.Select((file, i) => new WindowsLevelEntry(id, i + 1, file)));
            }
            return entries.AsReadOnly();
        }

        /// <summary>Loads the matching Windows master-design XML through the same bounded cache path checks.</summary>
        /// <param name="cache">Imported Windows cache root.</param>
        /// <returns>The parsed master-design settings and body records.</returns>
        /// <exception cref="InvalidDataException">The master design or cache path is invalid.</exception>
        /// <exception cref="IOException">The master design cannot be read.</exception>
        public static LevelDefinition LoadWindowsDesign(string cache)
        {
            string assets = Path.GetFullPath(Path.Combine(cache, "assets")), maps = Path.Combine(assets, "maps");
            foreach (string directory in new[] { Path.GetFullPath(cache), assets, maps })
            {
                RejectLink(directory);
            }
            string name = File.Exists(Path.Combine(maps, "!_MasterGameDesignDocument.xml"))
                ? "!_MasterGameDesignDocument.xml"
                : "%21_MasterGameDesignDocument.xml";
            return Read(ReadFile(Path.Combine(maps, name))) with
            {
                FileName = name
            };
        }

        /// <summary>Reads one regular bounded file without following a final-component link.</summary>
        /// <param name="path">Local file path.</param>
        /// <returns>The UTF-8 text contents.</returns>
        private static string ReadFile(string path)
        {
            RejectLink(path);
            using FileStream stream = File.OpenRead(path);
            if (stream.Length > DocumentLimit)
            {
                throw new InvalidDataException("Level XML exceeds 4 MiB.");
            }
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }

        /// <summary>Rejects symbolic links and other reparse points in selected cache components.</summary>
        /// <param name="path">Existing file or directory.</param>
        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Linked level cache paths are unsupported.");
            }
        }

        /// <summary>Validates size, nesting and XML syntax before constructing a bounded document tree.</summary>
        /// <param name="xml">Potentially hash-prefixed XML.</param>
        /// <returns>The validated XML document.</returns>
        private static XDocument Document(string xml)
        {
            if (xml.Length > DocumentLimit)
            {
                throw new InvalidDataException("Level XML exceeds 4 MiB.");
            }
            if (xml.Length >= 34 && xml[0] == '[' && xml[33] == ']' && xml.AsSpan(1, 32).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0)
            {
                xml = xml[34..].TrimStart();
            }
            XmlReaderSettings options = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = DocumentLimit
            };
            try
            {
                using (XmlReader bounded = XmlReader.Create(new StringReader(xml), options))
                {
                    int nodes = 0;
                    while (bounded.Read())
                    {
                        if (++nodes > 20000 || bounded.Depth > 8 || bounded.AttributeCount > 64)
                        {
                            throw new InvalidDataException("Level XML complexity exceeds bounds.");
                        }
                        if (bounded.NamespaceURI.Length != 0)
                        {
                            throw new InvalidDataException("Namespaced level XML is unsupported.");
                        }
                    }
                }
                using XmlReader reader = XmlReader.Create(new StringReader(xml), options);
                return XDocument.Load(reader);
            }
            catch (XmlException error)
            {
                throw new InvalidDataException("Invalid level XML: " + error.Message, error);
            }
        }

        /// <summary>Reads a required positive bounded editor canvas dimension.</summary>
        /// <param name="map">Canvas settings element.</param>
        /// <param name="name">Dimension attribute.</param>
        /// <returns>A dimension between 1 and 16384 editor units.</returns>
        private static int Dimension(XElement map, string name)
        {
            return !int.TryParse((string?)map.Attribute(name), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 1 || value > 16384
                ? throw new InvalidDataException("Invalid level canvas " + name)
                : value;
        }

        /// <summary>Copies flat authored attributes into an immutable view.</summary>
        /// <param name="element">Settings or object element.</param>
        /// <returns>A named attribute record.</returns>
        private static LevelElement Element(XElement element)
        {
            return element.HasElements || element.Value.Trim().Length != 0 || element.Attributes().Any(a => a.Name.Namespace != XNamespace.None || a.Value.Length > 65536)
                ? throw new InvalidDataException("Expected flat bounded level attributes.")
                : new(element.Name.LocalName, new ReadOnlyDictionary<string, string>(element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal)));
        }
    }
}
