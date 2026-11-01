using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The tools hold no user-visible text: all log wording belongs to the report step. Every string literal under Tools/ that holds
/// a phrase is listed here with its reason.
/// </summary>
public partial class ToolWordingTests
{
    private const string ToolsFolder = "Tools";

    private const string BugMessage = "Message of an exception thrown for a bug; it is never log wording.";

    /// <summary>The file, the literal as written between its quotes, and why it is not user-visible wording.</summary>
    private static readonly Dictionary<(string File, string Literal), string> AllowedPhrases = new()
    {
        [("Tools/MeshFileReading/MeshFileSource.cs", "game INI")] = "Key of the archive problem (first-record-wins with its kind); the report step's line for this kind never prints it.",
        [("Tools/MeshFileReading/MeshFiles.cs", "A read that did not succeed carries its failure.")] = BugMessage,
        [("Tools/MeshFileReading/AssetProblemLog.cs", "No spelling of {mesh} was noted.")] = BugMessage,
        [("Tools/MeshFileReading/NifShapeCollector.cs", "Shapes of kind {kind} cannot be left out of the geometry.")] = BugMessage,
        [("Tools/DenseIdTables/PerIndexTable.cs", "Index {index} has more than one item.")] = BugMessage,
        [("Tools/DenseIdTables/PerIndexTable.cs", "No item has index {index}.")] = BugMessage,
        [("Tools/CompassSectorAreas/DirectionAreaTally.cs", "The tally holds boxes in a category, so it has no totals with every box in none.")] = BugMessage,
        [("Tools/OrderIndependentParallelWork/WorkOrder.cs", "The work order covers {ItemsInOrder.Length} items, not {itemCount}.")] = BugMessage,
        [("Tools/OrderIndependentParallelWork/WorkOrder.cs", "The work order covers {_rank.Length} items.")] = BugMessage,
        [("Tools/OrderIndependentParallelWork/Permutation.cs", "Not a permutation of 0 to {order.Length - 1}: {item} is out of range or repeated.")] = BugMessage,
        [("Tools/GroundSurfaceQueries/TerrainHeights.cs", "The worldspace {worldspaceKey} has no terrain.")] = BugMessage,
        [("Tools/TriangleMeshTests/PointContactTest.cs", "Unknown line axis {axis}.")] = BugMessage,
        [("Tools/TriangleMeshTests/BoxMesh.cs", "A box mesh always has 12 triangles.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PlacedRecordWriter.cs", "Unknown write order.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PlacedOverrideWriter.cs", "Record {record.FormKey} was already moved to another list by this writer.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PlacedOverrideWriter.cs", "Part {part} of record {record.FormKey} holds bytes of another record format and cannot go back.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/CellPartWriter.cs", "Part {name.Part} of cell {cell} holds bytes of another record format and cannot go back.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/LandWriter.cs", "LAND record {land} lies in no cell.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/LandWriter.cs", "The heights ordered for LAND record {land} have a step too steep to store.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/NavmeshWriter.cs", "Navmesh {navmesh} has no navmesh data to set the height of.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/CellOverrides.cs", "Cell {cellKey} is deleted in the load order; nothing is written into it.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/NavmeshWriter.cs", "Navmesh {navmesh} has no such version to write from.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/NavmeshWriter.cs", "The geometry of navmesh {navmesh} holds bytes of another record format and cannot go back.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/FinalNavmeshWorld.cs", "A navmesh parent of kind {parent.GetType().Name} is not read.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/FinalNavmeshWorld.cs", "A navmesh info map entry's parent of kind {parent.GetType().Name} is not read.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PluginRecords.cs", "The placed records must be read before they are used.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PatchWriting.cs", "The records to write were not read by this plugin tool.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PatchMarker.cs", "Added Object Remover patch")] = "Header description text that marks a plugin as this patcher's output; plugin file content, never log wording.",
        [("Tools/PluginRecordReadingAndWriting/RunLinkCache.cs", "The record index was not opened by this plugin tool.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/LandscapeTerrain.cs", "The terrain versions were not read; ask for them in the read scope.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/LandscapeTerrain.cs", "The LAND area record versions were not read; ask for them in the read scope.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/LandscapeTerrain.cs", "The LAND versions were read below {landscapesBelow.Plugin}, not below {plugin}.")] = BugMessage,
        [("Tools/PluginRecordFacts/VersionDifferences.cs", "Versions {lower} and {higher} were not compared; only neighbours and the named plugin's version with one above it are.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/CellWater.cs", "The water copies were not read; ask for the water in the read scope.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RecordHandles.cs", "The area record versions were not read; ask for them in the read scope.")] = BugMessage,
        [("Tools/GroundSurfaceQueries/TerrainLayerShares.cs", "The vertex ({vertexX}, {vertexY}) is outside the cell's terrain.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/RecordKeyConversion.cs", "Plugin {plugin} is not in the load order's known plugin names.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PluginListReader.cs", "The library's malformed-line error names no line: {ex.Message}")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/TranslationMasks.cs", "{typeof(TMask)} has no field {name}.")] = BugMessage,
        [("Tools/PluginRecordReadingAndWriting/PlacedRecordParts.cs", "A {source.GetType().Name} cannot be copied into a {target.GetType().Name}.")] = BugMessage,
    };

    [Fact]
    public void TheToolsHoldNoUserVisibleText()
    {
        var found = SourceFile.All()
            .Where(file => file.IsIn([ToolsFolder]))
            .SelectMany(file => file.StringLiterals.Where(IsPhrase).Select(literal => (File: file.Path, Literal: literal)))
            .ToHashSet();

        var notAllowed = found.Except(AllowedPhrases.Keys).Select(Describe).Order(StringComparer.Ordinal).ToList();
        var stale = AllowedPhrases.Keys.Except(found).Select(Describe).Order(StringComparer.Ordinal).ToList();

        Assert.True(notAllowed.Count == 0, $"Text in a tool; move the wording to the report step:{Environment.NewLine}{string.Join(Environment.NewLine, notAllowed)}");
        AllowListAssert.NoneStale(stale, $"Allowed in {nameof(ToolWordingTests)} but no longer in a tool (remove)");
    }

    [Theory]
    [InlineData("  [mesh] planted {path}", true)]
    [InlineData("game INI", true)]
    [InlineData("NIF has no root NiNode.", true)]
    [InlineData(".nif", false)]
    [InlineData("meshes\\", false)]
    [InlineData("data\\meshes\\x.nif", false)]
    [InlineData("{0}, {1}", false)]
    [InlineData("{first} {second}", false)]
    [InlineData("{count} items", false)]
    [InlineData("", false)]
    public void APhraseIsTwoWordsOfLettersSeparatedByASpace(string literal, bool expected) =>
        Assert.Equal(expected, IsPhrase(literal));

    internal static bool IsPhrase(string literal)
    {
        var withoutHoles = InterpolationHolePattern().Replace(literal, "\u0001");
        return PhrasePattern().IsMatch(withoutHoles);
    }

    private static string Describe((string File, string Literal) phrase) => $"{phrase.File}: {phrase.Literal}";

    /// <summary>An interpolation hole counts as no word: the guard looks at the fixed text around it.</summary>
    [GeneratedRegex(@"(?<!\{)\{[^{}]*\}")]
    private static partial Regex InterpolationHolePattern();

    /// <summary>A word, then a gap of other characters that holds a space (and no hole), then a word.</summary>
    [GeneratedRegex(@"[A-Za-z]+[^A-Za-z\u0001]* [^A-Za-z\u0001]*[A-Za-z]+")]
    private static partial Regex PhrasePattern();
}
