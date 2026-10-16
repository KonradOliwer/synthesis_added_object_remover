using System.Reflection;

namespace AddedObjectRemover.Tests.Architecture;

/// <summary>
/// The public surface of each logic project (a step, the removal decision list and the base-object shapes) is its one
/// entry type. Everything else in the project is internal; the data it passes lives in the contracts projects.
/// </summary>
public class LogicPublicTypesTests
{
    private static readonly ProjectRules Rules = ProjectRules.Load();

    /// <summary>The entry type of each logic project, by project name.</summary>
    private static readonly Dictionary<string, string> EntryTypes = new()
    {
        ["BaseObjectShapeAndKind"] = "BaseObjectShapes",
        ["IdentifyTheMods"] = "ModIdentifier",
        ["CollectPlacedObjects"] = "PlacedObjectCollector",
        ["SelectObjectsThatCanCauseRemovals"] = "RemovalCauseSelector",
        ["FindTargetObjectsToKeep"] = "ObjectsToKeepFinder",
        ["RemovalDecisionList"] = "RemovalDecisions",
        ["RemoveObjectsOnChangedTerrain"] = "ChangedTerrainObjects",
        ["RemoveTooCloseObjects"] = "TooCloseObjects",
        ["RemoveObjectsRestingOnRemovedOnes"] = "RestingObjects",
        ["RemoveInvisibleObjectsLeftBehind"] = "LeftBehindObjects",
        ["MoveKeptMarkers"] = "MarkerMovePlanner",
        ["WriteThePatch"] = "PatchWriter",
        ["BuildLogAndReports"] = "LogAndReports",
    };

    /// <summary>The public members of each entry type, by project name; see <see cref="PublicMemberNames"/> for the spelling.</summary>
    private static readonly Dictionary<string, string[]> EntryMembers = new()
    {
        ["BaseObjectShapeAndKind"] =
        [
            "BaseObjectShapes(IBaseFacts, IMeshFiles)",
            "static ShapeInclusion SolidShapes",
            "static BoundsStats CountBounds(IReadOnlyList<MeasuredBase>, IReadOnlyList<MeshBounds>, Int32)",
            "IReadOnlyList<MeasuredBase> Computed()",
            "BaseShape Of(BaseKey?)",
            "ObjectVisibility VisibilityOf(BaseKey?, Boolean, Boolean)",
        ],
        ["IdentifyTheMods"] = ["static IdentifiedMods Identify(LoadOrderPlugins, ModIdentificationSettings)"],
        ["CollectPlacedObjects"] = ["static CollectedObjects Collect(IPluginRecords, IdentifiedMods, WhatToCollect)"],
        ["SelectObjectsThatCanCauseRemovals"] =
        [
            "static ObjectMeasurementRules MeasurementRules(IUnexpectedErrors)",
            "static SelectedObjects Select(CollectedObjects, IOtherModObjectsBySpace, IBaseObjectShapes, SelectionSettings, Execution)",
        ],
        ["FindTargetObjectsToKeep"] = ["static ObjectsToKeepResult Find(IReadOnlyList<TargetObject>, IReadOnlyList<LinkFact>, IBaseFacts)"],
        ["RemovalDecisionList"] =
        [
            "IEnumerable<ValueTuple<TargetId, Decision>> All()",
            "ImmutableArray<MarkerMove> Moves { get; }",
            "ImmutableArray<Round> Rounds { get; }",
            "ImmutableArray<Round> RoundsAfter(IRemovalDecisions)",
            "ImmutableArray<TargetId> KeptIn(Round)",
            "ImmutableArray<TargetId> RemovedIn(Round)",
            "ImmutableArray<TargetId> RemovedTargets()",
            "Boolean IsDecided(TargetId)",
            "Boolean IsProtected(TargetId)",
            "Boolean IsRemoved(TargetId)",
            "Decision Of(TargetId)",
            "IRemovalDecisionsCopy WorkingCopy()",
            "RemovalDecisions AddMoves(IReadOnlyList<MarkerMove>)",
            "RemovalDecisions Apply(RoundKind, IReadOnlyList<ProposedRemoval>)",
            "RemovalDecisions ApplyRounds(RoundKind, IReadOnlyList<IReadOnlyList<ProposedRemoval>>)",
            "static RemovalDecisions Start(IObjectsToKeep, Int32)",
        ],
        ["RemoveObjectsOnChangedTerrain"] =
        [
            "static ChangedTerrainResult Find(CollectedObjects, IdentifiedMods, LoadOrderPlugins, IPluginRecords, IBaseObjectShapes, Execution, ChangedTerrainSettings)",
        ],
        ["RemoveTooCloseObjects"] =
        [
            "static NpcBodyRules NpcBodyRules { get; }",
            "static PlacedNpcRules PlacedNpcRules { get; }",
            "static TooCloseResult Find(CollectedObjects, IVisibleTargetObjects, IObjectsThatCanCauseRemovals, IPlacedNpcsBySpace, Replacements, IBaseObjectShapes, ITriangleMeshes, Execution, TooCloseSettings)",
        ],
        ["RemoveObjectsRestingOnRemovedOnes"] =
        [
            "static RestingObjectsResult Find(RestingObjectsInput, IBaseFacts, IBaseObjectShapes, ITriangleMeshes, ITerrainHeights, Func<IVisibleObjectsOfAnyPlugin>)",
        ],
        ["RemoveInvisibleObjectsLeftBehind"] =
        [
            "static LeftBehindResult Find(ImmutableArray<TargetObject>, IObjectsThatCanCauseRemovals, IRemovalDecisions, IVisibleTargetObjects, IBaseFacts, IBaseObjectShapes, WorkOrder, Execution, LeftBehindSettings)",
        ],
        ["MoveKeptMarkers"] =
        [
            "static MarkerMoves Plan(ImmutableArray<TargetObject>, LeftBehindResult, IRemovalDecisions, Func<IVisibleObjectsOfAnyPlugin>, IVisibleTargetObjects, IPluginRecords, INavmeshes, ITerrainHeights, IBaseObjectShapes, Execution, MarkerMoveSettings)",
        ],
        ["WriteThePatch"] = ["static WriteSummary Write(IRemovalDecisions, ImmutableArray<TargetObject>, IPluginRecords)"],
        ["BuildLogAndReports"] =
        [
            "LogAndReports(RunSettings, ReportFolderPrepared, ILoadOrderCaches, IUnexpectedErrors)",
            "static ImmutableArray<LogSection> BuildReportFolderSections(ReportFolderPrepared)",
            "static ImmutableArray<LogSection> BuildStartSections(RunRequest)",
            "static ImmutableArray<LogSection> BuildStopSections(StopReason)",
            "ImmutableArray<LogSection> BuildArchiveSections()",
            "ImmutableArray<LogSection> BuildIndexSections(CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(IdentifiedMods)",
            "ImmutableArray<LogSection> BuildStepSections(CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(Replacements, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(ObjectsToKeepResult)",
            "ImmutableArray<LogSection> BuildStepSections(ChangedTerrainResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(TooCloseResult, IRemovalDecisions, CollectedObjects, IVisibleTargetObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(RestingObjectsResult, IRemovalDecisions, CollectedObjects)",
            "ImmutableArray<LogSection> BuildStepSections(LeftBehindResult, IRemovalDecisions, CollectedObjects, TimeSpan)",
            "ImmutableArray<LogSection> BuildStepSections(MarkerMoves, CollectedObjects)",
            "ImmutableArray<LogSection> BuildStepSections(WriteSummary, TimeSpan)",
            "EndOfRun BuildEndOfRun(StepResults, WriteSummary)",
            "ImmutableArray<LogSection> BuildFilesWrittenSections(EndOfRun, ReportFilesResult, TimeSpan)",
        ],
    };

    [Fact]
    public void EveryLogicProjectHasAListedEntryType()
    {
        var logicProjects = LogicProjectNames();

        Assert.Equal(logicProjects, EntryTypes.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryLogicProjectPublishesOnlyItsEntryType()
    {
        var wrong = LogicProjectNames()
            .SelectMany(project => PublicTypeNames(project).Where(name => name != EntryTypes[project]).Select(name => $"{project}: {name}"))
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryLogicProjectsEntryTypeIsPublic()
    {
        var missing = LogicProjectNames()
            .Where(project => !PublicTypeNames(project).Contains(EntryTypes[project]))
            .Select(project => $"{project}: {EntryTypes[project]}")
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEntryTypeHasExactlyTheListedPublicMembers()
    {
        Assert.Equal(EntryTypes.Keys.Order(StringComparer.Ordinal), EntryMembers.Keys.Order(StringComparer.Ordinal));

        var problems = EntryTypes.Keys.Order(StringComparer.Ordinal)
            .SelectMany(project =>
            {
                var actual = PublicMemberNames(project);
                return actual.Except(EntryMembers[project]).Select(member => $"{project}: not listed: {member}")
                    .Concat(EntryMembers[project].Except(actual).Select(member => $"{project}: listed but missing: {member}"));
            })
            .ToList();

        Assert.True(problems.Count == 0, $"Public members of the entry types differ from the list in {nameof(LogicPublicTypesTests)}:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    private static List<string> LogicProjectNames() =>
        Rules.Projects.Where(Rules.IsLogic).Select(project => project.Name).Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// The public constructors, methods, properties and fields the entry type declares itself, written as
    /// "[static ]ReturnType Name(ParameterTypes)" or "[static ]Type Name { accessors }". Names and types only.
    /// </summary>
    private static List<string> PublicMemberNames(string project)
    {
        var entry = Assembly.Load(project).GetTypes().Single(type => type.IsVisible && BusinessCode.PathName(type) == EntryTypes[project]);
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        var constructors = entry.GetConstructors(Declared).Select(constructor => $"{BusinessCode.NameWithoutArity(entry)}({ParameterTypes(constructor)})");
        var methods = entry.GetMethods(Declared)
            .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .Select(method => $"{StaticPrefix(method.IsStatic)}{TypeText(method.ReturnType)} {MethodName(method)}({ParameterTypes(method)})");
        var properties = entry.GetProperties(Declared)
            .Select(property => $"{StaticPrefix(property.GetMethod?.IsStatic ?? property.SetMethod!.IsStatic)}{TypeText(property.PropertyType)} {property.Name} {{ {Accessors(property)} }}");
        var fields = entry.GetFields(Declared)
            .Where(field => !field.IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute)))
            .Select(field => $"{StaticPrefix(field.IsStatic)}{TypeText(field.FieldType)} {field.Name}");

        return constructors.Concat(methods).Concat(properties).Concat(fields).Order(StringComparer.Ordinal).ToList();
    }

    private static string StaticPrefix(bool isStatic) => isStatic ? "static " : "";

    private static string MethodName(MethodInfo method) =>
        method.IsGenericMethodDefinition ? $"{method.Name}<{string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name))}>" : method.Name;

    private static string ParameterTypes(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(parameter => TypeText(parameter.ParameterType)));

    private static string Accessors(PropertyInfo property) =>
        string.Join(" ", new[] { property.GetMethod is { IsPublic: true } ? "get;" : null, property.SetMethod is { IsPublic: true } setter ? (IsInitOnly(setter) ? "init;" : "set;") : null }.OfType<string>());

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string TypeText(Type type)
    {
        if (type.IsByRef) return $"ref {TypeText(type.GetElementType()!)}";
        if (type.IsArray) return $"{TypeText(type.GetElementType()!)}[]";
        if (Nullable.GetUnderlyingType(type) is { } underlying) return $"{TypeText(underlying)}?";
        if (!type.IsGenericType) return type.Name;
        return $"{BusinessCode.NameWithoutArity(type)}<{string.Join(", ", type.GetGenericArguments().Select(TypeText))}>";
    }

    /// <summary>The types a caller outside the project can name, nested ones included.</summary>
    private static List<string> PublicTypeNames(string project) =>
        Assembly.Load(project).GetTypes()
            .Where(type => type.IsVisible && !BusinessCode.IsCompilerGenerated(type))
            .Select(BusinessCode.PathName)
            .Order(StringComparer.Ordinal)
            .ToList();
}
