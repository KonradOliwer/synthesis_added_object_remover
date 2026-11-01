using System.Reflection;
using System.Runtime.CompilerServices;
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
        ["Settings.cs"] = SettingsAttributes,
        ["Settings.ChangedObjects.cs"] = SettingsAttributes,
        ["Settings.FollowUpRemoval.cs"] = SettingsAttributes,
        ["Settings.LeftoverInvisibleObjects.cs"] = SettingsAttributes,
        ["Settings.LeftoverVisibleObjects.cs"] = SettingsAttributes,
        ["Settings.Margins.cs"] = SettingsAttributes,
        ["Settings.RestoreWhereRemoved.cs"] = SettingsAttributes,
        ["Settings.ShrinkOrTurn.cs"] = SettingsAttributes,
        ["Settings.TerrainChange.cs"] = SettingsAttributes,
        ["Settings.TooClose.cs"] = SettingsAttributes,
        ["Settings.Trust.cs"] = SettingsAttributes,
    };

    private const string SettingsAttributes = "The settings classes carry the Synthesis setting-name attributes.";

    private const string SettingsConverter = "The settings class names the converter Synthesis' settings.json reader must use for an unset plugin picker.";

    private static readonly Dictionary<string, string> NewtonsoftExceptions = new()
    {
        ["Settings.cs"] = SettingsConverter,
        ["Settings.Trust.cs"] = SettingsConverter,
    };

    /// <summary>Tools that touch a placed record's enable parent for another purpose than deciding whether the game has the record.</summary>
    private static readonly Dictionary<string, string> PresenceExceptions = new()
    {
        ["Tools/PluginRecordReadingAndWriting/LinkReader.cs"] = "An enable parent is a link; the link reader names its relation.",
        ["Tools/PluginRecordReadingAndWriting/PlacedRecordWriter.cs"] = "Writes the enable parent a removal sets.",
        ["Tools/PluginRecordReadingAndWriting/PlacedRecordParts.cs"] = "Compares and copies the presence flags with the enable parent as one part of the record; it decides nothing about whether the game has it.",
        ["Tools/PluginRecordReadingAndWriting/PlacedOverrideWriter.cs"] = "Writes a deleted record back whole when its presence part goes back; it decides nothing about whether the game has it.",
        ["Steps/BuildLogAndReports/Tables.Restored.cs"] = "Names the presence part in the report; it decides nothing about whether the game has the record.",
        ["Steps/BuildLogAndReports/RestoreOutcomes.cs"] = "Names a navmesh's existence part in the report, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/ChangeJudge.cs"] = "Names a restored record shown or hidden again when its presence part goes back; it decides nothing about whether the game has the record.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshPartComparison.cs"] = "Compares a navmesh's Deleted flag across its versions, not a placed record's presence.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshVersionCollector.cs"] = "Compares the Deleted flag of a navmesh's versions, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshUndoPlan.cs"] = "Reads the judged Deleted flag of a navmesh, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshGroupGuards.cs"] = "Reads the judged Deleted flag of a navmesh, and whether a later plugin changed a linked door's presence part; it decides nothing about whether the game has the door.",
        ["Steps/RestoreWhereRemoved/FinalPresence.cs"] = "Reads whether a restore brings back a record's presence part; whether the game has a version comes from the collector's presence rule, which it follows along the enable parents.",
        ["Steps/CollectPlacedObjects.Contracts/EarlierVersion.cs"] = "Reads whether a restore brings back the presence part, which puts the record back into its earlier child list; it decides nothing about whether the game has the record.",
        ["Steps/RestoreWhereRemoved/DoorLinks.cs"] ="Reads whether a door's restore brings back its presence part, which its navmesh link goes back with; it decides nothing about whether the game has the door.",
    };

    private static readonly string[] ParallelismApis = ["Parallel.", "AsParallel", "Partitioner", "Task.Run", "new Thread("];

    /// <summary>Atomic operations; the lookups tool also needs them for its lock-free memos.</summary>
    private static readonly string[] AtomicApis = ["Interlocked.", "Volatile."];

    private static readonly string[] DecisionListProjects = ["RemovalDecisionList", "RemovalDecisionList.Contracts"];

    private static readonly string[] DecidingStepInputs =
    [
        "ChangedTerrainObjects.Input", "ClashingObjectFitter.Input", "LeftBehindObjects.Input", "MarkerMovePlanner.Input", "OtherModsObjects.Input",
        "RestingObjects.Input", "TooCloseObjects.Input", "VisibleLeftBehindObjects.Input",
    ];

    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const string StepsNamespace = "AddedObjectRemover.Steps.";
    private const string InputRecordName = "Input";
    private const string RecordCloneMethod = "<Clone>$";
    private const string DeconstructMethod = "Deconstruct";

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
    /// Only what holds an <see cref="IRunDecisions"/> can add rounds or moves to the run's decisions. Outside the decision list and its
    /// contracts, neither it nor a type that implements it is the type, or part of the type, of a field, a property or a method or
    /// constructor parameter, except a property of a step's nested Input record, with the constructor, Deconstruct and accessor
    /// parameters and the backing field the compiler writes for it. Locals, lambdas, local functions and return types are not checked. The
    /// step Input records that hold the decisions are listed, so the check cannot pass by finding none.
    /// </summary>
    [Fact]
    public void OnlyTheInputRecordsOfTheDecidingStepsHoldTheRunsDecisions()
    {
        var types = ProductionSources.Assemblies()
            .Where(assembly => !DecisionListProjects.Contains(assembly.GetName().Name!))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !BusinessCode.IsCompilerGenerated(type))
            .ToList();

        var holders = types.SelectMany(type => FindDecisionHolders(type).Select(member => $"{type.FullName}: {member}")).Order(StringComparer.Ordinal).ToList();
        var inputs = types
            .Where(type => IsStepInputRecord(type) && type.GetProperties().Any(property => HoldsDecisions(property.PropertyType)))
            .Select(BusinessCode.PathName)
            .Order(StringComparer.Ordinal);

        Assert.True(holders.Count == 0, $"Only a step's nested Input record may hold the run's decisions:{Environment.NewLine}{string.Join(Environment.NewLine, holders)}");
        Assert.Equal(DecidingStepInputs, inputs);
    }

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

    /// <returns>Each field, property and parameter of <paramref name="type"/> that holds the run's decisions and is not allowed to.</returns>
    private static IEnumerable<string> FindDecisionHolders(Type type)
    {
        var isInput = IsStepInputRecord(type);
        var fields = type.GetFields(DeclaredMembers)
            .Where(field => HoldsDecisions(field.FieldType) && !(isInput && field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
            .Select(field => $"field {field.Name}");
        var properties = type.GetProperties(DeclaredMembers)
            .Where(property => HoldsDecisions(property.PropertyType) && !isInput)
            .Select(property => $"property {property.Name}");
        var parameters = type.GetMethods(DeclaredMembers).Where(method => !IsNamedByTheCompiler(method)).Cast<MethodBase>().Concat(type.GetConstructors(DeclaredMembers))
            .Where(method => !(isInput && IsWrittenForPositionalProperties(method)))
            .SelectMany(method => method.GetParameters().Where(parameter => HoldsDecisions(parameter.ParameterType)).Select(parameter => $"parameter {parameter.Name} of {method.Name}"));
        return [.. fields, .. properties, .. parameters];
    }

    private static bool HoldsDecisions(Type type) => BusinessCode.Mentions(type, typeof(IRunDecisions).IsAssignableFrom);

    private static bool IsStepInputRecord(Type type) =>
        type.Name == InputRecordName
            && type.DeclaringType is not null
            && type.Namespace?.StartsWith(StepsNamespace, StringComparison.Ordinal) == true
            && type.GetMethod(RecordCloneMethod, DeclaredMembers) is not null;

    /// <summary>Lambdas, local functions and a record's clone method: the compiler names the methods it writes for them with a leading '&lt;'.</summary>
    private static bool IsNamedByTheCompiler(MethodInfo method) => method.Name.StartsWith('<');

    private static bool IsWrittenForPositionalProperties(MethodBase method) =>
        method.IsConstructor || method.IsSpecialName || method.Name == DeconstructMethod;
}
