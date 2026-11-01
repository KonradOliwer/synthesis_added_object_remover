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

    private static readonly Dictionary<string, string> NewtonsoftExceptions = new()
    {
        ["Settings.cs"] = "The settings class names the converter Synthesis' settings.json reader must use for an unset plugin picker.",
    };

    /// <summary>Tools that touch a placed record's enable parent for another purpose than deciding whether the game has the record.</summary>
    private static readonly Dictionary<string, string> PresenceExceptions = new()
    {
        ["Tools/PluginRecordReadingAndWriting/LinkReader.cs"] = "An enable parent is a link; the link reader names its relation.",
        ["Tools/PluginRecordReadingAndWriting/PlacedRecordWriter.cs"] = "Writes the enable parent a removal sets.",
        ["Tools/PluginRecordReadingAndWriting/PlacedRecordParts.cs"] = "Compares and copies the presence flags with the enable parent as one part of the record; it decides nothing about whether the game has it.",
        ["Tools/PluginRecordReadingAndWriting/PlacedOverrideWriter.cs"] = "Writes a deleted record back whole when its presence part goes back; it decides nothing about whether the game has it.",
        ["Steps/BuildLogAndReports/Tables.Restored.cs"] = "Names the presence part in the report; it decides nothing about whether the game has the record.",
        ["Steps/BuildLogAndReports/RestoreOutcomes.cs"] = "Words a restored record as shown or hidden again when its presence part goes back; it decides nothing about whether the game has the record.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshPartHistory.cs"] = "Judges a navmesh's Deleted flag, not a placed record's presence.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshVersionCollector.cs"] = "Reads the judged Deleted flag of a navmesh, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshUndoPlan.cs"] = "Reads the judged Deleted flag of a navmesh, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshGroupGuards.cs"] = "Reads the judged Deleted flag of a navmesh, and whether a later plugin changed a linked door's presence part; it decides nothing about whether the game has the door.",
        ["Steps/RestoreWhereRemoved/FinalPresence.cs"] = "Reads whether a restore brings back a record's presence part; whether the game has a version comes from the collector's presence rule, which it follows along the enable parents.",
        ["Steps/CollectPlacedObjects.Contracts/EarlierVersion.cs"] = "Reads whether a restore brings back the presence part, which puts the record back into its earlier child list; it decides nothing about whether the game has the record.",
        ["Steps/RestoreWhereRemoved/DoorLinks.cs"] ="Reads whether a door's restore brings back its presence part, which its navmesh link goes back with; it decides nothing about whether the game has the door.",
    };

    private static readonly string[] ParallelismApis = ["Parallel.", "AsParallel", "Partitioner", "Task.Run", "new Thread("];

    /// <summary>Atomic operations; the lookups tool also needs them for its lock-free memos.</summary>
    private static readonly string[] AtomicApis = ["Interlocked.", "Volatile."];

    [Fact]
    public void MutagenAndNoggogAreUsedOnlyWhereRecordsAreReadOrWritten() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Mutagen", "Noggog"), [.. RecordReadingAndWriting, "Program.cs"], MutagenExceptions);

    /// <summary>Everything past the entry reads the final world; only the entry takes what it needs from the Synthesis state.</summary>
    [Fact]
    public void OnlyTheEntryMentionsTheSynthesisState() =>
        PlacementCheck.AssertUsedOnlyIn(file => Regex.IsMatch(file.Code, @"\bIPatcherState\b"), ["Program.cs"]);

    [Fact]
    public void ThePluginListIsReadOnlyByThePluginListReader() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.Code.Contains("GetLoadOrderListings", StringComparison.Ordinal),
            ["Tools/PluginRecordReadingAndWriting/PluginListReader.cs"]);

    [Fact]
    public void OnlyTheFinalWorldFactoryOpensPluginsFromTheDataFolder() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\bLoadOrderImporter\b|\bLoadOrder\.Import\b"),
            ["Tools/PluginRecordReadingAndWriting/FinalWorldFactory.cs"]);

    /// <summary>A writable mod is the patch: only the patch writing holds it as a value or overrides records into it.</summary>
    [Fact]
    public void OnlyThePatchWritingOverridesRecordsIntoThePatch() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\bGetOrAddAsOverride\b|\bI?SkyrimMod\??\s+[A-Za-z_]"),
            ["Tools/PluginRecordReadingAndWriting/PatchWriting.cs", "Tools/PluginRecordReadingAndWriting/CellOverrides.cs", "Tools/PluginRecordReadingAndWriting/NavmeshMapWriter.cs"]);

    /// <summary>
    /// Whether the game has a placed record is decided once, by the collector's presence rule, from the presence facts the
    /// reader takes from the record's final version; the patch writing writes the same convention.
    /// </summary>
    [Fact]
    public void OnlyTheCollectorAndThePatchWritingReadThePresenceOfPlacedRecords() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\.Presence\b|\bPresenceFacts\b|\bEnableParentFact\b|(?<!RelationKind)\.EnableParent\b|\bGetEnableParent\(|\bIsInitiallyDisabled\("),
            [
                "Steps/CollectPlacedObjects", "Steps/WriteThePatch", "Steps/WriteThePatch.Contracts",
                "Tools/PluginRecordFacts/PlacementFacts.cs",
                "Tools/PluginRecordReadingAndWriting/PlacedRecordReader.cs",
                "Tools/PluginRecordReadingAndWriting/PlacedRecordExtensions.cs",
            ],
            PresenceExceptions);

    /// <summary>The accepted-value rule is applied only where the properties it decides are compared: placed objects and terrain.</summary>
    [Fact]
    public void OnlyTheCollectorAndTheTerrainStepApplyTheAcceptedValueRule() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\bAcceptedVersions\.Decide\b"),
            ["Steps/CollectPlacedObjects", "Steps/RemoveObjectsOnChangedTerrain"]);

    [Fact]
    public void NiflyIsUsedOnlyInTheMeshReading() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Nifly"), [MeshReading]);

    [Fact]
    public void NetTopologySuiteIsUsedOnlyInTheFlatAreaTool() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("NetTopologySuite"), ["Tools/FlatAreaNearestPoint"]);

    [Fact]
    public void NewtonsoftIsUsedOnlyInTheSettingsReading() =>
        PlacementCheck.AssertUsedOnlyIn(UsesNamespace("Newtonsoft"), ["Run/RunSettingsReading"], NewtonsoftExceptions);

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

    /// <summary>
    /// Each step that decides rounds or moves adds them to the run's decisions in its entry, outside the step's error boundary. The tools
    /// are allowed as a whole: they cannot name the decision list (see ToolsNameNoBusinessTypeTests), so their Apply is the geometry transform.
    /// </summary>
    [Fact]
    public void OnlyTheDecisionListAndTheEntriesOfTheDecidingStepsAddToTheDecisions() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => Regex.IsMatch(file.Code, @"\b(Apply|ApplyFits|ApplyRounds|AddMoves)\("),
            [
                "Steps/RemovalDecisionList",
                "Steps/RemovalDecisionList.Contracts",
                "Steps/FitClashingObjects/ClashingObjectFitter.cs",
                "Steps/RemoveObjectsOnChangedTerrain/ChangedTerrainObjects.cs",
                "Steps/RemoveTooCloseObjects/TooCloseObjects.cs",
                "Steps/RemoveObjectsRestingOnRemovedOnes/RestingObjects.cs",
                "Steps/RemoveVisibleObjectsLeftBehind/VisibleLeftBehindObjects.cs",
                "Steps/OtherModsObjectsLeftWithoutSupport/OtherModsObjects.cs",
                "Steps/RemoveInvisibleObjectsLeftBehind/LeftBehindObjects.cs",
                "Steps/MoveKeptMarkers/MarkerMovePlanner.cs",
                "Tools",
            ]);

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
