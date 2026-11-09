using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        ["Settings.LeftoverObjects.cs"] = SettingsAttributes,
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
        ["Steps/RestoreWhereRemoved/ChangeJudge.cs"] = "Reads the removed flag of the presence before the cleaned mod, to name a restored record as shown or hidden again when its presence part goes back; it decides nothing about whether the game has the record.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshPartComparison.cs"] = "Compares a navmesh's Deleted flag across its versions, not a placed record's presence.",
        ["Tools/PluginRecordReadingAndWriting/NavmeshVersionCollector.cs"] = "Compares the Deleted flag of a navmesh's versions, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshUndoPlan.cs"] = "Reads the judged Deleted flag of a navmesh, not a placed record's presence.",
        ["Steps/RestoreWhereRemoved/NavmeshGroupGuards.cs"] = "Reads the judged Deleted flag of a navmesh, and whether a later plugin changed a linked door's presence part; it decides nothing about whether the game has the door.",
        ["Steps/RestoreWhereRemoved/FinalPresence.cs"] = "Walks the enable-parent chain itself over the collector's winning presence of each placed record, to tell which records end removed once the run's orders and the restore are applied; it reads no record, only the presence facts the collector took.",
        ["Steps/CollectPlacedObjects.Contracts/ResavedPlacedParts.cs"] = "Compares the presence of two versions of a placed record as one object property; it decides nothing about whether the game has the record.",
        ["Steps/CollectPlacedObjects.Contracts/EarlierVersion.cs"] = "Reads whether a restore brings back the presence part, which puts the record back into its earlier child list; it decides nothing about whether the game has the record.",
        ["Steps/RestoreWhereRemoved/DoorLinks.cs"] = "Reads whether a door's restore brings back its presence part, which its navmesh link goes back with; it decides nothing about whether the game has the door.",
        ["RestoredPartsText.cs"] = "Names the presence part in the restored parts file; it decides nothing about whether the game has the record.",
    };

    private static readonly string[] PresenceNames = ["PresenceFacts", "EnableParentFact", "PlacedPresence", "WinningPresence", "PresenceBefore"];

    private static readonly string[] DecisionListProjects = ["RemovalDecisionList", "RemovalDecisionList.Contracts"];

    /// <summary>Only the steps, the rule modules and the run can reach the run's decisions; a tool's call of the same name is on another type.</summary>
    private static readonly string[] DecisionHoldingFolders = ["Steps", "Rules", "Run"];

    private static readonly string[] DecidingStepInputs =
    [
        "ChangedTerrainObjects.Input", "ClashingObjectFitter.Input", "InvisibleLeftBehindObjects.Input", "MarkerMovePlanner.Input", "ObjectsInClearedAreas.Input",
        "OtherModsObjects.Input", "RestingObjects.Input", "TooCloseObjects.Input", "VisibleLeftBehindObjects.Input", "WholeGroups.Input",
    ];

    /// <summary>The step entry of each step that adds to the run's decisions, "Type.Method".</summary>
    private static readonly string[] DecidingStepEntries =
    [
        "ChangedTerrainObjects.Find", "ClashingObjectFitter.Fit", "InvisibleLeftBehindObjects.Find", "MarkerMovePlanner.Plan", "ObjectsInClearedAreas.Find",
        "OtherModsObjects.Settle", "RestingObjects.Find", "TooCloseObjects.Find", "VisibleLeftBehindObjects.Find", "WholeGroups.Find",
    ];

    private const string FailuresType = "Failures";
    private const string AttemptMethod = "Attempt";
    private const string DecidedTypeName = "Decided";

    private const string EnableParentName = "EnableParent";
    private const string RelationKindName = "RelationKind";

    private static readonly string[] ClockTypes = ["Stopwatch", "TimeProvider"];

    /// <summary>Each type with the members of it that read the clock.</summary>
    private static readonly (string Type, string[] Members)[] ClockReads =
    [
        ("DateTime", ["Now", "UtcNow", "Today"]), ("DateTimeOffset", ["Now", "UtcNow", "Today"]), ("Environment", ["TickCount", "TickCount64"]),
    ];

    private static readonly string[] RandomnessNames = ["Random", "ThreadLocal", "ThreadStatic", "AsyncLocal"];

    /// <summary>Each type with the members of it that read the machine or vary from run to run. Hash codes are left out: hashing a name is not variation.</summary>
    private static readonly (string Type, string[] Members)[] MachineReads =
    [
        ("Guid", ["NewGuid"]),
        ("Environment", ["ProcessorCount", "GetEnvironmentVariable", "GetEnvironmentVariables", "MachineName", "UserName", "CurrentDirectory", "CurrentManagedThreadId"]),
        ("Directory", ["GetCurrentDirectory"]),
        ("Path", ["GetTempPath", "GetTempFileName", "GetRandomFileName"]),
        ("CurrentThread", ["ManagedThreadId"]),
    ];

    private static readonly string[] LockNames = ["Lock", "Monitor", "SemaphoreSlim", "Semaphore", "Mutex", "ReaderWriterLockSlim", "SpinLock"];

    private static readonly string[] LockingPlaces = ["Tools", RunnerRulesTests.RunCachesFolder];

    private static readonly (string Type, string[] Members)[] FileWrites =
    [
        ("File", ["WriteAllText", "WriteAllLines", "WriteAllBytes", "AppendAllText", "AppendAllLines", "AppendText", "Create", "CreateText", "OpenWrite", "Delete", "Move", "Copy", "Replace"]),
        ("Directory", ["CreateDirectory", "Delete", "Move"]),
    ];

    private static readonly string[] FileWriterTypes = ["FileStream", "StreamWriter", "BinaryWriter"];

    private static readonly string[] ModWriteCalls = ["WriteToBinary", "WriteToBinaryParallel"];

    private static readonly string[] FileWritingPlaces = [.. Entry, "Tools/FileSystemAccess", "Tools/CsvFileWriting"];

    private static readonly string[] CollectionMarshalNames = ["CollectionsMarshal", "ImmutableCollectionsMarshal", "Unsafe"];

    private static readonly string[] WritableCollectionTypeNames =
    [
        "List", "Dictionary", "HashSet", "Queue", "Stack", "SortedSet", "SortedDictionary", "SortedList", "LinkedList", "IList", "ICollection", "IDictionary", "ISet",
    ];

    private static readonly string[] WarningSuppressionNames = ["SuppressMessage", "SuppressMessageAttribute", "UnconditionalSuppressMessage"];

    private static readonly SyntaxKind[] WarningDirectiveKinds = [SyntaxKind.PragmaWarningDirectiveTrivia, SyntaxKind.NullableDirectiveTrivia];

    private const BindingFlags DeclaredMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    private const string StepsNamespace = "AddedObjectRemover.Steps.";
    private const string RunnerNamespace = "AddedObjectRemover.Run.RunAllSteps";
    private const string InputRecordName = "Input";
    private const string RecordCloneMethod = "<Clone>$";
    private const string DeconstructMethod = "Deconstruct";

    [Fact]
    public void MutagenAndNoggogAreUsedOnlyWhereRecordsAreReadOrWritten() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.Uses("Mutagen", "Noggog"), [.. RecordReadingAndWriting, "Program.cs"], MutagenExceptions);

    /// <summary>Everything past the entry reads the final world; only the entry takes what it needs from the Synthesis state.</summary>
    [Fact]
    public void OnlyTheEntryMentionsTheSynthesisState() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.MentionsIdentifier("IPatcherState"), ["Program.cs"]);

    [Fact]
    public void ThePluginListIsReadOnlyByThePluginListReader() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.MentionsIdentifier("GetLoadOrderListings"),
            ["Tools/PluginRecordReadingAndWriting/PluginListReader.cs"]);

    [Fact]
    public void OnlyTheFinalWorldFactoryOpensPluginsFromTheDataFolder() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.MentionsIdentifier("LoadOrderImporter") || file.Reads("LoadOrder", "Import"),
            ["Tools/PluginRecordReadingAndWriting/FinalWorldFactory.cs"]);

    /// <summary>A writable mod is the patch: only the patch writing holds it as a value, overrides records into it or adds records to it.</summary>
    [Fact]
    public void OnlyThePatchWritingOverridesRecordsIntoThePatch() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.MentionsIdentifier("GetOrAddAsOverride") || file.DeclaresWithType("ISkyrimMod") || file.DeclaresWithType("SkyrimMod"),
            [
                "Tools/PluginRecordReadingAndWriting/PatchWriting.cs", "Tools/PluginRecordReadingAndWriting/CellOverrides.cs", "Tools/PluginRecordReadingAndWriting/NavmeshMapWriter.cs",
                "Tools/PluginRecordReadingAndWriting/PlacedOverrideWriter.cs",
            ]);

    /// <summary>
    /// Whether the game has a placed record is decided by the collector's presence rule, from the presence facts the reader takes
    /// from the record's final version; the patch writing writes the same convention. The restore step derives it again over those
    /// facts once the run's orders are applied (listed in <see cref="PresenceExceptions"/>).
    /// </summary>
    [Fact]
    public void OnlyTheCollectorAndThePatchWritingReadThePresenceOfPlacedRecords() =>
        PlacementCheck.AssertUsedOnlyIn(
            ReadsPresenceOfPlacedRecords,
            [
                "Steps/CollectPlacedObjects", "Steps/WriteThePatch", "Steps/WriteThePatch.Contracts",
                "Steps/CollectPlacedObjects.Contracts/PlacedPresence.cs",
                "Steps/CollectPlacedObjects.Contracts/CollectedObjects.cs",
                "Steps/CollectPlacedObjects.Contracts/RecordChangedByCleanedMod.cs",
                "Tools/PluginRecordFacts/PlacementFacts.cs",
                "Tools/PluginRecordReadingAndWriting/PlacedRecordReader.cs",
                "Tools/PluginRecordReadingAndWriting/PlacedRecordExtensions.cs",
            ],
            PresenceExceptions);

    /// <summary>The accepted-value rule is applied only where the properties it decides are compared: placed objects and terrain.</summary>
    [Fact]
    public void OnlyTheCollectorTheCauseRuleAndTheTerrainRuleApplyTheAcceptedValueRule() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.Reads("AcceptedVersions", "Decide"),
            ["Steps/CollectPlacedObjects", "Rules/ObjectsThatCanCauseRemovals", "Rules/TerrainChangedByOtherMods"]);

    [Fact]
    public void NiflyIsUsedOnlyInTheMeshReading() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.Uses("Nifly"), [MeshReading]);

    [Fact]
    public void NetTopologySuiteIsUsedOnlyInTheFlatAreaTool() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.Uses("NetTopologySuite"), ["Tools/FlatAreaNearestPoint"]);

    [Fact]
    public void NewtonsoftIsUsedOnlyInTheSettingsReadingAndTheJsonFileTool() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.Uses("Newtonsoft"), ["Run/RunSettingsReading", "Tools/JsonSettingsFiles"], NewtonsoftExceptions);

    [Fact]
    public void OnlyTheRunnerTheEntryAndTheCsvWritingReadTheClock() =>
        PlacementCheck.AssertUsedOnlyIn(
            ReadsTheClock,
            [.. Runner, .. Entry, "Tools/CsvFileWriting"]);

    [Fact]
    public void OnlyTheEntryUsesTheConsole() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.ReadsOn("Console"), Entry);

    [Fact]
    public void OnlyTheParallelToolRunsWorkInParallel() =>
        PlacementCheck.AssertUsedOnlyIn(RunsWorkInParallel, [ParallelTool]);

    [Fact]
    public void OnlyTheParallelAndLookupToolsUseAtomicOperations() =>
        PlacementCheck.AssertUsedOnlyIn(file => file.ReadsOn("Interlocked") || file.ReadsOn("Volatile"), [ParallelTool, LookupTool]);

    [Fact]
    public void OnlyTheEntryReadsTheMachineOrDrawsRandomValues() =>
        PlacementCheck.AssertUsedOnlyIn(file => RandomnessNames.Any(file.MentionsIdentifier) || ReadsAnyOf(file, MachineReads), Entry);

    [Fact]
    public void OnlyTheToolsAndTheRunCachesLock() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => file.Root.DescendantNodes().OfType<LockStatementSyntax>().Any() || LockNames.Any(file.MentionsIdentifier),
            LockingPlaces);

    [Fact]
    public void OnlyTheEntryAndTheFileToolsWriteFiles() =>
        PlacementCheck.AssertUsedOnlyIn(
            file => ReadsAnyOf(file, FileWrites) || FileWriterTypes.Any(file.Creates) || ModWriteCalls.Any(file.Calls),
            FileWritingPlaces);

    [Fact]
    public void NoCodeCastsToAWritableCollectionOrUsesTheCollectionMarshals() =>
        PlacementCheck.AssertUsedOnlyIn(ReachesIntoACollection, []);

    [Theory]
    [InlineData("var list = (List<int>)items;", true)]
    [InlineData("var list = (List<int>?)items;", true)]
    [InlineData("var map = items as Dictionary<int, int>;", true)]
    [InlineData("var map = items as Dictionary<int, int>?;", true)]
    [InlineData("var isSet = items is ISet<int>;", true)]
    [InlineData("var isArray = items is int[] values;", true)]
    [InlineData("var span = CollectionsMarshal.AsSpan(new List<int>());", true)]
    [InlineData("var view = (IReadOnlyList<int>)items;", false)]
    [InlineData("var view = items as IReadOnlyDictionary<int, int>?;", false)]
    [InlineData("var isView = items is IReadOnlySet<int> view;", false)]
    public void TheWritableCollectionCheckFindsCastsAndTestsToAWritableCollectionAndTheMarshals(string statement, bool found) =>
        Assert.Equal(found, ReachesIntoACollection(SourceFile.OfSnippet($"class Sample {{ void Run(object items) {{ {statement} }} }}")));

    [Fact]
    public void NoCodeSilencesAWarning() =>
        PlacementCheck.AssertUsedOnlyIn(SilencesAWarning, []);

    [Theory]
    [InlineData("#pragma warning disable CS0168\nclass Sample { }", true)]
    [InlineData("#nullable disable\nclass Sample { }", true)]
    [InlineData("class Sample { [SuppressMessage(\"Style\", \"IDE0001\")] void Run() { } }", true)]
    [InlineData("class Sample { [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute(\"Style\", \"IDE0001\")] void Run() { } }", true)]
    [InlineData("#region Members\nclass Sample { [Obsolete] void Run() { } }\n#endregion", false)]
    public void TheWarningCheckFindsWarningDirectivesAndSuppressionAttributes(string code, bool found) =>
        Assert.Equal(found, SilencesAWarning(SourceFile.OfSnippet(code)));

    /// <summary>
    /// Only what holds an <see cref="IRunDecisions"/> can add rounds or moves to the run's decisions. Outside the decision list and its
    /// contracts, neither it nor a type that implements it nor a type that carries it (such as <c>Decided&lt;T&gt;</c>) is the type, or part
    /// of the type, of a field, a property or a method or
    /// constructor parameter, except a property of a step's nested Input record, with the constructor, Deconstruct and accessor
    /// parameters and the backing field the compiler writes for it. Locals, lambdas, local functions and return types are not checked. The
    /// step Input records that hold the decisions are listed, so the check cannot pass by finding none. The runner may take the carrier,
    /// because it hands each step's result on to the next step, but never the decisions themselves.
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

    private static bool ReadsTheClock(SourceFile file) => ClockTypes.Any(file.MentionsIdentifier) || ReadsAnyOf(file, ClockReads);

    private static bool ReadsAnyOf(SourceFile file, IEnumerable<(string Type, string[] Members)> reads) =>
        reads.Any(read => read.Members.Any(member => file.Reads(read.Type, member)));

    private static bool ReachesIntoACollection(SourceFile file) =>
        CollectionMarshalNames.Any(file.MentionsIdentifier) || TypesTestedOrCastTo(file).Any(IsWritableCollectionType);

    private static bool SilencesAWarning(SourceFile file) =>
        file.Root.DescendantTrivia().Any(trivia => WarningDirectiveKinds.Contains(trivia.Kind())) || WarningSuppressionNames.Any(file.MentionsIdentifier);

    /// <summary>The type of each cast, <c>as</c> or <c>is</c> test and declaration pattern.</summary>
    private static IEnumerable<TypeSyntax> TypesTestedOrCastTo(SourceFile file) =>
        file.Root.DescendantNodes().SelectMany<SyntaxNode, TypeSyntax>(node => node switch
        {
            CastExpressionSyntax cast => [cast.Type],
            BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AsExpression or (int)SyntaxKind.IsExpression, Right: TypeSyntax type } => [type],
            DeclarationPatternSyntax pattern => [pattern.Type],
            _ => [],
        });

    private static bool IsWritableCollectionType(TypeSyntax type) =>
        type is NullableTypeSyntax nullable
            ? IsWritableCollectionType(nullable.ElementType)
            : type is ArrayTypeSyntax || WritableCollectionTypeNames.Contains(SyntaxNames.RightmostName(type));

    private static bool RunsWorkInParallel(SourceFile file) =>
        file.ReadsOn("Parallel") || file.MentionsIdentifier("AsParallel") || file.MentionsIdentifier("Partitioner") || file.Reads("Task", "Run") || file.Creates("Thread");

    private static bool ReadsPresenceOfPlacedRecords(SourceFile file) =>
        file.Reads("Presence")
            || PresenceNames.Any(file.MentionsIdentifier)
            || file.Calls("GetEnableParent") || file.Calls("IsInitiallyDisabled")
            || file.ReadsExceptOn(EnableParentName, RelationKindName);

    /// <returns>Each field, property and parameter of <paramref name="type"/> that holds the run's decisions and is not allowed to.</returns>
    private static IEnumerable<string> FindDecisionHolders(Type type)
    {
        var isInput = IsStepInputRecord(type);
        var holds = HoldsDecisionsAs(type);
        var fields = type.GetFields(DeclaredMembers)
            .Where(field => holds(field.FieldType) && !(isInput && field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)))
            .Select(field => $"field {field.Name}");
        var properties = type.GetProperties(DeclaredMembers)
            .Where(property => holds(property.PropertyType) && !isInput)
            .Select(property => $"property {property.Name}");
        var parameters = type.GetMethods(DeclaredMembers).Where(method => !IsNamedByTheCompiler(method)).Cast<MethodBase>().Concat(type.GetConstructors(DeclaredMembers))
            .Where(method => !(isInput && IsWrittenForPositionalProperties(method)))
            .SelectMany(method => method.GetParameters().Where(parameter => holds(parameter.ParameterType)).Select(parameter => $"parameter {parameter.Name} of {method.Name}"));
        return [.. fields, .. properties, .. parameters];
    }

    private static bool HoldsDecisions(Type type) => BusinessCode.Mentions(type, IsDecisionsOrCarrierOfDecisions);

    /// <summary>The runner hands each step's <c>Decided</c> result on to the next step's input and adds nothing itself, so it may take the carrier but not the decisions.</summary>
    private static Func<Type, bool> HoldsDecisionsAs(Type holder) =>
        held => BusinessCode.Mentions(held, holder.Namespace == RunnerNamespace ? IsDecisions : IsDecisionsOrCarrierOfDecisions);

    private static bool IsDecisions(Type type) => typeof(IRunDecisions).IsAssignableFrom(type);

    private static bool IsDecisionsOrCarrierOfDecisions(Type type) => IsDecisions(type) || IsCarrierOfDecisions(type);

    /// <summary>A type of the decision list's contracts with a property that is the run's decisions, such as <c>Decided&lt;TResult&gt;</c>; it carries them to whoever holds it.</summary>
    private static bool IsCarrierOfDecisions(Type type) =>
        type.Assembly == typeof(IRunDecisions).Assembly
            && type.GetProperties(DeclaredMembers).Any(property => typeof(IRunDecisions).IsAssignableFrom(property.PropertyType));

    /// <summary>
    /// A call that adds rounds, fits or moves to the run's decisions sits in the step's entry (the method that returns the <c>Decided</c>
    /// result), and outside <c>Failures.Attempt</c>, so a failure while adding is never taken for a failure of the step's own work. The
    /// entries that add are listed, so the check cannot pass by finding none.
    /// </summary>
    [Fact]
    public void EveryCallThatAddsToTheRunsDecisionsSitsInAStepEntryOutsideFailuresAttempt()
    {
        var addingNames = typeof(IRunDecisions).GetMethods().Select(method => method.Name).ToHashSet();
        var members = WrittenMember.Of(SourceFile.All().Where(file => file.IsIn(DecisionHoldingFolders) && !DecisionListProjects.Contains(file.Project)));

        var adders = members.Where(member => member.CallsOfMethodsNamed(addingNames).Any()).ToList();
        var wrong = adders.SelectMany(member => FindMisplacedAdditions(member, addingNames)).ToList();

        Assert.True(wrong.Count == 0, $"Calls that add to the run's decisions outside a step entry or inside Failures.Attempt:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
        Assert.Equal(DecidingStepEntries, adders.Select(member => member.Place).Order(StringComparer.Ordinal));
    }

    private static IEnumerable<string> FindMisplacedAdditions(WrittenMember member, IReadOnlyCollection<string> addingNames)
    {
        if (!ReturnsDecided(member)) yield return $"{member.Place} ({member.Location}) adds to the decisions but does not return the Decided result";
        foreach (var call in member.CallsOfMethodsNamed(addingNames))
        {
            if (SyntaxNames.IsInArgumentsOfCallTo(call, FailuresType, AttemptMethod)) yield return $"{member.Place} ({member.Location}) adds to the decisions inside {FailuresType}.{AttemptMethod}";
        }
    }

    /// <summary>The member is a method whose return type names the type <c>Decided</c>.</summary>
    private static bool ReturnsDecided(WrittenMember member) =>
        member.Node is MethodDeclarationSyntax method
            && method.ReturnType.DescendantNodesAndSelf().OfType<SimpleNameSyntax>().Any(name => name.Identifier.Text == DecidedTypeName);

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
