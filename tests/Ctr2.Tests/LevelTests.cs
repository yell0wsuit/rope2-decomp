using System;
using System.IO;
using System.Linq;
using System.Numerics;

using Ctr2.Assets;
using Ctr2.Engine;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks the level coordinate contract with invented XML, without original assets.</summary>
    public sealed class LevelTests
    {
        /// <summary>Small authored scene with a negative position, polygon and target link.</summary>
        private const string Map = """
            <map><layer name="settings"><map width="320" height="480" gridSize="32"/>
            <gameDesign location="forest"/></layer><layer name="Objects">
            <candy x="100" y="140" id="sweet"/><omnom x="64" y="-97" angle="3"/>
            <grab x="100" y="40" length="105" target_id="sweet" target_x="0" target_y="0"/>
            <polygon_materialized x="20" y="30" polygon="-2,1,4,1,4,5," smoothFactor="3"/>
            <future_object x="3" y="4" mystery="retained"/></layer></map>
            """;

        /// <summary>Checks logical units, off-screen placement, raw vertices and pending object preservation.</summary>
        [Fact]
        public void LayoutUsesDoubledEditorUnitsAndKeepsUnsupportedObjects()
        {
            LevelDefinition level = LevelReader.Read(Map);
            LevelLayout layout = LevelLayout.Create(level);
            Assert.Equal(new Vector2(640, 960), layout.Size);
            Assert.Equal(new Vector2(128, -194), layout.Objects.Single(o => o.Source.Kind == "omnom").Position);
            Assert.Equal(new Vector2(200, 80), Assert.Single(layout.Ropes).Start);
            Assert.Equal(new Vector2(200, 280), layout.Ropes[0].End);
            Assert.Equal(210, layout.Ropes[0].RestLength);
            Assert.Equal([new Vector2(40, 60), new Vector2(36, 62), new Vector2(48, 62), new Vector2(48, 70)], Assert.Single(layout.Polygons).Vertices);
            Assert.Equal("retained", level.Objects.Last().Attributes["mystery"]);
            Assert.Equal("forest", level.Settings.Single(s => s.Kind == "gameDesign").Attributes["location"]);
            Assert.Contains(layout.Diagnostics, d => d.Contains("future_object", StringComparison.Ordinal));
            Assert.Contains(layout.Diagnostics, d => d.Contains("off-screen", StringComparison.Ordinal));
        }

        /// <summary>Rejects malformed or unbounded scenes instead of reading malformed numbers as zero.</summary>
        /// <param name="xml">Invented invalid level document.</param>
        [Theory]
        [InlineData("<!DOCTYPE map [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><map>&e;</map>")]
        [InlineData("<map><layer name='settings'><map width='0' height='480'/></layer></map>")]
        [InlineData("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='NaN' y='0'/></layer></map>")]
        [InlineData("<map><layer name='Objects'><candy x='0' y='0'/></layer></map>")]
        [InlineData("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='settings'><map width='320' height='480'/></layer></map>")]
        public void InvalidScenesFailClearly(string xml)
        {
            _ = Assert.Throws<InvalidDataException>(() => LevelLayout.Create(LevelReader.Read(xml)));
        }

        /// <summary>Reads objects from every non-settings layer in document order, as the scene loader ignores layer names.</summary>
        [Fact]
        public void ObjectLayerNamesAreIgnored()
        {
            LevelDefinition level = LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Layer 1'><candy x='1' y='2'/></layer><layer name='Layer 4'><star x='3' y='4'/></layer></map>");
            Assert.Equal(["candy", "star"], level.Objects.Select(o => o.Kind));
        }

        /// <summary>Resolves duplicate editor IDs to the last registering object; unregistered kinds never shadow a name.</summary>
        [Fact]
        public void DuplicateIdsResolveToLastRegisteredObject()
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><candy x='10' y='10' id='a'/><candy x='20' y='20' id='a'/><star x='30' y='30' id='a'/><grab x='0' y='0' length='5' target_id='a'/></layer></map>"));
            Assert.Equal(new Vector2(40, 40), Assert.Single(layout.Ropes).End);
            Assert.Equal(new Vector2(40, 40), layout.Named["a"].Position);
        }

        /// <summary>Keeps a grab without a rope when its target name is not registered, instead of rejecting the level.</summary>
        [Fact]
        public void UnresolvedGrabTargetCreatesNoRope()
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><star x='1' y='1' id='s'/><grab x='0' y='0' length='1' target_id='s'/><grab x='0' y='0' length='1' target_id='missing'/></layer></map>"));
            Assert.Empty(layout.Ropes);
            Assert.Equal(2, layout.Diagnostics.Count(d => d.StartsWith("Grab without rope target", StringComparison.Ordinal)));
        }

        /// <summary>Checks desktop selection by pack ID and authored entry order, with safe flat filenames.</summary>
        [Fact]
        public void DesktopIndexUsesAuthoredOrderAndRejectsTraversal()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-level-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist.xml"), "wrong mobile list");
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='z.xml'/><file short='a.xml'/></pack></filelist>");
                File.WriteAllText(Path.Combine(maps, "z.xml"), Map);
                LevelDefinition level = LevelReader.LoadWindows(root, 1, 1);
                Assert.Equal("z.xml", level.FileName);
                Assert.Equal(5, level.Objects.Count);
                _ = Assert.Throws<InvalidDataException>(() => LevelReader.LoadWindows(root, 1, 3));
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='../z.xml'/></pack></filelist>");
                _ = Assert.Throws<InvalidDataException>(() => LevelReader.LoadWindows(root, 1, 1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Candy and grabs keep the integer digits of their position, as the scene loader's <c>2 * intValue</c> does;
        /// other objects read floats, and absent coordinates read zero.</summary>
        [Fact]
        public void PositionsReadLikeTheSceneLoader()
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<candy x='1e2' y='-7.9'/><grab x='100.9' length='5'/><pump x='280.25' y='10.5'/><omnom y='3'/></layer></map>"));
            Assert.Equal([new Vector2(2, -14), new Vector2(200, 0), new Vector2(560.5f, 21), new Vector2(0, 6)], layout.Objects.Select(o => o.Position!.Value));
        }

        /// <summary>A polygon path of two authored points is a triangle with the implicit origin, as in the smooth path reader.</summary>
        [Fact]
        public void TwoPointPolygonsAreTriangles()
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'>" +
                "<polygon_materialized x='10' y='20' material='wood' polygon='67,99,69,1,' smoothFactor='4'/></layer></map>"));
            Assert.Equal([new Vector2(20, 40), new Vector2(154, 238), new Vector2(158, 42)], Assert.Single(layout.Polygons).Vertices);
        }

        /// <summary>Places offset attachments at the doubled target shift, in the target's rotated frame.</summary>
        [Fact]
        public void RopeOffsetsUseTheTargetFrame()
        {
            LevelLayout layout = LevelLayout.Create(LevelReader.Read("<map><layer name='settings'><map width='320' height='480'/></layer><layer name='Objects'><rectangle_materialized x='100' y='100' width='40' height='10' angle='90' id='p'/><grab x='0' y='0' length='50' target_id='p' target_x='10' target_y='0'/><candy x='10' y='10' id='candy'/><grab x='0' y='0' length='50'/></layer></map>"));
            Assert.Equal(2, layout.Ropes.Count);
            Assert.Equal(200, layout.Ropes[0].End.X, .001f);
            Assert.Equal(220, layout.Ropes[0].End.Y, .001f);
            Assert.Equal(new Vector2(20, 20), layout.Ropes[1].End);
        }

        /// <summary>Rejects excessive XML nesting before constructing the tree.</summary>
        [Fact]
        public void ExcessiveNestingIsRejected()
        {
            _ = Assert.Throws<InvalidDataException>(() => LevelReader.Read(string.Concat(Enumerable.Repeat("<map>", 10)) + string.Concat(Enumerable.Repeat("</map>", 10))));
        }

        /// <summary>Rejects linked map directories instead of reading outside the selected cache.</summary>
        /// <remarks>Windows returns early because link creation may require elevation.</remarks>
        [Fact]
        public void LinkedMapsDirectoryIsRejected()
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }
            string root = Path.Combine(Path.GetTempPath(), "ctr2-level-link-" + Guid.NewGuid());
            _ = Directory.CreateDirectory(Path.Combine(root, "cache", "assets"));
            _ = Directory.CreateDirectory(Path.Combine(root, "outside"));
            try
            {
                _ = Directory.CreateSymbolicLink(Path.Combine(root, "cache", "assets", "maps"), Path.Combine(root, "outside"));
                _ = Assert.Throws<InvalidDataException>(() => LevelReader.LoadWindows(Path.Combine(root, "cache"), 1, 1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Rejects ambiguous duplicate pack IDs even when the first matching entry is valid.</summary>
        [Fact]
        public void DuplicatePackIdsAreRejected()
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-level-pack-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, "sortedlist_desktop.xml"), "<filelist><pack id='1'><file short='a.xml'/></pack><pack id='1'><file short='b.xml'/></pack></filelist>");
                _ = Assert.Throws<InvalidDataException>(() => LevelReader.LoadWindows(root, 1, 1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Loads master designs from package filenames and older encoded cache filenames.</summary>
        /// <param name="name">Filename preserved by the local package import.</param>
        [Theory]
        [InlineData("!_MasterGameDesignDocument.xml")]
        [InlineData("%21_MasterGameDesignDocument.xml")]
        public void WindowsDesignSupportsLiteralAndEncodedFilenames(string name)
        {
            string root = Path.Combine(Path.GetTempPath(), "ctr2-design-" + Guid.NewGuid());
            string maps = Path.Combine(root, "assets", "maps");
            _ = Directory.CreateDirectory(maps);
            try
            {
                File.WriteAllText(Path.Combine(maps, name), Map);
                LevelDefinition design = LevelReader.LoadWindowsDesign(root);
                Assert.Equal(name, design.FileName);
                Assert.Equal(5, design.Objects.Count);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        /// <summary>Checks optional hash-prefix handling and the document character budget.</summary>
        [Fact]
        public void ReaderBoundsDocumentsAndRecognizesHashPrefix()
        {
            Assert.Equal(5, LevelReader.Read("[0123456789abcdef0123456789abcdef]\n" + Map).Objects.Count);
            _ = Assert.Throws<InvalidDataException>(() => LevelReader.Read(new string(' ', (4 * 1024 * 1024) + 1)));
        }
    }
}
