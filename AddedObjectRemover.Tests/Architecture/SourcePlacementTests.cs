using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Where certain code may appear. While everything is one project, the places are explicit folder and file
/// lists; the project split replaces them with project boundaries.
/// </summary>
public class SourcePlacementTests
{
    private const string ParallelTool = "Tools/OrderIndependentParallelWork";
    private const string LookupTool = "Tools/ComputedOnceLookups";

    private static readonly string[] Entry = ["Program.cs", "LogSink.cs", "ConsoleLogFile.cs"];

    private static readonly string[] Runner = ["Run/RunAllSteps"];

    /// <summary>The plugin and load order readers and writers, and the asset readers that go through Mutagen.</summary>
    private static readonly string[] RecordReadingAndWriting =
    [
        "PluginRecords.cs", "PluginRecordsFactory.cs", "RecordKeyConversion.cs", "RecordHandles.cs", "LoadOrderPluginsReader.cs", "BaseFactsReader.cs",
        "PlacedRecordReader.cs", "PlacedRecordLocation.cs", "PlacedRecordExtensions.cs", "PlacedOverrideWriter.cs", "PlacedRecordWriter.cs",
        "LinkReader.cs", "LandscapeTerrain.cs", "LandHeightReader.cs", "NavmeshRecords.cs", "NavmeshTriangles.cs", "CellNavmesh.cs",
        "NpcRecordReads.cs", "NpcTraitsReader.cs", "NpcTemplateChain.cs", "MeshFileSource.cs", "MeshFilesFactory.cs",
    ];

    private static readonly string[] MeshReading =
    [
        "NifGeometryReader.cs", "NifRootFinder.cs", "NifShapes.cs", "NifShapeCollector.cs", "NodeTransformResolver.cs",
        "RenderGeometryTypes.cs", "AvObjectFlags.cs", "Similarity.cs",
    ];

    private static readonly Dictionary<string, string> MutagenExceptions = new()
    {
        ["Settings.cs"] = "The settings class carries the Synthesis setting-name attributes; the project split moves it to the entry.",
    };

    /// <summary>These are about the geometry transform or a private writer step, not the decision list.</summary>
    private static readonly Dictionary<string, string> ApplyExceptions = new()
    {
        ["Similarity.cs"] = "Applies a mesh transform to a vertex.",
        ["NifShapeCollector.cs"] = "Applies a mesh transform to a vertex and a sphere center.",
        ["PlacedRecordWriter.cs"] = "Private step that applies one write order to a record.",
    };

    private static readonly string[] ParallelismApis = ["Parallel.", "AsParallel", "Partitioner", "Task.Run", "new Thread("];

    /// <summary>Atomic operations; the lookups tool also needs them for its lock-free memos.</summary>
    private static readonly string[] AtomicApis = ["Interlocked.", "Volatile."];

    [Fact]
    public void MutagenAndNoggogAreUsedOnlyWhereRecordsAreReadOrWritten() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Mutagen", "Noggog"), [.. RecordReadingAndWriting, "Program.cs"], MutagenExceptions);

    [Fact]
    public void NiflyIsUsedOnlyInTheMeshReading() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Nifly"), MeshReading);

    [Fact]
    public void NetTopologySuiteIsUsedOnlyInTheFlatAreaTool() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("NetTopologySuite"), ["Tools/FlatAreaNearestPoint"]);

    [Fact]
    public void NewtonsoftIsUsedOnlyInTheSettingsValidation() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Newtonsoft"), ["Run/RunSettingsValidation"]);

    [Fact]
    public void OnlyTheRunnerTheEntryAndTheCsvWritingReadTheClock() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\b(Stopwatch|TimeProvider)\b|\bDateTime(Offset)?\.(Now|UtcNow|Today)\b|\bEnvironment\.TickCount"),
            [.. Runner, .. Entry, "Tools/CsvFileWriting"]);

    [Fact]
    public void OnlyTheEntryUsesTheConsole() =>
        PlacementCheck.AssertUsedOnlyIn(file => Regex.IsMatch(file.Code, @"\bConsole\."), Entry);

    [Fact]
    public void OnlyTheParallelToolRunsWorkInParallel() =>
        PlacementCheck.AssertUsedOnlyIn(file => ContainsAny(file, ParallelismApis), [ParallelTool]);

    [Fact]
    public void OnlyTheParallelAndLookupToolsUseAtomicOperations() =>
        PlacementCheck.AssertUsedOnlyIn(file => ContainsAny(file, AtomicApis), [ParallelTool, LookupTool]);

    /// <summary>The tools are allowed as a whole: they cannot name the decision list (see ToolsNameNoBusinessTypeTests), so their Apply is the geometry transform.</summary>
    [Fact]
    public void OnlyTheRunnerAndTheDecisionListApplyDecisions() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\b(Apply|ApplyRoundsUntilNothingRemoved|AddMoves)\("),
            [.. Runner, "Steps/RemovalDecisionList", "Tools"],
            ApplyExceptions);

    [Fact]
    public void CommentsAndTextInQuotesDoNotCountAsCode()
    {
        var code = SourceFile.CodeOf("var a = 1; // Console.WriteLine\n/* Stopwatch */ var b = \"Console.\" + @\"Parallel.\" + $\"{a}Task.Run\";");

        Assert.DoesNotMatch(@"Console|Stopwatch|Parallel|Task", code);
    }

    private static Func<SourceFile, bool> UsesNamespace(params string[] prefixes) =>
        file => Regex.IsMatch(file.Code, $@"^\s*(global\s+)?using\s+(static\s+)?(\w+\s*=\s*)?({string.Join("|", prefixes)})", RegexOptions.Multiline);

    private static bool ContainsAny(SourceFile file, string[] apis) =>
        apis.Any(api => file.Code.Contains(api, StringComparison.Ordinal));
}
