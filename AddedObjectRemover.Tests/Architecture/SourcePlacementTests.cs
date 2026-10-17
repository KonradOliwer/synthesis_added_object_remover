using System.Text.RegularExpressions;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// Where certain code may appear: explicit folder and file lists for the rules the project references cannot express.
/// </summary>
public class SourcePlacementTests
{
    private const string ParallelTool = "Tools/OrderIndependentParallelWork";
    private const string LookupTool = "Tools/ComputedOnceLookups";

    private static readonly string[] Entry = ["AddedObjectRemover"];

    private static readonly string[] Runner = ["Run/RunAllSteps"];

    private const string MeshReading = "Tools/MeshFileReading";

    /// <summary>The plugin and load order readers and writers, and the asset readers that go through Mutagen.</summary>
    private static readonly string[] RecordReadingAndWriting =
    [
        "Tools/PluginRecordReadingAndWriting", MeshReading,
    ];

    private static readonly Dictionary<string, string> MutagenExceptions = new()
    {
        ["Settings.cs"] = "The settings class carries the Synthesis setting-name attributes.",
    };

    private static readonly string[] ParallelismApis = ["Parallel.", "AsParallel", "Partitioner", "Task.Run", "new Thread("];

    /// <summary>Atomic operations; the lookups tool also needs them for its lock-free memos.</summary>
    private static readonly string[] AtomicApis = ["Interlocked.", "Volatile."];

    [Fact]
    public void MutagenAndNoggogAreUsedOnlyWhereRecordsAreReadOrWritten() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Mutagen", "Noggog"), [.. RecordReadingAndWriting, "Program.cs"], MutagenExceptions);

    [Fact]
    public void NiflyIsUsedOnlyInTheMeshReading() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Nifly"), [MeshReading]);

    [Fact]
    public void NetTopologySuiteIsUsedOnlyInTheFlatAreaTool() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("NetTopologySuite"), ["Tools/FlatAreaNearestPoint"]);

    [Fact]
    public void NewtonsoftIsUsedOnlyInTheSettingsReading() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Newtonsoft"), ["Run/RunSettingsReading"]);

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
            file => Regex.IsMatch(file.Code, @"\b(Apply|ApplyRounds|AddMoves)\("),
            [.. Runner, "Steps/RemovalDecisionList", "Tools"]);

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
